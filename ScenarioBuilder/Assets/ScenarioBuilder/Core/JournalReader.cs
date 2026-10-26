using System;
using System.Collections.Generic;

namespace ScenarioBuilder.Core
{
    /// <summary>Checks only the shared facts needed by scoring; unknown events remain extensible.</summary>
    internal static class JournalReader
    {
        private sealed class StepFacts
        {
            public StepScore Row;
            public JournalEntry Terminal;
            public int StartedAttempt;
            public int FailedAttempt;
            public int CountedViolations;
        }

        public static AnalysisResult Read(Journal journal, string analyzerName, int maximum)
        {
            var result = new AnalysisResult { AnalyzerName = analyzerName, Maximum = maximum, IsValid = true };
            if (journal == null)
            {
                Error(result, "Журнал не выбран.");
                return result;
            }
            if (journal.SchemaVersion <= 0) Error(result, "В журнале отсутствует версия контракта.");
            else if (journal.SchemaVersion > 1)
                Warn(result, "Версия журнала новее версии анализатора; используются известные поля контракта.");
            if (string.IsNullOrWhiteSpace(journal.SessionId)) Error(result, "В журнале отсутствует идентификатор прохождения.");
            if (journal.Steps == null || journal.Steps.Count == 0)
            {
                Error(result, "Журнал не содержит снимок шагов прохождения.");
                return result;
            }

            var facts = new Dictionary<string, StepFacts>(StringComparer.Ordinal);
            foreach (var snapshot in journal.Steps)
            {
                if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.Id))
                {
                    Error(result, "Снимок содержит шаг без идентификатора.");
                    continue;
                }
                if (facts.ContainsKey(snapshot.Id))
                {
                    Error(result, "Повторный идентификатор шага в снимке: " + snapshot.Id + ".");
                    continue;
                }
                var row = new StepScore
                {
                    Id = snapshot.Id,
                    Title = string.IsNullOrWhiteSpace(snapshot.Title) ? snapshot.Id : snapshot.Title,
                    Status = "Pending"
                };
                facts.Add(snapshot.Id, new StepFacts { Row = row });
                result.Steps.Add(row);
            }
            if (journal.Groups == null || journal.Groups.Count == 0)
                Warn(result, "Нет снимка групп; для выбранного алгоритма достаточно снимка шагов.");
            if (string.IsNullOrWhiteSpace(journal.StartedUtc))
                Warn(result, "Не задано время начала; на расчёт баллов это не влияет.");
            if (journal.Entries == null)
            {
                Error(result, "В журнале отсутствует список событий.");
                return result;
            }

            var entriesById = new Dictionary<string, JournalEntry>(StringComparer.Ordinal);
            bool started = false;
            bool completed = false;
            bool missingEventTime = false;
            foreach (var entry in journal.Entries)
            {
                if (entry == null)
                {
                    Error(result, "Журнал содержит пустую запись.");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(entry.Type))
                {
                    Error(result, "Запись не содержит тип события.");
                    continue;
                }
                // New event kinds and their extra fields do not participate in this reader.
                if (!IsKnown(entry.Type)) continue;
                if (string.IsNullOrWhiteSpace(entry.Id))
                    Error(result, "Известное событие не содержит идентификатор записи.");
                else if (entriesById.TryGetValue(entry.Id, out var previous))
                {
                    if (!Equivalent(previous, entry)) Error(result, "Противоречивые записи с одним ID: " + entry.Id + ".");
                    continue;
                }
                else entriesById.Add(entry.Id, entry);

                if (string.IsNullOrWhiteSpace(entry.TimestampUtc)) missingEventTime = true;
                if (entry.Type == JournalEvents.SessionStarted)
                {
                    if (started || completed) Error(result, "Повторное или несвоевременное начало прохождения.");
                    started = true;
                    continue;
                }
                if (!started) Error(result, "Событие записано до начала прохождения.");
                if (entry.Type == JournalEvents.SessionCompleted)
                {
                    if (completed) Error(result, "Повторное завершение прохождения.");
                    completed = true;
                    continue;
                }
                // Ignored actions can document unknown targets, and never affect scores.
                if (entry.Type == JournalEvents.ActionIgnored) continue;
                if (completed) Error(result, "Событие шага записано после завершения прохождения.");
                if (string.IsNullOrWhiteSpace(entry.StepId) || !facts.TryGetValue(entry.StepId, out var step))
                {
                    Error(result, "Событие относится к шагу, которого нет в снимке: " + (entry.StepId ?? "<нет ID>") + ".");
                    continue;
                }
                if (entry.Attempt < 0 || entry.Violations < 0)
                    Error(result, "Отрицательный счётчик в шаге " + entry.StepId + ".");
                step.Row.Violations = Math.Max(step.Row.Violations, Math.Max(0, entry.Violations));
                if (entry.Type == JournalEvents.Violation)
                {
                    // Pending-step violations legitimately have Attempt=0 and the CURRENT group ID.
                    step.CountedViolations++;
                    if (entry.Attempt > 0) step.FailedAttempt = Math.Max(step.FailedAttempt, entry.Attempt);
                    continue;
                }
                if (entry.Type == JournalEvents.AttemptStarted)
                {
                    if (entry.Attempt <= 0) Error(result, "Номер начавшейся попытки должен быть положительным: " + entry.StepId + ".");
                    step.StartedAttempt = Math.Max(step.StartedAttempt, entry.Attempt);
                    continue;
                }
                string status = TerminalStatus(entry.Type);
                if (status == null) continue;
                if (step.Terminal != null)
                {
                    Error(result, "Для шага записано несколько терминальных событий: " + entry.StepId + ".");
                    continue;
                }
                step.Terminal = entry;
                step.Row.Status = status;
                step.Row.Attempts = Math.Max(0, entry.Attempt);
                if (!string.IsNullOrEmpty(entry.Result) && !string.Equals(entry.Result, status, StringComparison.Ordinal))
                    Error(result, "Результат события противоречит его типу: " + entry.StepId + ".");
                if (status != "Skipped" && entry.Attempt <= 0)
                    Error(result, "Для завершённого или ошибочного шага отсутствует номер попытки: " + entry.StepId + ".");
            }

