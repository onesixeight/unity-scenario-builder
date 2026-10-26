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
        public DemoActionView[] Actions;
        public GameObject HelpPanel;
        public UnityEngine.UI.ScrollRect JournalScroll;
        public UnityEngine.UI.Button PreviousJournalButton;
        public UnityEngine.UI.Button NextJournalButton;
        private int knownEntryCount;
        private Journal knownJournal;

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
                if (Runner.Analyzer != null && (Analyzers == null || Array.IndexOf(Analyzers, Runner.Analyzer) < 0))
                {
                    var configured = new System.Collections.Generic.List<ResultAnalyzerAsset>(Analyzers ?? Array.Empty<ResultAnalyzerAsset>());
                    configured.Add(Runner.Analyzer);
                    Analyzers = configured.ToArray();
                }
                var labels = new System.Collections.Generic.List<string>();
                if (Analyzers != null)
                    foreach (var analyzer in Analyzers) labels.Add(analyzer != null ? analyzer.name : "Не назначен");
                AnalyzerPicker.AddOptions(labels);
                AnalyzerPicker.onValueChanged.AddListener(SelectAnalyzer);
            }
            if (PowerToggle != null) PowerToggle.onValueChanged.AddListener(SetPower);
            if (ShieldToggle != null) ShieldToggle.onValueChanged.AddListener(SetShield);
            var configuredIndex = Analyzers != null ? Array.IndexOf(Analyzers, Runner.Analyzer) : -1;
            if (configuredIndex >= 0 && AnalyzerPicker != null)
                AnalyzerPicker.SetValueWithoutNotify(configuredIndex);
            else SelectModeDefaults(ModePicker != null ? ModePicker.value : 0);
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

        // Keep method names so existing scene button bindings remain valid.
        public void PreviousPage() { if (JournalScroll != null) JournalScroll.verticalNormalizedPosition = 1f; }
        public void NextPage() { if (JournalScroll != null) JournalScroll.verticalNormalizedPosition = 0f; }
        public void SetPower(bool value) { if (Power != null) Power.SetValue(value); Refresh(); }
        public void SetShield(bool value) { if (Shield != null) Shield.SetValue(value); Refresh(); }
        public void ToggleHelp() { if (HelpPanel != null) HelpPanel.SetActive(!HelpPanel.activeSelf); }
        public void CloseHelp() { if (HelpPanel != null) HelpPanel.SetActive(false); }

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
            knownEntryCount = 0;
            knownJournal = null;
            if (PowerToggle != null && Power != null) PowerToggle.SetIsOnWithoutNotify(Power.Value);
            if (ShieldToggle != null && Shield != null) ShieldToggle.SetIsOnWithoutNotify(Shield.Value);
        }

        public void Refresh()
        {
            if (Runner == null) return;
            if (MessageText != null) MessageText.text = Runner.Message;
            var session = Runner.Session;
            var viewingImportedJournal = Runner.LastJournal != null &&
                (session == null || !ReferenceEquals(Runner.LastJournal, session.Journal));
            if (StatusText != null)
                StatusText.text = viewingImportedJournal ? "Просмотр загруженного журнала · можно сменить анализатор"
                    : session == null ? "Готов к запуску"
                    : session.IsFinished ? "Прохождение завершено · можно сменить анализатор"
                    : $"Группа: {session.CurrentGroupId}   ·   Нарушений в группе: {session.CurrentGroupViolations}";

            if (TasksText != null)
            {
                var text = new StringBuilder();
                if (viewingImportedJournal) text.Append("Открыт сохранённый журнал.\n\nСправа показаны его события и результат. Можно выбрать другой анализатор без повторного прохождения.\n\nЧтобы начать новый сценарий, выберите режим и нажмите «Начать».");
                else if (session == null) text.Append("1. Для первого знакомства выберите «Тренировка» и нажмите «Начать».\n\n2. Выполняйте зелёные активные шаги. Условия написаны на карточках действий.\n\n3. Перед S07 включите питание, перед S10 — защиту над стендом.\n\nКнопка «Как пройти» открывает краткую инструкцию.");
                else foreach (var step in session.Steps)
                {
                    text.Append("<color=").Append(StatusColor(step.Status)).Append("><b>")
                        .Append(step.Spec.Id).Append(" · ").Append(StatusLabel(step.Status)).Append("</b></color>\n")
                        .Append(step.Spec.Title).Append("\n<color=#A6B2C5>Попытки: ")
                        .Append(step.Attempts).Append(" · нарушения: ").Append(step.Violations).Append("</color>\n");
                    var reason = LastStepReason(step.Spec.Id, step.Status);
                    if (!string.IsNullOrEmpty(step.ProgressMessage) && step.Status == StepStatus.Active &&
                        (reason == null || !reason.EndsWith(step.ProgressMessage, StringComparison.Ordinal)))
                        text.Append("<size=15>").Append(step.ProgressMessage).Append("</size>\n");
                    if (!string.IsNullOrEmpty(reason))
                        text.Append("<size=14><color=#FCD34D>").Append(reason).Append("</color></size>\n");
                    text.Append("<size=6> </size>\n");
                }
                TasksText.text = text.ToString();
            }

            var journal = Runner.LastJournal;
            var count = journal?.Entries?.Count ?? 0;
            if (journal != knownJournal) { knownEntryCount = 0; knownJournal = journal; }
            var followLatest = count != knownEntryCount &&
                (knownEntryCount == 0 || JournalScroll == null || JournalScroll.verticalNormalizedPosition < 0.05f ||
                 JournalScroll.content.rect.height <= JournalScroll.viewport.rect.height + 1f);
            if (PreviousJournalButton != null) PreviousJournalButton.interactable = count > 0;
            if (NextJournalButton != null) NextJournalButton.interactable = count > 0;
            if (JournalPageText != null) JournalPageText.text = $"Записей: {count} · прокрутка колёсиком";
            if (JournalText != null)
            {
                var text = new StringBuilder();
                if (count == 0) text.Append("Здесь появятся факты прохождения.\nЖурнал можно сохранить и загрузить после перезапуска приложения.");
                else for (var i = 0; i < count; i++)
                {
                    var entry = journal.Entries[i];
                    if (entry == null) continue;
                    var time = DateTime.TryParse(entry.TimestampUtc, out var parsed) ? parsed.ToLocalTime().ToString("HH:mm:ss") : "—";
                    text.Append("<color=#A6B2C5>").Append(time).Append(" · ").Append(entry.StepId ?? "Сессия")
                        .Append("</color>\n").Append(EventLabel(entry.Type)).Append(" · ").Append(StatusLabel(entry.Result));
                    if (entry.Attempt > 0) text.Append("  <size=14>попытка ").Append(entry.Attempt).Append("</size>");
                    text.Append('\n');
                    if (!string.IsNullOrEmpty(entry.Message)) text.Append("<size=15>").Append(entry.Message).Append("</size>\n");
                    text.Append("<size=6> </size>\n");
                }
                var value = text.ToString();
                if (JournalText.text != value)
                {
                    JournalText.text = value;
                    if (JournalScroll != null && JournalScroll.content != null)
                    {
                        // TMP needs the final viewport width before reporting wrapped preferred height.
                        Canvas.ForceUpdateCanvases();
                        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(JournalScroll.content);
                        if (followLatest) JournalScroll.verticalNormalizedPosition = 0f;
                    }
                }
            }
            knownEntryCount = count;

            if (Actions != null)
                foreach (var action in Actions) RefreshAction(action, viewingImportedJournal);
            RefreshToggleLabel(PowerToggle, "Питание", Power);
            RefreshToggleLabel(ShieldToggle, "Защита", Shield);

            if (ResultText != null)
            {
                var result = Runner.LastAnalysis;
                if (result == null) ResultText.text = "Итог появится после завершения.\nВыбор другого анализатора пересчитает тот же журнал.";
                else if (!result.IsValid) ResultText.text = "Расчёт недоступен\n" + string.Join("\n", result.Diagnostics);
                else
                {
                    var text = new StringBuilder($"<size=29><b>{result.Score} / {result.Maximum}</b></size>  {result.AnalyzerName}\n");
                    foreach (var row in result.Steps)
                        text.Append(row.Id).Append("  ").Append(StatusLabel(row.Status)).Append("  попыток: ").Append(row.Attempts)
                            .Append("  балл: ").Append(row.Score).Append("  штраф: ").Append(row.Penalty).Append('\n');
                    foreach (var diagnostic in result.Diagnostics) text.Append(diagnostic).Append('\n');
                    ResultText.text = text.ToString();
                }
            }
        }

        private string LastStepReason(string id, StepStatus status)
        {
            var entries = Runner.LastJournal?.Entries;
            if (entries == null) return null;
            for (var index = entries.Count - 1; index >= 0; index--)
            {
                var entry = entries[index];
                if (entry == null || entry.StepId != id) continue;
                if (entry.Type == JournalEvents.StepSkipped)
                {
                    var count = Field(entry, "groupViolations");
                    var threshold = "";
                    if (Runner.LastJournal.Groups != null)
                        foreach (var group in Runner.LastJournal.Groups)
                            if (group != null && group.Id == entry.GroupId) { threshold = group.ViolationThreshold.ToString(); break; }
                    if (string.IsNullOrEmpty(count) || string.IsNullOrEmpty(threshold)) return entry.Message;
                    for (var causeIndex = index - 1; causeIndex >= 0; causeIndex--)
                    {
                        var cause = entries[causeIndex];
                        if (cause == null || cause.Type != JournalEvents.Violation || cause.GroupId != entry.GroupId) continue;
                        var reason = Field(cause, "reason") == "out-of-order" ? "действие до очереди"
                            : Field(cause, "signal") == "error" ? "кнопка «Ошибка»" : cause.Message;
                        return "Порог группы " + entry.GroupId + " (" + count + "/" + threshold + "). Причина: " + cause.StepId + " — " + reason + ".";
                    }
                }
                if (entry.Type == JournalEvents.StepSkipped || entry.Type == JournalEvents.StepFailed ||
                    entry.Type == JournalEvents.Violation)
                    return status == StepStatus.Completed ? "Ранее: " + entry.Message
                        : status == StepStatus.Active ? "Последнее нарушение: " + entry.Message : entry.Message;
            }
            return null;
        }

        private static string Field(JournalEntry entry, string key)
        {
            if (entry.Extra != null)
                foreach (var field in entry.Extra)
                    if (field != null && field.Key == key) return field.Value;
            return "";
        }

        private void RefreshAction(DemoActionView view, bool viewingImportedJournal)
        {
            if (view == null || view.Step == null) return;
            IStepProgress progress = null;
            if (Runner.Session != null && !viewingImportedJournal)
                foreach (var item in Runner.Session.Steps)
                    if (item.Spec.Id == view.Step.Id) { progress = item; break; }
            if (view.Title != null)
                view.Title.text = (progress?.Spec.Id ?? view.Step.Id) + " · " + (progress?.Spec.Title ?? view.Step.Title);
            var color = "#A6B2C5";
            var hint = viewingImportedJournal ? "Просмотр журнала · новый запуск: «Начать»"
                : Runner.Session != null ? "Не участвует в этом прохождении"
                : "Выберите режим и нажмите «Начать».";
            var buttonColor = new Color32(66, 80, 99, 255);
            var interactive = Runner.IsRunning && progress != null &&
                (progress.Status == StepStatus.Active || progress.Status == StepStatus.Pending);
            if (progress != null)
            {
                color = StatusColor(progress.Status);
                switch (progress.Status)
                {
                    case StepStatus.Pending:
                        hint = "Ожидает очереди · действие даст нарушение";
                        var mode = Runner.Modes[Runner.SelectedMode];
                        if (mode.OutOfOrderPolicy == OutOfOrderPolicy.Ignore)
                            hint = "Ожидает очереди · действие игнорируется";
                        break;
                    case StepStatus.Active:
                        buttonColor = new Color32(22, 107, 87, 255);
                        hint = "Активен · " + view.Step.Description;
                        if (!string.IsNullOrEmpty(progress.ProgressMessage)) hint = "Активен · " + progress.ProgressMessage;
                        if (view.Step.Condition is StateConditionAsset stateCondition)
                        {
                            var state = FindState(stateCondition.StateKey);
                            if (state == null || state.Value != stateCondition.RequiredValue)
                            {
                                color = "#FCD34D";
                                buttonColor = new Color32(126, 91, 36, 255);
                                hint = "Активен · " + (string.IsNullOrWhiteSpace(stateCondition.FailureMessage)
                                    ? view.Step.Description : stateCondition.FailureMessage);
                            }
                            else hint = "Активен · условие выполнено, можно действовать";
                        }
                        break;
                    default: hint = StatusLabel(progress.Status) + " · подробности слева"; break;
                }
            }
            if (view.Hint != null) view.Hint.text = "<color=" + color + ">" + hint + "</color>";
            if (view.Buttons == null) return;
            foreach (var button in view.Buttons)
            {
                if (button == null) continue;
                button.interactable = interactive;
                if (button.name != "Error" && button.targetGraphic != null) button.targetGraphic.color = buttonColor;
            }
        }

        private ContextState FindState(string key)
        {
            if (Runner.ContextStates != null)
                foreach (var state in Runner.ContextStates)
                    if (state != null && state.Key == key) return state;
            return null;
        }

        private static void RefreshToggleLabel(UnityEngine.UI.Toggle toggle, string title, ContextState state)
        {
            if (toggle == null || state == null) return;
            var label = toggle.GetComponentInChildren<TMP_Text>();
            if (label != null) label.text = title + (state.Value ? " · ВКЛ" : " · выкл");
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
                case StepStatus.Completed: return "Выполнен";
                case StepStatus.Failed: return "Ошибка";
                case StepStatus.Skipped: return "Пропущен";
                default: return "Ожидает";
            }
        }

        private static string StatusLabel(string status)
        {
            if (status == "Started") return "Начато";
            return Enum.TryParse(status, out StepStatus parsed) ? StatusLabel(parsed) : status;
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

    [Serializable]
    public sealed class DemoActionView
    {
        public StepDefinition Step;
        public TMP_Text Title;
        public TMP_Text Hint;
        public UnityEngine.UI.Button[] Buttons;
    }
}
