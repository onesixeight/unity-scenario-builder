using ScenarioBuilder.Core;
using UnityEngine;

namespace ScenarioBuilder.Authoring
{
    [CreateAssetMenu(menuName = "Учебные сценарии/Журнал/В памяти", fileName = "MemoryLogger")]
    public sealed class MemoryLoggerAsset : JournalLoggerAsset
    {
        public override IJournalLogger CreateLogger()
        {
            return new MemoryJournalLogger();
        }
    }
}
