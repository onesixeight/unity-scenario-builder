using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ScenarioBuilder.Core;
using UnityEngine;

namespace ScenarioBuilder.Tests
{
    public sealed class OverlapAnalysisTests
    {
        [Test]
        public void ConcurrentExamJournalScoresSeventyAndCanBeReanalyzedWithoutReplay()
        {
            var plan = Plan(
                Group("first", 2, new[] { "a1", "a2", "a3", "a4" }),
                Group("second", 2, new[] { "b1" }, new[] { "b2" }));
            var session = Start(plan, new ExamExecutionPolicy());
            Send(session, "b1", "ok");
            Send(session, "a1", "bad");
            Send(session, "a2", "bad");
            Assert.That(session.IsFinished, Is.False);
            Send(session, "b2", "ok");
            Assert.That(session.IsFinished, Is.True);

            var journal = session.Journal;
            var entries = journal.Entries.Count;
            var id = journal.SessionId;
            var exam = new ExamResultAnalyzer().Analyze(journal);
            var training = new TrainingResultAnalyzer().Analyze(journal);

            Assert.That(exam.IsValid, Is.True, string.Join("\n", exam.Diagnostics));
            Assert.That(exam.Score, Is.EqualTo(70));
            Assert.That(training.IsValid, Is.True, string.Join("\n", training.Diagnostics));
            Assert.That(training.Score, Is.EqualTo(20));
            Assert.That(training.Maximum, Is.EqualTo(60));
            Assert.That(session.Journal, Is.SameAs(journal));
            Assert.That(journal.SessionId, Is.EqualTo(id));
            Assert.That(journal.Entries.Count, Is.EqualTo(entries));
            Assert.That(journal.Groups.Select(group => group.ActivationRule),
                Is.EqualTo(new[] { "AtStart", "AtStart" }));
            Assert.That(journal.Entries.Where(entry => entry.Type == JournalEvents.StepSkipped)
                .All(entry => entry.GroupId == "first"), Is.True);
            Assert.That(journal.Entries.Where(entry => entry.Type == JournalEvents.StepCompleted)
                .All(entry => entry.GroupId == "second"), Is.True);
        }

        [Test]
        public void ConcurrentCustomRetryPolicyProducesFifteenTrainingPointsFromOneTwoThreeAttempts()
        {
            var session = Start(Plan(
                Group("first", 10, new[] { "a1", "a2" }),
                Group("second", 10, new[] { "b1" })), new RetryPolicy());
            Send(session, "a1", "ok");
            Send(session, "b1", "bad");
            Send(session, "a2", "bad");
            Send(session, "b1", "ok");
            Send(session, "a2", "bad");
            Send(session, "a2", "ok");

            Assert.That(session.IsFinished, Is.True);
            var journal = session.Journal;
            var entries = journal.Entries.Count;
            var training = new TrainingResultAnalyzer().Analyze(journal);
            var exam = new ExamResultAnalyzer().Analyze(journal);

            Assert.That(training.IsValid, Is.True, string.Join("\n", training.Diagnostics));
            Assert.That(training.Score, Is.EqualTo(15));
            Assert.That(training.Maximum, Is.EqualTo(30));
            Assert.That(training.Steps.Single(step => step.Id == "a1").Attempts, Is.EqualTo(1));
            Assert.That(training.Steps.Single(step => step.Id == "b1").Attempts, Is.EqualTo(2));
            Assert.That(training.Steps.Single(step => step.Id == "a2").Attempts, Is.EqualTo(3));
            Assert.That(exam.IsValid, Is.True, string.Join("\n", exam.Diagnostics));
            Assert.That(exam.Score, Is.EqualTo(100));
            Assert.That(session.Journal, Is.SameAs(journal));
            Assert.That(journal.Entries.Count, Is.EqualTo(entries));
        }

        [TestCase("Training")]
        [TestCase("Exam")]
        [TestCase("custom.executor")]
        public void SerializedOverlapMetadataDoesNotCoupleAnalyzersToTheRunMode(string mode)
        {
            var journal = MetadataJournal(mode);
            var restored = JsonUtility.FromJson<Journal>(JsonUtility.ToJson(journal));

            Assert.That(restored.SchemaVersion, Is.EqualTo(1));
            Assert.That(restored.Groups[0].ActivationRule, Is.EqualTo("AtStart"));
            Assert.That(restored.Groups[1].ActivationRule, Is.EqualTo("AfterStep"));
            Assert.That(restored.Groups[1].AfterStepId, Is.EqualTo("one"));
            var training = new TrainingResultAnalyzer().Analyze(restored);
            var exam = new ExamResultAnalyzer().Analyze(restored);
            Assert.That(training.IsValid, Is.True, string.Join("\n", training.Diagnostics));
            Assert.That(training.Score, Is.EqualTo(10));
            Assert.That(training.Maximum, Is.EqualTo(30));
            Assert.That(exam.IsValid, Is.True, string.Join("\n", exam.Diagnostics));
            Assert.That(exam.Score, Is.EqualTo(85));
        }

        [Test]
        public void UnknownFutureActivationMetadataRemainsOptionalForScoring()
        {
            var journal = MetadataJournal("custom.executor");
            journal.Groups[1].ActivationRule = "plugin.future-start-policy";
            journal.Groups[1].AfterStepId = "plugin-only-reference";
            var result = new ExamResultAnalyzer().Analyze(journal);
            Assert.That(result.IsValid, Is.True, string.Join("\n", result.Diagnostics));
            Assert.That(result.Score, Is.EqualTo(85));
        }

