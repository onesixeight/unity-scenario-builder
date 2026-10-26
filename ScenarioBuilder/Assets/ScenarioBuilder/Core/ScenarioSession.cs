using System;
using System.Collections.Generic;
using System.Globalization;

namespace ScenarioBuilder.Core
{
    public sealed class ScenarioSession : IScenarioExecutor, IGroupProgressProvider
    {
        private readonly ScenarioPlan plan;
        private readonly RunMode mode;
        private readonly bool retryOnViolation;
        private readonly IJournalLogger logger;
        private readonly Func<DateTime> utcNow;
        private readonly List<StepRuntime> steps = new List<StepRuntime>();
        private readonly List<GroupRuntime> groups = new List<GroupRuntime>();
        private readonly Dictionary<string, StepRuntime> stepsById = new Dictionary<string, StepRuntime>(StringComparer.Ordinal);
        private readonly Dictionary<string, GroupRuntime> owners = new Dictionary<string, GroupRuntime>(StringComparer.Ordinal);
        private readonly HashSet<string> handledEvents = new HashSet<string>(StringComparer.Ordinal);
        private int sequence;
        private bool started;

        public IReadOnlyList<StepRuntime> Steps { get; }
        IReadOnlyList<IStepProgress> IScenarioExecutor.Steps => Steps;
        public IReadOnlyList<IGroupProgress> Groups { get; }
        public Journal Journal => logger.Journal;
        public bool IsFinished { get; private set; }
        public string CurrentGroupId => FirstActiveGroup()?.Id;
        public int CurrentGroupViolations => FirstActiveGroup()?.Violations ?? 0;
        public string LastMessage { get; private set; } = string.Empty;
        public event Action Changed;

        public ScenarioSession(ScenarioPlan plan, IExecutionPolicy policy, IJournalLogger logger, Func<DateTime> utcNow = null)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.utcNow = utcNow ?? (() => DateTime.UtcNow);
            var errors = plan.Validate();
            if (!Enum.IsDefined(typeof(RunMode), policy.Mode)) errors.Add("Unknown execution mode.");
            if (policy.Mode == RunMode.Training && plan.Groups != null && plan.Groups.Count != 1)
                errors.Add("Training uses exactly one common group.");
            if (errors.Count > 0) throw new ArgumentException(string.Join("\n", errors), nameof(plan));
            this.plan = SnapshotPlan(plan);
            mode = policy.Mode;
            retryOnViolation = policy.RetryOnViolation;
            var conditions = new HashSet<IStepCondition>();
            foreach (var spec in this.plan.Steps)
            {
                var step = new StepRuntime(spec);
                if (!conditions.Add(step.Condition))
                    throw new ArgumentException("Condition factories must create independent instances for each step.", nameof(plan));
                steps.Add(step);
                stepsById.Add(spec.Id, step);
            }
            for (var index = 0; index < this.plan.Groups.Count; index++)
            {
                var group = new GroupRuntime(this.plan.Groups[index], index, policy.StopGroupAtThreshold);
                groups.Add(group);
                foreach (var id in group.StepIds) owners.Add(id, group);
            }
            Steps = steps.AsReadOnly();
            Groups = groups.AsReadOnly();
        }

        public void Start()
        {
            if (started) throw new InvalidOperationException("Create a new session to restart the scenario.");
            var journal = new Journal { Mode = mode.ToString(), StartedUtc = Timestamp() };
            foreach (var step in steps)
                journal.Steps.Add(new StepSnapshot
                {
                    Id = step.Spec.Id,
                    Title = step.Spec.Title,
                    Result = step.Spec.Result,
                    GroupId = owners[step.Spec.Id].Id
                });
            foreach (var group in groups)
                journal.Groups.Add(new GroupSnapshot
                {
                    Id = group.Id,
                    Title = group.Title,
                    ViolationThreshold = group.ViolationThreshold,
                    ActivationRule = group.StartRule.ToString(),
                    AfterStepId = group.StartAfterStepId,
                    StepIds = new List<string>(group.StepIds)
                });
            logger.Begin(journal);
            started = true;
            Append(JournalEvents.SessionStarted, null, "Started", "Сценарий начат.", groups[0]);
            LastMessage = "Сценарий начат.";
            PumpGroups();
            Changed?.Invoke();
        }

