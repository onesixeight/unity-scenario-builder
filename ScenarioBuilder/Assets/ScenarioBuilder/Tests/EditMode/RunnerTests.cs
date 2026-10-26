using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ScenarioBuilder.Authoring;
using ScenarioBuilder.Core;
using ScenarioBuilder.Runtime;
using UnityEngine;
using UnityEngine.TestTools;

namespace ScenarioBuilder.Tests
{
    public sealed class RunnerTests
    {
        private readonly List<ScriptableObject> assets = new List<ScriptableObject>();
        private readonly List<GameObject> objects = new List<GameObject>();
        private ScenarioRunner runner;
        private RunnerTestLoggerAsset logger;
        private RunnerTestAnalyzerAsset analyzer;

        [SetUp]
        public void SetUp()
        {
            runner = CreateObject("Runner test").AddComponent<ScenarioRunner>();
            logger = CreateAsset<RunnerTestLoggerAsset>();
            analyzer = CreateAsset<RunnerTestAnalyzerAsset>();
            runner.Logger = logger;
            runner.Analyzer = analyzer;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var item in objects) UnityEngine.Object.DestroyImmediate(item);
            objects.Clear();
            foreach (var asset in assets) UnityEngine.Object.DestroyImmediate(asset);
            assets.Clear();
        }

        [Test]
        public void FailedLoggerBeginPreservesCompletedSessionAndDoesNotBlockImport()
        {
            var step = CreateStep("first");
            Configure(step);
            runner.Begin(0);
            runner.Submit(step, "ok");
            Assert.That(runner.Session.IsFinished, Is.True);
            Assert.That(runner.LastAnalysis.IsValid, Is.True);
            var previousSession = runner.Session;
            var previousJournal = runner.LastJournal;
            var previousAnalysis = runner.LastAnalysis;

            logger.ThrowOnBegin = true;
            runner.Begin(0);

            Assert.That(runner.Session, Is.SameAs(previousSession));
            Assert.That(runner.LastJournal, Is.SameAs(previousJournal));
            Assert.That(runner.LastAnalysis, Is.SameAs(previousAnalysis));
            Assert.That(runner.IsRunning, Is.False);
            Assert.That(runner.Message, Does.Contain("Expected logger begin failure"));

            // The file may exist or be absent; neither case should hit the active-run guard.
            // This checks the public import path without changing the user's saved file.
            runner.ImportJournal();
            Assert.That(runner.Message, Does.Not.Contain("Завершите прохождение перед загрузкой журнала"));
        }

