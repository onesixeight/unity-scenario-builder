using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ScenarioBuilder.Core;

namespace ScenarioBuilder.Tests
{
    public sealed class OverlapSessionTests
    {
        [Test]
        public void AtStartGroupsRunTogetherAndSessionWaitsForAllGroups()
        {
            var session = Start(Plan(
                Group("first", GroupStartRule.AtStart, null, new ActivationBatch("a1", "a2")),
                Group("second", GroupStartRule.AtStart, null, new ActivationBatch("b1", "b2"))));
            Assert.That(session.Groups.Select(group => group.Status), Is.EqualTo(new[] { GroupStatus.Active, GroupStatus.Active }));
            Assert.That(session.Steps.All(step => step.Status == StepStatus.Active), Is.True);
            Assert.That(session.CurrentGroupId, Is.EqualTo("first"));
            Send(session, "a2");
            Send(session, "a1");
            Assert.That(session.Groups[0].Status, Is.EqualTo(GroupStatus.Completed));
            Assert.That(session.Groups[1].Status, Is.EqualTo(GroupStatus.Active));
            Assert.That(session.IsFinished, Is.False);
            Assert.That(session.CurrentGroupId, Is.EqualTo("second"));
            Send(session, "b2");
            Send(session, "b1");
            Assert.That(session.IsFinished, Is.True);
            Assert.That(session.CurrentGroupId, Is.Null);
            Assert.That(session.Journal.Entries.Count(entry => entry.Type == JournalEvents.SessionCompleted), Is.EqualTo(1));
            Assert.That(session.Journal.Entries.Count, Is.EqualTo(14), "Group progress does not add new lifecycle record kinds.");
        }

        [Test]
        public void AfterStepStartsBeforeItsOwnerClosesAndDoesNotReplayTheTriggerInput()
        {
            var session = Start(Plan(
                Group("owner", GroupStartRule.AtStart, null, new ActivationBatch("trigger", "remaining")),
                Group("dependent", GroupStartRule.AfterStep, "trigger", new ActivationBatch("next"))));
            Assert.That(Step(session, "next").Status, Is.EqualTo(StepStatus.Pending));
            Send(session, "trigger");
            Assert.That(session.Groups.All(group => group.Status == GroupStatus.Active), Is.True);
            Assert.That(Step(session, "remaining").Status, Is.EqualTo(StepStatus.Active));
            Assert.That(Step(session, "next").Status, Is.EqualTo(StepStatus.Active));
            Assert.That(Step(session, "next").Attempts, Is.Zero);
            Send(session, "next");
            Assert.That(session.IsFinished, Is.False);
            Assert.That(session.Groups[1].Status, Is.EqualTo(GroupStatus.Completed));
            Send(session, "remaining");
            Assert.That(session.IsFinished, Is.True);
        }

        [Test]
        public void ConcurrentViolationsAndThresholdsStayInsideTheirOwningGroups()
        {
            var session = Start(Plan(
                Group("first", GroupStartRule.AtStart, null, new ActivationBatch("a1", "a2", "a3")),
                Group("second", GroupStartRule.AtStart, null, new ActivationBatch("b1", "b2", "b3"))));
            Send(session, "b1", "bad");
            Assert.That(session.Groups[0].Violations, Is.Zero);
            Assert.That(session.Groups[1].Violations, Is.EqualTo(1));
            Send(session, "a1", "bad");
            Send(session, "b2", "bad");
            Assert.That(session.Groups[0].Violations, Is.EqualTo(1));
            Assert.That(session.Groups[0].Status, Is.EqualTo(GroupStatus.Active));
            Assert.That(session.Groups[1].Status, Is.EqualTo(GroupStatus.Skipped));
            Assert.That(Step(session, "b3").Status, Is.EqualTo(StepStatus.Skipped));
            Assert.That(Step(session, "a2").Status, Is.EqualTo(StepStatus.Active));
            Assert.That(Step(session, "a3").Status, Is.EqualTo(StepStatus.Active));
            Assert.That(session.IsFinished, Is.False);
            var bViolations = session.Journal.Entries.Where(entry => entry.Type == JournalEvents.Violation && entry.StepId.StartsWith("b")).ToList();
            Assert.That(bViolations.All(entry => entry.GroupId == "second"), Is.True);
            Send(session, "a2", "bad");
            Assert.That(Step(session, "a3").Status, Is.EqualTo(StepStatus.Skipped));
            Assert.That(session.Groups.Select(group => group.Violations), Is.EqualTo(new[] { 2, 2 }));
            Assert.That(session.IsFinished, Is.True);
            var result = new ExamResultAnalyzer().Analyze(session.Journal);
            Assert.That(result.IsValid, Is.True, string.Join("\n", result.Diagnostics));
            Assert.That(result.Score, Is.EqualTo(50));
        }

