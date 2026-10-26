using System;
using System.Collections.Generic;
using NUnit.Framework;
using ScenarioBuilder.Authoring;
using ScenarioBuilder.Core;
using UnityEngine;

namespace ScenarioBuilder.Tests
{
    public sealed class OverlapAuthoringTests
    {
        private readonly List<ScriptableObject> assets = new List<ScriptableObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var asset in assets)
                UnityEngine.Object.DestroyImmediate(asset);
            assets.Clear();
        }

        [Test]
        public void AfterStepRuleCopiesSharedTriggerIdIntoPlan()
        {
            var mode = CreateExamMode();
            var first = mode.Scenario.Steps[0];
            var second = mode.Scenario.Steps[1];
            mode.Groups[1].StartRule = GroupStartRule.AfterStep;
            mode.Groups[1].StartAfterStep = first;
            mode.Groups[2].StartRule = GroupStartRule.AfterStep;
            mode.Groups[2].StartAfterStep = second;

            var plan = mode.BuildPlan();

            Assert.That(plan.Groups[0].StartRule, Is.EqualTo(GroupStartRule.AfterPreviousGroup));
            Assert.That(plan.Groups[1].StartRule, Is.EqualTo(GroupStartRule.AfterStep));
            Assert.That(plan.Groups[1].StartAfterStepId, Is.EqualTo(first.Id));
            Assert.That(plan.Groups[2].StartAfterStepId, Is.EqualTo(second.Id));
            Assert.That(mode.Groups[0].Steps[0], Is.SameAs(first));
            Assert.That(mode.Groups[1].StartAfterStep, Is.SameAs(first));
            Assert.That(plan.Steps.Count, Is.EqualTo(3));
        }

        [Test]
        public void ReorderingIndependentGroupsKeepsTheirRulesAndTriggerReferences()
        {
            var mode = CreateExamMode();
            var firstGroup = mode.Groups[0];
            var secondGroup = mode.Groups[1];
            var dependentGroup = mode.Groups[2];
            firstGroup.StartRule = GroupStartRule.AtStart;
            secondGroup.StartRule = GroupStartRule.AtStart;
            dependentGroup.StartRule = GroupStartRule.AfterStep;
            dependentGroup.StartAfterStep = mode.Scenario.Steps[0];
            mode.Groups = new List<StepGroup> { secondGroup, firstGroup, dependentGroup };

            var plan = mode.BuildPlan();

            Assert.That(plan.Groups[0].Id, Is.EqualTo("group-2"));
            Assert.That(plan.Groups[1].Id, Is.EqualTo("group-1"));
            Assert.That(plan.Groups[0].StartRule, Is.EqualTo(GroupStartRule.AtStart));
            Assert.That(plan.Groups[1].StartRule, Is.EqualTo(GroupStartRule.AtStart));
            Assert.That(plan.Groups[2].StartRule, Is.EqualTo(GroupStartRule.AfterStep));
            Assert.That(plan.Groups[2].StartAfterStepId, Is.EqualTo("first"));
        }

        [Test]
        public void DisabledTriggerIsReportedThroughAuthoringConfiguration()
        {
            var mode = CreateExamMode();
            var trigger = mode.Scenario.Steps[0];
            mode.Groups[1].StartRule = GroupStartRule.AfterStep;
            mode.Groups[1].StartAfterStep = trigger;
            trigger.Enabled = false;

            Assert.That(mode.ValidateConfiguration(), Has.Some.Contains("шаг запуска 'first' выключен"));
            Assert.Throws<InvalidOperationException>(() => mode.BuildPlan());
        }

        [Test]
        public void AfterStepRequiresAnAssignedTrigger()
        {
            var mode = CreateExamMode();
            mode.Groups[1].StartRule = GroupStartRule.AfterStep;
            mode.Groups[1].StartAfterStep = null;

            Assert.That(mode.ValidateConfiguration(), Has.Some.Contains("выберите шаг запуска"));
            Assert.Throws<InvalidOperationException>(() => mode.BuildPlan());
        }

        [Test]
        public void TriggerAssetMustBelongToTheSharedScenarioEvenWhenItsIdMatches()
        {
            var mode = CreateExamMode();
            mode.Groups[1].StartRule = GroupStartRule.AfterStep;
            mode.Groups[1].StartAfterStep = CreateStep("first");

            Assert.That(mode.ValidateConfiguration(), Has.Some.Contains("отсутствует в общем сценарии"));
            Assert.Throws<InvalidOperationException>(() => mode.BuildPlan());
        }

        [Test]
        public void UnusedTriggerReferenceDoesNotChangeSequentialGroupRule()
        {
            var mode = CreateExamMode();
            mode.Groups[1].StartAfterStep = CreateStep("outside-scenario");

            var plan = mode.BuildPlan();

            Assert.That(plan.Groups[1].StartRule, Is.EqualTo(GroupStartRule.AfterPreviousGroup));
            Assert.That(plan.Groups[1].StartAfterStepId, Is.Null);
        }

        private ModeDefinition CreateExamMode()
        {
            var scenario = CreateAsset<ScenarioDefinition>();
            scenario.Steps = new List<StepDefinition>
            {
                CreateStep("first"), CreateStep("second"), CreateStep("third")
            };
            var mode = CreateAsset<ModeDefinition>();
            mode.Scenario = scenario;
            mode.ExecutionPolicy = CreateAsset<ExamPolicyAsset>();
            for (var index = 0; index < scenario.Steps.Count; index++)
            {
                var step = scenario.Steps[index];
                mode.Groups.Add(new StepGroup
                {
                    Id = "group-" + (index + 1),
                    Title = "Группа " + (index + 1),
                    Steps = new List<StepDefinition> { step },
                    Batches = new List<StepBatch>
                    {
                        new StepBatch { Steps = new List<StepDefinition> { step } }
                    }
                });
            }
            return mode;
        }

        private StepDefinition CreateStep(string id)
        {
            var step = CreateAsset<StepDefinition>();
            step.Id = id;
            step.Title = id;
            step.Result = "Готово";
            step.Condition = CreateAsset<SignalConditionAsset>();
            return step;
        }

        private T CreateAsset<T>() where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            assets.Add(asset);
            return asset;
        }
    }
}
