using System;
using System.Collections.Generic;

namespace ScenarioBuilder.Core
{
    public interface IGroupProgress
    {
        string Id { get; }
        string Title { get; }
        GroupStatus Status { get; }
        int Violations { get; }
        int ViolationThreshold { get; }
        bool StopsAtThreshold { get; }
        GroupStartRule StartRule { get; }
        string StartAfterStepId { get; }
        IReadOnlyList<string> StepIds { get; }
    }

    public interface IGroupProgressProvider
    {
        IReadOnlyList<IGroupProgress> Groups { get; }
    }

    public interface IStepProgress
    {
        StepSpec Spec { get; }
        StepStatus Status { get; }
        int Attempts { get; }
        int Violations { get; }
        string ProgressMessage { get; }
    }

    public interface IScenarioExecutor
    {
        IReadOnlyList<IStepProgress> Steps { get; }
        Journal Journal { get; }
        bool IsFinished { get; }
        string CurrentGroupId { get; }
        int CurrentGroupViolations { get; }
        string LastMessage { get; }
        event Action Changed;
        void Start();
        void Handle(ScenarioAction action);
    }
}