        public void Handle(ScenarioAction action)
        {
            if (!started || IsFinished || action == null) return;
            if (!string.IsNullOrEmpty(action.EventId) && !handledEvents.Add(action.EventId)) return;
            StepRuntime step;
            if (string.IsNullOrWhiteSpace(action.TargetStepId) || !stepsById.TryGetValue(action.TargetStepId, out step))
            {
                LastMessage = "Источник указал неизвестный шаг: " + (action.TargetStepId ?? "<пусто>") + ".";
                Append(JournalEvents.ActionIgnored, null, "Ignored", LastMessage, FirstActiveGroup(), action);
                Changed?.Invoke();
                return;
            }
            var owner = owners[step.Spec.Id];
            if (string.IsNullOrWhiteSpace(action.Signal))
            {
                LastMessage = "Пустой сигнал проигнорирован.";
                Append(JournalEvents.ActionIgnored, step, step.Status.ToString(), LastMessage, owner, action);
                Changed?.Invoke();
                return;
            }
            if (IsTerminal(step.Status))
            {
                Append(JournalEvents.ActionIgnored, step, step.Status.ToString(), "Шаг уже завершён.", owner, action);
                return;
            }
            if (step.Status == StepStatus.Pending)
            {
                if (plan.OutOfOrderPolicy == OutOfOrderPolicy.Ignore) return;
                // A waiting batch belongs to its active owner; a not-yet-started owner uses the earliest active group.
                var accounting = owner.Status == GroupStatus.Active ? owner : FirstActiveGroup();
                if (accounting == null) throw new InvalidOperationException("No active group can account for this action.");
                BeginAttempt(step, accounting, action);
                step.Violations++;
                accounting.Violations++;
                step.HasOpenAttempt = false;
                var count = accounting.Violations.ToString(CultureInfo.InvariantCulture);
                if (accounting.StopsAtThreshold) count += " из " + accounting.ViolationThreshold;
                step.ProgressMessage = "Шаг " + StepLabel(step) + " ещё ожидает своей очереди. " +
                    "Действие вне очереди засчитано текущей группе " + GroupLabel(accounting) +
                    " (нарушений: " + count + "). Неудачная попытка " + step.Attempts + " засчитана. " +
                    (retryOnViolation ? "Повторите шаг после его активации." : "Шаг завершён с ошибкой; повторной попытки нет.");
                LastMessage = step.ProgressMessage;
                Append(JournalEvents.Violation, step, StepStatus.Pending.ToString(), LastMessage, accounting, action, "out-of-order");
                if (retryOnViolation) step.Condition.Reset();
                else
                {
                    step.Status = StepStatus.Failed;
                    Append(JournalEvents.StepFailed, step, step.Status.ToString(), step.ProgressMessage, accounting, action);
                }
                CheckThreshold(accounting, step, action, "Шаг ещё ожидает своей очереди.");
                PumpGroups();
                Changed?.Invoke();
                return;
            }

            // Only the addressed step sees the input, even if it unlocks another group or batch.
            var result = step.Condition.Evaluate(action);
            if (result.Kind == ConditionOutcome.Ignore) return;
            BeginAttempt(step, owner, action);
            switch (result.Kind)
            {
                case ConditionOutcome.Progress:
                    step.ProgressMessage = result.Message;
                    LastMessage = result.Message;
                    Append(JournalEvents.ConditionProgress, step, StepStatus.Active.ToString(), result.Message, owner, action);
                    break;
                case ConditionOutcome.Success:
                    step.Status = StepStatus.Completed;
                    step.HasOpenAttempt = false;
                    step.ProgressMessage = string.IsNullOrWhiteSpace(step.Spec.Result) ? "Шаг выполнен." : step.Spec.Result;
                    LastMessage = step.ProgressMessage;
                    Append(JournalEvents.StepCompleted, step, step.Status.ToString(), step.ProgressMessage, owner, action);
                    PumpGroups();
                    break;
                case ConditionOutcome.Violation:
                    step.Violations++;
                    owner.Violations++;
                    step.HasOpenAttempt = false;
                    step.ProgressMessage = string.IsNullOrWhiteSpace(result.Message) ? "Условие шага нарушено." : result.Message;
                    LastMessage = step.ProgressMessage;
                    Append(JournalEvents.Violation, step, step.Status.ToString(), step.ProgressMessage, owner, action, "condition");
                    if (retryOnViolation) step.Condition.Reset();
                    else
                    {
                        step.Status = StepStatus.Failed;
                        Append(JournalEvents.StepFailed, step, step.Status.ToString(), step.ProgressMessage, owner, action);
                    }
                    CheckThreshold(owner, step, action, step.ProgressMessage);
                    PumpGroups();
                    break;
                default:
                    throw new InvalidOperationException("A condition returned an unknown outcome.");
            }
            Changed?.Invoke();
        }

