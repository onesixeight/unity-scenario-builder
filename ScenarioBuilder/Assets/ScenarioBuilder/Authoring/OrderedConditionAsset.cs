using ScenarioBuilder.Core;
using UnityEngine;

namespace ScenarioBuilder.Authoring
{
    [CreateAssetMenu(menuName = "Учебные сценарии/Условия/Последовательность сигналов", fileName = "OrderedCondition")]
    public sealed class OrderedConditionAsset : StepConditionAsset
    {
        [InspectorName("Порядок сигналов"), Tooltip("Перетащите элементы для изменения порядка. Несколько правильных сигналов составляют одну попытку.")]
        public string[] Sequence = { "A", "B" };

        [InspectorName("Сигналы нарушения"), Tooltip("Явные ошибки, дополнительно к неверному порядку сигналов из последовательности.")]
        public string[] FailureSignals = { "error" };

        public override IStepCondition CreateCondition()
        {
            return new OrderedSignalCondition(Sequence, FailureSignals);
        }
    }
}
