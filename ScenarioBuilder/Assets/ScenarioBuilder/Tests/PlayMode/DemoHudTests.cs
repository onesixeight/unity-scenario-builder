using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using ScenarioBuilder.Authoring;
using ScenarioBuilder.Core;
using ScenarioBuilder.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace ScenarioBuilder.Tests
{
    public sealed class DemoHudTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private readonly List<UnityEngine.Object> assets = new List<UnityEngine.Object>();

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
        public IEnumerator JournalKeepsAllEntriesGrowsAndAcceptsScrollEvents()
        {
            var fixture = CreateFixture();
            yield return SettleLayout(fixture);
            var initialHeight = fixture.Scroll.content.rect.height;
            for (var index = 0; index < 14; index++)
                fixture.Runner.Submit(fixture.Step, Marker(index));
            yield return SettleLayout(fixture);

            Assert.That(fixture.Runner.LastJournal.Entries.Count, Is.GreaterThan(9));
            for (var index = 0; index < 14; index++)
                Assert.That(fixture.JournalText.text, Does.Contain(Marker(index)), "An earlier journal entry was lost.");
            Assert.That(fixture.Scroll.content.rect.height, Is.GreaterThan(initialHeight));
            Assert.That(fixture.Scroll.content.rect.height, Is.GreaterThan(fixture.Scroll.viewport.rect.height));
            Assert.That(fixture.Scroll.verticalNormalizedPosition, Is.LessThan(0.02f),
                "A journal that starts short must follow new entries when it first overflows.");
            var before = fixture.Scroll.content.anchoredPosition.y;
            var scrollEvent = new PointerEventData(fixture.EventSystem) { scrollDelta = new Vector2(0f, 6f) };

            var delivered = ExecuteEvents.Execute(fixture.Scroll.gameObject, scrollEvent, ExecuteEvents.scrollHandler);
            yield return null;

            Assert.That(delivered, Is.True);
            Assert.That(Mathf.Abs(fixture.Scroll.content.anchoredPosition.y - before), Is.GreaterThan(10f));
            Assert.That(fixture.Scroll.verticalNormalizedPosition, Is.GreaterThan(0.05f));
        }

        [UnityTest]
        public IEnumerator ReadingPositionSurvivesRefreshAndNewEntriesWhileBottomFollowsLatest()
        {
            var fixture = CreateFixture();
            for (var index = 0; index < 14; index++)
                fixture.Runner.Submit(fixture.Step, Marker(index));
            yield return SettleLayout(fixture);
            fixture.Scroll.StopMovement();
            fixture.Scroll.verticalNormalizedPosition = 0.5f;
            yield return SettleLayout(fixture);
            var reading = VisibleCharacter(fixture);

            fixture.Hud.Refresh();
            yield return SettleLayout(fixture);
            Assert.That(Vector3.Distance(reading.WorldPosition, CharacterPosition(fixture.JournalText, reading.SourceIndex)), Is.LessThan(1f));
            fixture.Runner.Submit(fixture.Step, Marker(14));
            yield return SettleLayout(fixture);
            Assert.That(Vector3.Distance(reading.WorldPosition, CharacterPosition(fixture.JournalText, reading.SourceIndex)), Is.LessThan(1f),
                "Appending a record must not move the text currently being read.");

            fixture.Scroll.StopMovement();
            fixture.Scroll.verticalNormalizedPosition = 0f;
            yield return SettleLayout(fixture);
            fixture.Runner.Submit(fixture.Step, Marker(15));
            yield return SettleLayout(fixture);

            Assert.That(fixture.JournalText.text, Does.Contain(Marker(15)));
            Assert.That(fixture.Scroll.verticalNormalizedPosition, Is.LessThan(0.02f));
            var lastCharacter = LastVisibleCharacter(fixture.JournalText);
            var viewportPoint = fixture.Scroll.viewport.InverseTransformPoint(
                fixture.JournalText.transform.TransformPoint(lastCharacter.bottomLeft));
            var viewportTopRight = fixture.Scroll.viewport.InverseTransformPoint(
                fixture.JournalText.transform.TransformPoint(lastCharacter.topRight));
            var viewportRect = fixture.Scroll.viewport.rect;
            var textRect = fixture.JournalText.rectTransform.rect;
            var contentRect = fixture.Scroll.content.rect;
            var contentBottom = fixture.Scroll.viewport.InverseTransformPoint(
                fixture.Scroll.content.TransformPoint(new Vector3(0f, contentRect.yMin, 0f)));
            Assert.That(fixture.Scroll.viewport.rect.Contains(new Vector2(viewportPoint.x, viewportPoint.y)), Is.True,
                "The newest visible text must remain inside the viewport when following the bottom. " +
                $"Tail character='{lastCharacter.character}', sourceIndex={lastCharacter.index}, line={lastCharacter.lineNumber}; " +
                $"viewport bottomLeft={viewportPoint}, topRight={viewportTopRight}, rect={viewportRect}; " +
                $"text preferredHeight={fixture.JournalText.preferredHeight:F3}, rect={textRect}, " +
                $"content rect={contentRect}, contentBottomInViewport={contentBottom}, " +
                $"anchoredPosition={fixture.Scroll.content.anchoredPosition}, " +
                $"normalized={fixture.Scroll.verticalNormalizedPosition:F6}, " +
                $"margin={fixture.JournalText.margin}, glyph baseline={lastCharacter.baseLine:F3}, " +
                $"ascender={lastCharacter.ascender:F3}, descender={lastCharacter.descender:F3}, " +
                $"font={fixture.JournalText.font.name}, lines={fixture.JournalText.textInfo.lineCount}.");
        }

        [UnityTest]
        public IEnumerator PowerToggleUpdatesContextAndActionHintWithoutStartingAnAttempt()
        {
            var fixture = CreateFixture(true);
            yield return SettleLayout(fixture);
            var entryCount = fixture.Runner.LastJournal.Entries.Count;
            var blockedHint = fixture.Hint.text;
            Assert.That(fixture.Power.Value, Is.False);
            Assert.That(blockedHint, Does.Contain("Включите питание"));
            var click = new PointerEventData(fixture.EventSystem) { button = PointerEventData.InputButton.Left };

            Assert.That(ExecuteEvents.Execute(fixture.PowerToggle.gameObject, click, ExecuteEvents.pointerClickHandler), Is.True);
            yield return null;

            Assert.That(fixture.Power.Value, Is.True);
            Assert.That(fixture.Hint.text, Is.Not.EqualTo(blockedHint));
            Assert.That(fixture.Hint.text, Does.Not.Contain("Включите питание"));
            Assert.That(fixture.PowerLabel.text, Does.Contain("ВКЛ"));
            Assert.That(fixture.Runner.Session.Steps[0].Attempts, Is.Zero);
            Assert.That(fixture.Runner.Session.Steps[0].Violations, Is.Zero);
            Assert.That(fixture.Runner.LastJournal.Entries.Count, Is.EqualTo(entryCount));

            ExecuteEvents.Execute(fixture.PowerToggle.gameObject, click, ExecuteEvents.pointerClickHandler);
            yield return null;
            Assert.That(fixture.Power.Value, Is.False);
            Assert.That(fixture.Hint.text, Is.EqualTo(blockedHint));
            Assert.That(fixture.Runner.Session.Steps[0].Attempts, Is.Zero);
            Assert.That(fixture.Runner.LastJournal.Entries.Count, Is.EqualTo(entryCount));
        }

        [UnityTest]
        public IEnumerator OptionalGroupMetadataDoesNotBreakSkippedStepDisplayOrAnalysis()
        {
            for (var variant = 0; variant < 2; variant++)
            {
                var fixture = CreateFixture(exam: true);
                fixture.Runner.LastJournal.Groups = variant == 0 ? null : new List<GroupSnapshot> { null };
                fixture.Runner.Submit(fixture.Step, Marker(0));
                yield return SettleLayout(fixture);

                Assert.That(fixture.Runner.Session.IsFinished, Is.True);
                Assert.That(fixture.Runner.LastAnalysis.IsValid, Is.True);
                Assert.That(fixture.Runner.LastAnalysis.Score, Is.EqualTo(85));
                Assert.That(fixture.Runner.Session.Steps[1].Status, Is.EqualTo(StepStatus.Skipped));
                Assert.That(fixture.Hud.TasksText.text, Does.Contain("Пропущен"));
                Assert.That(fixture.JournalText.text, Does.Contain("Пропуск"));
                Assert.DoesNotThrow(fixture.Hud.Refresh);
                if (variant == 0)
                {
                    Assert.That(fixture.Runner.LastAnalysis.Diagnostics, Has.Some.Contains("снимка групп"));
                    Assert.That(fixture.Hud.ResultText.text, Does.Contain("снимка групп"));
                }
            }
        }

        private Fixture CreateFixture(bool context = false, bool exam = false)
        {
            // Use the configured standard font; shared TMP resources belong to the project.
            var font = TMP_Settings.defaultFontAsset;
            if (font == null) font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            Assert.That(font, Is.Not.Null, "Configure a standard TMP font before running HUD integration tests.");
            var step = Asset<StepDefinition>();
            step.Id = "hud-step";
            step.Title = "HUD step";
            step.Description = "Включите питание перед подтверждением.";
            step.Result = "Done";
            if (context)
            {
                var condition = Asset<StateConditionAsset>();
                condition.SuccessSignal = "ok";
                condition.StateKey = "power";
                condition.RequiredValue = true;
                condition.FailureMessage = "Включите питание перед действием.";
                step.Condition = condition;
            }
            else
            {
                var condition = Asset<SignalConditionAsset>();
                condition.SuccessSignal = "ok";
                var failures = new string[32];
                for (var index = 0; index < failures.Length; index++) failures[index] = Marker(index);
                condition.FailureSignals = failures;
                step.Condition = condition;
            }
            var scenario = Asset<ScenarioDefinition>();
            scenario.Steps.Add(step);
            if (exam)
            {
                var peer = Asset<StepDefinition>();
                peer.Id = "skipped-peer";
                peer.Title = "Skipped peer";
                peer.Condition = step.Condition;
                scenario.Steps.Add(peer);
            }
            var mode = Asset<ModeDefinition>();
            mode.DisplayName = "HUD training";
            mode.Scenario = scenario;
            mode.ExecutionPolicy = exam ? (ExecutionPolicyAsset)Asset<ExamPolicyAsset>() : Asset<TrainingPolicyAsset>();
            mode.Groups.Add(new StepGroup
            {
                Id = "training", Title = "Training",
                ViolationThreshold = exam ? 1 : 2,
                Steps = new List<StepDefinition>(scenario.Steps),
                Batches = new List<StepBatch> { new StepBatch { Steps = new List<StepDefinition>(scenario.Steps) } }
            });
            var runner = Object("HUD runner").AddComponent<ScenarioRunner>();
            runner.Modes = new[] { mode };
            runner.Logger = Asset<MemoryLoggerAsset>();
            runner.Analyzer = exam ? (ResultAnalyzerAsset)Asset<ExamAnalyzerAsset>() : Asset<TrainingAnalyzerAsset>();
            var power = Object("HUD power").AddComponent<ContextState>();
            power.Key = "power";
            power.InitialValue = false;
            runner.ContextStates = new[] { power };

            var canvasObject = Object("HUD test canvas", null, true);
            canvasObject.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObject.AddComponent<GraphicRaycaster>();
            var scrollObject = Object("Journal scroll", canvasObject.transform, true);
            Rect(scrollObject).sizeDelta = new Vector2(440f, 180f);
            scrollObject.AddComponent<Image>();
            var scroll = scrollObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.inertia = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 45f;
            var viewport = Object("Journal viewport", scrollObject.transform, true);
            Stretch(Rect(viewport));
            viewport.AddComponent<Image>();
            viewport.AddComponent<Mask>().showMaskGraphic = false;
            var contentObject = Object("Journal content", viewport.transform, true);
            var content = Rect(contentObject);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0f, 1f);
            content.sizeDelta = Vector2.zero;
            content.anchoredPosition = Vector2.zero;
            var layout = contentObject.AddComponent<VerticalLayoutGroup>();
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            // Match ConfigureScrolling: SDF glyph quads extend below TMP's preferred-height bounds.
            layout.padding = new RectOffset(0, 2, 0, 6);
            var fitter = contentObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var journalText = Text("Journal text", contentObject.transform, font);
            journalText.rectTransform.anchorMin = new Vector2(0f, 1f);
            journalText.rectTransform.anchorMax = new Vector2(0f, 1f);
            journalText.rectTransform.pivot = new Vector2(0f, 1f);
            scroll.viewport = Rect(viewport);
            scroll.content = content;
            var hint = Text("Action hint", canvasObject.transform, font);
            hint.rectTransform.anchoredPosition = new Vector2(0f, -150f);
            var toggleObject = Object("Power toggle", canvasObject.transform, true);
            Rect(toggleObject).anchoredPosition = new Vector2(0f, -250f);
            Rect(toggleObject).sizeDelta = new Vector2(220f, 40f);
            var toggle = toggleObject.AddComponent<Toggle>();
            toggle.SetIsOnWithoutNotify(false);
            var powerLabel = Text("Power label", toggleObject.transform, font);
            Stretch(powerLabel.rectTransform);
            var hud = canvasObject.AddComponent<DemoHud>();
            hud.Runner = runner;
            hud.Analyzers = new[] { runner.Analyzer };
            hud.JournalText = journalText;
            hud.TasksText = Text("Tasks text", canvasObject.transform, font);
            hud.ResultText = Text("Result text", canvasObject.transform, font);
            hud.JournalScroll = scroll;
            hud.Power = power;
            hud.PowerToggle = toggle;
            hud.Actions = new[] { new DemoActionView { Step = step, Hint = hint } };

            var eventSystem = EventSystem.current;
            if (eventSystem == null) eventSystem = Object("HUD event system").AddComponent<EventSystem>();
            foreach (var item in objects) item.SetActive(true);
            runner.Begin(0);
            Assert.That(runner.IsRunning, Is.True, runner.Message);
            return new Fixture
            {
                Runner = runner, Step = step, Hud = hud, Scroll = scroll,
                JournalText = journalText, Hint = hint, Power = power,
                PowerToggle = toggle, PowerLabel = powerLabel, EventSystem = eventSystem
            };
        }

        private static IEnumerator SettleLayout(Fixture fixture)
        {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(fixture.Scroll.content);
            fixture.JournalText.ForceMeshUpdate();
            yield return null;
            Canvas.ForceUpdateCanvases();
            fixture.JournalText.ForceMeshUpdate();
        }

        private static ReadingPosition VisibleCharacter(Fixture fixture)
        {
            var info = fixture.JournalText.textInfo;
            for (var index = 0; index < info.characterCount; index++)
            {
                var character = info.characterInfo[index];
                if (!character.isVisible) continue;
                var world = fixture.JournalText.transform.TransformPoint(character.bottomLeft);
                var local = fixture.Scroll.viewport.InverseTransformPoint(world);
                if (fixture.Scroll.viewport.rect.Contains(new Vector2(local.x, local.y)))
                    return new ReadingPosition { SourceIndex = character.index, WorldPosition = world };
            }
            Assert.Fail("The middle of the journal should show readable text.");
            return default;
        }

        private static Vector3 CharacterPosition(TMP_Text text, int sourceIndex)
        {
            for (var index = 0; index < text.textInfo.characterCount; index++)
            {
                var character = text.textInfo.characterInfo[index];
                if (character.index == sourceIndex) return text.transform.TransformPoint(character.bottomLeft);
            }
            Assert.Fail("Previously visible journal text disappeared.");
            return Vector3.zero;
        }

        private static TMP_CharacterInfo LastVisibleCharacter(TMP_Text text)
        {
            for (var index = text.textInfo.characterCount - 1; index >= 0; index--)
                if (text.textInfo.characterInfo[index].isVisible)
                    return text.textInfo.characterInfo[index];
            Assert.Fail("The journal should have a visible last character.");
            return default;
        }

        private TextMeshProUGUI Text(string name, Transform parent, TMP_FontAsset font)
        {
            var text = Object(name, parent, true).AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSize = 18f;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.raycastTarget = false;
            text.rectTransform.sizeDelta = new Vector2(440f, 70f);
            return text;
        }

        private GameObject Object(string name, Transform parent = null, bool ui = false)
        {
            var item = ui ? new GameObject(name, typeof(RectTransform)) : new GameObject(name);
            item.SetActive(false);
            if (parent != null) item.transform.SetParent(parent, false);
            objects.Add(item);
            return item;
        }

        private T Asset<T>() where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            assets.Add(asset);
            return asset;
        }

        private static RectTransform Rect(GameObject item) => (RectTransform)item.transform;
        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static string Marker(int index) => "journal-marker-" + index.ToString("D2");

        private struct ReadingPosition
        {
            public int SourceIndex;
            public Vector3 WorldPosition;
        }

        private sealed class Fixture
        {
            public ScenarioRunner Runner;
            public StepDefinition Step;
            public DemoHud Hud;
            public ScrollRect Scroll;
            public TMP_Text JournalText;
            public TMP_Text Hint;
            public ContextState Power;
            public Toggle PowerToggle;
            public TMP_Text PowerLabel;
            public EventSystem EventSystem;
        }
    }
}
