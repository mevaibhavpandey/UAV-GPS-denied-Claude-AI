using UnityEngine;
using Astra.Core.Logging;

namespace Astra.Core
{
    /// <summary>
    /// Global scenario seed for reproducible randomness (ASTRA spec Sec 21, 61).
    ///
    /// A research platform must be able to reproduce a run exactly (same seed = same sensor noise,
    /// same dropouts, same procedural placement) and to deliberately vary it (new seed = new
    /// scenario). This is the single authority for that seed. Systems that draw random numbers
    /// should apply it via <see cref="Apply"/> at scenario start; anything using UnityEngine.Random
    /// then becomes deterministic for a given seed.
    ///
    /// HONESTY: this governs the SIMULATION's pseudo-randomness only. It does not make any physical
    /// claim; it exists so demonstrations and experiments are repeatable and comparable.
    /// </summary>
    public static class ScenarioSeed
    {
        /// <summary>The current scenario seed. Default is fixed so the app is reproducible out of the box.</summary>
        public static int Current { get; private set; } = 20260930;

        /// <summary>Applies the current seed to UnityEngine.Random. Call at scenario start.</summary>
        public static void Apply()
        {
            Random.InitState(Current);
            EventLog.Info(LogSource.System, $"Scenario seed applied: {Current} (SIMULATED reproducible randomness).");
        }

        /// <summary>Sets a specific seed and applies it. Use to REUSE a known scenario.</summary>
        public static void SetSeed(int seed)
        {
            Current = seed;
            Apply();
        }

        /// <summary>Picks a fresh pseudo-random seed and applies it. Use to generate a NEW scenario.</summary>
        public static int NewSeed()
        {
            // Derive from the system clock so successive presses differ; the chosen value is then
            // the reproducible handle for this run.
            Current = unchecked((int)(System.DateTime.UtcNow.Ticks & 0x7FFFFFFF));
            Apply();
            return Current;
        }
    }
}
