using System;
using System.Collections.Generic;
using System.Globalization;

namespace ScenarioBuilder.Core
{
    public sealed class ScenarioSession : IScenarioExecutor
    {
        private readonly ScenarioPlan plan;
        private readonly RunMode mode;
        private readonly bool retryOnViolation;
        private readonly bool stopGroupAtThreshold;
        private readonly IJournalLogger logger;
        private readonly Func<DateTime> utcNow;
        private readonly List<StepRuntime> steps = new List<StepRuntime>();
        private readonly Dictionary<string, StepRuntime> stepsById = new Dictionary<string, StepRuntime>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> stepGroups = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> handledEvents = new HashSet<string>(StringComparer.Ordinal);
        private int groupIndex = -1;
        private int batchIndex;
        private int sequence;
        private bool started;

        public IReadOnlyList<StepRuntime> Steps { get; }
        IReadOnlyList<IStepProgress> IScenarioExecutor.Steps => Steps;
        public Journal Journal => logger.Journal;
        public bool IsFinished { get; private set; }
        public string CurrentGroupId => groupIndex >= 0 && groupIndex < plan.Groups.Count ? plan.Groups[groupIndex].Id : null;
        public int CurrentGroupViolations { get; private set; }
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
            stopGroupAtThreshold = policy.StopGroupAtThreshold;
            var conditions = new HashSet<IStepCondition>();
            foreach (var spec in this.plan.Steps)
            {
                var step = new StepRuntime(spec);
                if (!conditions.Add(step.Condition))
                    throw new ArgumentException("Condition factories must create independent instances for each step.", nameof(plan));
                steps.Add(step);
                stepsById.Add(spec.Id, step);
            }
            foreach (var group in this.plan.Groups)
                foreach (var id in group.StepIds) stepGroups.Add(id, group.Id);
            Steps = steps.AsReadOnly();
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
                    GroupId = stepGroups[step.Spec.Id]
                });
            foreach (var group in plan.Groups)
                journal.Groups.Add(new GroupSnapshot
                {
                    Id = group.Id,
                    Title = group.Title,
                    ViolationThreshold = group.ViolationThreshold,
                    StepIds = new List<string>(group.StepIds)
                });
            // Loggers receive a complete run snapshot before the first event is written.
            logger.Begin(journal);
            started = true;
            groupIndex = 0;
            Append(JournalEvents.SessionStarted, null, "Started", "Сценарий начат.");
            ActivateBatch();
            LastMessage = "Сценарий начат.";
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
                Append(JournalEvents.ActionIgnored, null, "Ignored", LastMessage, action);
                Changed?.Invoke();
                return;
            }
            if (string.IsNullOrWhiteSpace(action.Signal))
            {
                LastMessage = "Пустой сигнал проигнорирован.";
                Append(JournalEvents.ActionIgnored, step, step.Status.ToString(), LastMessage, action);
                Changed?.Invoke();
                return;
            }
            if (IsTerminal(step.Status))
            {
                Append(JournalEvents.ActionIgnored, step, step.Status.ToString(), "Шаг уже завершён.", action);
                return;
            }
            if (step.Status == StepStatus.Pending)
            {
                if (plan.OutOfOrderPolicy == OutOfOrderPolicy.Ignore) return;
                step.Violations++;
                CurrentGroupViolations++;
                var group = plan.Groups[groupIndex];
                var count = CurrentGroupViolations.ToString(CultureInfo.InvariantCulture);
                if (stopGroupAtThreshold) count += " из " + group.ViolationThreshold;
                LastMessage = "Шаг " + StepLabel(step) + " ещё ожидает своей очереди. " +
                    "Действие вне очереди засчитано текущей группе " + GroupLabel(group) +
                    " (нарушений: " + count + "). Попытка шага не начата.";
                Append(JournalEvents.Violation, step, StepStatus.Pending.ToString(), LastMessage, action, "out-of-order");
                CheckThreshold(step, action, "Шаг ещё ожидает своей очереди.");
                Changed?.Invoke();
                return;
            }

            // Evaluate only the explicitly addressed step. Advancing a batch never replays this input.
            var result = step.Condition.Evaluate(action);
            if (result.Kind == ConditionOutcome.Ignore) return;
            if (!step.HasOpenAttempt)
            {
                step.Attempts++;
                step.HasOpenAttempt = true;
                Append(JournalEvents.AttemptStarted, step, StepStatus.Active.ToString(), "Начата попытка.", action);
            }
            switch (result.Kind)
            {
                case ConditionOutcome.Progress:
                    step.ProgressMessage = result.Message;
                    LastMessage = result.Message;
                    Append(JournalEvents.ConditionProgress, step, StepStatus.Active.ToString(), result.Message, action);
                    break;
                case ConditionOutcome.Success:
                    step.Status = StepStatus.Completed;
                    step.HasOpenAttempt = false;
                    step.ProgressMessage = string.IsNullOrWhiteSpace(step.Spec.Result) ? "Шаг выполнен." : step.Spec.Result;
                    LastMessage = step.ProgressMessage;
                    Append(JournalEvents.StepCompleted, step, step.Status.ToString(), step.ProgressMessage, action);
                    Advance();
                    break;
                case ConditionOutcome.Violation:
                    step.Violations++;
                    CurrentGroupViolations++;
                    step.HasOpenAttempt = false;
                    step.ProgressMessage = string.IsNullOrWhiteSpace(result.Message) ? "Условие шага нарушено." : result.Message;
                    LastMessage = step.ProgressMessage;
                    Append(JournalEvents.Violation, step, step.Status.ToString(), step.ProgressMessage, action, "condition");
                    if (retryOnViolation) step.Condition.Reset();
                    else
                    {
                        step.Status = StepStatus.Failed;
                        Append(JournalEvents.StepFailed, step, step.Status.ToString(), step.ProgressMessage, action);
                    }
                    if (!CheckThreshold(step, action, step.ProgressMessage)) Advance();
                    break;
                default:
                    throw new InvalidOperationException("A condition returned an unknown outcome.");
            }
            Changed?.Invoke();
        }

        private bool CheckThreshold(StepRuntime causeStep, ScenarioAction action, string cause)
        {
            if (!stopGroupAtThreshold || CurrentGroupViolations < plan.Groups[groupIndex].ViolationThreshold) return false;
            var group = plan.Groups[groupIndex];
            var message = "В группе " + GroupLabel(group) + " достигнут порог нарушений " +
                CurrentGroupViolations + "/" + group.ViolationThreshold + ". " +
                "Причина последнего нарушения: " + ActionLabel(action) + " для шага " + StepLabel(causeStep) +
                ". " + cause + " Поэтому незавершённые шаги этой группы пропущены.";
            foreach (var id in group.StepIds)
            {
                var step = stepsById[id];
                if (IsTerminal(step.Status)) continue;
                step.Status = StepStatus.Skipped;
                step.HasOpenAttempt = false;
                step.ProgressMessage = message;
                Append(JournalEvents.StepSkipped, step, step.Status.ToString(), message);
            }
            LastMessage = message;
            Advance();
            return true;
        }

        private static string StepLabel(StepRuntime step)
        {
            return step.Spec.Id + (string.IsNullOrWhiteSpace(step.Spec.Title) ? string.Empty : " «" + step.Spec.Title + "»");
        }

        private static string GroupLabel(GroupPlan group)
        {
            return group.Id + (string.IsNullOrWhiteSpace(group.Title) ? string.Empty : " «" + group.Title + "»");
        }

        private static string ActionLabel(ScenarioAction action)
        {
            if (action.Signal == "error") return "имитация ошибки («Ошибка»)";
            if (action.Signal == "confirm") return "подтверждение";
            return "сигнал «" + action.Signal + "»";
        }

        private void Advance()
        {
            while (groupIndex < plan.Groups.Count)
            {
                var group = plan.Groups[groupIndex];
                var batch = group.Batches[batchIndex];
                foreach (var id in batch.StepIds)
                    if (!IsTerminal(stepsById[id].Status)) return;
                batchIndex++;
                if (batchIndex < group.Batches.Count)
                {
                    ActivateBatch();
                    continue;
                }
                groupIndex++;
                if (groupIndex == plan.Groups.Count)
                {
                    IsFinished = true;
                    LastMessage = "Сценарий завершён.";
                    Append(JournalEvents.SessionCompleted, null, "Completed", LastMessage);
                    logger.Finish();
                    return;
                }
                batchIndex = 0;
                CurrentGroupViolations = 0;
                ActivateBatch();
            }
        }

        private void ActivateBatch()
        {
            foreach (var id in plan.Groups[groupIndex].Batches[batchIndex].StepIds)
            {
                var step = stepsById[id];
                if (IsTerminal(step.Status)) continue;
                step.Status = StepStatus.Active;
                step.Condition.Reset();
                Append(JournalEvents.StepActivated, step, StepStatus.Active.ToString(), "Шаг активирован.");
            }
        }

        private void Append(string type, StepRuntime step, string result, string message, ScenarioAction action = null, string reason = null)
        {
            var entry = new JournalEntry
            {
                Sequence = ++sequence,
                TimestampUtc = Timestamp(),
                StepId = step?.Spec.Id,
                GroupId = CurrentGroupId,
                Type = type,
                Result = result,
                Attempt = step?.Attempts ?? 0,
                Violations = step?.Violations ?? 0,
                Message = message
            };
            if (step != null) entry.Extra.Add(new JournalField("targetGroupId", stepGroups[step.Spec.Id]));
            entry.Extra.Add(new JournalField("groupViolations", CurrentGroupViolations.ToString(CultureInfo.InvariantCulture)));
            if (reason != null) entry.Extra.Add(new JournalField("reason", reason));
            if (action != null)
            {
                entry.Extra.Add(new JournalField("eventId", action.EventId));
                entry.Extra.Add(new JournalField("signal", action.Signal));
            }
            logger.Write(entry);
        }

        private string Timestamp() => utcNow().ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

        private static bool IsTerminal(StepStatus status)
        {
            return status == StepStatus.Completed || status == StepStatus.Failed || status == StepStatus.Skipped;
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
                    StepIds = new List<string>(group.StepIds)
                };
                foreach (var batch in group.Batches)
                    copy.Batches.Add(new ActivationBatch { StepIds = new List<string>(batch.StepIds) });
                snapshot.Groups.Add(copy);
            }
            return snapshot;
        }
    }
}
