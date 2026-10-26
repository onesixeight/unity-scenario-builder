using ScenarioBuilder.Core;
using UnityEngine;

namespace ScenarioBuilder.Authoring
{
    [CreateAssetMenu(menuName = "Учебные сценарии/Исполнители/Стандартный", fileName = "DefaultExecutor")]
    public sealed class DefaultExecutorAsset : ScenarioExecutorAsset
    {
        public override IScenarioExecutor CreateExecutor(ScenarioPlan plan, IExecutionPolicy policy, IJournalLogger logger)
        {
            return new ScenarioSession(plan, policy, logger);
        }
    }
}
