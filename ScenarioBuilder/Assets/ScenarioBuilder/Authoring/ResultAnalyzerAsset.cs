using ScenarioBuilder.Core;
using UnityEngine;

namespace ScenarioBuilder.Authoring
{
    public abstract class ResultAnalyzerAsset : ScriptableObject
    {
        public abstract IResultAnalyzer CreateAnalyzer();
    }
}
