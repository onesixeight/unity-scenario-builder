using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ScenarioBuilder.Authoring;
using ScenarioBuilder.Core;
using ScenarioBuilder.Runtime;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;

namespace ScenarioBuilder.Editor
{
    /// <summary>Creates the editable demo once; never rebuilds an existing scenario.</summary>
    public static class DemoSceneBuilder
    {
        private const string RootPath = "Assets/ScenarioBuilder/Demo";
        private const string ScenePath = RootPath + "/ScenarioDemo.unity";
        private const float ReferenceWidth = 1920f;
        private const float ReferenceHeight = 1080f;
        private static readonly Color Background = Hex("101722");
        private static readonly Color PanelColor = Hex("1D2939");
        private static readonly Color CardColor = Hex("263548");
        private static readonly Color Blue = Hex("245BB0");
        private static readonly Color Green = Hex("166B57");
        private static readonly Color Red = Hex("753C49");
        private static readonly Color TextColor = Hex("EEF3FB");
        private static readonly string[] DemoInstructions =
        {
            "Нажмите «Выполнить» для проверки связи.",
            "Нажмите «Выполнить» для проверки комплекта.",
            "Нажмите «Выполнить», чтобы передать сигнал.",
            "Нажмите «Войти в зону» и дождитесь входа.",
            "Нажмите «К упору» и дождитесь контакта.",
            "Сначала в зону A, дождитесь входа, затем в зону B.",
            "Включите «Питание» сверху, затем «Выполнить».",
            "Нажмите «Выполнить» для подготовки.",
            "Нажмите «Выполнить», чтобы подтвердить первый узел.",
            "Включите «Защита» сверху, затем нажмите «К доку».",
            "Нажмите «Выполнить», чтобы подтвердить второй узел.",
            "Нажмите «Выполнить», чтобы передать итоговый сигнал."
        };
        private const string HelpInstructions =
            "1. Для первого знакомства выберите «Тренировка», затем «Начать».\n\n" +
            "2. Выполняйте активные шаги: их кнопки зелёные. Несколько активных можно выполнить в любом порядке. Серые ожидают очереди — преждевременное действие считается нарушением.\n\n" +
            "3. На S06 войдите в A, дождитесь входа, затем идите в B. Перед S07 включите питание, перед S10 — защиту над стендом.\n\n" +
            "4. Дождитесь движения участника и физического контакта. Чтобы войти повторно, сначала вернитесь «Домой».\n\n" +
            "5. Красная «Ошибка» специально имитирует нарушение. В тренировке можно повторить шаг, в экзамене ошибка окончательна.\n\n" +
            "Журнал справа прокручивается колёсиком или ползунком. Причины ошибок и пропусков остаются возле заданий слева.";
        private static bool tmpImportPending;

        [MenuItem("Tools/Учебные сценарии/Открыть или создать демо")]
        public static void CreateDemo()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Создайте или откройте демо вне Play Mode.");

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            {
                if (SceneManager.GetActiveScene().path == ScenePath)
                    return;
                RequireSavedScenes();
                EditorSceneManager.OpenScene(ScenePath);
                return;
            }

            RequireSavedScenes();
            EnsureFolder(RootPath);
            EnsureFolder(RootPath + "/Config/Steps");
            EnsureFolder(RootPath + "/Config/Conditions");
            EnsureFolder(RootPath + "/Materials");
            EnsureFolder(RootPath + "/Fonts");

            if (!EnsureTmpResources())
                return;
            var font = CreateFont();
            TMP_Settings.defaultFontAsset = font;
            EditorUtility.SetDirty(TMP_Settings.instance);
            var steps = CreateDefinitions(out var training, out var exam,
                out var memoryLogger, out var trainingAnalyzer, out var examAnalyzer);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("ScenarioDemo");
            var runnerObject = Child("ScenarioRunner", root.transform);
            var runner = runnerObject.AddComponent<ScenarioRunner>();
            runner.Modes = new[] { training, exam };
            runner.Executor = Asset<DefaultExecutorAsset>("Config/DefaultExecutor.asset", "Стандартный исполнитель");
            runner.Logger = memoryLogger;
            runner.Analyzer = trainingAnalyzer;

            var power = Child("PowerState", runnerObject.transform).AddComponent<ContextState>();
            power.Key = "power";
            power.InitialValue = false;
            var shield = Child("ShieldState", runnerObject.transform).AddComponent<ContextState>();
            shield.Key = "shield";
            shield.InitialValue = false;
            runner.ContextStates = new[] { power, shield };

            var actor = CreateWorld(root.transform, runner, steps, font,
                out var home, out var triggerStart, out var collisionStop,
                out var triggerA, out var triggerB, out var collisionDock);
            CreateHud(root.transform, runner, steps, actor, home,
                new[] { triggerStart, collisionStop, triggerA, triggerB, collisionDock },
                power, shield, trainingAnalyzer, examAnalyzer, font);

            PlayerSettings.productName = "Scenario Builder";
            PlayerSettings.companyName = "ScenarioBuilder";
            PlayerSettings.runInBackground = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.resizableWindow = true;
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new IOException("Не удалось сохранить демонстрационную сцену.");

            var buildScenes = EditorBuildSettings.scenes.ToList();
            if (!buildScenes.Any(item => item.path == ScenePath))
                buildScenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = buildScenes.ToArray();
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = runnerObject;
            Debug.Log("Демонстрация создана: " + ScenePath);
        }