        [Test]
        public void WaitingBatchViolationUsesItsActiveOwnerRatherThanTheFirstActiveGroup()
        {
            var session = Start(Plan(
                Group("first", GroupStartRule.AtStart, null, new ActivationBatch("a1"), new ActivationBatch("a2")),
                Group("second", GroupStartRule.AtStart, null, new ActivationBatch("b1"), new ActivationBatch("b2"))));
            Send(session, "b2", "bad");
            Assert.That(session.CurrentGroupId, Is.EqualTo("first"));
            Assert.That(session.CurrentGroupViolations, Is.Zero);
            Assert.That(session.Groups[1].Violations, Is.EqualTo(1));
            Assert.That(Step(session, "b2").Status, Is.EqualTo(StepStatus.Failed));
            Assert.That(session.Journal.Entries.Single(entry => entry.Type == JournalEvents.Violation).GroupId, Is.EqualTo("second"));
            Assert.That(Step(session, "a1").Status, Is.EqualTo(StepStatus.Active));
            Assert.That(Step(session, "b1").Status, Is.EqualTo(StepStatus.Active));
        }

        [Test]
        public void PrematureFutureFailureDoesNotTriggerDependentUntilTheOwnerHasStarted()
        {
            var session = Start(Plan(
                Group("current", GroupStartRule.AtStart, null, new ActivationBatch("a1", "a2")),
                Group("future", GroupStartRule.AfterPreviousGroup, null, new ActivationBatch("trigger"), new ActivationBatch("remaining")),
                Group("dependent", GroupStartRule.AfterStep, "trigger", new ActivationBatch("next"))));
            Send(session, "trigger", "bad");
            Assert.That(Step(session, "trigger").Status, Is.EqualTo(StepStatus.Failed));
            Assert.That(session.Groups[0].Violations, Is.EqualTo(1));
            Assert.That(session.Groups[1].Violations, Is.Zero);
            Assert.That(session.Groups[1].Status, Is.EqualTo(GroupStatus.Pending));
            Assert.That(session.Groups[2].Status, Is.EqualTo(GroupStatus.Pending));
            Send(session, "a1");
            Send(session, "a2");
            Assert.That(session.Groups[1].Status, Is.EqualTo(GroupStatus.Active));
            Assert.That(session.Groups[2].Status, Is.EqualTo(GroupStatus.Active));
            Assert.That(Step(session, "remaining").Status, Is.EqualTo(StepStatus.Active));
            Assert.That(Step(session, "next").Status, Is.EqualTo(StepStatus.Active));
            Assert.That(session.IsFinished, Is.False);
            Send(session, "next");
            Assert.That(session.IsFinished, Is.False);
            Send(session, "remaining");
            Assert.That(session.IsFinished, Is.True);
        }

        [Test]
        public void FailedActiveTriggerStillOpensItsDependentGroup()
        {
            var session = Start(Plan(
                Group("owner", GroupStartRule.AtStart, null, new ActivationBatch("trigger", "remaining")),
                Group("dependent", GroupStartRule.AfterStep, "trigger", new ActivationBatch("next"))));
            Send(session, "trigger", "bad");
            Assert.That(Step(session, "trigger").Status, Is.EqualTo(StepStatus.Failed));
            Assert.That(Step(session, "remaining").Status, Is.EqualTo(StepStatus.Active));
            Assert.That(Step(session, "next").Status, Is.EqualTo(StepStatus.Active));
            Assert.That(session.IsFinished, Is.False);
        }

        [Test]
        public void SkippedTriggerOpensItsDependentAfterTheOwnerStopsAtThreshold()
        {
            var owner = Group("owner", GroupStartRule.AtStart, null,
                new ActivationBatch("error", "parallel"), new ActivationBatch("trigger"));
            owner.ViolationThreshold = 1;
            var session = Start(Plan(owner,
                Group("dependent", GroupStartRule.AfterStep, "trigger", new ActivationBatch("next"))));
            Send(session, "error", "bad");
            Assert.That(session.Groups[0].Status, Is.EqualTo(GroupStatus.Skipped));
            Assert.That(Step(session, "parallel").Status, Is.EqualTo(StepStatus.Skipped));
            Assert.That(Step(session, "trigger").Status, Is.EqualTo(StepStatus.Skipped));
            Assert.That(Step(session, "next").Status, Is.EqualTo(StepStatus.Active));
            Send(session, "next");
            Assert.That(session.IsFinished, Is.True);
        }

