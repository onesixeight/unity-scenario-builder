using UnityEngine;
using UnityEngine.Events;

namespace ScenarioBuilder.Runtime
{
    [AddComponentMenu("Учебные сценарии/Источники/UnityEvent")]
    public sealed class UnityEventSource : ScenarioEventSource
    {
        [InspectorName("Источник UnityEvent")]
        public UnityEventRelay Relay;

        private UnityEvent subscribedEvent;

        protected override void OnEnable()
        {
            base.OnEnable();
            UnsubscribeEvent();
            if (Relay != null)
                subscribedEvent = Relay.Event;
            subscribedEvent?.AddListener(Emit);
        }

        protected override void OnDisable()
        {
            UnsubscribeEvent();
            base.OnDisable();
        }

        private void UnsubscribeEvent()
        {
            subscribedEvent?.RemoveListener(Emit);
            subscribedEvent = null;
        }
    }
}
