using System;
using System.Collections.Generic;

namespace ScenarioBuilder.Core
{
    [Serializable]
    public sealed class Journal
    {
        public int SchemaVersion = 1;
        public string SessionId = Guid.NewGuid().ToString("N");
        public string Mode;
        public string StartedUtc;
        public List<StepSnapshot> Steps = new List<StepSnapshot>();
        public List<GroupSnapshot> Groups = new List<GroupSnapshot>();
        public List<JournalEntry> Entries = new List<JournalEntry>();
    }

    [Serializable]
    public sealed class StepSnapshot
    {
        public string Id;
        public string Title;
        public string Result;
        public string GroupId;
    }

    [Serializable]
    public sealed class GroupSnapshot
    {
        public string Id;
        public string Title;
        public int ViolationThreshold;
        public string ActivationRule;
        public string AfterStepId;
        public List<string> StepIds = new List<string>();
    }

    [Serializable]
    public sealed class JournalEntry
    {
        public string Id = Guid.NewGuid().ToString("N");
        public int Sequence;
        public string TimestampUtc;
        public string StepId;
        public string GroupId;
        public string Type;
        public string Result;
        public int Attempt;
        public int Violations;
        public string Message;
        public List<JournalField> Extra = new List<JournalField>();
    }

    [Serializable]
    public sealed class JournalField
    {
        public string Key;
        public string Value;
        public JournalField() { }
        public JournalField(string key, string value) { Key = key; Value = value; }
    }

    public static class JournalEvents
    {
        public const string SessionStarted = "session.started";
        public const string StepActivated = "step.activated";
        public const string AttemptStarted = "attempt.started";
        public const string ConditionProgress = "condition.progress";
        public const string Violation = "action.violation";
        public const string StepCompleted = "step.completed";
        public const string StepFailed = "step.failed";
        public const string StepSkipped = "step.skipped";
        public const string SessionCompleted = "session.completed";
        public const string ActionIgnored = "action.ignored";
    }

    public interface IJournalLogger
    {
        Journal Journal { get; }
        void Begin(Journal journal);
        void Write(JournalEntry entry);
        void Finish();
    }

    public interface IResultAnalyzer
    {
        string Name { get; }
        AnalysisResult Analyze(Journal journal);
    }

    [Serializable]
    public sealed class AnalysisResult
    {
        public string AnalyzerName;
        public bool IsValid;
        public int Score;
        public int Maximum;
        public List<StepScore> Steps = new List<StepScore>();
        public List<string> Diagnostics = new List<string>();
    }

    [Serializable]
    public sealed class StepScore
    {
        public string Id;
        public string Title;
        public string Status;
        public int Attempts;
        public int Violations;
        public int Score;
        public int Penalty;
    }
}
