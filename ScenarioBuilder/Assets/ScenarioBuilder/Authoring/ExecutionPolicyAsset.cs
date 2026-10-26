using ScenarioBuilder.Core;
using UnityEngine;

namespace ScenarioBuilder.Authoring
{
    public abstract class ExecutionPolicyAsset : ScriptableObject
    {
        public abstract IExecutionPolicy CreatePolicy();
    }
}
