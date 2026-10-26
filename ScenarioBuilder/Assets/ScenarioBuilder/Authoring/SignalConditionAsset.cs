using ScenarioBuilder.Core;
using UnityEngine;

namespace ScenarioBuilder.Authoring
{
    [CreateAssetMenu(menuName = "Учебные сценарии/Условия/Один сигнал", fileName = "SignalCondition")]
    public sealed class SignalConditionAsset : StepConditionAsset
    {
        [InspectorName("Сигнал успеха"), Tooltip("Ключ сигнала от источника события. Должен совпадать с настройкой адаптера.")]
        public string SuccessSignal = "success";

        [InspectorName("Сигналы нарушения"), Tooltip("Любой из этих сигналов означает ошибку. Остальные сигналы игнорируются.")]
        public string[] FailureSignals = { "error" };

        public override IStepCondition CreateCondition()
        {
            return new SignalCondition(SuccessSignal, FailureSignals);
        }
    }
}