        [MenuItem("Tools/Учебные сценарии/Обновить подсказки и прокрутку демо")]
        public static void UpgradeDemoUsability()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Обновите демо вне Play Mode.");
            RequireSavedScenes();
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
                throw new InvalidOperationException("Сначала откройте демонстрационную сцену.");
            var hud = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<DemoHud>(true)).Single();
            ApplyUsability(hud);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Не удалось сохранить демо.");
            AssetDatabase.SaveAssets();
        }

        private static void RequireSavedScenes()
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Сохраните изменения открытой сцены перед созданием или открытием демо.");
        }

        private static StepDefinition[] CreateDefinitions(out ModeDefinition training, out ModeDefinition exam,
            out MemoryLoggerAsset memoryLogger, out TrainingAnalyzerAsset trainingAnalyzer,
            out ExamAnalyzerAsset examAnalyzer)
        {
            var signal = Asset<SignalConditionAsset>("Config/Conditions/Confirm.asset", "Сигнал confirm / ошибка error");
            signal.SuccessSignal = "confirm";
            signal.FailureSignals = new[] { "error" };
            var ordered = Asset<OrderedConditionAsset>("Config/Conditions/TriggerOrder.asset", "Сначала A, затем B");
            ordered.Sequence = new[] { "A", "B" };
            ordered.FailureSignals = new[] { "error" };
            var power = Asset<StateConditionAsset>("Config/Conditions/PowerRequired.asset", "Действие при включённом питании");
            power.SuccessSignal = "confirm";
            power.StateKey = "power";
            power.RequiredValue = true;
            power.FailureSignals = new[] { "error" };
            power.FailureMessage = "Сначала включите «Питание» над стендом.";
            var shield = Asset<StateConditionAsset>("Config/Conditions/ShieldRequired.asset", "Контакт при включённой защите");
            shield.SuccessSignal = "confirm";
            shield.StateKey = "shield";
            shield.RequiredValue = true;
            shield.FailureSignals = new[] { "error" };
            shield.FailureMessage = "Сначала включите «Защита» над стендом, затем двигайтесь к доку.";

            var titles = new[]
            {
                "Проверить связь", "Проверить комплект", "Передать сигнал", "Войти в стартовую зону",
                "Коснуться упора", "Пройти зоны A, затем B", "Подтвердить питание", "Подготовить оборудование",
                "Подтвердить первый узел", "Состыковаться с защитой", "Подтвердить второй узел", "Передать итоговый сигнал"
            };
            var steps = new StepDefinition[12];
            for (var i = 0; i < steps.Length; i++)
            {
                var step = Asset<StepDefinition>($"Config/Steps/S{i + 1:00}.asset", $"S{i + 1:00} · {titles[i]}");
                step.Id = $"S{i + 1:00}";
                step.Title = titles[i];
                step.Description = DemoInstructions[i];
                step.Result = titles[i] + ": выполнено.";
                step.Enabled = true;
                step.Condition = i == 5 ? (StepConditionAsset)ordered : i == 6 ? power : i == 9 ? shield : signal;
                steps[i] = step;
            }

            var scenario = Asset<ScenarioDefinition>("Config/Scenario.asset", "Демонстрационный сценарий · 12 шагов");
            scenario.Steps = new List<StepDefinition>(steps);
            var trainingPolicy = Asset<TrainingPolicyAsset>("Config/TrainingPolicy.asset", "Правила тренировки");
            var examPolicy = Asset<ExamPolicyAsset>("Config/ExamPolicy.asset", "Правила экзамена");
            training = Asset<ModeDefinition>("Config/Training.asset", "Тренировка");
            training.DisplayName = "Тренировка";
            training.Scenario = scenario;
            training.ExecutionPolicy = trainingPolicy;
            training.OutOfOrderPolicy = OutOfOrderPolicy.Violation;
            training.Groups = new List<StepGroup>
            {
                Group(steps, "TRAINING", "Общая тренировочная группа", Enumerable.Range(1, 12).ToArray(),
                    new[] { 1, 2, 3, 4 }, new[] { 5 }, new[] { 6 }, new[] { 7 }, new[] { 8 }, new[] { 9, 10 }, new[] { 11, 12 })
            };
            exam = Asset<ModeDefinition>("Config/Exam.asset", "Экзамен");
            exam.DisplayName = "Экзамен";
            exam.Scenario = scenario;
            exam.ExecutionPolicy = examPolicy;
            exam.OutOfOrderPolicy = OutOfOrderPolicy.Violation;
            exam.Groups = new List<StepGroup>
            {
                Group(steps, "EXAM-1", "Проверка готовности", new[] { 1, 2, 3, 4 }, new[] { 1, 2, 3, 4 }),
                Group(steps, "EXAM-2", "Работа на стенде", new[] { 5, 6, 7, 8 }, new[] { 5, 6 }, new[] { 7, 8 }),
                Group(steps, "EXAM-3", "Завершение", new[] { 9, 10, 11, 12 }, new[] { 9, 10 }, new[] { 11, 12 })
            };

            memoryLogger = Asset<MemoryLoggerAsset>("Config/MemoryLogger.asset", "Журнал в памяти");
            var jsonLogger = Asset<JsonLoggerAsset>("Config/JsonLogger.asset", "Журнал с записью в JSON");
            jsonLogger.FileName = "AutoSavedScenarioJournal.json";
            trainingAnalyzer = Asset<TrainingAnalyzerAsset>("Config/TrainingAnalyzer.asset", "Баллы за попытки");
            examAnalyzer = Asset<ExamAnalyzerAsset>("Config/ExamAnalyzer.asset", "Штрафы за ошибки и пропуски");
            training.SuggestedAnalyzer = trainingAnalyzer;
            exam.SuggestedAnalyzer = examAnalyzer;
            var errors = training.ValidateConfiguration().Concat(exam.ValidateConfiguration()).ToArray();
            if (errors.Length > 0)
                throw new InvalidOperationException("Ошибка создаваемой конфигурации:\n" + string.Join("\n", errors));
            AssetDatabase.SaveAssets();
            return steps;
        }

        private static StepGroup Group(StepDefinition[] steps, string id, string title, int[] members, params int[][] batches)
        {
            return new StepGroup
            {
                Id = id,
                Title = title,
                ViolationThreshold = 2,
                Steps = members.Select(index => steps[index - 1]).ToList(),
                Batches = batches.Select(batch => new StepBatch
                {
                    Steps = batch.Select(index => steps[index - 1]).ToList()
                }).ToList()
            };
        }

        private static DemoActor CreateWorld(Transform parent, ScenarioRunner runner, StepDefinition[] steps,
            TMP_FontAsset font, out Transform home, out Transform triggerStart, out Transform collisionStop,
            out Transform triggerA, out Transform triggerB, out Transform collisionDock)
        {
            var world = Child("PhysicalStand", parent).transform;
            var backdrop = Child("BackdropCamera", world).AddComponent<Camera>();
            backdrop.clearFlags = CameraClearFlags.SolidColor;
            backdrop.backgroundColor = Background;
            backdrop.cullingMask = 0;
            backdrop.depth = -10;

            var camera = Child("StandCamera", world).AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(0f, 11f, -10f);
            camera.transform.LookAt(new Vector3(0f, 0f, -0.4f));
            camera.orthographic = true;
            camera.orthographicSize = 4.2f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Hex("182638");
            camera.rect = new Rect(518f / ReferenceWidth, 1f - 650f / ReferenceHeight,
                860f / ReferenceWidth, 450f / ReferenceHeight);
            camera.depth = 0;
            camera.gameObject.AddComponent<AudioListener>();

            var light = Child("StandLight", world).AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(48f, -30f, 0f);
            light.intensity = 1.3f;
            light.shadows = LightShadows.Soft;
            RenderSettings.ambientLight = new Color(0.5f, 0.55f, 0.65f);

            var floor = Primitive("Floor", PrimitiveType.Cube, world, new Vector3(0f, -0.1f, -0.5f),
                new Vector3(13f, 0.1f, 8f), Material("Floor", Hex("35465C")));
            floor.GetComponent<Collider>().enabled = true;
            home = Child("Home", world).transform;
            home.position = new Vector3(-5f, 0.5f, -2f);
            var actorObject = Primitive("Participant", PrimitiveType.Sphere, world, home.position,
                Vector3.one, Material("Participant", Hex("F7C873")));
            var body = actorObject.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.isKinematic = false;
            body.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionY;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var actor = actorObject.AddComponent<DemoActor>();
            actor.Runner = runner;
            actor.Home = home;
            actor.Speed = 8f;
            actor.CorridorZ = -2f;

            triggerStart = TriggerStation("TriggerStart", -4f, "S04 · СТАРТ", steps[3], "confirm", body, runner, world, camera, font);
            collisionStop = CollisionStation("CollisionStop", -2f, "S05 · УПОР", steps[4], body, runner, world, camera, font);
            triggerA = TriggerStation("TriggerA", 0f, "S06 · A", steps[5], "A", body, runner, world, camera, font);
            triggerB = TriggerStation("TriggerB", 2f, "S06 · B", steps[5], "B", body, runner, world, camera, font);
            collisionDock = CollisionStation("CollisionDock", 4f, "S10 · ДОК", steps[9], body, runner, world, camera, font);

            var corridor = Primitive("SafeCorridor", PrimitiveType.Cube, world, new Vector3(0f, -0.025f, -2f),
                new Vector3(12f, 0.04f, 0.12f), Material("Corridor", Hex("6DA8CF")));
            UnityEngine.Object.DestroyImmediate(corridor.GetComponent<Collider>());
            WorldLabel("HomeLabel", "ДОМОЙ", new Vector3(-5f, 0.1f, -2.6f), world, camera, font, 2.2f);
            return actor;
        }

        private static Transform TriggerStation(string name, float x, string label, StepDefinition step, string signal,
            Rigidbody actor, ScenarioRunner runner, Transform parent, Camera camera, TMP_FontAsset font)
        {
            var station = Child(name, parent);
            station.transform.position = new Vector3(x, 0.5f, 1f);
            var collider = station.AddComponent<BoxCollider>();
            collider.size = new Vector3(1.15f, 1.5f, 1.15f);
            collider.isTrigger = true;
            var marker = Primitive(name + "Marker", PrimitiveType.Cube, station.transform,
                new Vector3(x, 0.07f, 1f), new Vector3(1.1f, 0.14f, 1.1f), Material("Trigger", Hex("39A791")));
            UnityEngine.Object.DestroyImmediate(marker.GetComponent<Collider>());
            var source = station.AddComponent<TriggerEventSource>();
            Configure(source, runner, step, signal);
            source.Actor = actor;
            WorldLabel(name + "Label", label, new Vector3(x, 1.35f, 1f), parent, camera, font, 2.7f);
            return station.transform;
        }

        private static Transform CollisionStation(string name, float x, string label, StepDefinition step,
            Rigidbody actor, ScenarioRunner runner, Transform parent, Camera camera, TMP_FontAsset font)
        {
            var station = Primitive(name, PrimitiveType.Cube, parent, new Vector3(x, 0.5f, 1f),
                new Vector3(0.95f, 1f, 0.95f), Material("Collision", Hex("5B86C9")));
            var source = station.AddComponent<CollisionEventSource>();
            Configure(source, runner, step, "confirm");
            source.Actor = actor;
            WorldLabel(name + "Label", label, new Vector3(x, 1.45f, 1f), parent, camera, font, 2.7f);
            return station.transform;
        }

        private static void CreateHud(Transform parent, ScenarioRunner runner, StepDefinition[] steps,
            DemoActor actor, Transform home, Transform[] stations, ContextState power, ContextState shield,
            TrainingAnalyzerAsset trainingAnalyzer, ExamAnalyzerAsset examAnalyzer, TMP_FontAsset font)
        {
            var canvasObject = new GameObject("ScenarioCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            canvasObject.transform.SetParent(parent, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
            scaler.matchWidthOrHeight = 0.5f;
            var hud = canvasObject.AddComponent<DemoHud>();
            hud.Runner = runner;
            hud.Analyzers = new ResultAnalyzerAsset[] { trainingAnalyzer, examAnalyzer };
            hud.Power = power;
            hud.Shield = shield;

            var eventSystem = Child("EventSystem", parent);
            eventSystem.AddComponent<EventSystem>();
            var input = eventSystem.AddComponent<InputSystemUIInputModule>();
            input.AssignDefaultActions();

            var header = Panel("Header", canvasObject.transform, 0, 0, 1920, 112, PanelColor, true);
            Text("Title", header, "КОНСТРУКТОР СЦЕНАРИЕВ", 24, 14, 420, 40, 25, font, FontStyles.Bold);
            Text("Subtitle", header, "12 шагов · общие определения · два режима", 24, 63, 420, 28, 15, font);
            hud.ModePicker = Dropdown("ModePicker", header, 450, 19, 195, 42, font);
            var start = Button("BeginButton", header, "Начать", 657, 19, 110, 42, Green, font);
            UnityEventTools.AddPersistentListener(start.onClick, hud.BeginSelected);
            var restart = Button("RestartButton", header, "Повторить", 779, 19, 120, 42, Blue, font);
            UnityEventTools.AddPersistentListener(restart.onClick, runner.Restart);
            hud.AnalyzerPicker = Dropdown("AnalyzerPicker", header, 911, 19, 290, 42, font);
            var recalculate = Button("ReanalyzeButton", header, "Пересчитать", 1213, 19, 125, 42, Blue, font);
            UnityEventTools.AddPersistentListener(recalculate.onClick, hud.Reanalyze);
            var save = Button("ExportButton", header, "Сохранить журнал", 1350, 19, 145, 42, Blue, font, 14);
            UnityEventTools.AddPersistentListener(save.onClick, runner.ExportJournal);
            var load = Button("ImportButton", header, "Загрузить журнал", 1507, 19, 145, 42, Blue, font, 14);
            UnityEventTools.AddPersistentListener(load.onClick, runner.ImportJournal);
            var homeButton = Button("HomeButton", header, "Домой", 1664, 19, 210, 42, Green, font);
            WireMovement(homeButton, actor, home);
            hud.StatusText = Text("Status", header, "Готов к запуску", 450, 71, 1424, 26, 17, font);

            var left = Panel("TasksPanel", canvasObject.transform, 24, 130, 470, 900, PanelColor, true);
            Text("TasksHeading", left, "Задания и прогресс", 16, 14, 438, 32, 22, font, FontStyles.Bold);
            Text("TasksHint", left, "Зелёный — активен. Серый — ожидает своей очереди.", 16, 54, 438, 40, 15, font);
            hud.TasksText = ScrollText("Tasks", left, 14, 102, 442, 780, 16, font);

            var standHeader = Panel("StandHeader", canvasObject.transform, 518, 130, 860, 60, PanelColor, true);
            Text("StandHeading", standHeader, "Физический стенд · Trigger / Collision", 10, 6, 415, 34, 18, font, FontStyles.Bold);
            hud.PowerToggle = Toggle("PowerToggle", standHeader, "Питание (S07)", 440, 12, 190, 32, font);
            hud.ShieldToggle = Toggle("ShieldToggle", standHeader, "Защита (S10)", 640, 12, 205, 32, font);
            Text("StandHint", canvasObject.transform,
                "Участник идёт через безопасный коридор. Для повторного входа нажмите «Домой».",
                530, 656, 848, 28, 16, font);
            Text("ActionsHeading", canvasObject.transform, "Действия по шагам", 518, 682, 420, 26, 18, font, FontStyles.Bold);
            Text("ActionsHint", canvasObject.transform, "Действие до очереди — нарушение", 970, 682, 408, 26, 15, font);
            var actions = new GameObject("StepActions", typeof(RectTransform));
            actions.transform.SetParent(canvasObject.transform, false);
            SetRect(actions.GetComponent<RectTransform>(), 518, 714, 860, 316);
            CreateActions(actions.transform, runner, steps, actor, stations, font);

            var right = Panel("ResultsAndJournalPanel", canvasObject.transform, 1402, 130, 494, 900, PanelColor, true);
            Text("ResultHeading", right, "Результат", 14, 14, 466, 32, 22, font, FontStyles.Bold);
            hud.ResultText = ScrollText("Result", right, 14, 52, 466, 280, 15, font);
            Text("JournalHeading", right, "Журнал событий", 14, 354, 466, 32, 22, font, FontStyles.Bold);
            hud.JournalText = ScrollText("Journal", right, 14, 396, 466, 434, 16, font);
            var previous = Button("PreviousJournalPage", right, "Назад", 14, 850, 84, 34, Blue, font, 15);
            UnityEventTools.AddPersistentListener(previous.onClick, hud.PreviousPage);
            hud.JournalPageText = Text("JournalPage", right, "1 / 1", 104, 850, 282, 34, 15, font);
            hud.JournalPageText.alignment = TextAlignmentOptions.Center;
            var next = Button("NextJournalPage", right, "Далее", 394, 850, 86, 34, Blue, font, 15);
            UnityEventTools.AddPersistentListener(next.onClick, hud.NextPage);

            var message = Panel("MessageBar", canvasObject.transform, 0, 1040, 1920, 40, PanelColor, true);
            hud.MessageText = Text("Message", message, "Выберите режим и нажмите «Начать».", 24, 2, 1872, 36, 16, font);
            hud.MessageText.alignment = TextAlignmentOptions.MidlineLeft;
            ApplyUsability(hud);
            hud.Refresh();
        }

        private static void ApplyUsability(DemoHud hud)
        {
            var canvas = hud.transform;
            var font = hud.TasksText.font;
            hud.MessageText.overflowMode = TextOverflowModes.Ellipsis;
            var header = canvas.Find("Header");
            var helpButton = header.Find("HelpButton")?.GetComponent<UnityEngine.UI.Button>();
            if (helpButton == null)
            {
                helpButton = Button("HelpButton", header, "Как пройти", 24, 65, 165, 31, Blue, font, 16);
                UnityEventTools.AddPersistentListener(helpButton.onClick, hud.ToggleHelp);
            }
            var subtitle = header.Find("Subtitle").GetComponent<TMP_Text>();
            SetRect(subtitle.rectTransform, 205, 68, 235, 26);
            subtitle.text = "Тренировка и экзамен";
            subtitle.fontSize = 14;

            canvas.Find("TasksPanel/TasksHint").GetComponent<TMP_Text>().text =
                "Зелёный — активен. Причина ошибки остаётся у шага.";
            var standHint = canvas.Find("StandHint").GetComponent<TMP_Text>();
            SetRect(standHint.rectTransform, 530, 555, 848, 28);
            standHint.text = "Дождитесь входа или контакта. Для повторного входа сначала нажмите «Домой».";
            SetRect(canvas.Find("ActionsHeading").GetComponent<RectTransform>(), 518, 586, 300, 25);
            var actionsHint = canvas.Find("ActionsHint").GetComponent<TMP_Text>();
            SetRect(actionsHint.rectTransform, 820, 586, 558, 36);
            actionsHint.fontSize = 14;
            actionsHint.text = "«Ошибка» — имитация нарушения. Действие до очереди — нарушение.";

            var camera = hud.Runner.transform.parent.Find("PhysicalStand/StandCamera").GetComponent<Camera>();
            camera.rect = new Rect(518f / ReferenceWidth, 1f - 550f / ReferenceHeight,
                860f / ReferenceWidth, 350f / ReferenceHeight);
            foreach (var toggle in new[] { hud.PowerToggle, hud.ShieldToggle })
            {
                var hitArea = toggle.GetComponent<UnityEngine.UI.Image>();
                if (hitArea == null) hitArea = toggle.gameObject.AddComponent<UnityEngine.UI.Image>();
                hitArea.color = Color.clear;
                hitArea.raycastTarget = true;
            }
            var actionsRoot = canvas.Find("StepActions");
            SetRect(actionsRoot.GetComponent<RectTransform>(), 518, 630, 860, 400);
            var actionViews = new List<DemoActionView>();
            for (var index = 0; index < actionsRoot.childCount; index++)
            {
                var card = actionsRoot.GetChild(index);
                SetRect(card.GetComponent<RectTransform>(), index % 3 * 288, index / 3 * 100, 280, 94);
                var step = card.GetComponentsInChildren<ScenarioEventSource>(true).First().Step;
                if (index < DemoInstructions.Length)
                {
                    step.Description = DemoInstructions[index];
                    EditorUtility.SetDirty(step);
                }
                var label = card.Find("Label").GetComponent<TMP_Text>();
                label.enableAutoSizing = true;
                label.fontSizeMin = 12;
                label.fontSizeMax = 15;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.overflowMode = TextOverflowModes.Ellipsis;
                var hint = card.Find("Hint")?.GetComponent<TMP_Text>();
                if (hint == null) hint = Text("Hint", card, "", 8, 25, 264, 30, 13, font);
                hint.enableAutoSizing = true;
                hint.fontSizeMin = 11;
                hint.fontSizeMax = 13;
                hint.overflowMode = TextOverflowModes.Ellipsis;
                var buttons = card.GetComponentsInChildren<UnityEngine.UI.Button>(true);
                foreach (var button in buttons)
                {
                    var rect = button.GetComponent<RectTransform>();
                    rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, -58);
                }
                actionViews.Add(new DemoActionView { Step = step, Title = label, Hint = hint, Buttons = buttons });
            }
            hud.Actions = actionViews.ToArray();
            ConfigureStateHint("PowerRequired", "Сначала включите «Питание» над стендом.");
            ConfigureStateHint("ShieldRequired", "Включите «Защита» сверху, затем нажмите «К доку».");
            foreach (var scroll in canvas.GetComponentsInChildren<UnityEngine.UI.ScrollRect>(true))
                if (scroll.content != null && scroll.transform.name.EndsWith("Scroll", StringComparison.Ordinal))
                    ConfigureScrolling(scroll);
            hud.JournalScroll = hud.JournalText.GetComponentInParent<UnityEngine.UI.ScrollRect>();
            var right = canvas.Find("ResultsAndJournalPanel");
            hud.PreviousJournalButton = right.Find("PreviousJournalPage").GetComponent<UnityEngine.UI.Button>();
            hud.NextJournalButton = right.Find("NextJournalPage").GetComponent<UnityEngine.UI.Button>();
            hud.PreviousJournalButton.GetComponentInChildren<TMP_Text>().text = "В начало";
            hud.NextJournalButton.GetComponentInChildren<TMP_Text>().text = "В конец";
            hud.JournalPageText.fontSize = 13;
            if (hud.HelpPanel == null)
            {
                var overlay = Panel("HelpPanel", canvas, 0, 0, ReferenceWidth, ReferenceHeight,
                    new Color(0.02f, 0.04f, 0.08f, 0.94f), true);
                overlay.GetComponent<UnityEngine.UI.Image>().raycastTarget = true;
                hud.HelpPanel = overlay.gameObject;
                var box = Panel("HelpCard", overlay, 480, 190, 960, 700, PanelColor);
                Text("Heading", box, "Как пройти демо", 30, 25, 900, 45, 30, font, FontStyles.Bold);
                Text("Instructions", box, HelpInstructions,
                    30, 90, 900, 500, 21, font);
                var close = Button("CloseHelp", box, "Понятно, к тренажёру", 30, 615, 900, 52, Green, font, 21);
                UnityEventTools.AddPersistentListener(close.onClick, hud.CloseHelp);
            }
            hud.HelpPanel.transform.Find("HelpCard/Instructions").GetComponent<TMP_Text>().text = HelpInstructions;
            hud.HelpPanel.transform.SetAsLastSibling();
            hud.HelpPanel.SetActive(true);
            hud.Refresh();
            EditorUtility.SetDirty(hud);
        }

        private static void ConfigureStateHint(string name, string message)
        {
            var asset = AssetDatabase.LoadAssetAtPath<StateConditionAsset>(RootPath + "/Config/Conditions/" + name + ".asset");
            if (asset == null) return;
            asset.FailureMessage = message;
            EditorUtility.SetDirty(asset);
        }

        private static void ConfigureScrolling(UnityEngine.UI.ScrollRect scroll)
        {
            // A persistent track makes overflow discoverable and supports dragging as well as the wheel.
            var track = scroll.transform.Find("Scrollbar")?.GetComponent<UnityEngine.UI.Scrollbar>();
            if (track == null)
            {
                var trackObject = new GameObject("Scrollbar", typeof(RectTransform), typeof(UnityEngine.UI.Image),
                    typeof(UnityEngine.UI.Scrollbar));
                trackObject.transform.SetParent(scroll.transform, false);
                var handle = Panel("Handle", trackObject.transform, 0, 0, 16, 40, Hex("8396AF"));
                handle.GetComponent<UnityEngine.UI.Image>().raycastTarget = true;
                Stretch(handle.GetComponent<RectTransform>());
                track = trackObject.GetComponent<UnityEngine.UI.Scrollbar>();
                track.handleRect = handle.GetComponent<RectTransform>();
                track.targetGraphic = handle.GetComponent<UnityEngine.UI.Image>();
                track.direction = UnityEngine.UI.Scrollbar.Direction.BottomToTop;
                var navigation = track.navigation;
                navigation.mode = UnityEngine.UI.Navigation.Mode.None;
                track.navigation = navigation;
            }
            var trackRect = track.GetComponent<RectTransform>();
            trackRect.anchorMin = new Vector2(1, 0);
            trackRect.anchorMax = Vector2.one;
            trackRect.pivot = new Vector2(1, 1);
            trackRect.offsetMin = new Vector2(-16, 0);
            trackRect.offsetMax = Vector2.zero;
            track.GetComponent<UnityEngine.UI.Image>().color = Hex("304056");
            scroll.verticalScrollbar = track;
            scroll.verticalScrollbarVisibility = UnityEngine.UI.ScrollRect.ScrollbarVisibility.Permanent;
            scroll.viewport.offsetMax = new Vector2(-24, 0);
            scroll.scrollSensitivity = 45;
            scroll.inertia = false;
            scroll.content.GetComponent<UnityEngine.UI.VerticalLayoutGroup>().padding = new RectOffset(0, 2, 0, 6);
        }

        private static void CreateActions(Transform parent, ScenarioRunner runner, StepDefinition[] steps,
            DemoActor actor, Transform[] stations, TMP_FontAsset font)
        {
            var shortTitles = new[]
            {
                "Связь", "Комплект", "Сигнал", "Стартовая зона", "Упор", "Зоны A → B",
                "Питание включено", "Подготовка", "Первый узел", "Док с защитой", "Второй узел", "Итоговый сигнал"
            };
            for (var i = 0; i < steps.Length; i++)
            {
                var card = Panel($"Action_{steps[i].Id}", parent, (i % 3) * 288, (i / 3) * 79, 280, 73, CardColor);
                Text("Label", card, steps[i].Id + " · " + shortTitles[i], 8, 4, 264, 23, 15, font, FontStyles.Bold);
                if (i == 5)
                {
                    var a = Button("MoveA", card, "В зону A", 8, 31, 90, 34, Green, font, 14);
                    var b = Button("MoveB", card, "В зону B", 104, 31, 90, 34, Green, font, 14);
                    WireMovement(a, actor, stations[2]);
                    WireMovement(b, actor, stations[3]);
                }
                else
                {
                    var label = i == 3 ? "Войти в зону" : i == 4 ? "К упору" : i == 9 ? "К доку" : "Выполнить";
                    var success = Button("Success", card, label, 8, 31, 186, 34, Green, font, 14);
                    if (i == 3 || i == 4 || i == 9)
                        WireMovement(success, actor, stations[i == 3 ? 0 : i == 4 ? 1 : 4]);
                    else if (i == 2 || i == 7 || i == 11)
                    {
                        var relay = success.gameObject.AddComponent<UnityEventRelay>();
                        var source = success.gameObject.AddComponent<UnityEventSource>();
                        Configure(source, runner, steps[i], "confirm");
                        source.Relay = relay;
                        UnityEventTools.AddPersistentListener(success.onClick, relay.Raise);
                    }
                    else
                    {
                        var source = success.gameObject.AddComponent<ButtonEventSource>();
                        Configure(source, runner, steps[i], "confirm");
                        source.Button = success;
                    }
                }

                var error = Button("Error", card, "Ошибка", 200, 31, 72, 34, Red, font, 13);
                var errorSource = error.gameObject.AddComponent<ButtonEventSource>();
                Configure(errorSource, runner, steps[i], "error");
                errorSource.Button = error;
            }
        }

        private static void Configure(ScenarioEventSource source, ScenarioRunner runner, StepDefinition step, string signal)
        {
            source.Runner = runner;
            source.Step = step;
            source.Signal = signal;
        }

        private static void WireMovement(UnityEngine.UI.Button button, DemoActor actor, Transform target)
        {
            var action = button.gameObject.AddComponent<ActorMoveAction>();
            action.Actor = actor;
            action.Target = target;
            UnityEventTools.AddPersistentListener(button.onClick, action.Execute);
        }

        private static TMP_FontAsset CreateFont()
        {
            const string path = RootPath + "/Fonts/ScenarioFont.asset";
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (existing != null)
                return existing;

            // TMP needs an imported font with readable font data; LegacyRuntime is opaque to FontEngine.
            var source = AssetDatabase.LoadAssetAtPath<Font>("Assets/TextMesh Pro/Fonts/LiberationSans.ttf");
            if (source == null)
                throw new InvalidOperationException("Не найден шрифт из стандартных ресурсов TextMeshPro.");
            var font = TMP_FontAsset.CreateFontAsset(source, 48, 6, GlyphRenderMode.SDFAA, 2048, 2048,
                AtlasPopulationMode.Dynamic, false);
            if (font == null)
                throw new InvalidOperationException("Не удалось создать TMP-шрифт из стандартного шрифта Unity.");
            font.name = "Scenario Font Latin Cyrillic";
            var characters = new StringBuilder();
            for (var code = 32; code <= 126; code++) characters.Append((char)code);
            for (var code = 0x0400; code <= 0x045F; code++) characters.Append((char)code);
            characters.Append("№–—•→·… «»▼");
            if (!font.TryAddCharacters(characters.ToString(), out var missing))
                Debug.LogWarning("Стандартный шрифт не содержит некоторые символы: " + missing);
            font.atlasPopulationMode = AtlasPopulationMode.Static;
            AssetDatabase.CreateAsset(font, path);
            font.material.name = "Scenario Font Material";
            if (!AssetDatabase.Contains(font.material))
                AssetDatabase.AddObjectToAsset(font.material, font);
            for (var i = 0; i < font.atlasTextures.Length; i++)
            {
                var atlas = font.atlasTextures[i];
                if (atlas == null) continue;
                atlas.name = "Scenario Font Atlas " + i;
                if (!AssetDatabase.Contains(atlas))
                    AssetDatabase.AddObjectToAsset(atlas, font);
            }
            EditorUtility.SetDirty(font);
            AssetDatabase.SaveAssets();
            return font;
        }

        private static bool EnsureTmpResources()
        {
            // Standard TMP shaders and settings are required even when the font is generated locally.
            // Use the noninteractive Unity API, never the modal import menu command.
            if (tmpImportPending)
                return false;
            if (TmpResourcesAvailable())
                return true;

            tmpImportPending = true;
            AssetDatabase.importPackageCompleted += OnTmpImportCompleted;
            AssetDatabase.importPackageFailed += OnTmpImportFailed;
            AssetDatabase.importPackageCancelled += OnTmpImportCancelled;
            try
            {
                Debug.Log("Импортируются стандартные ресурсы TextMeshPro. После импорта демо будет создано автоматически.");
                TMP_PackageResourceImporter.ImportResources(true, false, false);
            }
            catch
            {
                ResetTmpImport();
                throw;
            }
            return false;
        }

        private static bool TmpResourcesAvailable()
        {
            return Resources.Load<TMP_Settings>("TMP Settings") != null &&
                   Shader.Find("TextMeshPro/Mobile/Distance Field") != null;
        }

        private static bool IsTmpPackage(string packageName)
        {
            return string.IsNullOrEmpty(packageName) ||
                   packageName.IndexOf("TMP Essential Resources", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void OnTmpImportCompleted(string packageName)
        {
            if (!tmpImportPending || !IsTmpPackage(packageName)) return;
            RemoveTmpImportCallbacks();
            // Package import completion can precede the final asset refresh. Continue on an Editor tick.
            EditorApplication.delayCall += ContinueAfterTmpImport;
        }

        private static void ContinueAfterTmpImport()
        {
            if (!tmpImportPending) return;
            if (EditorApplication.isUpdating || EditorApplication.isCompiling)
            {
                EditorApplication.delayCall += ContinueAfterTmpImport;
                return;
            }

            tmpImportPending = false;
            try
            {
                if (!TmpResourcesAvailable())
                    throw new InvalidOperationException("Импорт TextMeshPro завершился, но стандартные настройки или SDF-шейдер недоступны.");
                CreateDemo();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static void OnTmpImportFailed(string packageName, string error)
        {
            if (!tmpImportPending || !IsTmpPackage(packageName)) return;
            ResetTmpImport();
            Debug.LogError("Не удалось импортировать стандартные ресурсы TextMeshPro: " + error);
        }

        private static void OnTmpImportCancelled(string packageName)
        {
            if (!tmpImportPending || !IsTmpPackage(packageName)) return;
            ResetTmpImport();
            Debug.LogWarning("Импорт стандартных ресурсов TextMeshPro отменён. Генерацию демо можно запустить повторно.");
        }

        private static void ResetTmpImport()
        {
            RemoveTmpImportCallbacks();
            EditorApplication.delayCall -= ContinueAfterTmpImport;
            tmpImportPending = false;
        }

        private static void RemoveTmpImportCallbacks()
        {
            AssetDatabase.importPackageCompleted -= OnTmpImportCompleted;
            AssetDatabase.importPackageFailed -= OnTmpImportFailed;
            AssetDatabase.importPackageCancelled -= OnTmpImportCancelled;
        }

        private static T Asset<T>(string relativePath, string name) where T : ScriptableObject
        {
            var path = RootPath + "/" + relativePath;
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(asset, path);
            }
            asset.name = name;
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var slash = path.LastIndexOf('/');
            var parent = path.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }

        private static Material Material(string name, Color color)
        {
            var path = RootPath + "/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader != null)
                material = new Material(shader);
            else if (GraphicsSettings.currentRenderPipeline != null && GraphicsSettings.currentRenderPipeline.defaultMaterial != null)
                material = new Material(GraphicsSettings.currentRenderPipeline.defaultMaterial);
            else
                throw new InvalidOperationException("Не найден стандартный материал URP.");
            material.name = name;
            material.color = color;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.2f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static GameObject Primitive(string name, PrimitiveType type, Transform parent,
            Vector3 position, Vector3 scale, Material material)
        {
            var gameObject = GameObject.CreatePrimitive(type);
            gameObject.name = name;
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.position = position;
            gameObject.transform.localScale = scale;
            gameObject.GetComponent<Renderer>().sharedMaterial = material;
            return gameObject;
        }

        private static void WorldLabel(string name, string value, Vector3 position, Transform parent,
            Camera camera, TMP_FontAsset font, float size)
        {
            var label = Child(name, parent).AddComponent<TextMeshPro>();
            label.transform.position = position;
            label.transform.rotation = camera.transform.rotation;
            label.transform.localScale = Vector3.one;
            label.rectTransform.sizeDelta = new Vector2(30f, 8f);
            label.font = font;
            label.fontSize = size;
            label.text = value;
            label.color = TextColor;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
        }

        private static GameObject Child(string name, Transform parent)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child;
        }

        private static Transform Panel(string name, Transform parent, float x, float y, float width, float height,
            Color color, bool responsive = false)
        {
            var panel = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image));
            panel.transform.SetParent(parent, false);
            var rect = panel.GetComponent<RectTransform>();
            SetRect(rect, x, y, width, height);
            if (responsive)
            {
                rect.anchorMin = new Vector2(x / ReferenceWidth, 1f - (y + height) / ReferenceHeight);
                rect.anchorMax = new Vector2((x + width) / ReferenceWidth, 1f - y / ReferenceHeight);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }
            var image = panel.GetComponent<UnityEngine.UI.Image>();
            image.color = color;
            image.raycastTarget = false;
            return panel.transform;
        }

        private static TextMeshProUGUI Text(string name, Transform parent, string value, float x, float y,
            float width, float height, float size, TMP_FontAsset font, FontStyles style = FontStyles.Normal)
        {
            var gameObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            gameObject.transform.SetParent(parent, false);
            SetRect(gameObject.GetComponent<RectTransform>(), x, y, width, height);
            var text = gameObject.GetComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = TextColor;
            text.text = value;
            text.raycastTarget = false;
            text.richText = true;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            text.alignment = TextAlignmentOptions.TopLeft;
            return text;
        }

        private static UnityEngine.UI.Button Button(string name, Transform parent, string caption, float x, float y,
            float width, float height, Color color, TMP_FontAsset font, float fontSize = 16f)
        {
            var gameObject = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button));
            gameObject.transform.SetParent(parent, false);
            SetRect(gameObject.GetComponent<RectTransform>(), x, y, width, height);
            var image = gameObject.GetComponent<UnityEngine.UI.Image>();
            image.color = color;
            var button = gameObject.GetComponent<UnityEngine.UI.Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.highlightedColor = new Color(1.18f, 1.18f, 1.18f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            button.colors = colors;
            var label = Text("Label", gameObject.transform, caption, 6, 0, width - 12, height, fontSize, font);
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            return button;
        }

        private static TMP_Dropdown Dropdown(string name, Transform parent, float x, float y, float width, float height,
            TMP_FontAsset font)
        {
            var gameObject = TMP_DefaultControls.CreateDropdown(new TMP_DefaultControls.Resources());
            gameObject.name = name;
            gameObject.transform.SetParent(parent, false);
            SetRect(gameObject.GetComponent<RectTransform>(), x, y, width, height);
            gameObject.GetComponent<UnityEngine.UI.Image>().color = Blue;
            var dropdown = gameObject.GetComponent<TMP_Dropdown>();
            dropdown.ClearOptions();
            foreach (var text in gameObject.GetComponentsInChildren<TMP_Text>(true))
            {
                text.font = font;
                text.fontSize = 16;
                text.color = TextColor;
            }
            var templateImage = dropdown.template.GetComponent<UnityEngine.UI.Image>();
            if (templateImage != null) templateImage.color = PanelColor;
            foreach (var image in dropdown.template.GetComponentsInChildren<UnityEngine.UI.Image>(true))
                image.color = CardColor;
            foreach (var toggle in dropdown.template.GetComponentsInChildren<UnityEngine.UI.Toggle>(true))
                if (toggle.graphic != null) toggle.graphic.color = Hex("6EE7B7");
            var arrow = gameObject.transform.Find("Arrow");
            if (arrow != null)
            {
                var arrowImage = arrow.GetComponent<UnityEngine.UI.Image>();
                if (arrowImage != null) arrowImage.enabled = false;
                var caret = Text("Caret", arrow, "▼", 0, 0, 20, 20, 14, font);
                Stretch(caret.rectTransform);
                caret.alignment = TextAlignmentOptions.Center;
            }
            return dropdown;
        }

        private static UnityEngine.UI.Toggle Toggle(string name, Transform parent, string caption,
            float x, float y, float width, float height, TMP_FontAsset font)
        {
            var gameObject = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Toggle));
            gameObject.transform.SetParent(parent, false);
            SetRect(gameObject.GetComponent<RectTransform>(), x, y, width, height);
            var background = Panel("Background", gameObject.transform, 0, 4, 24, 24, Hex("53647A"));
            var check = Panel("Checkmark", background, 4, 4, 16, 16, Hex("6EE7B7"));
            var backgroundImage = background.GetComponent<UnityEngine.UI.Image>();
            backgroundImage.raycastTarget = true;
            var toggle = gameObject.GetComponent<UnityEngine.UI.Toggle>();
            toggle.targetGraphic = backgroundImage;
            toggle.graphic = check.GetComponent<UnityEngine.UI.Image>();
            toggle.isOn = false;
            var label = Text("Label", gameObject.transform, caption, 33, 0, width - 33, height, 16, font);
            label.alignment = TextAlignmentOptions.MidlineLeft;
            return toggle;
        }

        private static TextMeshProUGUI ScrollText(string name, Transform parent, float x, float y,
            float width, float height, float size, TMP_FontAsset font)
        {
            var scrollObject = new GameObject(name + "Scroll", typeof(RectTransform), typeof(UnityEngine.UI.ScrollRect));
            scrollObject.transform.SetParent(parent, false);
            SetRect(scrollObject.GetComponent<RectTransform>(), x, y, width, height);
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Mask));
            viewport.transform.SetParent(scrollObject.transform, false);
            Stretch(viewport.GetComponent<RectTransform>());
            viewport.GetComponent<UnityEngine.UI.Image>().color = Color.white;
            viewport.GetComponent<UnityEngine.UI.Mask>().showMaskGraphic = false;
            var content = new GameObject("Content", typeof(RectTransform), typeof(UnityEngine.UI.VerticalLayoutGroup),
                typeof(UnityEngine.UI.ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = Vector2.one;
            contentRect.pivot = new Vector2(0f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = Vector2.zero;
            var layout = content.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            content.GetComponent<UnityEngine.UI.ContentSizeFitter>().verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            var text = Text(name + "Text", content.transform, "", 0, 0, width, height, size, font);
            var scroll = scrollObject.GetComponent<UnityEngine.UI.ScrollRect>();
            scroll.viewport = viewport.GetComponent<RectTransform>();
            scroll.content = contentRect;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28f;
            return text;
        }

        private static void SetRect(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static Color Hex(string value)
        {
            if (!ColorUtility.TryParseHtmlString("#" + value, out var color))
                throw new ArgumentException("Invalid color: " + value);
            return color;
        }
    }
}
