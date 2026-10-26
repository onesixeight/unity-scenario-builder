using ScenarioBuilder.Core;
using UnityEngine;

namespace ScenarioBuilder.Authoring
{
    [CreateAssetMenu(menuName = "Учебные сценарии/Правила/Экзамен", fileName = "ExamPolicy")]
    public sealed class ExamPolicyAsset : ExecutionPolicyAsset
    {
        public override IExecutionPolicy CreatePolicy()
        {
            return new ExamExecutionPolicy();
        }
    }
}
