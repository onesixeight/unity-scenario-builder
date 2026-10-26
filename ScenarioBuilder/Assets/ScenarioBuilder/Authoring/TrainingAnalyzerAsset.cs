using ScenarioBuilder.Core;
using UnityEngine;

namespace ScenarioBuilder.Authoring
{
    [CreateAssetMenu(menuName = "Учебные сценарии/Оценивание/Баллы за попытки", fileName = "TrainingAnalyzer")]
    public sealed class TrainingAnalyzerAsset : ResultAnalyzerAsset
    {
        public override IResultAnalyzer CreateAnalyzer()
        {
            return new TrainingResultAnalyzer();
        }
    }
}
