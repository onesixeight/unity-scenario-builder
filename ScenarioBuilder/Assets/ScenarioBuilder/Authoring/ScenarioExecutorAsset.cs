using ScenarioBuilder.Core;
using UnityEngine;

namespace ScenarioBuilder.Authoring
{
    public abstract class ScenarioExecutorAsset : ScriptableObject
    {
        public abstract IScenarioExecutor CreateExecutor(ScenarioPlan plan, IExecutionPolicy policy, IJournalLogger logger);
    }
}
