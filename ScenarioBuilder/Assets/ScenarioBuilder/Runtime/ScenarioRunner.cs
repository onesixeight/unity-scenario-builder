using System;
using System.Collections.Generic;
using System.IO;
using ScenarioBuilder.Authoring;
using ScenarioBuilder.Core;
using UnityEngine;

namespace ScenarioBuilder.Runtime
{
    public sealed class ScenarioRunner : MonoBehaviour
    {
        [Header("Настройки прохождения")]
        public ModeDefinition[] Modes;
        [Tooltip("Заменяемый алгоритм исполнения. Если не назначен, используется стандартный исполнитель.")]
        public ScenarioExecutorAsset Executor;
        public JournalLoggerAsset Logger;
        public ResultAnalyzerAsset Analyzer;
        [Tooltip("Состояния сцены, доступные контекстным условиям. Ключи должны быть уникальны.")]
        public ContextState[] ContextStates;

        public IScenarioExecutor Session { get; private set; }
        public Journal LastJournal { get; private set; }
        public AnalysisResult LastAnalysis { get; private set; }
        public string Message { get; private set; } = "Выберите режим и начните прохождение.";
        public int SelectedMode { get; private set; }
        public bool IsRunning => Session != null && !Session.IsFinished;
        public event Action Updated;
        public event Action Restarted;
        public string JournalPath => Path.Combine(Application.persistentDataPath, "ScenarioJournal.json");
        private readonly Dictionary<StepDefinition, string> sessionStepIds = new Dictionary<StepDefinition, string>();

        public void Begin(int modeIndex)
        {
            try
            {
                if (Modes == null || modeIndex < 0 || modeIndex >= Modes.Length || Modes[modeIndex] == null)
                    throw new InvalidOperationException("Назначьте конфигурацию режима в ScenarioRunner.");
                if (Logger == null || Analyzer == null)
                    throw new InvalidOperationException("Назначьте логгер и анализатор в ScenarioRunner.");
                ValidateContext();
                var mode = Modes[modeIndex];
                var plan = mode.BuildPlan();
                var logger = Logger.CreateLogger();
                if (logger == null) throw new InvalidOperationException("Логгер не создан.");
                var policy = mode.ExecutionPolicy.CreatePolicy();
                IScenarioExecutor next = Executor != null
                    ? Executor.CreateExecutor(plan, policy, logger)
                    : new ScenarioSession(plan, policy, logger);
                if (next == null) throw new InvalidOperationException("Исполнитель не создан.");
                // A failing logger must not replace the last usable session or its journal.
                next.Start();
                if (Session != null) Session.Changed -= OnSessionChanged;
                SelectedMode = modeIndex;
                Session = next;
                sessionStepIds.Clear();
                foreach (var step in mode.Scenario.Steps)
                    if (step != null && step.Enabled) sessionStepIds[step] = step.Id;
                LastAnalysis = null;
                LastJournal = null;
                if (ContextStates != null)
                    foreach (var state in ContextStates) if (state != null) state.ResetState();
                NotifyRestarted();
                Session.Changed += OnSessionChanged;
                LastJournal = Session.Journal;
                Message = "Начато: " + mode.DisplayName;
            }
            catch (Exception exception)
            {
                Message = "Ошибка настройки: " + exception.Message;
            }
            Updated?.Invoke();
        }

        public void Restart() { Begin(SelectedMode); }

        public void Submit(StepDefinition step, string signal, string eventId = null)
        {
            if (step == null || !sessionStepIds.TryGetValue(step, out var stepId)) return;
            SubmitById(stepId, signal, eventId);
        }

        public void SubmitById(string stepId, string signal, string eventId = null)
        {
            if (!isActiveAndEnabled || !IsRunning) return;
            var action = new ScenarioAction(stepId, signal, eventId);
            if (ContextStates != null)
                foreach (var state in ContextStates)
                    if (state != null && !string.IsNullOrWhiteSpace(state.Key)) action.State[state.Key] = state.Value;
            try { Session.Handle(action); }
            catch (Exception exception)
            {
                Message = "Не удалось обработать действие: " + exception.Message;
                Updated?.Invoke();
            }
        }

        public void Reanalyze(ResultAnalyzerAsset selectedAnalyzer = null)
        {
            if (selectedAnalyzer != null) Analyzer = selectedAnalyzer;
            LastAnalysis = null;
            try
            {
                if (Analyzer == null) throw new InvalidOperationException("Анализатор не выбран.");
                LastAnalysis = Analyzer.CreateAnalyzer().Analyze(LastJournal);
                Message = LastAnalysis.IsValid
                    ? $"{LastAnalysis.AnalyzerName}: {LastAnalysis.Score} / {LastAnalysis.Maximum}"
                    : "Недостаточно данных: " + string.Join("; ", LastAnalysis.Diagnostics);
            }
            catch (Exception exception) { Message = "Ошибка анализа: " + exception.Message; }
            Updated?.Invoke();
        }

        public void ExportJournal()
        {
            try
            {
                if (LastJournal == null) throw new InvalidOperationException("Нет журнала для сохранения.");
                Directory.CreateDirectory(Application.persistentDataPath);
                File.WriteAllText(JournalPath, JsonUtility.ToJson(LastJournal, true));
                Message = "Журнал сохранён: " + JournalPath;
            }
            catch (Exception exception) { Message = "Ошибка сохранения: " + exception.Message; }
            Updated?.Invoke();
        }

        public void ImportJournal()
        {
            try
            {
                if (IsRunning) throw new InvalidOperationException("Завершите прохождение перед загрузкой журнала.");
                LastJournal = JsonUtility.FromJson<Journal>(File.ReadAllText(JournalPath));
                Reanalyze();
            }
            catch (Exception exception)
            {
                Message = "Ошибка загрузки: " + exception.Message;
                Updated?.Invoke();
            }
        }

        private void ValidateContext()
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            if (ContextStates == null) return;
            foreach (var state in ContextStates)
                if (state == null || string.IsNullOrWhiteSpace(state.Key) || !keys.Add(state.Key))
                    throw new InvalidOperationException("Проверьте состояния сцены: пустая ссылка, пустой или повторный ключ.");
        }

        private void NotifyRestarted()
        {
            if (Restarted == null) return;
            foreach (Action subscriber in Restarted.GetInvocationList())
            {
                try { subscriber(); }
                catch (Exception exception) { Debug.LogWarning("Ошибка сброса объекта сцены: " + exception.Message); }
            }
        }

        private void OnSessionChanged()
        {
            LastJournal = Session.Journal;
            Message = Session.LastMessage;
            if (Session.IsFinished) Reanalyze();
            else Updated?.Invoke();
        }

        private void OnDestroy()
        {
            if (Session != null) Session.Changed -= OnSessionChanged;
        }
    }
}
