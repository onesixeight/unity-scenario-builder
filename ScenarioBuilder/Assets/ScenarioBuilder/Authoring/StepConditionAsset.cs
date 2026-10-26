using ScenarioBuilder.Core;
using UnityEngine;

namespace ScenarioBuilder.Authoring
{
    /// <summary>Creates fresh condition state for each run; assets never store progress.</summary>
    public abstract class StepConditionAsset : ScriptableObject
    {
        public abstract IStepCondition CreateCondition();
    }
}