        [Test]
        public void FailedLoggerBeginLeavesAnActiveSessionUsable()
        {
            var step = CreateStep("first");
            Configure(step);
            runner.Begin(0);
            runner.Submit(step, "bad");
            var previousSession = runner.Session;
            var previousJournal = runner.LastJournal;
            var previousCount = previousJournal.Entries.Count;

            logger.ThrowOnBegin = true;
            runner.Restart();

            Assert.That(runner.Session, Is.SameAs(previousSession));
            Assert.That(runner.LastJournal, Is.SameAs(previousJournal));
            Assert.That(runner.LastJournal.Entries.Count, Is.EqualTo(previousCount));
            Assert.That(runner.IsRunning, Is.True);
            runner.Submit(step, "ok");
            Assert.That(previousSession.IsFinished, Is.True);
            Assert.That(previousSession.Steps[0].Attempts, Is.EqualTo(2));
            Assert.That(runner.LastAnalysis.Score, Is.EqualTo(5));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FailingNewAnalyzerClearsThePreviousScore(bool failDuringCreation)
        {
            var step = CreateStep("first");
            Configure(step);
            runner.Begin(0);
            runner.Submit(step, "ok");
            Assert.That(runner.LastAnalysis.Score, Is.EqualTo(10));
            var journal = runner.LastJournal;
            var broken = CreateAsset<RunnerTestAnalyzerAsset>();
            broken.ThrowOnCreate = failDuringCreation;
            broken.ThrowOnAnalyze = !failDuringCreation;

            runner.Reanalyze(broken);

            Assert.That(runner.LastAnalysis, Is.Null);
            Assert.That(runner.LastJournal, Is.SameAs(journal));
            Assert.That(runner.Message, Does.Contain("Ошибка анализа"));
            Assert.That(runner.Message, Does.Contain("Expected analyzer failure"));
        }

        [Test]
        public void EditingStepIdDoesNotRetargetTheCurrentRunAndAppliesOnRestart()
        {
            var first = CreateStep("first");
            var second = CreateStep("second");
            Configure(first, second);
            runner.Begin(0);
            first.Id = "edited-id";

            runner.Submit(first, "ok");

            Assert.That(runner.Session.Steps[0].Spec.Id, Is.EqualTo("first"));
            Assert.That(runner.Session.Steps[0].Status, Is.EqualTo(StepStatus.Completed));
            Assert.That(runner.LastJournal.Steps[0].Id, Is.EqualTo("first"));
            runner.Submit(second, "ok");
            runner.Restart();
            Assert.That(runner.Session.Steps[0].Spec.Id, Is.EqualTo("edited-id"));
            runner.Submit(first, "ok");
            Assert.That(runner.Session.Steps[0].Status, Is.EqualTo(StepStatus.Completed));
        }

        [Test]
        public void EditingEnabledStateOnlyChangesMembershipOnTheNextRun()
        {
            var first = CreateStep("first");
            var second = CreateStep("second");
            var initiallyDisabled = CreateStep("disabled");
            initiallyDisabled.Enabled = false;
            Configure(first, second, initiallyDisabled);
            runner.Begin(0);
            first.Enabled = false;
            initiallyDisabled.Enabled = true;

            runner.Submit(initiallyDisabled, "ok");
            Assert.That(runner.Session.Steps.Count, Is.EqualTo(2));
            Assert.That(runner.Session.CurrentGroupViolations, Is.Zero);
            runner.Submit(first, "ok");
            Assert.That(runner.Session.Steps[0].Status, Is.EqualTo(StepStatus.Completed));
            runner.Submit(second, "ok");
            Assert.That(runner.Session.IsFinished, Is.True);

            runner.Restart();

            Assert.That(runner.Session.Steps.Select(step => step.Spec.Id), Is.EqualTo(new[] { "second", "disabled" }));
            runner.Submit(first, "ok");
            Assert.That(runner.Session.CurrentGroupViolations, Is.Zero);
            runner.Submit(initiallyDisabled, "ok");
            Assert.That(runner.Session.Steps[1].Status, Is.EqualTo(StepStatus.Completed));
        }

        [Test]
        public void RestartResetsSceneStateAttemptsAndPartialConditionProgress()
        {
            var first = CreateStep("ordered", true);
            var second = CreateStep("single");
            Configure(first, second);
            var state = CreateObject("Context state").AddComponent<ContextState>();
            state.Key = "power";
            state.InitialValue = false;
            runner.ContextStates = new[] { state };
            int resetCalls = 0;
            runner.Restarted += () => resetCalls++;
            runner.Begin(0);
            state.SetValue(true);
            runner.Submit(first, "a");
            runner.Submit(second, "bad");
            var oldSession = runner.Session;
            var oldJournal = runner.LastJournal;
            Assert.That(oldSession.Steps[0].ProgressMessage, Is.Not.Empty);

            runner.Restart();

            Assert.That(runner.Session, Is.Not.SameAs(oldSession));
            Assert.That(runner.LastJournal.SessionId, Is.Not.EqualTo(oldJournal.SessionId));
            Assert.That(runner.LastAnalysis, Is.Null);
            Assert.That(state.Value, Is.False);
            Assert.That(resetCalls, Is.EqualTo(2));
            Assert.That(runner.Session.Steps.All(step => step.Status == StepStatus.Active), Is.True);
            Assert.That(runner.Session.Steps.All(step => step.Attempts == 0 && step.Violations == 0), Is.True);
            Assert.That(runner.Session.Steps.All(step => step.ProgressMessage == string.Empty), Is.True);
            runner.Submit(first, "b");
            Assert.That(runner.Session.Steps[0].Violations, Is.EqualTo(1), "The previous run's 'a' must not survive.");
            Assert.That(runner.Session.Steps[1].Attempts, Is.Zero);
            Assert.That(oldSession.Steps[1].Violations, Is.EqualTo(1));
        }

        [Test]
        public void FailingResetSubscriberDoesNotPreventOtherResetsOrTheRun()
        {
            var step = CreateStep("first");
            Configure(step);
            int successfulResets = 0;
            runner.Restarted += () => throw new InvalidOperationException("Expected reset failure");
            runner.Restarted += () => successfulResets++;
            LogAssert.Expect(LogType.Warning, "Ошибка сброса объекта сцены: Expected reset failure");

            runner.Begin(0);

            Assert.That(successfulResets, Is.EqualTo(1));
            Assert.That(runner.IsRunning, Is.True);
            Assert.That(runner.LastJournal, Is.Not.Null);
            runner.Submit(step, "ok");
            Assert.That(runner.Session.IsFinished, Is.True);
            Assert.That(runner.LastAnalysis.Score, Is.EqualTo(10));
        }

        [Test]
        public void SelectedExecutorFactoryReceivesTheRunAndOwnsInputHandling()
        {
            var step = CreateStep("first");
            Configure(step);
            var executorAsset = CreateAsset<RunnerTestExecutorAsset>();
            runner.Executor = executorAsset;

            runner.Begin(0);

            var selected = executorAsset.LastCreated;
            Assert.That(executorAsset.CreateCount, Is.EqualTo(1));
            Assert.That(runner.Session, Is.SameAs(selected));
            Assert.That(selected.StartCalls, Is.EqualTo(1));
            runner.Submit(step, "ok");
            Assert.That(selected.HandleCalls, Is.EqualTo(1));
            Assert.That(runner.Session.IsFinished, Is.True);
            Assert.That(runner.LastJournal, Is.SameAs(selected.Journal));
            Assert.That(runner.LastAnalysis.Score, Is.EqualTo(10));

            runner.Restart();

            Assert.That(executorAsset.CreateCount, Is.EqualTo(2));
            Assert.That(runner.Session, Is.Not.SameAs(selected));
        }

        private void Configure(params StepDefinition[] steps)
        {
            var scenario = CreateAsset<ScenarioDefinition>();
            scenario.Steps.AddRange(steps);
            var mode = CreateAsset<ModeDefinition>();
            mode.DisplayName = "Test training";
            mode.Scenario = scenario;
            mode.ExecutionPolicy = CreateAsset<RunnerTestPolicyAsset>();
            mode.Groups.Add(new StepGroup
            {
                Id = "training",
                Title = "Training",
                Steps = new List<StepDefinition>(steps),
                Batches = new List<StepBatch> { new StepBatch { Steps = new List<StepDefinition>(steps) } }
            });
            runner.Modes = new[] { mode };
        }

        private StepDefinition CreateStep(string id, bool ordered = false)
        {
            var step = CreateAsset<StepDefinition>();
            step.Id = id;
            step.Title = id;
            step.Result = "Done";
            var condition = CreateAsset<RunnerTestConditionAsset>();
            condition.Ordered = ordered;
            step.Condition = condition;
            return step;
        }

        private T CreateAsset<T>() where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            assets.Add(asset);
            return asset;
        }

