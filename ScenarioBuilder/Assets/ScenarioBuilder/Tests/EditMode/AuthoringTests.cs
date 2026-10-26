using System;
using System.Collections.Generic;
using NUnit.Framework;
using ScenarioBuilder.Authoring;
using UnityEngine;

namespace ScenarioBuilder.Tests
{
    public sealed class AuthoringTests
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
        public void DisabledStepIsRemovedFromMembershipAndActivation()
        {
            var first = CreateStep("first");
            var disabled = CreateStep("disabled");
            disabled.Enabled = false;
            var mode = CreateMode(first, disabled);
            mode.Groups[0].Batches.Add(new StepBatch { Steps = new List<StepDefinition> { disabled } });

            var plan = mode.BuildPlan();

            Assert.That(plan.Steps.Count, Is.EqualTo(1));
            Assert.That(plan.Groups[0].StepIds, Is.EqualTo(new[] { "first" }));
            Assert.That(plan.Groups[0].Batches.Count, Is.EqualTo(1));
            Assert.That(plan.Groups[0].Batches[0].StepIds, Is.EqualTo(new[] { "first" }));
        }

        [Test]
        public void GroupCannotSubstituteDifferentAssetWithSameId()
        {
            var original = CreateStep("shared-id");
            var differentAsset = CreateStep("shared-id");
            var mode = CreateMode(original);
            mode.Groups[0].Steps[0] = differentAsset;
            mode.Groups[0].Batches[0].Steps[0] = differentAsset;

            Assert.That(mode.ValidateConfiguration(), Has.Some.Contains("отсутствует в общем сценарии"));
            Assert.Throws<InvalidOperationException>(() => mode.BuildPlan());
        }

        [Test]
        public void TrainingRequiresOneSharedGroup()
        {
            var first = CreateStep("first");
            var second = CreateStep("second");
            var mode = CreateMode(first, second);
            mode.Groups[0].Steps.Remove(second);
            mode.Groups[0].Batches[0].Steps.Remove(second);
            mode.Groups.Add(new StepGroup
            {
                Id = "other",
                Steps = new List<StepDefinition> { second },
                Batches = new List<StepBatch>
                {
                    new StepBatch { Steps = new List<StepDefinition> { second } }
                }
            });

            Assert.That(mode.ValidateConfiguration(), Has.Some.Contains("одна общая группа"));
        }

        [Test]
        public void NullStepReferenceIsReportedEvenWhenAnotherStepIsValid()
        {
            var mode = CreateMode(CreateStep("valid"));
            mode.Groups[0].Batches[0].Steps.Add(null);

            Assert.That(mode.ValidateConfiguration(), Has.Some.Contains("не назначен шаг"));
            Assert.Throws<InvalidOperationException>(() => mode.BuildPlan());
        }

        [Test]
        public void MissingConditionProducesDiagnosticInsteadOfPartialPlan()
        {
            var step = CreateStep("needs-condition");
            step.Condition = null;
            var mode = CreateMode(step);

            Assert.That(mode.ValidateConfiguration(), Has.Some.Contains("не назначено условие"));
            Assert.Throws<InvalidOperationException>(() => mode.BuildPlan());
        }

        [Test]
        public void MembershipDoesNotSilentlyAddMissingActivation()
        {
            var first = CreateStep("first");
            var second = CreateStep("second");
            var mode = CreateMode(first, second);
            mode.Groups[0].Batches[0].Steps.Remove(second);

            Assert.That(mode.ValidateConfiguration(), Is.Not.Empty);
            Assert.Throws<InvalidOperationException>(() => mode.BuildPlan());
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

        private ModeDefinition CreateMode(params StepDefinition[] steps)
        {
            var scenario = CreateAsset<ScenarioDefinition>();
            scenario.Steps.AddRange(steps);
            var mode = CreateAsset<ModeDefinition>();
            mode.Scenario = scenario;
            mode.ExecutionPolicy = CreateAsset<TrainingPolicyAsset>();
            mode.Groups.Add(new StepGroup
            {
                Id = "training",
                Title = "Тренировка",
                Steps = new List<StepDefinition>(steps),
                Batches = new List<StepBatch>
                {
                    new StepBatch { Steps = new List<StepDefinition>(steps) }
                }
            });
            return mode;
        }

        private T CreateAsset<T>() where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            assets.Add(asset);
            return asset;
        }
    }
}