        private void BeginAttempt(StepRuntime step, GroupRuntime group, ScenarioAction action)
        {
            if (step.HasOpenAttempt) return;
            step.Attempts++;
            step.HasOpenAttempt = true;
            Append(JournalEvents.AttemptStarted, step, step.Status.ToString(), "Начата попытка.", group, action);
        }

        private void CheckThreshold(GroupRuntime group, StepRuntime causeStep, ScenarioAction action, string cause)
        {
            if (!group.StopsAtThreshold || group.Violations < group.ViolationThreshold) return;
            bool skipped = false;
            foreach (var id in group.StepIds)
                if (!IsTerminal(stepsById[id].Status)) { skipped = true; break; }
            var message = "В группе " + GroupLabel(group) + " достигнут порог нарушений " +
                group.Violations + "/" + group.ViolationThreshold + ". " +
                "Причина последнего нарушения: " + ActionLabel(action) + " для шага " + StepLabel(causeStep) +
                ". " + cause + " " + (skipped ? "Поэтому незавершённые шаги этой группы пропущены."
                    : "Незавершённых шагов в группе нет.");
            foreach (var id in group.StepIds)
            {
                var step = stepsById[id];
                if (IsTerminal(step.Status)) continue;
                step.Status = StepStatus.Skipped;
                step.HasOpenAttempt = false;
                step.ProgressMessage = message;
                Append(JournalEvents.StepSkipped, step, step.Status.ToString(), message, group);
            }
            group.Status = skipped ? GroupStatus.Skipped : GroupStatus.Completed;
            LastMessage = message;
        }

        private void PumpGroups()
        {
            bool changed;
            do
            {
                changed = false;
                foreach (var group in groups)
                {
                    if (group.Status != GroupStatus.Active) continue;
                    while (BatchIsTerminal(group))
                    {
                        group.BatchIndex++;
                        changed = true;
                        if (group.BatchIndex == group.Plan.Batches.Count)
                        {
                            group.Status = GroupStatus.Completed;
                            break;
                        }
                        ActivateBatch(group);
                    }
                }
                foreach (var group in groups)
                {
                    if (group.Status != GroupStatus.Pending || !CanStart(group)) continue;
                    group.Status = GroupStatus.Active;
                    group.BatchIndex = 0;
                    ActivateBatch(group);
                    changed = true;
                }
            } while (changed);

            if (IsFinished) return;
            foreach (var group in groups)
                if (!IsClosed(group.Status)) return;
            IsFinished = true;
            LastMessage = "Сценарий завершён.";
            Append(JournalEvents.SessionCompleted, null, "Completed", LastMessage, null);
            logger.Finish();
        }

        private bool CanStart(GroupRuntime group)
        {
            switch (group.StartRule)
            {
                case GroupStartRule.AtStart: return true;
                case GroupStartRule.AfterPreviousGroup:
                    return group.Index == 0 || IsClosed(groups[group.Index - 1].Status);
                case GroupStartRule.AfterStep:
                    return owners[group.StartAfterStepId].Status != GroupStatus.Pending &&
                           IsTerminal(stepsById[group.StartAfterStepId].Status);
                default: throw new InvalidOperationException("Unknown group activation rule.");
            }
        }

        private bool BatchIsTerminal(GroupRuntime group)
        {
            foreach (var id in group.Plan.Batches[group.BatchIndex].StepIds)
                if (!IsTerminal(stepsById[id].Status)) return false;
            return true;
        }

