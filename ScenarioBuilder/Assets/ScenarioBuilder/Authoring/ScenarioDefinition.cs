using System.Collections.Generic;
using UnityEngine;

namespace ScenarioBuilder.Authoring
{
    [CreateAssetMenu(menuName = "Учебные сценарии/Сценарий", fileName = "Scenario")]
    public sealed class ScenarioDefinition : ScriptableObject
    {
        [InspectorName("Общие шаги"), Tooltip("Одни определения используются всеми режимами. Перетащите шаги для изменения порядка отображения; активация настраивается в режиме.")]
        public List<StepDefinition> Steps = new List<StepDefinition>();
    }
}
