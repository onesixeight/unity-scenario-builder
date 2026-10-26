using System;
using System.Text;
using ScenarioBuilder.Authoring;
using ScenarioBuilder.Core;
using TMPro;
using UnityEngine;

namespace ScenarioBuilder.Runtime
{
    public sealed class DemoHud : MonoBehaviour
    {
        public ScenarioRunner Runner;
        public TMP_Dropdown ModePicker;
        public TMP_Dropdown AnalyzerPicker;
        public ResultAnalyzerAsset[] Analyzers;
        public TMP_Text TasksText;
        public TMP_Text MessageText;
        public TMP_Text JournalText;
        public TMP_Text JournalPageText;
        public TMP_Text ResultText;
        public TMP_Text StatusText;
        public UnityEngine.UI.Toggle PowerToggle;
        public UnityEngine.UI.Toggle ShieldToggle;
        public ContextState Power;
        public ContextState Shield;
        private const int PageSize = 9;
        private int journalPage;
        private int knownEntryCount;

        private void OnEnable()
        {
            if (Runner == null) return;
            Runner.Updated += Refresh;
            Runner.Restarted += ResetView;
            if (ModePicker != null)
            {
                ModePicker.ClearOptions();
                var labels = new System.Collections.Generic.List<string>();
                if (Runner.Modes != null)
                    foreach (var mode in Runner.Modes) labels.Add(mode != null ? mode.DisplayName : "Не назначен");
                ModePicker.AddOptions(labels);
                ModePicker.onValueChanged.AddListener(SelectModeDefaults);
            }
            if (AnalyzerPicker != null)
            {
                AnalyzerPicker.ClearOptions();
                var labels = new System.Collections.Generic.List<string>();
                if (Analyzers != null)
                    foreach (var analyzer in Analyzers) labels.Add(analyzer != null ? analyzer.name : "Не назначен");
                AnalyzerPicker.AddOptions(labels);
                AnalyzerPicker.onValueChanged.AddListener(SelectAnalyzer);
            }
            if (PowerToggle != null) PowerToggle.onValueChanged.AddListener(SetPower);
            if (ShieldToggle != null) ShieldToggle.onValueChanged.AddListener(SetShield);
            SelectModeDefaults(ModePicker != null ? ModePicker.value : 0);
            Refresh();
        }

        private void OnDisable()
        {
            if (Runner != null) { Runner.Updated -= Refresh; Runner.Restarted -= ResetView; }
            if (AnalyzerPicker != null) AnalyzerPicker.onValueChanged.RemoveListener(SelectAnalyzer);
            if (ModePicker != null) ModePicker.onValueChanged.RemoveListener(SelectModeDefaults);
            if (PowerToggle != null) PowerToggle.onValueChanged.RemoveListener(SetPower);
            if (ShieldToggle != null) ShieldToggle.onValueChanged.RemoveListener(SetShield);
        }

        public void BeginSelected()
        {
            SelectAnalyzer(AnalyzerPicker != null ? AnalyzerPicker.value : 0);
            Runner.Begin(ModePicker != null ? ModePicker.value : 0);
        }

        public void Reanalyze()
        {
            var index = AnalyzerPicker != null ? AnalyzerPicker.value : 0;
            if (Analyzers == null || index < 0 || index >= Analyzers.Length) return;
            Runner.Reanalyze(Analyzers[index]);
        }

        public void PreviousPage() { journalPage = Mathf.Max(0, journalPage - 1); Refresh(); }
        public void NextPage() { journalPage++; Refresh(); }
        public void SetPower(bool value) { if (Power != null) Power.SetValue(value); }
        public void SetShield(bool value) { if (Shield != null) Shield.SetValue(value); }

        private void SelectAnalyzer(int index)
        {
            if (Analyzers == null || index < 0 || index >= Analyzers.Length || Analyzers[index] == null) return;
            Runner.Analyzer = Analyzers[index];
            if (Runner.LastJournal != null && !Runner.IsRunning) Runner.Reanalyze();
        }

        private void SelectModeDefaults(int index)
        {
            if (Runner.Modes == null || index < 0 || index >= Runner.Modes.Length || Runner.Modes[index] == null || Analyzers == null) return;
            var suggested = Runner.Modes[index].SuggestedAnalyzer;
            var analyzerIndex = Array.IndexOf(Analyzers, suggested);
            if (suggested == null || analyzerIndex < 0) return;
            if (AnalyzerPicker != null) AnalyzerPicker.SetValueWithoutNotify(analyzerIndex);
            SelectAnalyzer(analyzerIndex);
        }

        private void ResetView()
        {
            journalPage = 0;
            knownEntryCount = 0;
            if (PowerToggle != null && Power != null) PowerToggle.SetIsOnWithoutNotify(Power.Value);
            if (ShieldToggle != null && Shield != null) ShieldToggle.SetIsOnWithoutNotify(Shield.Value);
        }

