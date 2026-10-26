namespace ScenarioBuilder.Core
{
    public sealed class TrainingResultAnalyzer : IResultAnalyzer
    {
        public string Name => "Тренировка — баллы за попытки";

        public AnalysisResult Analyze(Journal journal)
        {
            int count = journal != null && journal.Steps != null ? journal.Steps.Count : 0;
            var result = JournalReader.Read(journal, Name, 10 * count);
            if (!result.IsValid) return result;
            foreach (var row in result.Steps)
            {
                row.Score = row.Status == "Completed" ? (row.Attempts == 1 ? 10 : row.Attempts == 2 ? 5 : 0) : 0;
                result.Score += row.Score;
            }
            return result;
        }
    }
}
