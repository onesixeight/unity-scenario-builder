using System;
using NUnit.Framework;
using ScenarioBuilder.Core;

namespace ScenarioBuilder.Tests
{
    public sealed class AnalysisTests
    {
        [Test]
        public void Training_ThreeCompletedStepsWithOneTwoThreeAttempts_ScoreFifteen()
        {
            var journal = CompleteJournal("Completed", "Completed", "Completed");
            Terminal(journal, "1").Attempt = 1;
            Terminal(journal, "2").Attempt = 2;
            Terminal(journal, "3").Attempt = 3;
            var result = new TrainingResultAnalyzer().Analyze(journal);
            Assert.That(result.IsValid, Is.True, string.Join("\n", result.Diagnostics));
            Assert.That(result.Score, Is.EqualTo(15));
            Assert.That(result.Maximum, Is.EqualTo(30));
        }

        [Test]
        public void Exam_TwoFailedAndTwoSkipped_ScoreSeventy()
        {
            var result = new ExamResultAnalyzer().Analyze(CompleteJournal("Failed", "Failed", "Skipped", "Skipped"));
            Assert.That(result.IsValid, Is.True, string.Join("\n", result.Diagnostics));
            Assert.That(result.Score, Is.EqualTo(70));
            Assert.That(result.Steps[0].Penalty, Is.EqualTo(10));
            Assert.That(result.Steps[2].Penalty, Is.EqualTo(5));
        }

        [Test]
        public void AnalyzerSelection_DoesNotDependOnExecutionModeOrMutateJournal()
        {
            var journal = CompleteJournal("Completed", "Failed", "Skipped");
            journal.Mode = "custom.executor";
            var training = new TrainingResultAnalyzer().Analyze(journal);
            var exam = new ExamResultAnalyzer().Analyze(journal);
            Assert.That(training.IsValid && exam.IsValid, Is.True);
            Assert.That(training.Score, Is.EqualTo(10));
            Assert.That(exam.Score, Is.EqualTo(85));
            Assert.That(journal.Mode, Is.EqualTo("custom.executor"));
            Assert.That(new TrainingResultAnalyzer().Analyze(journal).Score, Is.EqualTo(10));
        }

        [Test]
        public void NewEventKindsAndExtraFields_DoNotBreakScoring()
        {
            var journal = CompleteJournal("Completed");
            journal.SchemaVersion = 2;
            journal.Entries.Insert(1, new JournalEntry { Type = "extension.operator-note", StepId = "not-a-step" });
            Terminal(journal, "1").Extra.Add(new JournalField("future-data", "42"));
            var result = new TrainingResultAnalyzer().Analyze(journal);
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Score, Is.EqualTo(10));
        }