        private GameObject CreateObject(string name)
        {
            var item = new GameObject(name);
            item.hideFlags = HideFlags.DontSave;
            objects.Add(item);
            return item;
        }
    }

    public sealed class RunnerTestConditionAsset : StepConditionAsset
    {
        public bool Ordered;
        public override IStepCondition CreateCondition()
        {
            return Ordered
                ? (IStepCondition)new OrderedSignalCondition(new[] { "a", "b" }, new[] { "bad" })
                : new SignalCondition("ok", new[] { "bad" });
        }
    }

    public sealed class RunnerTestPolicyAsset : ExecutionPolicyAsset
    {
        public override IExecutionPolicy CreatePolicy() { return new TrainingExecutionPolicy(); }
    }

    public sealed class RunnerTestLoggerAsset : JournalLoggerAsset
    {
        public bool ThrowOnBegin;
        public override IJournalLogger CreateLogger() { return new TestLogger(ThrowOnBegin); }

        private sealed class TestLogger : IJournalLogger
        {
            private readonly bool throwOnBegin;
            private readonly MemoryJournalLogger memory = new MemoryJournalLogger();
            public Journal Journal => memory.Journal;
            public TestLogger(bool throwOnBegin) { this.throwOnBegin = throwOnBegin; }
            public void Begin(Journal journal)
            {
                if (throwOnBegin) throw new IOException("Expected logger begin failure");
                memory.Begin(journal);
            }
            public void Write(JournalEntry entry) { memory.Write(entry); }
            public void Finish() { memory.Finish(); }
        }
    }

    public sealed class RunnerTestAnalyzerAsset : ResultAnalyzerAsset
    {
        public bool ThrowOnCreate;
        public bool ThrowOnAnalyze;
        public override IResultAnalyzer CreateAnalyzer()
        {
            if (ThrowOnCreate) throw new InvalidOperationException("Expected analyzer failure");
            return new TestAnalyzer(ThrowOnAnalyze);
        }

        private sealed class TestAnalyzer : IResultAnalyzer
        {
            private readonly bool throwOnAnalyze;
            public string Name => "Runner test analyzer";
            public TestAnalyzer(bool throwOnAnalyze) { this.throwOnAnalyze = throwOnAnalyze; }
            public AnalysisResult Analyze(Journal journal)
            {
                if (throwOnAnalyze) throw new InvalidOperationException("Expected analyzer failure");
                return new TrainingResultAnalyzer().Analyze(journal);
            }
        }
    }

    public sealed class RunnerTestExecutorAsset : ScenarioExecutorAsset
    {
        public int CreateCount { get; private set; }
        public RecordingExecutor LastCreated { get; private set; }
        public override IScenarioExecutor CreateExecutor(ScenarioPlan plan, IExecutionPolicy policy, IJournalLogger logger)
        {
            CreateCount++;
            LastCreated = new RecordingExecutor(new ScenarioSession(plan, policy, logger));
            return LastCreated;
        }

        public sealed class RecordingExecutor : IScenarioExecutor
        {
            private readonly IScenarioExecutor inner;
            public int StartCalls { get; private set; }
            public int HandleCalls { get; private set; }
            public IReadOnlyList<IStepProgress> Steps => inner.Steps;
            public Journal Journal => inner.Journal;
            public bool IsFinished => inner.IsFinished;
            public string CurrentGroupId => inner.CurrentGroupId;
            public int CurrentGroupViolations => inner.CurrentGroupViolations;
            public string LastMessage => inner.LastMessage;
            public event Action Changed { add { inner.Changed += value; } remove { inner.Changed -= value; } }
            public RecordingExecutor(IScenarioExecutor inner) { this.inner = inner; }
            public void Start() { StartCalls++; inner.Start(); }
            public void Handle(ScenarioAction action) { HandleCalls++; inner.Handle(action); }
        }
    }
}
