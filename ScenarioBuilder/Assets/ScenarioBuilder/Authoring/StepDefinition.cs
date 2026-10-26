using System;
using ScenarioBuilder.Core;
using UnityEngine;

namespace ScenarioBuilder.Authoring
{
    [CreateAssetMenu(menuName = "Учебные сценарии/Шаг", fileName = "Step")]
    public sealed class StepDefinition : ScriptableObject
    {
        [InspectorName("Идентификатор"), Tooltip("Уникальный постоянный ID. Источники событий адресуют шаг по этому ID.")]
        public string Id;

        [InspectorName("Название"), Tooltip("Краткое название задания для пользователя.")]
        public string Title;

        [InspectorName("Инструкция"), TextArea(2, 5), Tooltip("Что пользователь должен сделать. Код менять не требуется.")]
        public string Description;

        [InspectorName("Результат"), TextArea(1, 3), Tooltip("Описание успешного результата, сохраняемое в журнале.")]
        public string Result;

        [InspectorName("Включён"), Tooltip("Выключенный шаг исключается из обоих режимов вместе с его местами в пакетах и группах.")]
        public bool Enabled = true;

        [InspectorName("Условие"), Tooltip("Переиспользуемое описание условий успеха и нарушения. Прогресс хранится только в прохождении.")]
        public StepConditionAsset Condition;

        public StepSpec ToSpec()
        {
            if (Condition == null)
                throw new InvalidOperationException($"У шага '{Id}' не назначено условие.");

            var condition = Condition;
            return new StepSpec(Id, Title, Result, () => condition.CreateCondition());
        }
    }
}
