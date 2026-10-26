using UnityEngine;
using UnityEngine.Events;

namespace ScenarioBuilder.Runtime
{
    [AddComponentMenu("Учебные сценарии/Источники/UnityEvent Relay")]
    public sealed class UnityEventRelay : MonoBehaviour
    {
        [InspectorName("Событие"), Tooltip("Обычный UnityEvent. Его можно вызывать из кнопки, анимации или собственного компонента.")]
        public UnityEvent Event = new UnityEvent();

        public void Raise()
        {
            if (isActiveAndEnabled)
                Event?.Invoke();
        }
    }
}
