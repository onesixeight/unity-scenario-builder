using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ScenarioBuilder.Authoring;
using ScenarioBuilder.Core;
using ScenarioBuilder.Runtime;
using UnityEngine;
using UnityEngine.TestTools;

namespace ScenarioBuilder.Tests
{
    public sealed class OverlapIntegrationTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private readonly List<ScriptableObject> assets = new List<ScriptableObject>();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var item in objects)
                if (item != null) Object.Destroy(item);
            objects.Clear();
            yield return null;
            foreach (var asset in assets)
                if (asset != null) Object.Destroy(asset);
            assets.Clear();
            yield return null;
        }

        [UnityTest]
        public IEnumerator ConcurrentGroupsConsumeInterleavedRealButtonAndUnityEventInputs()
        {
            var a1 = SignalStep("a1");
            var a2 = SignalStep("a2");
            var b1 = SignalStep("b1");
            var b2 = SignalStep("b2");
            var runner = CreateRunner(
                Group("first", new[] { a1 }, new[] { a2 }),
                Group("second", new[] { b1 }, new[] { b2 }));
            var firstButton = CreateButton(runner, a1, "ok");
            var secondButton = CreateButton(runner, a2, "ok");
            var firstRelay = CreateRelay(runner, b1, "ok");
            var secondRelay = CreateRelay(runner, b2, "ok");
            Begin(runner);
            yield return null;

            Assert.That(Progress(runner).Groups.All(group => group.Status == GroupStatus.Active), Is.True);
            firstRelay.Raise();
            Assert.That(Step(runner, "b1").Status, Is.EqualTo(StepStatus.Completed));
            Assert.That(Step(runner, "b2").Status, Is.EqualTo(StepStatus.Active));
            Assert.That(Step(runner, "a1").Status, Is.EqualTo(StepStatus.Active));
            Assert.That(Step(runner, "a2").Status, Is.EqualTo(StepStatus.Pending));

            firstButton.onClick.Invoke();
            secondRelay.Raise();
            Assert.That(Group(runner, "second").Status, Is.EqualTo(GroupStatus.Completed));
            Assert.That(Group(runner, "first").Status, Is.EqualTo(GroupStatus.Active));
            Assert.That(Step(runner, "a2").Status, Is.EqualTo(StepStatus.Active));
            Assert.That(runner.IsRunning, Is.True);
            secondButton.onClick.Invoke();

            Assert.That(runner.Session.IsFinished, Is.True);
            Assert.That(Progress(runner).Groups.All(group => group.Status == GroupStatus.Completed), Is.True);
            Assert.That(runner.LastAnalysis.IsValid, Is.True);
            Assert.That(runner.LastAnalysis.Score, Is.EqualTo(100));
            Assert.That(runner.LastJournal.Entries.Where(entry => entry.Type == JournalEvents.StepCompleted)
                .Select(entry => entry.StepId), Is.EqualTo(new[] { "b1", "a1", "b2", "a2" }));
            Assert.That(runner.LastJournal.Entries.Where(entry => entry.StepId == "b1" || entry.StepId == "b2")
                .All(entry => entry.GroupId == "second"), Is.True);
        }

        [UnityTest]
        public IEnumerator ThresholdInOneConcurrentGroupDoesNotCancelTheOtherGroupOrReplayOnReanalysis()
        {
            var a1 = SignalStep("a1");
            var a2 = SignalStep("a2");
            var a3 = SignalStep("a3");
            var a4 = SignalStep("a4");
            var b1 = SignalStep("b1");
            var b2 = SignalStep("b2");
            var runner = CreateRunner(
                Group("first", new[] { a1, a2, a3, a4 }),
                Group("second", new[] { b1 }, new[] { b2 }));
            var firstError = CreateButton(runner, a1, "bad");
            var secondError = CreateButton(runner, a2, "bad");
            var firstRelay = CreateRelay(runner, b1, "ok");
            var secondRelay = CreateRelay(runner, b2, "ok");
            Begin(runner);
            yield return null;

            firstRelay.Raise();
            firstError.onClick.Invoke();
            secondError.onClick.Invoke();

            Assert.That(Group(runner, "first").Status, Is.EqualTo(GroupStatus.Skipped));
            Assert.That(Group(runner, "first").Violations, Is.EqualTo(2));
            Assert.That(Group(runner, "second").Status, Is.EqualTo(GroupStatus.Active));
            Assert.That(Group(runner, "second").Violations, Is.Zero);
            Assert.That(Step(runner, "a3").Status, Is.EqualTo(StepStatus.Skipped));
            Assert.That(Step(runner, "a4").Status, Is.EqualTo(StepStatus.Skipped));
            Assert.That(Step(runner, "b2").Status, Is.EqualTo(StepStatus.Active));
            Assert.That(runner.IsRunning, Is.True);
            secondRelay.Raise();

            Assert.That(runner.Session.IsFinished, Is.True);
            Assert.That(runner.LastAnalysis.IsValid, Is.True);
            Assert.That(runner.LastAnalysis.Score, Is.EqualTo(70));
            var session = runner.Session;
            var journal = runner.LastJournal;
            var entryCount = journal.Entries.Count;
            runner.Reanalyze(Asset<TrainingAnalyzerAsset>());
            Assert.That(runner.LastAnalysis.IsValid, Is.True);
            Assert.That(runner.LastAnalysis.Score, Is.EqualTo(20));
            Assert.That(runner.LastAnalysis.Maximum, Is.EqualTo(60));
            runner.Reanalyze(Asset<ExamAnalyzerAsset>());
            Assert.That(runner.LastAnalysis.Score, Is.EqualTo(70));
            Assert.That(runner.Session, Is.SameAs(session));
            Assert.That(runner.LastJournal, Is.SameAs(journal));
            Assert.That(journal.Entries.Count, Is.EqualTo(entryCount));
        }

        [UnityTest]
        public IEnumerator RestartResetsEveryConcurrentGroupCounterAndCompositeProgress()
        {
            var aOrdered = OrderedStep("a-ordered", "a", "b");
            var aProbe = SignalStep("a-probe");
            var bOrdered = OrderedStep("b-ordered", "x", "y");
            var bProbe = SignalStep("b-probe");
            var runner = CreateRunner(
                Group("first", new[] { aOrdered, aProbe }),
                Group("second", new[] { bOrdered, bProbe }));
            var aStart = CreateButton(runner, aOrdered, "a");
            var aFinish = CreateButton(runner, aOrdered, "b");
            var aError = CreateButton(runner, aProbe, "bad");
            var bStart = CreateRelay(runner, bOrdered, "x");
            var bFinish = CreateRelay(runner, bOrdered, "y");
            var bError = CreateRelay(runner, bProbe, "bad");
            Begin(runner);
            yield return null;

            aError.onClick.Invoke();
            bError.Raise();
            aStart.onClick.Invoke();
            bStart.Raise();
            Assert.That(Progress(runner).Groups.All(group => group.Violations == 1), Is.True);
            Assert.That(Step(runner, "a-ordered").ProgressMessage, Is.Not.Empty);
            Assert.That(Step(runner, "b-ordered").ProgressMessage, Is.Not.Empty);
            var journal = runner.LastJournal;
            var entryCount = journal.Entries.Count;
            runner.Restart();

            Assert.That(runner.LastJournal, Is.Not.SameAs(journal));
            Assert.That(Progress(runner).Groups.All(group => group.Status == GroupStatus.Active && group.Violations == 0), Is.True);
            Assert.That(runner.Session.Steps.All(step => step.Status == StepStatus.Active && step.Attempts == 0 && step.Violations == 0), Is.True);
            Assert.That(Step(runner, "a-ordered").ProgressMessage, Is.Empty);
            Assert.That(Step(runner, "b-ordered").ProgressMessage, Is.Empty);
            Assert.That(journal.Entries.Count, Is.EqualTo(entryCount));

            aFinish.onClick.Invoke();
            bFinish.Raise();
            Assert.That(Step(runner, "a-ordered").Status, Is.EqualTo(StepStatus.Failed));
            Assert.That(Step(runner, "b-ordered").Status, Is.EqualTo(StepStatus.Failed));
            Assert.That(Step(runner, "a-ordered").Attempts, Is.EqualTo(1));
            Assert.That(Step(runner, "b-ordered").Attempts, Is.EqualTo(1));
            Assert.That(Progress(runner).Groups.All(group => group.Violations == 1), Is.True);
        }

        private ScenarioRunner CreateRunner(params StepGroup[] groups)
        {
            var scenario = Asset<ScenarioDefinition>();
            foreach (var group in groups) scenario.Steps.AddRange(group.Steps);
            var mode = Asset<ModeDefinition>();
            mode.DisplayName = "Concurrent exam";
            mode.Scenario = scenario;
            mode.ExecutionPolicy = Asset<ExamPolicyAsset>();
            mode.Groups = new List<StepGroup>(groups);
            var runner = InactiveObject("Concurrent runner").AddComponent<ScenarioRunner>();
            runner.Modes = new[] { mode };
            runner.Logger = Asset<MemoryLoggerAsset>();
            runner.Analyzer = Asset<ExamAnalyzerAsset>();
            return runner;
        }

        private static StepGroup Group(string id, params StepDefinition[][] batches)
        {
            return new StepGroup
            {
                Id = id,
                Title = id,
                StartRule = GroupStartRule.AtStart,
                ViolationThreshold = 2,
                Steps = batches.SelectMany(batch => batch).ToList(),
                Batches = batches.Select(batch => new StepBatch { Steps = new List<StepDefinition>(batch) }).ToList()
            };
        }

        private static IGroupProgressProvider Progress(ScenarioRunner runner)
        {
            var progress = runner.Session as IGroupProgressProvider;
            Assert.That(progress, Is.Not.Null, "The standard executor must expose optional per-group progress.");
            return progress;
        }

        private static IGroupProgress Group(ScenarioRunner runner, string id)
        {
            return Progress(runner).Groups.Single(group => group.Id == id);
        }

        private static IStepProgress Step(ScenarioRunner runner, string id)
        {
            return runner.Session.Steps.Single(step => step.Spec.Id == id);
        }

        private static void Begin(ScenarioRunner runner)
        {
            runner.gameObject.SetActive(true);
            runner.Begin(0);
            Assert.That(runner.IsRunning, Is.True, runner.Message);
        }

        private StepDefinition SignalStep(string id)
        {
            var condition = Asset<SignalConditionAsset>();
            condition.SuccessSignal = "ok";
            condition.FailureSignals = new[] { "bad" };
            return Step(id, condition);
        }

        private StepDefinition OrderedStep(string id, params string[] sequence)
        {
            var condition = Asset<OrderedConditionAsset>();
            condition.Sequence = sequence;
            condition.FailureSignals = new[] { "bad" };
            return Step(id, condition);
        }

        private StepDefinition Step(string id, StepConditionAsset condition)
        {
            var step = Asset<StepDefinition>();
            step.Id = id;
            step.Title = id;
            step.Result = "Done";
            step.Condition = condition;
            return step;
        }

        private UnityEngine.UI.Button CreateButton(ScenarioRunner runner, StepDefinition step, string signal)
        {
            var item = InactiveObject("Button " + step.Id, true);
            var button = item.AddComponent<UnityEngine.UI.Button>();
            var source = item.AddComponent<ButtonEventSource>();
            source.Runner = runner;
            source.Step = step;
            source.Signal = signal;
            source.Button = button;
            item.SetActive(true);
            return button;
        }

        private UnityEventRelay CreateRelay(ScenarioRunner runner, StepDefinition step, string signal)
        {
            var item = InactiveObject("Event " + step.Id);
            var relay = item.AddComponent<UnityEventRelay>();
            var source = item.AddComponent<UnityEventSource>();
            source.Runner = runner;
            source.Step = step;
            source.Signal = signal;
            source.Relay = relay;
            item.SetActive(true);
            return relay;
        }

        private GameObject InactiveObject(string name, bool ui = false)
        {
            var item = ui ? new GameObject(name, typeof(RectTransform)) : new GameObject(name);
            item.SetActive(false);
            objects.Add(item);
            return item;
        }

        private T Asset<T>() where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            assets.Add(asset);
            return asset;
        }
    }
}
