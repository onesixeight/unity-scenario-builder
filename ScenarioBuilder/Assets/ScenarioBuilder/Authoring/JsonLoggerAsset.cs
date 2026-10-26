using System;
using System.IO;
using ScenarioBuilder.Core;
using UnityEngine;

namespace ScenarioBuilder.Authoring
{
    [CreateAssetMenu(menuName = "Учебные сценарии/Логгеры/JSON файл", fileName = "JsonLogger")]
    public sealed class JsonLoggerAsset : JournalLoggerAsset
    {
        [Tooltip("Имя файла внутри Application.persistentDataPath. Пишется при завершении прохождения.")]
        public string FileName = "AutoSavedScenarioJournal.json";
        public override IJournalLogger CreateLogger()
        {
            var name = Path.GetFileName(FileName);
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Задайте имя JSON-файла.");
            return new JsonFileJournalLogger(Path.Combine(Application.persistentDataPath, name));
        }
    }

    // Persistence is an output adapter. The runner and both analyzers use the same journal contract.
    internal sealed class JsonFileJournalLogger : IJournalLogger
    {
        private readonly MemoryJournalLogger memory = new MemoryJournalLogger();
        private readonly string path;
        public Journal Journal => memory.Journal;
        public JsonFileJournalLogger(string path) { this.path = path; }
        public void Begin(Journal journal) { memory.Begin(journal); }
        public void Write(JournalEntry entry) { memory.Write(entry); }
        public void Finish()
        {
            memory.Finish();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonUtility.ToJson(Journal, true));
            }
            catch (Exception exception) { Debug.LogWarning("Не удалось сохранить журнал: " + exception.Message); }
        }
    }
}
