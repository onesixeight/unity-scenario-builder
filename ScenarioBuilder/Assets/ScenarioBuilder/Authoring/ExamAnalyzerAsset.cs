using ScenarioBuilder.Core;
using UnityEngine;

namespace ScenarioBuilder.Authoring
{
    [CreateAssetMenu(menuName = "Учебные сценарии/Оценивание/Штрафы за ошибки и пропуски", fileName = "ExamAnalyzer")]
    public sealed class ExamAnalyzerAsset : ResultAnalyzerAsset
    {
        public override IResultAnalyzer CreateAnalyzer()
        {
            return new ExamResultAnalyzer();
        }
    }
}
