using UnityEngine;

namespace ScenarioBuilder.Runtime
{
    [AddComponentMenu("Учебные сценарии/Источники/Button")]
    public sealed class ButtonEventSource : ScenarioEventSource
    {
        [InspectorName("Кнопка"), Tooltip("Настоящий UI Button. Подписка снимается при отключении источника.")]
        public UnityEngine.UI.Button Button;

        private UnityEngine.UI.Button subscribedButton;

        protected override void OnEnable()
        {
            base.OnEnable();
            UnsubscribeButton();
            if (Button == null)
                Button = GetComponent<UnityEngine.UI.Button>();
            subscribedButton = Button;
            if (subscribedButton != null)
                subscribedButton.onClick.AddListener(Emit);
        }

        protected override void OnDisable()
        {
            UnsubscribeButton();
            base.OnDisable();
        }

        private void UnsubscribeButton()
        {
            if (subscribedButton != null)
                subscribedButton.onClick.RemoveListener(Emit);
            subscribedButton = null;
        }
    }
}