        [Test]
        public void AcyclicForwardTriggerReferenceIsValidAndStartsTheCorrectRoot()
        {
            var plan = Plan(
                Group("dependent", GroupStartRule.AfterStep, "trigger", new ActivationBatch("next")),
                Group("root", GroupStartRule.AtStart, null, new ActivationBatch("trigger")));
            Assert.That(plan.Validate(), Is.Empty);
            var session = Start(plan);
            Assert.That(session.CurrentGroupId, Is.EqualTo("root"));
            Assert.That(Step(session, "next").Status, Is.EqualTo(StepStatus.Pending));
            Send(session, "trigger");
            Assert.That(session.CurrentGroupId, Is.EqualTo("dependent"));
            Assert.That(Step(session, "next").Status, Is.EqualTo(StepStatus.Active));
            Send(session, "next");
            Assert.That(session.IsFinished, Is.True);
        }

        [Test]
        public void StartRulesAndTriggerIdsAreCopiedIntoTheRunAndJournalSnapshot()
        {
            var plan = Plan(
                Group("dependent", GroupStartRule.AfterStep, "trigger", new ActivationBatch("next")),
                Group("root", GroupStartRule.AtStart, null, new ActivationBatch("trigger")));
            var session = new ScenarioSession(plan, new ExamExecutionPolicy(), new MemoryJournalLogger());
            plan.Groups[0].StartRule = GroupStartRule.AtStart;
            plan.Groups[0].StartAfterStepId = null;
            plan.Groups[1].StartRule = GroupStartRule.AfterStep;
            plan.Groups[1].StartAfterStepId = "next";
            session.Start();
            Assert.That(session.Groups[0].Status, Is.EqualTo(GroupStatus.Pending));
            Assert.That(session.Groups[1].Status, Is.EqualTo(GroupStatus.Active));
            Assert.That(session.Journal.Groups[0].ActivationRule, Is.EqualTo("AfterStep"));
            Assert.That(session.Journal.Groups[0].AfterStepId, Is.EqualTo("trigger"));
            Assert.That(session.Journal.Groups[1].ActivationRule, Is.EqualTo("AtStart"));
            Send(session, "trigger");
            Assert.That(Step(session, "next").Status, Is.EqualTo(StepStatus.Active));
        }

        [Test]
        public void SelfUnknownAndCyclicGroupDependenciesAreRejected()
        {
            var self = Plan(Group("self", GroupStartRule.AfterStep, "a", new ActivationBatch("a")));
            Assert.That(self.Validate().Any(error => error.Contains("self dependency")), Is.True);
            var unknown = Plan(Group("unknown", GroupStartRule.AfterStep, "not-a-step", new ActivationBatch("a")));
            Assert.That(unknown.Validate().Any(error => error.Contains("unknown or disabled")), Is.True);
            var cycle = Plan(
                Group("first", GroupStartRule.AfterStep, "b", new ActivationBatch("a")),
                Group("second", GroupStartRule.AfterStep, "a", new ActivationBatch("b")));
            Assert.That(cycle.Validate().Any(error => error.Contains("cycle")), Is.True);
            var mixed = Plan(
                Group("first", GroupStartRule.AfterStep, "b", new ActivationBatch("a")),
                Group("second", GroupStartRule.AfterPreviousGroup, null, new ActivationBatch("b")));
            Assert.That(mixed.Validate().Any(error => error.Contains("cycle")), Is.True);
            Assert.Throws<ArgumentException>(() => new ScenarioSession(cycle, new ExamExecutionPolicy(), new MemoryJournalLogger()));
        }

        [Test]
        public void ThresholdOnTheLastFailedStepClosesWithoutClaimingSkippedSteps()
        {
            var group = Group("single", GroupStartRule.AtStart, null, new ActivationBatch("a"));
            group.ViolationThreshold = 1;
            var session = Start(Plan(group,
                Group("remaining", GroupStartRule.AtStart, null, new ActivationBatch("b"))));
            Send(session, "a", "bad");
            Assert.That(session.Groups[0].Status, Is.EqualTo(GroupStatus.Completed));
            Assert.That(session.Groups[0].Violations, Is.EqualTo(1));
            Assert.That(session.Journal.Entries.Any(entry => entry.Type == JournalEvents.StepSkipped), Is.False);
            Assert.That(session.LastMessage, Does.Contain("Незавершённых шагов в группе нет"));
            Assert.That(session.LastMessage, Does.Not.Contain("пропущены"));
            Assert.That(session.Groups[1].Status, Is.EqualTo(GroupStatus.Active));
            Assert.That(session.IsFinished, Is.False);
            Send(session, "b");
            Assert.That(session.IsFinished, Is.True);
        }