        [Test]
        public void LegacyJsonWithoutGroupActivationFieldsRemainsCompatible()
        {
            const string legacyJson = @"{
                ""SchemaVersion"":1,""SessionId"":""legacy"",""Mode"":""custom.executor"",
                ""Steps"":[{""Id"":""one"",""Title"":""One"",""GroupId"":""first""}],
                ""Groups"":[{""Id"":""first"",""Title"":""First"",""ViolationThreshold"":2,""StepIds"":[""one""]}],
                ""Entries"":[
                    {""Id"":""start"",""Type"":""session.started""},
                    {""Id"":""terminal"",""Type"":""step.completed"",""StepId"":""one"",""Result"":""Completed"",""Attempt"":1},
                    {""Id"":""finish"",""Type"":""session.completed""}
                ]
            }";
            var journal = JsonUtility.FromJson<Journal>(legacyJson);
            Assert.That(string.IsNullOrEmpty(journal.Groups[0].ActivationRule), Is.True);
            Assert.That(string.IsNullOrEmpty(journal.Groups[0].AfterStepId), Is.True);
            var training = new TrainingResultAnalyzer().Analyze(journal);
            var exam = new ExamResultAnalyzer().Analyze(journal);
            Assert.That(training.IsValid, Is.True, string.Join("\n", training.Diagnostics));
            Assert.That(training.Score, Is.EqualTo(10));
            Assert.That(exam.IsValid, Is.True, string.Join("\n", exam.Diagnostics));
            Assert.That(exam.Score, Is.EqualTo(100));
        }

        [Test]
        public void GroupActivationSnapshotIsIndependentOfLaterPlanEdits()
        {
            var plan = Plan(Group("first", 2, new[] { "a1" }), Group("second", 2, new[] { "b1" }));
            var session = new ScenarioSession(plan, new ExamExecutionPolicy(), new MemoryJournalLogger());
            plan.Groups[1].StartRule = GroupStartRule.AfterStep;
            plan.Groups[1].StartAfterStepId = "a1";
            session.Start();

            Assert.That(session.Steps.All(step => step.Status == StepStatus.Active), Is.True);
            var snapshot = session.Journal.Groups.Single(group => group.Id == "second");
            Assert.That(snapshot.ActivationRule, Is.EqualTo("AtStart"));
            Assert.That(string.IsNullOrEmpty(snapshot.AfterStepId), Is.True);
        }

        private static ScenarioPlan Plan(params GroupPlan[] groups)
        {
            var plan = new ScenarioPlan { Groups = new List<GroupPlan>(groups) };
            foreach (var id in groups.SelectMany(group => group.StepIds))
                plan.Steps.Add(new StepSpec(id, id, "Done", () => new SignalCondition("ok", "bad")));
            return plan;
        }

        private static GroupPlan Group(string id, int threshold, params string[][] batches)
        {
            return new GroupPlan
            {
                Id = id,
                Title = id,
                StartRule = GroupStartRule.AtStart,
                ViolationThreshold = threshold,
                StepIds = batches.SelectMany(batch => batch).ToList(),
                Batches = batches.Select(batch => new ActivationBatch(batch)).ToList()
            };
        }

        private static ScenarioSession Start(ScenarioPlan plan, IExecutionPolicy policy)
        {
            var session = new ScenarioSession(plan, policy, new MemoryJournalLogger());
            session.Start();
            return session;
        }

        private static void Send(ScenarioSession session, string id, string signal)
        {
            session.Handle(new ScenarioAction(id, signal));
        }

        private static Journal MetadataJournal(string mode)
        {
            return new Journal
            {
                Mode = mode,
                Steps = new List<StepSnapshot>
                {
                    new StepSnapshot { Id = "one", Title = "One", GroupId = "first" },
                    new StepSnapshot { Id = "two", Title = "Two", GroupId = "second" },
                    new StepSnapshot { Id = "three", Title = "Three", GroupId = "second" }
                },
                Groups = new List<GroupSnapshot>
                {
                    new GroupSnapshot
                    {
                        Id = "first", Title = "First", ViolationThreshold = 2,
                        ActivationRule = "AtStart", StepIds = new List<string> { "one" }
                    },
                    new GroupSnapshot
                    {
                        Id = "second", Title = "Second", ViolationThreshold = 2,
                        ActivationRule = "AfterStep", AfterStepId = "one", StepIds = new List<string> { "two", "three" }
                    }
                },
                Entries = new List<JournalEntry>
                {
                    new JournalEntry { Type = JournalEvents.SessionStarted },
                    new JournalEntry { Type = JournalEvents.StepCompleted, StepId = "one", GroupId = "first", Result = "Completed", Attempt = 1 },
                    new JournalEntry { Type = JournalEvents.StepFailed, StepId = "two", GroupId = "second", Result = "Failed", Attempt = 1 },
                    new JournalEntry { Type = JournalEvents.StepSkipped, StepId = "three", GroupId = "second", Result = "Skipped", Attempt = 0 },
                    new JournalEntry { Type = JournalEvents.SessionCompleted }
                }
            };
        }

        private sealed class RetryPolicy : IExecutionPolicy
        {
            public RunMode Mode => RunMode.Exam;
            public bool RetryOnViolation => true;
            public bool StopGroupAtThreshold => true;
        }
    }
}
