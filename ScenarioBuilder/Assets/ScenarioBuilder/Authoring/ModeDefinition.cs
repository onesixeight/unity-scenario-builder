using System;
using System.Collections.Generic;
using ScenarioBuilder.Core;
using UnityEngine;

namespace ScenarioBuilder.Authoring
{
    [Serializable]
    public sealed class StepBatch
    {
        [InspectorName("Одновременно активные шаги"), Tooltip("Все шаги пакета доступны одновременно. Следующий пакет откроется после завершения текущего.")]
        public List<StepDefinition> Steps = new List<StepDefinition>();
    }

    [Serializable]
    public sealed class StepGroup
    {
        [InspectorName("ID группы"), Tooltip("Уникальный ID группы внутри режима.")]
        public string Id;

        [InspectorName("Название группы")]
        public string Title;

        [InspectorName("Порог нарушений"), Min(1), Tooltip("Для экзамена: при достижении порога незавершённые шаги группы пропускаются. В тренировке не применяется.")]
        public int ViolationThreshold = 2;

        [InspectorName("Состав группы"), Tooltip("Шаги, относящиеся к этой группе. Каждый включённый шаг сценария должен быть ровно в одной группе.")]
        public List<StepDefinition> Steps = new List<StepDefinition>();

        [InspectorName("Порядок пакетов активации"), Tooltip("Перетащите пакеты для изменения порядка. Каждый шаг группы должен быть ровно в одном пакете. Один шаг в пакете даёт последовательную активацию.")]
        public List<StepBatch> Batches = new List<StepBatch>();
    }

    [CreateAssetMenu(menuName = "Учебные сценарии/Режим прохождения", fileName = "Mode")]
    public sealed class ModeDefinition : ScriptableObject
    {
        [InspectorName("Название режима")]
        public string DisplayName;

        [InspectorName("Общий сценарий"), Tooltip("Один и тот же сценарий назначается тренировке и экзамену. Дублировать определения шагов не нужно.")]
        public ScenarioDefinition Scenario;

        [InspectorName("Правила исполнения"), Tooltip("Реакция на нарушения: повтор шага в тренировке или Failed и порог группы в экзамене.")]
        public ExecutionPolicyAsset ExecutionPolicy;

        [InspectorName("Анализатор по умолчанию"), Tooltip("Удобный начальный выбор для интерфейса. После прохождения можно выбрать любой другой совместимый анализатор.")]
        public ResultAnalyzerAsset SuggestedAnalyzer;

        [InspectorName("Действия до активации"), Tooltip("Violation — регистрировать нарушение. Ignore — полностью игнорировать события до очереди шага.")]
        public OutOfOrderPolicy OutOfOrderPolicy = OutOfOrderPolicy.Violation;

        [InspectorName("Группы"), Tooltip("В тренировке одна общая группа. В экзамене группы идут последовательно; пакеты внутри каждой группы настраиваются отдельно.")]
        public List<StepGroup> Groups = new List<StepGroup>();

        public ScenarioPlan BuildPlan()
        {
            var errors = new List<string>();
            var plan = BuildPlanData(errors);
            errors.AddRange(plan.Validate());
            ValidatePolicy(plan, errors);
            if (errors.Count > 0)
                throw new InvalidOperationException(string.Join("\n", errors));
            return plan;
        }

        public List<string> ValidateConfiguration()
        {
            var errors = new List<string>();
            try
            {
                var plan = BuildPlanData(errors);
                errors.AddRange(plan.Validate());
                ValidatePolicy(plan, errors);
            }
            catch (Exception exception)
            {
                errors.Add("Проверка конфигурации: " + exception.Message);
            }
            return errors;
        }

