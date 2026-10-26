using ScenarioBuilder.Core;
using UnityEngine;

namespace ScenarioBuilder.Authoring
{
    [CreateAssetMenu(menuName = "Учебные сценарии/Правила/Тренировка", fileName = "TrainingPolicy")]
    public sealed class TrainingPolicyAsset : ExecutionPolicyAsset
    {
        public override IExecutionPolicy CreatePolicy()
        {
            return new TrainingExecutionPolicy();
        }
    }
}
