using UnityEngine;
using Astra.Core;
using Astra.Core.Config;
using Astra.Core.Logging;

namespace Astra.Drone
{
    /// <summary>
    /// The battery and power distribution system.
    ///
    /// WHY THIS IS NOT JUST A COUNTDOWN TIMER
    /// --------------------------------------
    /// A percentage that ticks down linearly with time would satisfy the letter of "show battery
    /// state" and would teach the operator nothing. Three properties of real lithium-polymer packs
    /// change how a mission has to be flown, and all three are modelled here:
    ///
    /// 1. THE DISCHARGE CURVE IS FLAT IN THE MIDDLE AND STEEP AT THE ENDS. A pack sits near 3.8 V per
    ///    cell for most of its useful life and then falls off a cliff. This is why voltage alone is a
    ///    poor state-of-charge indicator, why serious systems integrate consumed capacity in
    ///    milliamp-hours instead, and why a pilot watching only the voltmeter gets very little warning.
    ///
    /// 2. VOLTAGE SAGS UNDER LOAD. Pack internal resistance means the terminal voltage during a climb
    ///    is well below its resting value. A pack that reads a comfortable 22.5 V hovering can sag
    ///    below the failsafe threshold the moment full throttle is demanded, which is why low-voltage
    ///    failsafes fire during climbs rather than during cruise. Modelling sag means the failsafe
    ///    demonstration triggers for the right reason.
    ///
    /// 3. SAG REDUCES AVAILABLE THRUST. Lower voltage means lower rotor speed means less thrust, so a
    ///    tired pack degrades the aircraft's control authority precisely when it is being asked for
    ///    the most. MotorUnit reads the sagged voltage for exactly this reason, which closes the loop:
    ///    a hard climb on a low battery genuinely performs worse.
    ///
    /// HONESTY NOTE: the discharge curve below is a representative generic LiPo curve, not a measured
    /// curve for the specific pack. Cell chemistry, age and cycle count all shift it. Treat the
    /// percentage as indicative and the consumed-milliamp-hour figure as the more trustworthy number,
    /// which is also the correct habit for real flight operations.
    /// </summary>
    [DisallowMultipleComponent]
    public class BatterySystem : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private UavConfiguration config;

        [Header("Initial state")]
        [Tooltip("State of charge at mission start, in [0,1]. Set below 1 to demonstrate a " +
                 "low-battery return-to-launch without waiting for a full discharge.")]
        [Range(0.05f, 1f)]
        [SerializeField] private float initialStateOfCharge = 1f;

        [Header("Warning thresholds")]
        [Tooltip("Remaining fraction at which a first advisory is issued.")]
        [Range(0.1f, 0.9f)]
        [SerializeField] private float advisoryThreshold = 0.35f;

        [Tooltip("Remaining fraction at which a warning is issued.")]
        [Range(0.05f, 0.5f)]
        [SerializeField] private float warningThreshold = 0.20f;

        [Tooltip("Remaining fraction at which a critical alert is issued.")]
        [Range(0.02f, 0.3f)]
        [SerializeField] private float criticalThreshold = 0.12f;

        // ---- State ----
        private float _consumedMah;
        private float _currentDrawA;
        private float _terminalVoltage;
        private float _openCircuitVoltage;
        private float _peakCurrentA;
        private float _totalEnergyUsedWh;
        private bool _advisoryIssued;
        private bool _warningIssued;
        private bool _criticalIssued;

        /// <summary>
        /// Generic LiPo open-circuit voltage per cell against state of charge, from 0% to 100% in
        /// 10% steps. Representative, not measured. Note how little the voltage moves between 30%
        /// and 80% - that flatness is the whole reason capacity integration beats voltage watching.
        /// </summary>
        private static readonly float[] CellVoltageCurve =
        {
            3.30f, // 0%
            3.53f, // 10%
            3.63f, // 20%
            3.69f, // 30%
            3.73f, // 40%
            3.77f, // 50%
            3.81f, // 60%
            3.87f, // 70%
            3.95f, // 80%
            4.05f, // 90%
            4.20f  // 100%
        };

        // ====================================================================================
        // READOUTS
        // ====================================================================================

        /// <summary>Remaining capacity as a fraction in [0,1], from integrated consumption.</summary>
        public float StateOfCharge
        {
            get
            {
                if (config == null || config.BatteryCapacityMah <= 1f)
                {
                    return 0f;
                }
                float remaining = 1f - (_consumedMah / config.BatteryCapacityMah);
                return Mathf.Clamp01(remaining);
            }
        }