            if (!started) Error(result, "Нет события начала прохождения.");
            if (!completed) Error(result, "Прохождение не завершено; итоговая оценка недоступна.");
            if (missingEventTime) Warn(result, "У части событий отсутствует время; на расчёт баллов это не влияет.");
            foreach (var step in facts.Values)
            {
                step.Row.Violations = Math.Max(step.Row.Violations, step.CountedViolations);
                if (step.Terminal == null)
                    Error(result, "Нет итогового статуса шага " + step.Row.Id + ".");
                else if (step.FailedAttempt > step.Row.Attempts)
                    Error(result, "Итоговый номер попытки меньше попытки с зарегистрированной ошибкой: " + step.Row.Id + ".");
                else if (step.Row.Status == "Completed" && step.FailedAttempt > 0 && step.Row.Attempts <= step.FailedAttempt)
                    Error(result, "Успех после ошибки должен относиться к следующей попытке: " + step.Row.Id + ".");
                else if (step.StartedAttempt > step.Row.Attempts)
                    Error(result, "Итоговый номер попытки меньше зарегистрированного: " + step.Row.Id + ".");
                else if (step.Row.Attempts > 0 && step.StartedAttempt == 0)
                    Warn(result, "Шаг " + step.Row.Id + ": используются итоговые данные о попытках без промежуточных событий.");
            }
            return result;
        }

        private static bool IsKnown(string type)
        {
            return type == JournalEvents.SessionStarted || type == JournalEvents.SessionCompleted ||
                   type == JournalEvents.StepActivated || type == JournalEvents.AttemptStarted ||
                   type == JournalEvents.ConditionProgress || type == JournalEvents.Violation ||
                   type == JournalEvents.StepCompleted || type == JournalEvents.StepFailed ||
                   type == JournalEvents.StepSkipped || type == JournalEvents.ActionIgnored;
        }

        private static string TerminalStatus(string type)
        {
            if (type == JournalEvents.StepCompleted) return "Completed";
            if (type == JournalEvents.StepFailed) return "Failed";
            if (type == JournalEvents.StepSkipped) return "Skipped";
            return null;
        }

        private static bool Equivalent(JournalEntry a, JournalEntry b)
        {
            return a.Type == b.Type && a.StepId == b.StepId && a.GroupId == b.GroupId &&
                   a.Result == b.Result && a.Attempt == b.Attempt && a.Violations == b.Violations;
        }

        private static void Error(AnalysisResult result, string message)
        {
            result.IsValid = false;
            result.Diagnostics.Add(message);
        }

        private static void Warn(AnalysisResult result, string message)
        {
            result.Diagnostics.Add("Предупреждение: " + message);
        }
    }
}
