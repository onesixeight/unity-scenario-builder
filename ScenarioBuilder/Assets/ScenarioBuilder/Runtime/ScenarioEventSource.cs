using ScenarioBuilder.Authoring;
using UnityEngine;

namespace ScenarioBuilder.Runtime
{
    /// <summary>Adapts a scene event to a step ID and a configured condition signal.</summary>
    public class ScenarioEventSource : MonoBehaviour
    {
        [InspectorName("Исполнитель"), Tooltip("Исполнитель сценария в этой сцене.")]
        public ScenarioRunner Runner;

        [InspectorName("Шаг"), Tooltip("Общее определение шага, которому адресовано действие.")]
        public StepDefinition Step;

        [InspectorName("Сигнал"), Tooltip("Ключ сигнала, настроенный в условии шага: например confirm, A, B или error.")]
        public string Signal = "confirm";

        private ScenarioRunner subscribedRunner;

        protected bool CanEmit => isActiveAndEnabled && Runner != null && Step != null &&
                                  !string.IsNullOrWhiteSpace(Signal);

        protected virtual void OnEnable()
        {
            UnsubscribeRestart();
            subscribedRunner = Runner;
            if (subscribedRunner != null)
                subscribedRunner.Restarted += ResetCounts;
            ResetCounts();
        }

        protected virtual void OnDisable()
        {
            UnsubscribeRestart();
        }

        public virtual void Emit()
        {
            if (CanEmit)
                Runner.Submit(Step, Signal);
        }

        public virtual void ResetCounts() { }

        private void UnsubscribeRestart()
        {
            if (subscribedRunner != null)
                subscribedRunner.Restarted -= ResetCounts;
            subscribedRunner = null;
        }
    }
}
