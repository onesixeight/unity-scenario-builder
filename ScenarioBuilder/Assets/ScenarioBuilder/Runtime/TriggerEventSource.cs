using UnityEngine;

namespace ScenarioBuilder.Runtime
{
    [AddComponentMenu("Учебные сценарии/Источники/Trigger")]
    public sealed class TriggerEventSource : ScenarioEventSource
    {
        [InspectorName("Тело участника"), Tooltip("Только вход этого Rigidbody вызывает действие. Collider на этом объекте должен быть Is Trigger.")]
        public Rigidbody Actor;

        public int CallbackCount { get; private set; }

        private void OnTriggerEnter(Collider other)
        {
            if (!isActiveAndEnabled || Actor == null || other.attachedRigidbody != Actor)
                return;
            CallbackCount++;
            Emit();
        }

        public override void ResetCounts()
        {
            CallbackCount = 0;
        }
    }
}