        /// <summary>Remaining capacity as a percentage, for display.</summary>
        public float PercentRemaining { get { return StateOfCharge * 100f; } }

        /// <summary>Terminal voltage under the present load, volts. This is what a real voltmeter on
        /// the aircraft would show.</summary>
        public float VoltageV { get { return _terminalVoltage; } }

        /// <summary>Resting voltage with no load, volts. Shown alongside terminal voltage so the sag
        /// is visible rather than inferred.</summary>
        public float OpenCircuitVoltageV { get { return _openCircuitVoltage; } }

        /// <summary>Sag in volts, i.e. resting minus terminal.</summary>
        public float SagV { get { return _openCircuitVoltage - _terminalVoltage; } }

        /// <summary>Present total current draw, amperes.</summary>
        public float CurrentA { get { return _currentDrawA; } }

        /// <summary>Highest current drawn this mission, amperes. Compare against the ESC rating.</summary>
        public float PeakCurrentA { get { return _peakCurrentA; } }

        /// <summary>Capacity consumed, milliamp-hours. The trustworthy state-of-charge figure.</summary>
        public float ConsumedMah { get { return _consumedMah; } }

        /// <summary>Energy consumed, watt-hours.</summary>
        public float EnergyUsedWh { get { return _totalEnergyUsedWh; } }

        /// <summary>Instantaneous power draw, watts.</summary>
        public float PowerW { get { return _currentDrawA * _terminalVoltage; } }

        /// <summary>Volts per cell, terminal. The figure a pilot actually watches.</summary>
        public float VoltsPerCell
        {
            get
            {
                if (config == null || config.BatteryCells <= 0)
                {
                    return 0f;
                }
                return _terminalVoltage / config.BatteryCells;
            }
        }

        /// <summary>
        /// Estimated flight time remaining in minutes, from the present draw.
        ///
        /// Uses the CURRENT draw rather than an average, so it swings substantially during
        /// manoeuvres - a full-throttle climb makes it plummet and a descent makes it jump. That
        /// volatility is honest: it is what the number actually means. The display smooths it for
        /// readability but does not pretend to a precision it does not have.
        /// </summary>
        public float EstimatedMinutesRemaining
        {
            get
            {
                if (config == null || _currentDrawA < 0.1f)
                {
                    return 0f;
                }
                // Reserve 20% for pack longevity and for the landing itself.
                float usableMah = config.BatteryCapacityMah * 0.8f - _consumedMah;
                if (usableMah <= 0f)
                {
                    return 0f;
                }
                return usableMah / (_currentDrawA * 1000f) * 60f;
            }
        }

        /// <summary>True once the pack is below the critical threshold.</summary>
        public bool IsCritical { get { return StateOfCharge <= criticalThreshold; } }

        // ====================================================================================
        // LIFECYCLE
        // ====================================================================================

        private void Awake()
        {
            RecomputeVoltage(0f);
        }

        public void Configure(UavConfiguration configuration)
        {
            config = configuration;
            ResetState();
        }

        /// <summary>
        /// Restores the pack to its initial state of charge. Called on mission reset.
        /// </summary>
        public void ResetState()
        {
            if (config != null)
            {
                _consumedMah = config.BatteryCapacityMah * (1f - Mathf.Clamp01(initialStateOfCharge));
            }
            else
            {
                _consumedMah = 0f;
            }

            _currentDrawA = 0f;
            _peakCurrentA = 0f;
            _totalEnergyUsedWh = 0f;
            _advisoryIssued = false;
            _warningIssued = false;
            _criticalIssued = false;
            RecomputeVoltage(0f);
        }

        // ====================================================================================
        // SIMULATION STEP
        // ====================================================================================

