using System;
using System.Collections.Generic;

namespace ScenarioBuilder.Core
{
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
