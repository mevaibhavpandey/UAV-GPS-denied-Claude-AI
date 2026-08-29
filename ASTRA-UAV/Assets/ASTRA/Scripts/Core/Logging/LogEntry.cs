using System;

namespace Astra.Core.Logging
{
    /// <summary>
    /// Severity of an operational event. Deliberately mirrors the vocabulary used by ArduPilot's
    /// MAVLink STATUSTEXT severities so that, when a real flight controller is connected, its
    /// messages can be mapped onto this enum without inventing a translation layer.
    /// </summary>
    public enum LogSeverity
    {
        /// <summary>Routine progress. Most mission-phase transitions land here.</summary>
        Info = 0,

        /// <summary>Something noteworthy succeeded. Used sparingly so it stays meaningful.</summary>
        Success = 1,

        /// <summary>Degraded but flyable. Operator should be aware; mission continues.</summary>
        Warning = 2,

        /// <summary>A subsystem has failed. Mission integrity is compromised.</summary>
        Error = 3,

        /// <summary>Safety-critical. A failsafe has triggered or is about to.</summary>
        Critical = 4
    }

    /// <summary>
    /// Which subsystem raised an event. Having this as an enum rather than a free string means
    /// the event log can be filtered per subsystem in the GCS, and means a typo cannot create a
    /// phantom category.
    /// </summary>
    public enum LogSource
    {
        System,
        FlightController,
        Navigation,
        Perception,
        Localization,
        Mission,
        Power,
        Communication,
        Sensors,
        Planner,
        Operator
    }

    /// <summary>
    /// One immutable entry in the operational event log.
    /// </summary>
    public struct LogEntry
    {
        /// <summary>Wall-clock time the event was raised.</summary>
        public DateTime Timestamp;

        /// <summary>Seconds since the simulation started. The figure that matters for analysis.</summary>
        public double MissionTime;

        public LogSeverity Severity;
        public LogSource Source;
        public string Message;

        /// <summary>Monotonic sequence number, so identical messages remain distinguishable.</summary>
        public int Sequence;

        public LogEntry(int sequence, double missionTime, LogSeverity severity,
                        LogSource source, string message)
        {
            Sequence = sequence;
            Timestamp = DateTime.Now;
            MissionTime = missionTime;
            Severity = severity;
            Source = source;
            Message = message;
        }

        /// <summary>
        /// Formats in the GCS console style: [HH:MM:SS] MESSAGE
        /// This matches the log format specified for the ASTRA ground station.
        /// </summary>
        public string ToConsoleString()
        {
            return "[" + Timestamp.ToString("HH:mm:ss") + "] " + Message;
        }

        /// <summary>
        /// Verbose form including mission time, severity and source, for the exported flight
        /// record where post-flight analysis needs the full context.
        /// </summary>
        public string ToDetailedString()
        {
            return string.Format("[{0}] T+{1,8:F2}s  {2,-8} {3,-16} {4}",
                Timestamp.ToString("HH:mm:ss.fff"),
                MissionTime,
                Severity.ToString().ToUpperInvariant(),
                Source.ToString(),
                Message);
        }

        /// <summary>CSV row for export, with the message quoted and internal quotes escaped.</summary>
        public string ToCsvRow()
        {
            string escaped = Message == null ? string.Empty : Message.Replace("\"", "\"\"");
            return string.Format("{0},{1:F3},{2},{3},\"{4}\"",
                Sequence, MissionTime, Severity, Source, escaped);
        }

        public static string CsvHeader
        {
            get { return "sequence,mission_time_s,severity,source,message"; }
        }
    }
}
