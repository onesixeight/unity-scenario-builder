using UnityEngine;

namespace ScenarioBuilder.Runtime
{
    [AddComponentMenu("Учебные сценарии/Источники/Collision")]
    public sealed class CollisionEventSource : ScenarioEventSource
    {
        [InspectorName("Тело участника"), Tooltip("Контакт с этим динамическим Rigidbody вызывает действие. Collider на этом объекте не должен быть триггером.")]
        public Rigidbody Actor;

        public int CallbackCount { get; private set; }

        private void OnCollisionEnter(Collision collision)
        {
            if (!isActiveAndEnabled || Actor == null || collision.rigidbody != Actor)
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
