using ScenarioBuilder.Core;
using UnityEngine;

namespace ScenarioBuilder.Authoring
{
    public abstract class JournalLoggerAsset : ScriptableObject
    {
        public abstract IJournalLogger CreateLogger();
    }
}