        public void Refresh()
        {
            if (Runner == null) return;
            if (MessageText != null) MessageText.text = Runner.Message;
            var session = Runner.Session;
            if (StatusText != null)
                StatusText.text = session == null ? "Готов к запуску"
                    : session.IsFinished ? "Прохождение завершено · можно сменить анализатор"
                    : $"Группа: {session.CurrentGroupId}   ·   Нарушений в группе: {session.CurrentGroupViolations}";

            if (TasksText != null)
            {
                var text = new StringBuilder();
                if (session == null) text.Append("Запустите тренировку или экзамен.\n\nАктивные шаги будут выделены зелёным.\n\nКнопки действий доступны всегда: действие до очереди фиксируется как нарушение.\n\nДля повторного физического входа сначала нажмите «Домой».");
                else foreach (var step in session.Steps)
                {
                    text.Append("<color=").Append(StatusColor(step.Status)).Append("><b>")
                        .Append(step.Spec.Id).Append(" · ").Append(StatusLabel(step.Status)).Append("</b></color>\n")
                        .Append(step.Spec.Title).Append("\n<color=#A6B2C5>Попытки: ")
                        .Append(step.Attempts).Append(" · нарушения: ").Append(step.Violations).Append("</color>\n");
                    if (!string.IsNullOrEmpty(step.ProgressMessage) && step.Status == StepStatus.Active)
                        text.Append("<size=15>").Append(step.ProgressMessage).Append("</size>\n");
                    text.Append("<size=6> </size>\n");
                }
                TasksText.text = text.ToString();
            }

            var journal = Runner.LastJournal;
            var count = journal?.Entries?.Count ?? 0;
            var pages = Mathf.Max(1, (count + PageSize - 1) / PageSize);
            if (count != knownEntryCount) { journalPage = pages - 1; knownEntryCount = count; }
            journalPage = Mathf.Clamp(journalPage, 0, pages - 1);
            if (JournalPageText != null) JournalPageText.text = $"{journalPage + 1} / {pages}   ·   {count} записей";
            if (JournalText != null)
            {
                var text = new StringBuilder();
                if (count == 0) text.Append("Здесь появятся факты прохождения.\nЖурнал можно сохранить и загрузить после перезапуска приложения.");
                else for (var i = journalPage * PageSize; i < Mathf.Min(count, (journalPage + 1) * PageSize); i++)
                {
                    var entry = journal.Entries[i];
                    if (entry == null) continue;
                    var time = DateTime.TryParse(entry.TimestampUtc, out var parsed) ? parsed.ToLocalTime().ToString("HH:mm:ss") : "—";
                    text.Append("<color=#A6B2C5>").Append(time).Append(" · ").Append(entry.StepId ?? "Сессия")
                        .Append("</color>\n").Append(EventLabel(entry.Type)).Append(" · ").Append(entry.Result)
                        .Append("  <size=14>попытка ").Append(entry.Attempt).Append("</size>\n");
                    if (!string.IsNullOrEmpty(entry.Message)) text.Append("<size=15>").Append(entry.Message).Append("</size>\n");
                    text.Append("<size=6> </size>\n");
                }
                JournalText.text = text.ToString();
            }

            if (ResultText != null)
            {
                var result = Runner.LastAnalysis;
                if (result == null) ResultText.text = "Итог появится после завершения.\nВыбор другого анализатора пересчитает тот же журнал.";
                else if (!result.IsValid) ResultText.text = "Расчёт недоступен\n" + string.Join("\n", result.Diagnostics);
                else
                {
                    var text = new StringBuilder($"<size=29><b>{result.Score} / {result.Maximum}</b></size>  {result.AnalyzerName}\n");
                    foreach (var row in result.Steps)
                        text.Append(row.Id).Append("  ").Append(row.Status).Append("  попыток: ").Append(row.Attempts)
                            .Append("  балл: ").Append(row.Score).Append("  штраф: ").Append(row.Penalty).Append('\n');
                    foreach (var diagnostic in result.Diagnostics) text.Append(diagnostic).Append('\n');
                    ResultText.text = text.ToString();
                }
            }
        }

        private static string StatusColor(StepStatus status)
        {
            switch (status)
            {
                case StepStatus.Active: return "#6EE7B7";
                case StepStatus.Completed: return "#93C5FD";
                case StepStatus.Failed: return "#FCA5A5";
                case StepStatus.Skipped: return "#FCD34D";
                default: return "#A6B2C5";
            }
        }

        private static string StatusLabel(StepStatus status)
        {
            switch (status)
            {
                case StepStatus.Active: return "Активен";
                case StepStatus.Completed: return "Completed";
                case StepStatus.Failed: return "Failed";
                case StepStatus.Skipped: return "Skipped";
                default: return "Ожидает";
            }
        }

        private static string EventLabel(string type)
        {
            switch (type)
            {
                case JournalEvents.SessionStarted: return "Начало";
                case JournalEvents.SessionCompleted: return "Завершение";
                case JournalEvents.StepActivated: return "Активация";
                case JournalEvents.AttemptStarted: return "Попытка";
                case JournalEvents.ConditionProgress: return "Прогресс условия";
                case JournalEvents.Violation: return "Нарушение";
                case JournalEvents.StepCompleted: return "Успех";
                case JournalEvents.StepFailed: return "Ошибка";
                case JournalEvents.StepSkipped: return "Пропуск";
                case JournalEvents.ActionIgnored: return "Игнорирование";
                default: return type;
            }
        }
    }
}
