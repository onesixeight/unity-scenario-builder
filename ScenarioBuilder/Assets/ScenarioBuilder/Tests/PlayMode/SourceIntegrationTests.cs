using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using ScenarioBuilder.Authoring;
using ScenarioBuilder.Core;
using ScenarioBuilder.Runtime;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace ScenarioBuilder.Tests
{
    public sealed class SourceIntegrationTests
    {
        private static readonly Vector3 PhysicsOrigin = new Vector3(1000f, 0.5f, 1000f);
        private readonly List<GameObject> objects = new List<GameObject>();
        private readonly List<ScriptableObject> assets = new List<ScriptableObject>();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var item in objects)
                if (item != null) UnityEngine.Object.Destroy(item);
            objects.Clear();
            yield return null;
            foreach (var asset in assets)
                if (asset != null) UnityEngine.Object.Destroy(asset);
            assets.Clear();
            yield return null;
        }

        [UnityTest]
        public IEnumerator ButtonReenableDoesNotDuplicateSubscriptionsOrAttempts()
        {
            var step = SignalStep("button");
            var runner = CreateRunner(step);
            var buttonObject = InactiveObject("Button source", true);
            var button = buttonObject.AddComponent<Button>();
            var source = buttonObject.AddComponent<ButtonEventSource>();
            source.Runner = runner;
            source.Step = step;
            source.Signal = "bad";
            source.Button = button;
            buttonObject.SetActive(true);
            Begin(runner);
            yield return null;

            button.onClick.Invoke();
            Assert.That(runner.Session.Steps[0].Attempts, Is.EqualTo(1));
            Assert.That(runner.Session.Steps[0].Violations, Is.EqualTo(1));
            source.enabled = false;
            button.onClick.Invoke();
            Assert.That(runner.Session.Steps[0].Attempts, Is.EqualTo(1));

            for (var index = 0; index < 3; index++)
            {
                source.enabled = true;
                button.onClick.Invoke();
                Assert.That(runner.Session.Steps[0].Attempts, Is.EqualTo(index + 2));
                Assert.That(runner.Session.Steps[0].Violations, Is.EqualTo(index + 2));
                source.enabled = false;
            }

            source.enabled = true;
            source.Signal = "ok";
            step.Enabled = false; // Editor changes take effect when the next run is built.
            button.onClick.Invoke();
            Assert.That(runner.Session.IsFinished, Is.True);
            Assert.That(runner.Session.Steps[0].Attempts, Is.EqualTo(5));
        }

        [UnityTest]
        public IEnumerator UnityEventRelayAndSourceDeliverOnlyWhileSubscribed()
        {
            var step = SignalStep("event");
            var runner = CreateRunner(step);
            UnityEventSource source;
            var relay = CreateRelay(runner, step, "bad", out source);
            Begin(runner);
            yield return null;

            relay.Raise();
            Assert.That(runner.Session.Steps[0].Attempts, Is.EqualTo(1));
            source.enabled = false;
            relay.Raise();
            Assert.That(runner.Session.Steps[0].Attempts, Is.EqualTo(1));
            source.enabled = true;
            relay.enabled = false;
            relay.Raise();
            Assert.That(runner.Session.Steps[0].Attempts, Is.EqualTo(1));
            relay.enabled = true;
            relay.Raise();
            Assert.That(runner.Session.Steps[0].Attempts, Is.EqualTo(2));
            source.Signal = "ok";
            relay.Raise();
            Assert.That(runner.Session.IsFinished, Is.True);
            Assert.That(runner.Session.Steps[0].Attempts, Is.EqualTo(3));
        }

        [UnityTest]
        public IEnumerator RigidbodyMovementEntersTriggerFiltersOtherActorAndResetsCount()
        {
            var step = SignalStep("trigger");
            var runner = CreateRunner(step);
            var actor = CreateBody("Expected trigger actor", PhysicsOrigin + Vector3.left * 3f, true);
            var other = CreateBody("Other trigger actor", PhysicsOrigin + Vector3.right * 3f, true);
            var otherProbe = other.gameObject.GetComponent<SourceIntegrationProbe>();
            var triggerObject = InactiveObject("Trigger");
            triggerObject.transform.position = PhysicsOrigin;
            triggerObject.AddComponent<BoxCollider>().isTrigger = true;
            var source = triggerObject.AddComponent<TriggerEventSource>();
            source.Runner = runner;
            source.Step = step;
            source.Signal = "ok";
            source.Actor = actor;
            triggerObject.SetActive(true);
            Begin(runner);
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();

            yield return MoveUntil(other, PhysicsOrigin, () => otherProbe.TriggerCount > 0);
            Assert.That(otherProbe.TriggerCount, Is.GreaterThan(0), "The other body must really enter the trigger.");
            Assert.That(source.CallbackCount, Is.Zero);
            Assert.That(runner.Session.Steps[0].Attempts, Is.Zero);
            yield return MoveTo(other, PhysicsOrigin + Vector3.right * 3f);
            yield return MoveUntil(actor, PhysicsOrigin, () => source.CallbackCount > 0);
            yield return new WaitForFixedUpdate();

            Assert.That(source.CallbackCount, Is.EqualTo(1));
            Assert.That(runner.Session.Steps[0].Status, Is.EqualTo(StepStatus.Completed));
            Assert.That(runner.Session.Steps[0].Attempts, Is.EqualTo(1));
            runner.Restart();
            Assert.That(source.CallbackCount, Is.Zero);
            Assert.That(runner.Session.Steps[0].Status, Is.EqualTo(StepStatus.Active));
        }

        [UnityTest]
        public IEnumerator DynamicBodyCollidesWithStaticColliderFiltersOtherActorAndResetsCount()
        {
            var step = SignalStep("collision");
            var runner = CreateRunner(step);
            var actor = CreateBody("Expected collision actor", PhysicsOrigin + Vector3.left * 3f, false);
            var other = CreateBody("Other collision actor", PhysicsOrigin + Vector3.right * 3f, false);
            var otherProbe = other.gameObject.GetComponent<SourceIntegrationProbe>();
            var wall = InactiveObject("Static collision wall");
            wall.transform.position = PhysicsOrigin;
            wall.AddComponent<BoxCollider>();
            var source = wall.AddComponent<CollisionEventSource>();
            source.Runner = runner;
            source.Step = step;
            source.Signal = "ok";
            source.Actor = actor;
            wall.SetActive(true);
            Begin(runner);
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();

            yield return MoveUntil(other, PhysicsOrigin, () => otherProbe.CollisionCount > 0);
            Assert.That(otherProbe.CollisionCount, Is.GreaterThan(0), "The other dynamic body must really hit the wall.");
            Assert.That(source.CallbackCount, Is.Zero);
            Assert.That(runner.Session.Steps[0].Attempts, Is.Zero);
            yield return MoveTo(other, PhysicsOrigin + Vector3.right * 3f);
            yield return MoveUntil(actor, PhysicsOrigin, () => source.CallbackCount > 0);
            yield return new WaitForFixedUpdate();

            Assert.That(source.CallbackCount, Is.EqualTo(1));
            Assert.That(runner.Session.Steps[0].Status, Is.EqualTo(StepStatus.Completed));
            Assert.That(runner.Session.Steps[0].Attempts, Is.EqualTo(1));
            runner.Restart();
            Assert.That(source.CallbackCount, Is.Zero);
            Assert.That(runner.Session.Steps[0].Attempts, Is.Zero);
        }

        [UnityTest]
        public IEnumerator ContextActionRetriesUntilSceneStateMatches()
        {
            var condition = CreateAsset<StateConditionAsset>();
            condition.SuccessSignal = "ok";
            condition.FailureSignals = new[] { "bad" };
            condition.StateKey = "power";
            condition.RequiredValue = true;
            var step = Step("context", condition);
            var runner = CreateRunner(step);
            var stateObject = InactiveObject("Power state");
            var state = stateObject.AddComponent<ContextState>();
            state.Key = "power";
            state.InitialValue = false;
            runner.ContextStates = new[] { state };
            stateObject.SetActive(true);
            UnityEventSource source;
            var relay = CreateRelay(runner, step, "ok", out source);
            Begin(runner);
            yield return null;

            relay.Raise();
            Assert.That(runner.Session.Steps[0].Status, Is.EqualTo(StepStatus.Active));
            Assert.That(runner.Session.Steps[0].Violations, Is.EqualTo(1));
            state.SetValue(true);
            relay.Raise();
            Assert.That(runner.Session.IsFinished, Is.True);
            Assert.That(runner.Session.Steps[0].Attempts, Is.EqualTo(2));
            Assert.That(runner.LastAnalysis.IsValid, Is.True);
            Assert.That(runner.LastAnalysis.Score, Is.EqualTo(5));
        }

        [UnityTest]
        public IEnumerator NewRunResetsPhysicalActorSceneStateAndOrderedProgress()
        {
            var condition = CreateAsset<OrderedConditionAsset>();
            condition.Sequence = new[] { "a", "b" };
            condition.FailureSignals = new[] { "bad" };
            var ordered = Step("ordered", condition);
            var runner = CreateRunner(ordered, SignalStep("remaining"));
            var stateObject = InactiveObject("Restart context");
            var state = stateObject.AddComponent<ContextState>();
            state.Key = "power";
            state.InitialValue = false;
            runner.ContextStates = new[] { state };
            stateObject.SetActive(true);
            var home = InactiveObject("Actor home");
            home.transform.position = PhysicsOrigin + Vector3.left * 3f;
            home.SetActive(true);
            var target = InactiveObject("Actor destination");
            target.transform.position = PhysicsOrigin + Vector3.right * 3f;
            target.SetActive(true);
            var actorObject = InactiveObject("Restart actor");
            actorObject.transform.position = home.transform.position;
            actorObject.AddComponent<BoxCollider>();
            var body = actorObject.AddComponent<Rigidbody>();
            var actor = actorObject.AddComponent<DemoActor>();
            actor.Runner = runner;
            actor.Home = home.transform;
            actor.CorridorZ = PhysicsOrigin.z - 2f;
            actor.Speed = 20f;
            actorObject.SetActive(true);
            UnityEventSource aSource;
            UnityEventSource bSource;
            var aRelay = CreateRelay(runner, ordered, "a", out aSource);
            var bRelay = CreateRelay(runner, ordered, "b", out bSource);
            Begin(runner);
            yield return null;
            aRelay.Raise();
            state.SetValue(true);
            actor.MoveTo(target.transform);
            for (var tick = 0; tick < 40 && Vector3.Distance(body.position, home.transform.position) < 1f; tick++)
                yield return new WaitForFixedUpdate();
            Assert.That(Vector3.Distance(body.position, home.transform.position), Is.GreaterThanOrEqualTo(1f));
            Assert.That(runner.Session.Steps[0].ProgressMessage, Is.Not.Empty);
            var oldJournal = runner.LastJournal;

            runner.Restart();

            Assert.That(runner.LastJournal.SessionId, Is.Not.EqualTo(oldJournal.SessionId));
            Assert.That(state.Value, Is.False);
            Assert.That(actor.IsMoving, Is.False);
            Assert.That(Vector3.Distance(body.position, home.transform.position), Is.LessThan(0.01f));
            Assert.That(body.linearVelocity.sqrMagnitude, Is.LessThan(0.001f));
            Assert.That(runner.Session.Steps[0].Attempts, Is.Zero);
            Assert.That(runner.Session.Steps[0].ProgressMessage, Is.Empty);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.That(Vector3.Distance(body.position, home.transform.position), Is.LessThan(0.01f));
            bRelay.Raise();
            Assert.That(runner.Session.Steps[0].Violations, Is.EqualTo(1), "The first run's 'a' must not survive.");
            Assert.That(runner.Session.Steps[0].Status, Is.EqualTo(StepStatus.Active));
        }

        private ScenarioRunner CreateRunner(params StepDefinition[] steps)
        {
            var scenario = CreateAsset<ScenarioDefinition>();
            scenario.Steps.AddRange(steps);
            var mode = CreateAsset<ModeDefinition>();
            mode.DisplayName = "Integration training";
            mode.Scenario = scenario;
            mode.ExecutionPolicy = CreateAsset<TrainingPolicyAsset>();
            mode.Groups.Add(new StepGroup
            {
                Id = "training",
                Title = "Training",
                Steps = new List<StepDefinition>(steps),
                Batches = new List<StepBatch> { new StepBatch { Steps = new List<StepDefinition>(steps) } }
            });
            var runner = InactiveObject("Scenario runner").AddComponent<ScenarioRunner>();
            runner.Modes = new[] { mode };
            runner.Logger = CreateAsset<MemoryLoggerAsset>();
            runner.Analyzer = CreateAsset<TrainingAnalyzerAsset>();
            return runner;
        }

        private static void Begin(ScenarioRunner runner)
        {
            runner.gameObject.SetActive(true);
            runner.Begin(0);
            Assert.That(runner.IsRunning, Is.True, runner.Message);
        }

        private StepDefinition SignalStep(string id)
        {
            var condition = CreateAsset<SignalConditionAsset>();
            condition.SuccessSignal = "ok";
            condition.FailureSignals = new[] { "bad" };
            return Step(id, condition);
        }

        private StepDefinition Step(string id, StepConditionAsset condition)
        {
            var step = CreateAsset<StepDefinition>();
            step.Id = id;
            step.Title = id;
            step.Result = "Done";
            step.Condition = condition;
            return step;
        }

        private UnityEventRelay CreateRelay(ScenarioRunner runner, StepDefinition step, string signal, out UnityEventSource source)
        {
            var item = InactiveObject("UnityEvent " + signal);
            var relay = item.AddComponent<UnityEventRelay>();
            source = item.AddComponent<UnityEventSource>();
            source.Runner = runner;
            source.Step = step;
            source.Signal = signal;
            source.Relay = relay;
            item.SetActive(true);
            return relay;
        }

        private Rigidbody CreateBody(string name, Vector3 position, bool kinematic)
        {
            var item = InactiveObject(name);
            item.transform.position = position;
            item.AddComponent<BoxCollider>().size = Vector3.one * 0.6f;
            var body = item.AddComponent<Rigidbody>();
            body.isKinematic = kinematic;
            body.useGravity = false;
            body.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionY;
            body.collisionDetectionMode = kinematic ? CollisionDetectionMode.ContinuousSpeculative : CollisionDetectionMode.ContinuousDynamic;
            item.AddComponent<SourceIntegrationProbe>();
            item.SetActive(true);
            return body;
        }

        private static IEnumerator MoveUntil(Rigidbody body, Vector3 destination, Func<bool> completed)
        {
            for (var tick = 0; tick < 100; tick++)
            {
                if (completed()) yield break;
                body.MovePosition(Vector3.MoveTowards(body.position, destination, 0.15f));
                yield return new WaitForFixedUpdate();
            }
            Assert.That(completed(), Is.True, "Expected a real physics callback within 100 fixed updates.");
        }

        private static IEnumerator MoveTo(Rigidbody body, Vector3 destination)
        {
            for (var tick = 0; tick < 100 && Vector3.Distance(body.position, destination) > 0.02f; tick++)
            {
                body.MovePosition(Vector3.MoveTowards(body.position, destination, 0.15f));
                yield return new WaitForFixedUpdate();
            }
            Assert.That(Vector3.Distance(body.position, destination), Is.LessThanOrEqualTo(0.02f));
            yield return new WaitForFixedUpdate();
        }

        private GameObject InactiveObject(string name, bool ui = false)
        {
            var item = ui ? new GameObject(name, typeof(RectTransform)) : new GameObject(name);
            item.SetActive(false);
            objects.Add(item);
            return item;
        }

        private T CreateAsset<T>() where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            assets.Add(asset);
            return asset;
        }
    }

    public sealed class SourceIntegrationProbe : MonoBehaviour
    {
        public int TriggerCount { get; private set; }
        public int CollisionCount { get; private set; }
        private void OnTriggerEnter(Collider other) { TriggerCount++; }
        private void OnCollisionEnter(Collision collision) { CollisionCount++; }
    }
}
