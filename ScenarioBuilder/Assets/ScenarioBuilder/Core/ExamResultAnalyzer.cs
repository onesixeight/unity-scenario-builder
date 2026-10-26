using System;

namespace ScenarioBuilder.Core
{
    public sealed class ExamResultAnalyzer : IResultAnalyzer
    {
        public string Name => "Экзамен — штрафы за статусы";

        public AnalysisResult Analyze(Journal journal)
        {
            var result = JournalReader.Read(journal, Name, 100);
            if (!result.IsValid) return result;
            int penalties = 0;
            foreach (var row in result.Steps)
            {
                row.Penalty = row.Status == "Failed" ? 10 : row.Status == "Skipped" ? 5 : 0;
                penalties += row.Penalty;
            }
            result.Score = Math.Max(0, 100 - penalties);
            return result;
        }
    }
}
