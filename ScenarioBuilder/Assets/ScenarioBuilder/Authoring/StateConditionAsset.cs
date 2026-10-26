using ScenarioBuilder.Core;
using UnityEngine;

namespace ScenarioBuilder.Authoring
{
    [CreateAssetMenu(menuName = "Учебные сценарии/Условия/Сигнал и состояние", fileName = "StateCondition")]
    public sealed class StateConditionAsset : StepConditionAsset
    {
        [InspectorName("Сигнал действия"), Tooltip("Сигнал, который проверяется вместе с состоянием другого объекта.")]
        public string SuccessSignal = "success";

        [InspectorName("Ключ состояния"), Tooltip("Идентификатор состояния из источника контекста сцены, например safety.enabled.")]
        public string StateKey = "safety.enabled";

        [InspectorName("Нужное значение"), Tooltip("Значение состояния, необходимое для успешного действия.")]
        public bool RequiredValue = true;

        [InspectorName("Сигналы нарушения"), Tooltip("Сигналы, которые сразу означают ошибку, независимо от контекста.")]
        public string[] FailureSignals = { "error" };

        public override IStepCondition CreateCondition()
        {
            return new StateSignalCondition(SuccessSignal, StateKey, RequiredValue, FailureSignals);
        }
    }
}
