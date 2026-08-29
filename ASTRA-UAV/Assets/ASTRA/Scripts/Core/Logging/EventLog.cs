using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Astra.Core.Logging
{
    /// <summary>
    /// The operational event log. A bounded ring buffer of LogEntry, plus an event other systems
    /// (chiefly the GCS event-log panel) subscribe to.
    ///
    /// DESIGN NOTES
    /// - Bounded on purpose. An unbounded List grows without limit across a long demo session
    ///   and eventually causes a visible GC hitch mid-flight, which is exactly when you least
    ///   want one. A fixed ring buffer has constant memory and no reallocation.
    /// - Static access, because logging is genuinely cross-cutting and threading a logger
    ///   reference through every constructor would add noise without adding clarity. The trade-off
    ///   is accepted knowingly; see Docs/02-Software-Architecture.md.
    /// - Mission time is injected rather than read from Time.time, so that a paused or
    ///   time-scaled simulation still produces a coherent record.
    /// </summary>
    public static class EventLog
    {
        /// <summary>
        /// Capacity of the ring buffer. 2048 entries is far more than a demo flight produces and
        /// costs only a few hundred kilobytes.
        /// </summary>
        public const int Capacity = 2048;

        private static readonly LogEntry[] _buffer = new LogEntry[Capacity];
        private static int _count;
        private static int _head; // index where the next entry will be written
        private static int _sequence;

        /// <summary>
        /// Supplies the current mission time. Assigned by the simulation clock during startup so
        /// the log does not have to know about the clock's type.
        /// </summary>
        public static Func<double> MissionTimeProvider;

        /// <summary>Raised for every new entry. The GCS event-log panel listens to this.</summary>
        public static event Action<LogEntry> EntryAdded;

        /// <summary>
        /// When true, entries are also written to Unity's console. Useful in the editor, but it
        /// should be off for a presentation build because Debug.Log is not free.
        /// </summary>
        public static bool MirrorToUnityConsole = true;

        /// <summary>Minimum severity that will be recorded. Lets a build suppress Info noise.</summary>
        public static LogSeverity MinimumSeverity = LogSeverity.Info;

        public static int Count
        {
            get { return _count; }
        }

        // ------------------------------------------------------------------------------------
        // Writing
        // ------------------------------------------------------------------------------------

        public static void Write(LogSeverity severity, LogSource source, string message)
        {
            if (severity < MinimumSeverity)
            {
                return;
            }

            double missionTime = MissionTimeProvider != null ? MissionTimeProvider() : 0.0;
            LogEntry entry = new LogEntry(_sequence++, missionTime, severity, source, message);

            _buffer[_head] = entry;
            _head = (_head + 1) % Capacity;
            if (_count < Capacity)
            {
                _count++;
            }

            if (MirrorToUnityConsole)
            {
                MirrorToConsole(entry);
            }

            // A subscriber throwing must not break the flight loop. Log and carry on.
            if (EntryAdded != null)
            {
                try
                {
                    EntryAdded(entry);
                }
                catch (Exception ex)
                {
                    Debug.LogError("[EventLog] A subscriber threw while handling an entry: " + ex);
                }
            }
        }

        private static void MirrorToConsole(LogEntry entry)
        {
            string text = entry.ToDetailedString();
            switch (entry.Severity)
            {
                case LogSeverity.Warning:
                    Debug.LogWarning(text);
                    break;
                case LogSeverity.Error:
                case LogSeverity.Critical:
                    Debug.LogError(text);
                    break;
                default:
                    Debug.Log(text);
                    break;
            }
        }

        // Convenience wrappers. These read better at call sites than passing the enum every time.

        public static void Info(LogSource source, string message)
        {
            Write(LogSeverity.Info, source, message);
        }

        public static void Success(LogSource source, string message)
        {
            Write(LogSeverity.Success, source, message);
        }

        public static void Warning(LogSource source, string message)
        {
            Write(LogSeverity.Warning, source, message);
        }

        public static void Error(LogSource source, string message)
        {
            Write(LogSeverity.Error, source, message);
        }

        public static void Critical(LogSource source, string message)
        {
            Write(LogSeverity.Critical, source, message);
        }

        // ------------------------------------------------------------------------------------
        // Reading
        // ------------------------------------------------------------------------------------

        /// <summary>
        /// Copies entries into the supplied list, oldest first. Takes a list to fill rather than
        /// returning a new one so that a UI panel refreshing every frame allocates nothing.
        /// </summary>
        public static void GetEntries(List<LogEntry> into, int maxEntries = int.MaxValue)
        {
            if (into == null)
            {
                return;
            }
            into.Clear();

            int take = Math.Min(maxEntries, _count);
            // Oldest entry sits 'take' slots behind the head, modulo capacity.
            int start = ((_head - take) % Capacity + Capacity) % Capacity;
            for (int i = 0; i < take; i++)
            {
                into.Add(_buffer[(start + i) % Capacity]);
            }
        }

        /// <summary>Most recent entry, or a default LogEntry if the log is empty.</summary>
        public static LogEntry Latest
        {
            get
            {
                if (_count == 0)
                {
                    return default(LogEntry);
                }
                return _buffer[((_head - 1) % Capacity + Capacity) % Capacity];
            }
        }

        // ------------------------------------------------------------------------------------
        // Export and reset
        // ------------------------------------------------------------------------------------

        /// <summary>
        /// Renders the whole log as CSV. Handed to the flight-record exporter so a demonstration
        /// leaves behind an artefact that can be examined afterwards, which is the sort of thing
        /// an evaluation committee tends to ask for.
        /// </summary>
        public static string ExportCsv()
        {
            List<LogEntry> entries = new List<LogEntry>(_count);
            GetEntries(entries);

            StringBuilder sb = new StringBuilder(_count * 96);
            sb.AppendLine(LogEntry.CsvHeader);
            for (int i = 0; i < entries.Count; i++)
            {
                sb.AppendLine(entries[i].ToCsvRow());
            }
            return sb.ToString();
        }

        /// <summary>
        /// Clears the log. Called when a new mission starts so the operator is not reading
        /// events from a previous run.
        /// </summary>
        public static void Clear()
        {
            Array.Clear(_buffer, 0, _buffer.Length);
            _count = 0;
            _head = 0;
            _sequence = 0;
        }

        /// <summary>
        /// Detaches all subscribers and resets state. Unity does not reset static fields between
        /// play-mode sessions when domain reloading is disabled, so without this the log would
        /// accumulate dead subscribers from the previous session and throw on the first write.
        /// This is a genuine and easily-missed Unity pitfall.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            EntryAdded = null;
            MissionTimeProvider = null;
            Clear();
        }
    }
}