        /// <summary>
        /// Draws current for one timestep and updates the pack state.
        ///
        /// Called by QuadcopterPhysics with the summed motor current, before the motors are stepped,
        /// so that this step's voltage reflects the previous step's load. That one-step lag is
        /// physically correct - the sag is a consequence of the current, not simultaneous with it -
        /// and it avoids the circular dependency of needing the voltage to compute the current that
        /// determines the voltage.
        /// </summary>
        /// <param name="motorCurrentA">Total current drawn by all four motors, amperes.</param>
        /// <param name="dt">Timestep, seconds.</param>
        public void Tick(float motorCurrentA, float dt)
        {
            if (config == null || dt <= 0f)
            {
                return;
            }

            float avionics = config.AvionicsCurrentA;
            _currentDrawA = Mathf.Max(0f, motorCurrentA) + avionics;

            if (_currentDrawA > _peakCurrentA)
            {
                _peakCurrentA = _currentDrawA;
            }

            // Integrate consumption. Amperes x seconds / 3600 gives amp-hours; x1000 for milliamp-hours.
            float mahDrawn = _currentDrawA * dt / 3600f * 1000f;
            _consumedMah += mahDrawn;

            _totalEnergyUsedWh += _currentDrawA * _terminalVoltage * dt / 3600f;

            RecomputeVoltage(_currentDrawA);
            CheckThresholds();
        }

        /// <summary>
        /// Open-circuit voltage from the discharge curve, minus the ohmic drop from the present load.
        /// </summary>
        private void RecomputeVoltage(float loadA)
        {
            if (config == null)
            {
                _openCircuitVoltage = 0f;
                _terminalVoltage = 0f;
                return;
            }

            float soc = StateOfCharge;

            // Interpolate the per-cell curve. soc 0..1 maps onto indices 0..10.
            float scaled = Mathf.Clamp01(soc) * (CellVoltageCurve.Length - 1);
            int lower = Mathf.FloorToInt(scaled);
            int upper = Mathf.Min(lower + 1, CellVoltageCurve.Length - 1);
            float blend = scaled - lower;
            float perCell = Mathf.Lerp(CellVoltageCurve[lower], CellVoltageCurve[upper], blend);

            _openCircuitVoltage = perCell * config.BatteryCells;
            _terminalVoltage = Mathf.Max(
                config.BatteryCells * 2.8f,
                _openCircuitVoltage - loadA * config.PackInternalResistanceOhm);
        }

        /// <summary>
        /// Issues each warning once, on the way down. Latching them prevents the log filling with
        /// repeated messages, which is what would happen if the state of charge oscillated around a
        /// threshold - and a log that scrolls with duplicates is a log nobody reads.
        /// </summary>
        private void CheckThresholds()
        {
            float soc = StateOfCharge;

            if (!_advisoryIssued && soc <= advisoryThreshold)
            {
                _advisoryIssued = true;
                EventLog.Info(LogSource.Power, string.Format(
                    "Battery {0:F0}% ({1:F2} V/cell) - {2:F1} min estimated remaining",
                    soc * 100f, VoltsPerCell, EstimatedMinutesRemaining));
                AstraEvents.RaiseBatteryWarning(soc);
            }

            if (!_warningIssued && soc <= warningThreshold)
            {
                _warningIssued = true;
                EventLog.Warning(LogSource.Power, string.Format(
                    "BATTERY LOW {0:F0}% ({1:F2} V/cell) - return to launch advised",
                    soc * 100f, VoltsPerCell));
                AstraEvents.RaiseBatteryWarning(soc);
            }

            if (!_criticalIssued && soc <= criticalThreshold)
            {
                _criticalIssued = true;
                EventLog.Critical(LogSource.Power, string.Format(
                    "BATTERY CRITICAL {0:F0}% ({1:F2} V/cell) - land immediately",
                    soc * 100f, VoltsPerCell));
                AstraEvents.RaiseBatteryWarning(soc);
            }
        }

        /// <summary>
        /// Sets the state of charge directly. For demonstrations, so a low-battery scenario can be
        /// shown without a seventeen-minute wait. Logged, because silently altering the battery state
        /// during a demonstration would be exactly the kind of thing that should be on the record.
        /// </summary>
        public void SetStateOfCharge(float fraction)
        {
            if (config == null)
            {
                return;
            }
            fraction = Mathf.Clamp01(fraction);
            _consumedMah = config.BatteryCapacityMah * (1f - fraction);

            // Re-arm the warnings that are now above the new level, so they fire again on the way
            // down from here.
            _advisoryIssued = fraction <= advisoryThreshold;
            _warningIssued = fraction <= warningThreshold;
            _criticalIssued = fraction <= criticalThreshold;

            RecomputeVoltage(_currentDrawA);
            EventLog.Warning(LogSource.Power, string.Format(
                "Battery state of charge set to {0:F0}% by operator (demonstration control)",
                fraction * 100f));
        }
    }
}
