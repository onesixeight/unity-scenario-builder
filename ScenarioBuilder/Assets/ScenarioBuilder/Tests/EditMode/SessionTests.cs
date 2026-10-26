using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ScenarioBuilder.Core;

namespace ScenarioBuilder.Tests
{
    public sealed class SessionTests
    {
        [Test]
        public void TrainingRecordsFirstSecondAndThirdAttemptsForFifteenPoints()
        {
            var session = Start(Plan(3), new TrainingExecutionPolicy());
            Send(session, "s1", "ok");
            Send(session, "s2", "bad");
            Send(session, "s2", "ok");
            Send(session, "s3", "bad");
            Send(session, "s3", "bad");
            Send(session, "s3", "ok");

            Assert.That(session.IsFinished, Is.True);
            Assert.That(session.Steps.Select(step => step.Attempts), Is.EqualTo(new[] { 1, 2, 3 }));
            var completed = session.Journal.Entries.Where(entry => entry.Type == JournalEvents.StepCompleted).ToList();
            Assert.That(completed.Select(entry => entry.Attempt), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(completed.Sum(entry => entry.Attempt == 1 ? 10 : entry.Attempt == 2 ? 5 : 0), Is.EqualTo(15));
            Assert.That(session.Steps.All(step => step.Status == StepStatus.Completed), Is.True);
        }

        [Test]
        public void ExamTwoFailuresSkipTwoRemainingStepsAndProvideSeventyPointJournal()
        {
            var session = Start(Plan(4), new ExamExecutionPolicy());
            Send(session, "s1", "bad");
            Assert.That(session.Steps[1].Status, Is.EqualTo(StepStatus.Active));
            Send(session, "s2", "bad");

            Assert.That(session.IsFinished, Is.True);
            Assert.That(session.Steps.Select(step => step.Status), Is.EqualTo(new[]
            {
                StepStatus.Failed, StepStatus.Failed, StepStatus.Skipped, StepStatus.Skipped
            }));
            var terminal = session.Journal.Entries.Where(entry =>
                entry.Type == JournalEvents.StepFailed || entry.Type == JournalEvents.StepSkipped).ToList();
            Assert.That(100 - terminal.Sum(entry => entry.Type == JournalEvents.StepFailed ? 10 : 5), Is.EqualTo(70));
            Assert.That(terminal.Where(entry => entry.Result == "Failed").All(entry => entry.Attempt == 1), Is.True);
            Assert.That(terminal.Where(entry => entry.Result == "Skipped").All(entry => entry.Attempt == 0), Is.True);
            foreach (var skipped in terminal.Where(entry => entry.Type == JournalEvents.StepSkipped))
            {
                Assert.That(skipped.Message, Does.Contain("группе group"));
                Assert.That(skipped.Message, Does.Contain("2/2"));
                Assert.That(skipped.Message, Does.Contain("s2"));
                Assert.That(skipped.Message, Does.Contain("bad"));
            }
        }

        [TestCase(OutOfOrderPolicy.Violation, 1)]
        [TestCase(OutOfOrderPolicy.Ignore, 0)]
        public void WaitingStepRespectsOutOfOrderPolicyWithoutStartingItsAttempt(OutOfOrderPolicy policy, int violations)
        {
            var plan = Plan(2);
            plan.OutOfOrderPolicy = policy;
            var session = Start(plan, new TrainingExecutionPolicy());
            Send(session, "s2", "ok");

            Assert.That(session.Steps[0].Status, Is.EqualTo(StepStatus.Active));
            Assert.That(session.Steps[1].Status, Is.EqualTo(StepStatus.Pending));
            Assert.That(session.Steps[1].Attempts, Is.Zero);
            Assert.That(session.Steps[1].Violations, Is.EqualTo(violations));
            Assert.That(session.CurrentGroupViolations, Is.EqualTo(violations));
            Assert.That(session.Journal.Entries.Count(entry => entry.Type == JournalEvents.Violation), Is.EqualTo(violations));
        }

        [Test]
        public void FutureGroupActionCountsAgainstCurrentGroupAndDoesNotFailFutureStep()
        {
            var plan = Plan(3);
            plan.Groups = new List<GroupPlan>
            {
                Group("first", new[] { "s1", "s2" }, new ActivationBatch("s1"), new ActivationBatch("s2")),
                Group("second", new[] { "s3" }, new ActivationBatch("s3"))
            };
            var session = Start(plan, new ExamExecutionPolicy());
            Send(session, "s3", "ok");
            var violation = session.Journal.Entries.Single(entry => entry.Type == JournalEvents.Violation);
            Assert.That(violation.GroupId, Is.EqualTo("first"));
            Assert.That(violation.StepId, Is.EqualTo("s3"));
            Assert.That(violation.Attempt, Is.Zero);
            Assert.That(violation.Extra.Single(field => field.Key == "targetGroupId").Value, Is.EqualTo("second"));
            Assert.That(session.Steps[2].Status, Is.EqualTo(StepStatus.Pending));
            Assert.That(violation.Message, Does.Contain("s3"));
            Assert.That(violation.Message, Does.Contain("ожидает своей очереди"));
            Assert.That(violation.Message, Does.Contain("текущей группе first"));
            Assert.That(violation.Message, Does.Contain("Попытка шага не начата"));

            Send(session, "s3", "ok");
            Assert.That(session.Steps[0].Status, Is.EqualTo(StepStatus.Skipped));
            Assert.That(session.Steps[1].Status, Is.EqualTo(StepStatus.Skipped));
            Assert.That(session.Steps[2].Status, Is.EqualTo(StepStatus.Active));
            Assert.That(session.Steps[2].Attempts, Is.Zero);
            Assert.That(session.CurrentGroupId, Is.EqualTo("second"));
            Assert.That(session.CurrentGroupViolations, Is.Zero);
            foreach (var skipped in session.Journal.Entries.Where(entry => entry.Type == JournalEvents.StepSkipped))
            {
                Assert.That(skipped.Message, Does.Contain("группе first"));
                Assert.That(skipped.Message, Does.Contain("2/2"));
                Assert.That(skipped.Message, Does.Contain("s3"));
                Assert.That(skipped.Message, Does.Contain("ok"));
                Assert.That(skipped.Message, Does.Contain("ожидает своей очереди"));
            }
            Send(session, "s3", "ok");
            Assert.That(session.IsFinished, Is.True);
            Assert.That(session.Steps[2].Status, Is.EqualTo(StepStatus.Completed));
        }

        [Test]
        public void IgnoredFutureGroupActionsDoNotSkipTheCurrentGroup()
        {
            var plan = Plan(2);
            plan.OutOfOrderPolicy = OutOfOrderPolicy.Ignore;
            plan.Groups = new List<GroupPlan>
            {
                Group("first", new[] { "s1" }, new ActivationBatch("s1")),
                Group("second", new[] { "s2" }, new ActivationBatch("s2"))
            };
            var session = Start(plan, new ExamExecutionPolicy());
            for (var i = 0; i < 4; i++) Send(session, "s2", "ok");
            Assert.That(session.CurrentGroupId, Is.EqualTo("first"));
            Assert.That(session.CurrentGroupViolations, Is.Zero);
            Assert.That(session.Steps[1].Violations, Is.Zero);
        }

        [Test]
        public void CompletedAndFailedStepsIgnoreLaterActions()
        {
            var session = Start(Plan(3, true), new ExamExecutionPolicy());
            Send(session, "s1", "ok");
            Send(session, "s1", "bad");
            Send(session, "s2", "bad");
            Send(session, "s2", "bad");
            Send(session, "s2", "ok");

            Assert.That(session.Steps[0].Status, Is.EqualTo(StepStatus.Completed));
            Assert.That(session.Steps[0].Attempts, Is.EqualTo(1));
            Assert.That(session.Steps[0].Violations, Is.Zero);
            Assert.That(session.Steps[1].Status, Is.EqualTo(StepStatus.Failed));
            Assert.That(session.Steps[1].Attempts, Is.EqualTo(1));
            Assert.That(session.Steps[1].Violations, Is.EqualTo(1));
            Assert.That(session.CurrentGroupViolations, Is.EqualTo(1));
            Assert.That(session.Steps[2].Status, Is.EqualTo(StepStatus.Active));
        }

        [Test]
        public void EventIdIsProcessedOnlyOnce()
        {
            var session = Start(Plan(1), new TrainingExecutionPolicy());
            session.Handle(new ScenarioAction("s1", "bad", "one-callback"));
            session.Handle(new ScenarioAction("s1", "bad", "one-callback"));
            Assert.That(session.Steps[0].Attempts, Is.EqualTo(1));
            Assert.That(session.Steps[0].Violations, Is.EqualTo(1));
            Assert.That(session.Journal.Entries.Count(entry => entry.Type == JournalEvents.Violation), Is.EqualTo(1));
            Send(session, "s1", "ok");
            Assert.That(session.Steps[0].Attempts, Is.EqualTo(2));
        }

        [Test]
        public void OneInputDoesNotExecuteTheNextActivationBatch()
        {
            var session = Start(Plan(2), new TrainingExecutionPolicy());
            Send(session, "s1", "ok");
            Assert.That(session.Steps[1].Status, Is.EqualTo(StepStatus.Active));
            Assert.That(session.Steps[1].Attempts, Is.Zero);
            Assert.That(session.IsFinished, Is.False);
        }

        [Test]
        public void MixedActivationWaitsForTheWholeParallelBatch()
        {
            var plan = Plan(5);
            plan.Groups[0].Batches = new List<ActivationBatch>
            {
                new ActivationBatch("s1", "s2"), new ActivationBatch("s3"), new ActivationBatch("s4", "s5")
            };
            var session = Start(plan, new TrainingExecutionPolicy());
            Send(session, "s2", "ok");
            Assert.That(session.Steps[2].Status, Is.EqualTo(StepStatus.Pending));
            Send(session, "s1", "ok");
            Assert.That(session.Steps[2].Status, Is.EqualTo(StepStatus.Active));
            Send(session, "s3", "ok");
            Assert.That(session.Steps[3].Status, Is.EqualTo(StepStatus.Active));
            Assert.That(session.Steps[4].Status, Is.EqualTo(StepStatus.Active));
            Send(session, "s5", "ok");
            Send(session, "s4", "ok");
            Assert.That(session.IsFinished, Is.True);
        }

        [Test]
        public void OrderedConditionCountsTheWholeSequenceAsOneAttemptAndResetsAfterError()
        {
            var plan = Plan(1);
            plan.Steps[0] = new StepSpec("s1", "Ordered", "Done", () => new OrderedSignalCondition(new[] { "a", "b" }, new[] { "bad" }));
            var session = Start(plan, new TrainingExecutionPolicy());
            Send(session, "s1", "b");
            Assert.That(session.Steps[0].Attempts, Is.EqualTo(1));
            Send(session, "s1", "a");
            Assert.That(session.Steps[0].Attempts, Is.EqualTo(2));
            Assert.That(session.Steps[0].Status, Is.EqualTo(StepStatus.Active));
            Send(session, "s1", "b");
            Assert.That(session.Steps[0].Attempts, Is.EqualTo(2));
            Assert.That(session.Steps[0].Violations, Is.EqualTo(1));
            Assert.That(session.IsFinished, Is.True);
        }

        [Test]
        public void FailedTrainingAttemptPreservesOtherActiveStepProgress()
        {
            var plan = Plan(2, true);
            plan.Steps[0] = new StepSpec("s1", "First", "Done", () => new OrderedSignalCondition(new[] { "a", "b" }, new[] { "bad" }));
            plan.Steps[1] = new StepSpec("s2", "Second", "Done", () => new OrderedSignalCondition(new[] { "c", "d" }, new[] { "bad" }));
            var session = Start(plan, new TrainingExecutionPolicy());
            Send(session, "s1", "a");
            Send(session, "s2", "c");
            Send(session, "s1", "bad");
            Assert.That(session.Steps[1].Status, Is.EqualTo(StepStatus.Active));
            Assert.That(session.Steps[1].ProgressMessage, Does.Contain("1"));
            Send(session, "s2", "d");
            Assert.That(session.Steps[1].Status, Is.EqualTo(StepStatus.Completed));
            Assert.That(session.Steps[1].Attempts, Is.EqualTo(1));
            Send(session, "s1", "a");
            Send(session, "s1", "b");
            Assert.That(session.IsFinished, Is.True);
            Assert.That(session.Steps[0].Attempts, Is.EqualTo(2));
        }

        [Test]
        public void ContextConditionRequiresAnExplicitStateValue()
        {
            var plan = Plan(1);
            plan.Steps[0] = new StepSpec("s1", "Context", "Done", () => new StateSignalCondition("ok", "power", false, new[] { "bad" }));
            var session = Start(plan, new TrainingExecutionPolicy());
            Send(session, "s1", "ok");
            Assert.That(session.Steps[0].Violations, Is.EqualTo(1));
            var wrong = new ScenarioAction("s1", "ok");
            wrong.State.Add("power", true);
            session.Handle(wrong);
            Assert.That(session.Steps[0].Violations, Is.EqualTo(2));
            var correct = new ScenarioAction("s1", "ok");
            correct.State.Add("power", false);
            session.Handle(correct);
            Assert.That(session.IsFinished, Is.True);
            Assert.That(session.Steps[0].Attempts, Is.EqualTo(3));
        }

        [Test]
        public void ExplicitErrorSignalIsExplainedAsSimulationRatherThanSequenceOrContextFailure()
        {
            IStepCondition[] conditions =
            {
                new SignalCondition("ok", "error"),
                new OrderedSignalCondition(new[] { "a", "b" }, "error"),
                new StateSignalCondition("ok", "custom.state", true, "error")
            };
            foreach (var condition in conditions)
            {
                var result = condition.Evaluate(new ScenarioAction("s1", "error"));
                Assert.That(result.Kind, Is.EqualTo(ConditionOutcome.Violation));
                Assert.That(result.Message, Does.Contain("Имитация ошибки"));
                Assert.That(result.Message, Does.Not.Contain("Нарушен порядок"));
                Assert.That(result.Message, Does.Not.Contain("не передано"));
            }
            var customFailure = new SignalCondition("ok", "cancel").Evaluate(new ScenarioAction("s1", "cancel"));
            Assert.That(customFailure.Message, Does.Contain("cancel"));
            Assert.That(customFailure.Message, Does.Not.Contain("Имитация"));
        }

        [Test]
        public void ConfiguredContextFailureMessageIsSavedWithTheFailedStep()
        {
            const string explanation = "Не выполнено условие безопасности. Сначала включите защиту.";
            var plan = Plan(1);
            plan.Steps[0] = new StepSpec("s1", "Context", "Done",
                () => new StateSignalCondition("ok", "custom.guard", true, new[] { "error" }, explanation));
            var session = Start(plan, new ExamExecutionPolicy());
            var action = new ScenarioAction("s1", "ok");
            action.State["custom.guard"] = false;
            session.Handle(action);

            Assert.That(session.Steps[0].Status, Is.EqualTo(StepStatus.Failed));
            Assert.That(session.Steps[0].Attempts, Is.EqualTo(1));
            Assert.That(session.Steps[0].ProgressMessage, Is.EqualTo(explanation));
            Assert.That(session.Journal.Entries.Single(entry => entry.Type == JournalEvents.StepFailed).Message,
                Is.EqualTo(explanation));
        }

        [Test]
        public void ContextWithoutCustomMessageExplainsTheActualAndRequiredValues()
        {
            var condition = new StateSignalCondition("ok", "custom.guard", false, new string[0]);
            var action = new ScenarioAction("s1", "ok");
            action.State["custom.guard"] = true;
            var result = condition.Evaluate(action);

            Assert.That(result.Kind, Is.EqualTo(ConditionOutcome.Violation));
            Assert.That(result.Message, Does.Contain("custom.guard"));
            Assert.That(result.Message, Does.Contain("требуется «нет»"));
            Assert.That(result.Message, Does.Contain("получено «да»"));
            action.State["custom.guard"] = false;
            Assert.That(condition.Evaluate(action).Kind, Is.EqualTo(ConditionOutcome.Success));
        }

        [Test]
        public void MissingContextStateIsExplainedAsMissingDataInsteadOfClaimingAnIncorrectValue()
        {
            const string wrongValueMessage = "Защита выключена.";
            var condition = new StateSignalCondition("ok", "custom.guard", true, new string[0], wrongValueMessage);
            var result = condition.Evaluate(new ScenarioAction("s1", "ok"));

            Assert.That(result.Kind, Is.EqualTo(ConditionOutcome.Violation));
            Assert.That(result.Message, Does.Contain("custom.guard"));
            Assert.That(result.Message, Does.Contain("не передано из сцены"));
            Assert.That(result.Message, Is.Not.EqualTo(wrongValueMessage));
        }

        [Test]
        public void RestartCreatesFreshConditionStateAttemptsAndJournal()
        {
            var plan = Plan(1);
            plan.Steps[0] = new StepSpec("s1", "Ordered", "Done", () => new OrderedSignalCondition(new[] { "a", "b" }, new[] { "bad" }));
            var first = Start(plan, new TrainingExecutionPolicy());
            Send(first, "s1", "a");
            Send(first, "s1", "bad");
            var second = Start(plan, new TrainingExecutionPolicy());
            Assert.That(second.Steps[0].Attempts, Is.Zero);
            Assert.That(second.Steps[0].Violations, Is.Zero);
            Assert.That(second.Steps[0].ProgressMessage, Is.Empty);
            Assert.That(second.Journal.SessionId, Is.Not.EqualTo(first.Journal.SessionId));
            Send(second, "s1", "b");
            Assert.That(second.Steps[0].Status, Is.EqualTo(StepStatus.Active));
            Assert.That(second.Steps[0].Violations, Is.EqualTo(1));
            Assert.That(first.Steps[0].Violations, Is.EqualTo(1));
        }

        [Test]
        public void InputAfterCompletionDoesNotChangeTheSavedJournal()
        {
            var logger = new TestLogger();
            var session = Start(Plan(1), new TrainingExecutionPolicy(), logger);
            Send(session, "s1", "ok");
            var count = session.Journal.Entries.Count;
            Send(session, "s1", "bad");
            Assert.That(session.Journal.Entries.Count, Is.EqualTo(count));
            Assert.That(logger.FinishCount, Is.EqualTo(1));
        }

        [Test]
        public void SessionSnapshotsConfigurationBeforeTheRun()
        {
            var plan = Plan(2);
            var session = new ScenarioSession(plan, new ExamExecutionPolicy(), new TestLogger());
            plan.Steps.Clear();
            plan.Groups[0].StepIds.Clear();
            plan.Groups[0].Batches.Clear();
            plan.Groups[0].ViolationThreshold = 20;
            session.Start();
            Assert.That(session.Journal.Steps.Count, Is.EqualTo(2));
            Assert.That(session.Journal.Groups[0].StepIds.Count, Is.EqualTo(2));
            Assert.That(session.Journal.Groups[0].ViolationThreshold, Is.EqualTo(2));
            Send(session, "s1", "bad");
            Send(session, "s2", "bad");
            Assert.That(session.IsFinished, Is.True);
        }

        [Test]
        public void LoggerBeginsWithCompleteSnapshotBeforeAnyEvents()
        {
            var logger = new TestLogger();
            var session = Start(Plan(2), new TrainingExecutionPolicy(), logger);
            Assert.That(logger.StepCountAtBegin, Is.EqualTo(2));
            Assert.That(logger.GroupCountAtBegin, Is.EqualTo(1));
            Assert.That(logger.EntryCountAtBegin, Is.Zero);
            Assert.That(session.Journal.Entries[0].Type, Is.EqualTo(JournalEvents.SessionStarted));
            Assert.That(session.Journal.Entries.Select(entry => entry.Sequence), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(session.Journal.Entries.All(entry => !string.IsNullOrWhiteSpace(entry.TimestampUtc)), Is.True);
        }

        [Test]
        public void UnrelatedOrMalformedSignalsDoNotStartAnAttempt()
        {
            var session = Start(Plan(1), new TrainingExecutionPolicy());
            Send(session, "s1", "unrelated");
            Send(session, "s1", "");
            Send(session, "unknown", "ok");
            session.Handle(null);
            Assert.That(session.Steps[0].Attempts, Is.Zero);
            Assert.That(session.Steps[0].Violations, Is.Zero);
            Assert.That(session.IsFinished, Is.False);
        }

        [Test]
        public void TrainingDoesNotSkipAfterManyViolations()
        {
            var session = Start(Plan(2, true), new TrainingExecutionPolicy());
            for (var i = 0; i < 5; i++) Send(session, "s1", "bad");
            Assert.That(session.Steps[0].Status, Is.EqualTo(StepStatus.Active));
            Assert.That(session.Steps[1].Status, Is.EqualTo(StepStatus.Active));
            Assert.That(session.Steps[0].Attempts, Is.EqualTo(5));
            Assert.That(session.Journal.Entries.Any(entry => entry.Type == JournalEvents.StepSkipped), Is.False);
        }

        [Test]
        public void PlanValidationReportsMembershipSchedulingAndConditionProblems()
        {
            var plan = Plan(2);
            plan.Steps.Add(new StepSpec("s1", "Duplicate", "Done", null));
            plan.Groups[0].StepIds.Add("missing");
            plan.Groups[0].Batches.RemoveAt(1);
            plan.Groups[0].ViolationThreshold = 0;
            var errors = plan.Validate();
            Assert.That(errors.Any(error => error.Contains("Duplicate step")), Is.True);
            Assert.That(errors.Any(error => error.Contains("condition factory")), Is.True);
            Assert.That(errors.Any(error => error.Contains("unknown step")), Is.True);
            Assert.That(errors.Any(error => error.Contains("does not activate")), Is.True);
            Assert.That(errors.Any(error => error.Contains("threshold")), Is.True);
            Assert.Throws<ArgumentException>(() => new ScenarioSession(plan, new TrainingExecutionPolicy(), new TestLogger()));
        }

        [Test]
        public void ValidationRejectsNullCollectionsEntriesAndFactoryResults()
        {
            var empty = new ScenarioPlan { Steps = null, Groups = null };
            Assert.That(empty.Validate(), Is.Not.Empty);
            var plan = Plan(1);
            plan.Steps[0] = new StepSpec("s1", "Missing condition", "Done", () => null);
            plan.Groups[0].Batches.Add(null);
            plan.Groups.Add(null);
            Assert.That(plan.Validate(), Is.Not.Empty);
            Assert.Throws<ArgumentException>(() => new ScenarioSession(plan, new ExamExecutionPolicy(), new TestLogger()));
        }

        [Test]
        public void ValidationRejectsDuplicateMembershipAndDuplicateActivation()
        {
            var plan = Plan(2);
            plan.Groups.Add(Group("other", new[] { "s1" }, new ActivationBatch("s1")));
            plan.Groups[0].Batches.Add(new ActivationBatch("s1"));
            Assert.That(plan.Validate().Any(error => error.Contains("more than one group")), Is.True);
            Assert.That(plan.Validate().Any(error => error.Contains("multiple activation")), Is.True);
        }

        [Test]
        public void ConditionsRejectEmptyAndConflictingSignals()
        {
            Assert.Throws<ArgumentException>(() => new SignalCondition("", new string[0]));
            Assert.Throws<ArgumentException>(() => new SignalCondition("ok", new[] { "ok" }));
            Assert.Throws<ArgumentException>(() => new OrderedSignalCondition(new string[0], new string[0]));
            Assert.Throws<ArgumentException>(() => new OrderedSignalCondition(new[] { "a", "b" }, new[] { "b" }));
            Assert.Throws<ArgumentException>(() => new StateSignalCondition("ok", "", true, new string[0]));
        }

        [Test]
        public void TrainingRejectsMultipleGroupsAndStartCannotReuseSessionState()
        {
            var plan = Plan(2);
            plan.Groups = new List<GroupPlan>
            {
                Group("first", new[] { "s1" }, new ActivationBatch("s1")),
                Group("second", new[] { "s2" }, new ActivationBatch("s2"))
            };
            Assert.Throws<ArgumentException>(() => new ScenarioSession(plan, new TrainingExecutionPolicy(), new TestLogger()));
            var session = Start(Plan(1), new TrainingExecutionPolicy());
            Assert.Throws<InvalidOperationException>(() => session.Start());
        }

        private static ScenarioSession Start(ScenarioPlan plan, IExecutionPolicy policy, TestLogger logger = null)
        {
            var session = new ScenarioSession(plan, policy, logger ?? new TestLogger(),
                () => new DateTime(2026, 10, 26, 8, 0, 0, DateTimeKind.Utc));
            session.Start();
            return session;
        }

        private static ScenarioPlan Plan(int count, bool parallel = false)
        {
            var plan = new ScenarioPlan();
            var group = new GroupPlan { Id = "group", Title = "Group" };
            for (var index = 1; index <= count; index++)
            {
                var id = "s" + index;
                plan.Steps.Add(new StepSpec(id, "Step " + index, "Done", () => new SignalCondition("ok", new[] { "bad" })));
                group.StepIds.Add(id);
                if (!parallel) group.Batches.Add(new ActivationBatch(id));
            }
            if (parallel) group.Batches.Add(new ActivationBatch { StepIds = new List<string>(group.StepIds) });
            plan.Groups.Add(group);
            return plan;
        }

        private static GroupPlan Group(string id, IEnumerable<string> ids, params ActivationBatch[] batches)
        {
            return new GroupPlan { Id = id, Title = id, StepIds = new List<string>(ids), Batches = new List<ActivationBatch>(batches) };
        }

        private static void Send(ScenarioSession session, string stepId, string signal)
        {
            session.Handle(new ScenarioAction(stepId, signal));
        }

        private sealed class TestLogger : IJournalLogger
        {
            public Journal Journal { get; private set; }
            public int StepCountAtBegin { get; private set; }
            public int GroupCountAtBegin { get; private set; }
            public int EntryCountAtBegin { get; private set; }
            public int FinishCount { get; private set; }

            public void Begin(Journal journal)
            {
                Journal = journal;
                StepCountAtBegin = journal.Steps.Count;
                GroupCountAtBegin = journal.Groups.Count;
                EntryCountAtBegin = journal.Entries.Count;
            }

            public void Write(JournalEntry entry) { Journal.Entries.Add(entry); }
            public void Finish() { FinishCount++; }
        }
    }
}