        private ScenarioPlan BuildPlanData(List<string> errors)
        {
            var plan = new ScenarioPlan { OutOfOrderPolicy = OutOfOrderPolicy };
            var available = new HashSet<StepDefinition>();
            if (Scenario == null)
                errors.Add("Назначьте общий сценарий.");
            else if (Scenario.Steps == null || Scenario.Steps.Count == 0)
                errors.Add("В сценарии нет шагов.");
            else
            {
                for (var i = 0; i < Scenario.Steps.Count; i++)
                {
                    var step = Scenario.Steps[i];
                    if (step == null)
                    {
                        errors.Add($"Общий сценарий: не назначен шаг {i + 1}.");
                        continue;
                    }
                    if (!available.Add(step))
                        errors.Add($"Общий сценарий: шаг '{step.Id}' добавлен несколько раз.");
                    if (!step.Enabled)
                        continue;
                    try
                    {
                        var spec = step.ToSpec();
                        if (step.Condition.CreateCondition() == null)
                            errors.Add($"Условие шага '{step.Id}' не создаёт обработчик.");
                        plan.Steps.Add(spec);
                    }
                    catch (Exception exception)
                    {
                        errors.Add($"Шаг '{step.Id}': {exception.Message}");
                    }
                }
            }

            if (Groups == null || Groups.Count == 0)
            {
                errors.Add("Добавьте хотя бы одну группу и настройте пакеты активации.");
                return plan;
            }

            for (var groupIndex = 0; groupIndex < Groups.Count; groupIndex++)
            {
                var source = Groups[groupIndex];
                if (source == null)
                {
                    errors.Add($"Не назначена группа {groupIndex + 1}.");
                    continue;
                }

                var group = new GroupPlan
                {
                    Id = source.Id,
                    Title = source.Title,
                    ViolationThreshold = source.ViolationThreshold
                };
                AddStepIds(source.Steps, group.StepIds, available, errors, $"Группа '{source.Id}'");

                if (source.Batches == null || source.Batches.Count == 0)
                    errors.Add($"Группа '{source.Id}': добавьте пакеты активации.");
                else
                {
                    for (var batchIndex = 0; batchIndex < source.Batches.Count; batchIndex++)
                    {
                        var sourceBatch = source.Batches[batchIndex];
                        if (sourceBatch == null)
                        {
                            errors.Add($"Группа '{source.Id}': не назначен пакет {batchIndex + 1}.");
                            continue;
                        }
                        var batch = new ActivationBatch();
                        AddStepIds(sourceBatch.Steps, batch.StepIds, available, errors,
                            $"Группа '{source.Id}', пакет {batchIndex + 1}");
                        if (batch.StepIds.Count > 0)
                            group.Batches.Add(batch);
                    }
                }

                // Entirely disabled groups are omitted, but invalid references are still diagnosed.
                if (group.StepIds.Count > 0 || group.Batches.Count > 0)
                    plan.Groups.Add(group);
            }
            return plan;
        }

        private static void AddStepIds(List<StepDefinition> source, List<string> destination,
            HashSet<StepDefinition> available, List<string> errors, string location)
        {
            if (source == null || source.Count == 0)
            {
                errors.Add(location + ": список шагов пуст.");
                return;
            }
            for (var i = 0; i < source.Count; i++)
            {
                var step = source[i];
                if (step == null)
                {
                    errors.Add($"{location}: не назначен шаг {i + 1}.");
                    continue;
                }
                if (!available.Contains(step))
                    errors.Add($"{location}: шаг '{step.Id}' отсутствует в общем сценарии.");
                if (step.Enabled)
                    destination.Add(step.Id);
            }
        }

        private void ValidatePolicy(ScenarioPlan plan, List<string> errors)
        {
            if (ExecutionPolicy == null)
            {
                errors.Add("Назначьте правила исполнения режима.");
                return;
            }
            try
            {
                var policy = ExecutionPolicy.CreatePolicy();
                if (policy == null)
                    errors.Add("Правила исполнения не создают исполнительную политику.");
                else if (policy.Mode == RunMode.Training && plan.Groups.Count != 1)
                    errors.Add("Для тренировки нужна одна общая группа со всеми включёнными шагами.");
            }
            catch (Exception exception)
            {
                errors.Add("Правила исполнения: " + exception.Message);
            }
        }
    }
}
