using System;
using System.Collections.Generic;

namespace ScenarioBuilder.Core
{
    /// <summary>Stores a complete journal, without depending on the executor or Unity.</summary>
    public sealed class MemoryJournalLogger : IJournalLogger
    {
        private readonly HashSet<string> entryIds = new HashSet<string>(StringComparer.Ordinal);
        private int sequence;
        private bool finished;

        public Journal Journal { get; private set; }

        public void Begin(Journal journal)
        {
            Journal = journal ?? throw new ArgumentNullException(nameof(journal));
            if (Journal.Entries == null) Journal.Entries = new List<JournalEntry>();
            entryIds.Clear();
            sequence = 0;
            finished = false;
            foreach (var entry in Journal.Entries)
            {
                if (entry == null) continue;
                if (!string.IsNullOrEmpty(entry.Id)) entryIds.Add(entry.Id);
                sequence = Math.Max(sequence, entry.Sequence);
            }
        }

        public void Write(JournalEntry entry)
        {
            if (Journal == null) throw new InvalidOperationException("Begin must be called before writing a journal.");
            if (finished) throw new InvalidOperationException("The journal is already finished.");
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (string.IsNullOrWhiteSpace(entry.Id)) entry.Id = Guid.NewGuid().ToString("N");
            if (!entryIds.Add(entry.Id)) return;
            if (entry.Sequence <= 0) entry.Sequence = sequence + 1;
            sequence = Math.Max(sequence, entry.Sequence);
            Journal.Entries.Add(entry);
        }

        public void Finish()
        {
            if (Journal == null) throw new InvalidOperationException("Begin must be called before finishing a journal.");
            finished = true;
        }
    }
}