        private void ActivateBatch(GroupRuntime group)
        {
            foreach (var id in group.Plan.Batches[group.BatchIndex].StepIds)
            {
                var step = stepsById[id];
                if (IsTerminal(step.Status)) continue;
                step.Status = StepStatus.Active;
                step.ProgressMessage = string.Empty;
                step.Condition.Reset();
                Append(JournalEvents.StepActivated, step, StepStatus.Active.ToString(), "Шаг активирован.", group);
            }
        }

        private GroupRuntime FirstActiveGroup()
        {
            foreach (var group in groups)
                if (group.Status == GroupStatus.Active) return group;
            return null;
        }

        private void Append(string type, StepRuntime step, string result, string message, GroupRuntime group,
            ScenarioAction action = null, string reason = null)
        {
            var entry = new JournalEntry
            {
                Sequence = ++sequence,
                TimestampUtc = Timestamp(),
                StepId = step?.Spec.Id,
                GroupId = group?.Id,
                Type = type,
                Result = result,
                Attempt = step?.Attempts ?? 0,
                Violations = step?.Violations ?? 0,
                Message = message
            };
            if (step != null) entry.Extra.Add(new JournalField("targetGroupId", owners[step.Spec.Id].Id));
            entry.Extra.Add(new JournalField("groupViolations", (group?.Violations ?? 0).ToString(CultureInfo.InvariantCulture)));
            if (reason != null) entry.Extra.Add(new JournalField("reason", reason));
            if (action != null)
            {
                entry.Extra.Add(new JournalField("eventId", action.EventId));
                entry.Extra.Add(new JournalField("signal", action.Signal));
            }
            logger.Write(entry);
        }

        private string Timestamp() => utcNow().ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        private static bool IsTerminal(StepStatus status) => status == StepStatus.Completed || status == StepStatus.Failed || status == StepStatus.Skipped;
        private static bool IsClosed(GroupStatus status) => status == GroupStatus.Completed || status == GroupStatus.Skipped;
        private static string StepLabel(StepRuntime step) => step.Spec.Id + (string.IsNullOrWhiteSpace(step.Spec.Title) ? string.Empty : " «" + step.Spec.Title + "»");
        private static string GroupLabel(GroupRuntime group) => group.Id + (string.IsNullOrWhiteSpace(group.Title) ? string.Empty : " «" + group.Title + "»");
        private static string ActionLabel(ScenarioAction action)
        {
            if (action.Signal == "error") return "имитация ошибки («Ошибка»)";
            if (action.Signal == "confirm") return "подтверждение";
            return "сигнал «" + action.Signal + "»";
        }

        private static ScenarioPlan SnapshotPlan(ScenarioPlan source)
        {
            var snapshot = new ScenarioPlan { OutOfOrderPolicy = source.OutOfOrderPolicy };
            foreach (var step in source.Steps)
                snapshot.Steps.Add(new StepSpec(step.Id, step.Title, step.Result, step.ConditionFactory));
            foreach (var group in source.Groups)
            {
                var copy = new GroupPlan
                {
                    Id = group.Id,
                    Title = group.Title,
                    ViolationThreshold = group.ViolationThreshold,
                    StartRule = group.StartRule,
                    StartAfterStepId = group.StartAfterStepId,
                    StepIds = new List<string>(group.StepIds)
                };
                foreach (var batch in group.Batches)
                    copy.Batches.Add(new ActivationBatch { StepIds = new List<string>(batch.StepIds) });
                snapshot.Groups.Add(copy);
            }
            return snapshot;
        }

        private sealed class GroupRuntime : IGroupProgress
        {
            public GroupPlan Plan { get; }
            public int Index { get; }
            public int BatchIndex { get; set; }
            public string Id => Plan.Id;
            public string Title => Plan.Title;
            public GroupStatus Status { get; set; } = GroupStatus.Pending;
            public int Violations { get; set; }
            public int ViolationThreshold => Plan.ViolationThreshold;
            public bool StopsAtThreshold { get; }
            public GroupStartRule StartRule => Plan.StartRule;
            public string StartAfterStepId => Plan.StartAfterStepId;
            public IReadOnlyList<string> StepIds { get; }
            public GroupRuntime(GroupPlan plan, int index, bool stopsAtThreshold)
            {
                Plan = plan;
                Index = index;
                StopsAtThreshold = stopsAtThreshold;
                StepIds = plan.StepIds.AsReadOnly();
            }
        }
    }
}
