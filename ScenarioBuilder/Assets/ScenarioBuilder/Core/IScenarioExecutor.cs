using System;
using System.Collections.Generic;

namespace ScenarioBuilder.Core
{
    public interface IScenarioExecutor
    {
        IReadOnlyList<StepRuntime> Steps { get; }
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