        [Test]
        public void PendingViolationInAnotherGroup_WithAttemptZero_IsValid()
        {
            var journal = CompleteJournal("Completed");
            journal.Steps[0].GroupId = "future-group";
            journal.Entries.Insert(1, new JournalEntry
            {
                Type = JournalEvents.Violation, StepId = "1", GroupId = "current-group", Attempt = 0
            });
            var result = new TrainingResultAnalyzer().Analyze(journal);
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Score, Is.EqualTo(10));
            Assert.That(result.Steps[0].Violations, Is.EqualTo(1));
        }

        [Test]
        public void MissingOptionalTimestampsAndAttemptEvents_ProducesWarningWithScore()
        {
            var journal = CompleteJournal("Completed");
            journal.StartedUtc = null;
            var result = new TrainingResultAnalyzer().Analyze(journal);
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Score, Is.EqualTo(10));
            Assert.That(result.Diagnostics, Is.Not.Empty);
        }

        [Test]
        public void IncompleteJournal_ReturnsDiagnosticsInsteadOfFinalScore()
        {
            var journal = CompleteJournal("Completed");
            journal.Entries.RemoveAt(journal.Entries.Count - 1);
            var result = new TrainingResultAnalyzer().Analyze(journal);
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Diagnostics, Is.Not.Empty);
        }

        [Test]
        public void MissingAttemptOnCompletedStep_IsInvalid()
        {
            var journal = CompleteJournal("Completed");
            Terminal(journal, "1").Attempt = 0;
            Assert.That(new TrainingResultAnalyzer().Analyze(journal).IsValid, Is.False);
        }

        [Test]
        public void ConflictingTerminalStatuses_AreInvalid()
        {
            var journal = CompleteJournal("Completed");
            journal.Entries.Insert(2, new JournalEntry { StepId = "1", Type = JournalEvents.StepFailed, Attempt = 1 });
            Assert.That(new ExamResultAnalyzer().Analyze(journal).IsValid, Is.False);
        }

        [Test]
        public void MissingTerminalStatus_IsInvalid()
        {
            var journal = CompleteJournal("Completed");
            journal.Entries.Remove(Terminal(journal, "1"));
            Assert.That(new ExamResultAnalyzer().Analyze(journal).IsValid, Is.False);
        }

        [Test]
        public void SnapshotCannotBeMissingOrHaveDuplicateIds()
        {
            var journal = CompleteJournal("Completed");
            journal.Steps.Add(new StepSnapshot { Id = "1" });
            Assert.That(new TrainingResultAnalyzer().Analyze(journal).IsValid, Is.False);
            journal.Steps = null;
            Assert.That(new TrainingResultAnalyzer().Analyze(journal).IsValid, Is.False);
        }

        [Test]
        public void NullJournalAndNullRecords_ReturnDiagnosticsWithoutThrowing()
        {
            Assert.That(new ExamResultAnalyzer().Analyze(null).IsValid, Is.False);
            var journal = CompleteJournal("Completed");
            journal.Entries.Add(null);
            Assert.That(new ExamResultAnalyzer().Analyze(journal).IsValid, Is.False);
            journal.Entries = null;
            Assert.That(new TrainingResultAnalyzer().Analyze(journal).IsValid, Is.False);
        }

        [Test]
        public void ContradictoryAttemptFacts_AreInvalid()
        {
            var journal = CompleteJournal("Completed");
            journal.Entries.Insert(1, new JournalEntry { Type = JournalEvents.AttemptStarted, StepId = "1", Attempt = 2 });
            Assert.That(new TrainingResultAnalyzer().Analyze(journal).IsValid, Is.False);
        }

        [Test]
        public void CompletedStepCannotReuseAnAttemptThatAlreadyFailed()
        {
            var journal = CompleteJournal("Completed");
            journal.Entries.Insert(1, new JournalEntry
            {
                Type = JournalEvents.Violation, StepId = "1", Result = "Active", Attempt = 1, Violations = 1
            });

            var result = new TrainingResultAnalyzer().Analyze(journal);
            Assert.That(result.IsValid, Is.False, "Успех после ошибки требует следующую попытку, а не 10 баллов за первую.");
            Assert.That(new ExamResultAnalyzer().Analyze(journal).IsValid, Is.False);
        }

        [Test]
        public void CompletedStepAfterFailedAttemptUsesTerminalSummaryWithoutIntermediateAttemptEvents()
        {
            var journal = CompleteJournal("Completed");
            Terminal(journal, "1").Attempt = 2;
            journal.Entries.Insert(1, new JournalEntry
            {
                Type = JournalEvents.Violation, StepId = "1", Result = "Active", Attempt = 1, Violations = 1
            });

            var result = new TrainingResultAnalyzer().Analyze(journal);
            Assert.That(result.IsValid, Is.True, string.Join("\n", result.Diagnostics));
            Assert.That(result.Score, Is.EqualTo(5));
            Assert.That(result.Steps[0].Attempts, Is.EqualTo(2));
        }

        [Test]
        public void FailedTerminalCannotReportFewerAttemptsThanItsKnownViolation()
        {
            var journal = CompleteJournal("Failed");
            journal.Entries.Insert(1, new JournalEntry
            {
                Type = JournalEvents.Violation, StepId = "1", Result = "Active", Attempt = 2, Violations = 1
            });

            Assert.That(new ExamResultAnalyzer().Analyze(journal).IsValid, Is.False);
        }

        [Test]
        public void FailedTerminalCanShareTheNumberOfItsFailedAttempt()
        {
            var journal = CompleteJournal("Failed");
            journal.Entries.Insert(1, new JournalEntry
            {
                Type = JournalEvents.Violation, StepId = "1", Result = "Active", Attempt = 1, Violations = 1
            });

            var result = new ExamResultAnalyzer().Analyze(journal);
            Assert.That(result.IsValid, Is.True, string.Join("\n", result.Diagnostics));
            Assert.That(result.Score, Is.EqualTo(90));
        }

        [Test]
        public void SkippedTerminalPreservesKnownAttemptCountWhenThresholdStopsTheStep()
        {
            var journal = CompleteJournal("Skipped");
            journal.Entries.Insert(1, new JournalEntry
            {
                Type = JournalEvents.Violation, StepId = "1", Result = "Active", Attempt = 1, Violations = 1
            });
            Assert.That(new ExamResultAnalyzer().Analyze(journal).IsValid, Is.False);

            Terminal(journal, "1").Attempt = 1;
            var result = new ExamResultAnalyzer().Analyze(journal);
            Assert.That(result.IsValid, Is.True, string.Join("\n", result.Diagnostics));
            Assert.That(result.Score, Is.EqualTo(95));
        }

        [Test]
        public void DuplicateEntryId_IsIdempotentButContradictoryCopiesAreInvalid()
        {
            var journal = CompleteJournal("Completed");
            var terminal = Terminal(journal, "1");
            journal.Entries.Insert(2, terminal);
            Assert.That(new ExamResultAnalyzer().Analyze(journal).IsValid, Is.True);
            journal.Entries.Insert(2, new JournalEntry
            {
                Id = terminal.Id, Type = JournalEvents.StepFailed, StepId = "1", Attempt = 1
            });
            Assert.That(new ExamResultAnalyzer().Analyze(journal).IsValid, Is.False);
        }

        [Test]
        public void ExamScore_IsClampedToZero()
        {
            var result = new ExamResultAnalyzer().Analyze(CompleteJournal(
                "Failed", "Failed", "Failed", "Failed", "Failed", "Failed",
                "Failed", "Failed", "Failed", "Failed", "Failed"));
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Score, Is.EqualTo(0));
        }

        [Test]
        public void Logger_AssignsSequenceAndIgnoresDuplicateIds()
        {
            var logger = new MemoryJournalLogger();
            logger.Begin(new Journal());
            var first = new JournalEntry { Type = "a" };
            logger.Write(first);
            logger.Write(first);
            var second = new JournalEntry { Type = "b" };
            logger.Write(second);
            Assert.That(logger.Journal.Entries.Count, Is.EqualTo(2));
            Assert.That(first.Sequence, Is.EqualTo(1));
            Assert.That(second.Sequence, Is.EqualTo(2));
            logger.Finish();
            Assert.Throws<InvalidOperationException>(() => logger.Write(new JournalEntry()));
            logger.Begin(new Journal());
            logger.Write(first);
            Assert.That(logger.Journal.Entries.Count, Is.EqualTo(1));
        }

        private static Journal CompleteJournal(params string[] statuses)
        {
            var journal = new Journal { Mode = "Exam", StartedUtc = "2026-10-26T00:00:00Z" };
            journal.Groups.Add(new GroupSnapshot { Id = "group", ViolationThreshold = 2 });
            journal.Entries.Add(new JournalEntry { Type = JournalEvents.SessionStarted });
            for (int i = 0; i < statuses.Length; i++)
            {
                string id = (i + 1).ToString();
                journal.Steps.Add(new StepSnapshot { Id = id, Title = "Step " + id, GroupId = "group" });
                journal.Groups[0].StepIds.Add(id);
                journal.Entries.Add(new JournalEntry
                {
                    StepId = id,
                    Type = statuses[i] == "Completed" ? JournalEvents.StepCompleted :
                        statuses[i] == "Failed" ? JournalEvents.StepFailed : JournalEvents.StepSkipped,
                    Result = statuses[i],
                    Attempt = statuses[i] == "Skipped" ? 0 : 1
                });
            }
            journal.Entries.Add(new JournalEntry { Type = JournalEvents.SessionCompleted });
            return journal;
        }

        private static JournalEntry Terminal(Journal journal, string stepId)
        {
            return journal.Entries.Find(entry => entry.StepId == stepId &&
                (entry.Type == JournalEvents.StepCompleted || entry.Type == JournalEvents.StepFailed || entry.Type == JournalEvents.StepSkipped));
        }
    }
}