        [Test]
        public void ReverseOrderedPrefailedGroupChainUnlocksInOneSchedulerPass()
        {
            var root = Group("root", GroupStartRule.AtStart, null, new ActivationBatch("gate", "remaining"));
            root.ViolationThreshold = 10;
            var session = Start(Plan(
                Group("leaf", GroupStartRule.AfterStep, "middle-step", new ActivationBatch("leaf-step")),
                Group("middle", GroupStartRule.AfterStep, "owner-step", new ActivationBatch("middle-step")),
                Group("owner", GroupStartRule.AfterStep, "gate", new ActivationBatch("owner-step")),
                root));
            Send(session, "leaf-step", "bad");
            Send(session, "middle-step", "bad");
            Send(session, "owner-step", "bad");
            Assert.That(session.Groups.Take(3).All(group => group.Status == GroupStatus.Pending), Is.True,
                "A terminal future trigger must wait until its owning group starts.");
            Assert.That(session.Groups[3].Violations, Is.EqualTo(3));
            var beforeGate = session.Journal.Entries.Count;

            Send(session, "gate");

            Assert.That(session.Groups.Take(3).All(group => group.Status == GroupStatus.Completed), Is.True);
            Assert.That(session.Steps.Take(3).All(step => step.Status == StepStatus.Failed && step.Attempts == 1), Is.True);
            Assert.That(session.Journal.Entries.Count, Is.EqualTo(beforeGate + 2),
                "Unlocking and closing prefailed groups does not synthesize extra step actions or lifecycle records.");
            Assert.That(session.Groups[3].Status, Is.EqualTo(GroupStatus.Active));
            Assert.That(Step(session, "remaining").Status, Is.EqualTo(StepStatus.Active));
            Assert.That(session.IsFinished, Is.False);
            Send(session, "remaining");
            Assert.That(session.IsFinished, Is.True);
            Assert.That(new ExamResultAnalyzer().Analyze(session.Journal).Score, Is.EqualTo(70));
        }

        [Test]
        public void ReplayedEventAfterThresholdCannotAffectTheNewlyOpenedGroup()
        {
            var first = Group("first", GroupStartRule.AfterPreviousGroup, null, new ActivationBatch("a1", "a2"));
            first.ViolationThreshold = 1;
            var session = Start(Plan(first,
                Group("second", GroupStartRule.AfterPreviousGroup, null, new ActivationBatch("b1", "b2"))));
            var action = new ScenarioAction("a1", "bad", "one-physical-callback");
            session.Handle(action);
            Assert.That(session.Groups[0].Status, Is.EqualTo(GroupStatus.Skipped));
            Assert.That(session.Groups[1].Status, Is.EqualTo(GroupStatus.Active));
            var entries = session.Journal.Entries.Count;
            var attempts = session.Steps.Select(step => step.Attempts).ToArray();
            var violations = session.Groups.Select(group => group.Violations).ToArray();

            session.Handle(action);
            session.Handle(new ScenarioAction("b1", "bad", "one-physical-callback"));

            Assert.That(session.Journal.Entries.Count, Is.EqualTo(entries));
            Assert.That(session.Steps.Select(step => step.Attempts), Is.EqualTo(attempts));
            Assert.That(session.Groups.Select(group => group.Violations), Is.EqualTo(violations));
            Assert.That(Step(session, "b1").Status, Is.EqualTo(StepStatus.Active));
            Assert.That(Step(session, "b1").Violations, Is.Zero);
            Send(session, "b1");
            Send(session, "b2");
            Assert.That(session.IsFinished, Is.True);
            Assert.That(new ExamResultAnalyzer().Analyze(session.Journal).Score, Is.EqualTo(85));
        }

        private static ScenarioSession Start(ScenarioPlan plan)
        {
            var session = new ScenarioSession(plan, new ExamExecutionPolicy(), new MemoryJournalLogger());
            session.Start();
            return session;
        }

        private static GroupPlan Group(string id, GroupStartRule rule, string afterStep, params ActivationBatch[] batches)
        {
            var group = new GroupPlan { Id = id, Title = id, StartRule = rule, StartAfterStepId = afterStep };
            foreach (var batch in batches)
            {
                group.Batches.Add(batch);
                group.StepIds.AddRange(batch.StepIds);
            }
            return group;
        }

        private static ScenarioPlan Plan(params GroupPlan[] groups)
        {
            var plan = new ScenarioPlan();
            foreach (var group in groups)
            {
                plan.Groups.Add(group);
                foreach (var id in group.StepIds)
                    plan.Steps.Add(new StepSpec(id, id, "Done", () => new SignalCondition("ok", "bad")));
            }
            return plan;
        }

        private static StepRuntime Step(ScenarioSession session, string id) => session.Steps.Single(step => step.Spec.Id == id);
        private static void Send(ScenarioSession session, string id, string signal = "ok") => session.Handle(new ScenarioAction(id, signal));
    }
}
