using UnityEngine;

namespace Astra.Flight
{
    /// <summary>
    /// A PID controller with the details that actually matter on real hardware.
    ///
    /// A textbook three-line PID works in a textbook. Put one in a flight controller and it does
    /// three specific things wrong, all of which are visible to anyone who has flown a real
    /// aircraft, and all of which this class addresses:
    ///
    /// 1. INTEGRAL WINDUP. When the output saturates - the motors are already at full throttle, say,
    ///    during a hard climb - the error keeps accumulating in the integral term even though more
    ///    output is impossible. When the aircraft finally reaches the setpoint the integral is
    ///    enormous, and it overshoots badly and oscillates. The fix is conditional integration:
    ///    stop accumulating while saturated in the direction that would make saturation worse.
    ///    This is what ArduPilot and PX4 both do.
    ///
    /// 2. DERIVATIVE KICK. Computing the derivative of the ERROR means a step change in setpoint
    ///    produces an instantaneous infinite derivative, and therefore a violent output spike. Every
    ///    operator stick movement would jolt the aircraft. The fix is to differentiate the
    ///    MEASUREMENT instead of the error, which is mathematically equivalent for a constant
    ///    setpoint and well behaved when the setpoint moves.
    ///
    /// 3. DERIVATIVE NOISE. Differentiation amplifies high-frequency noise, and gyroscope output is
    ///    noisy. An unfiltered D term turns sensor noise into motor commands, which on real hardware
    ///    means hot motors, wasted battery and audible buzzing. The fix is a first-order low-pass
    ///    filter on the derivative, with a cutoff around 20-30 Hz - low enough to reject noise, high
    ///    enough to preserve the phase lead the D term exists to provide.
    ///
    /// Simulating these correctly is the difference between a controller that is tuned for the
    /// simulator and a controller whose gains would be a sensible starting point on the real
    /// aircraft. That transferability is the entire point of building a digital twin rather than an
    /// animation.
    /// </summary>
    [System.Serializable]
    public class PidController
    {
        [SerializeField] private float kp = 1f;
        [SerializeField] private float ki;
        [SerializeField] private float kd;

        [Tooltip("Output clamp. The integral term stops accumulating when the output is pinned here.")]
        [SerializeField] private float outputLimit = 1f;

        [Tooltip("Hard clamp on the integral term's contribution, as a fraction of the output limit. " +
                 "A second line of defence behind conditional integration: even with correct " +
                 "anti-windup, a persistent bias (a heavy battery mounted off-centre, a bent arm) " +
                 "should not let the integrator own the entire output range.")]
        [SerializeField] private float integralLimitFraction = 0.5f;

        [Tooltip("Low-pass cutoff for the derivative term, hertz. Around 25 Hz suits a multirotor " +
                 "attitude loop: it rejects gyro noise while preserving the phase lead the D term " +
                 "is there to provide. Set to zero to disable filtering.")]
        [SerializeField] private float derivativeCutoffHz = 25f;

        // ---- State ----
        private float _integral;
        private float _lastMeasurement;
        private float _filteredDerivative;
        private bool _hasHistory;

        // ---- Instrumentation, for the tuning panel ----
        private float _lastP;
        private float _lastI;
        private float _lastD;
        private float _lastOutput;
        private float _lastError;
        private bool _wasSaturated;

        public PidController()
        {
        }

        public PidController(float p, float i, float d, float limit)
        {
            kp = p;
            ki = i;
            kd = d;
            outputLimit = limit;
        }

        public float Kp { get { return kp; } set { kp = value; } }
        public float Ki { get { return ki; } set { ki = value; } }
        public float Kd { get { return kd; } set { kd = value; } }

        public float OutputLimit
        {
            get { return outputLimit; }
            set { outputLimit = Mathf.Max(0.0001f, value); }
        }

        /// <summary>Contribution of each term on the last update. Displayed by the tuning panel so
        /// gains can be diagnosed rather than guessed at.</summary>
        public float LastP { get { return _lastP; } }
        public float LastI { get { return _lastI; } }
        public float LastD { get { return _lastD; } }
        public float LastOutput { get { return _lastOutput; } }
        public float LastError { get { return _lastError; } }

        /// <summary>True if the output was clamped on the last update, meaning the controller is
        /// asking for more authority than the aircraft has.</summary>
        public bool WasSaturated { get { return _wasSaturated; } }

        public float Integral { get { return _integral; } }

        public void SetGains(float p, float i, float d)
        {
            kp = p;
            ki = i;
            kd = d;
        }

        /// <summary>
        /// Runs one control step.
        /// </summary>
        /// <param name="setpoint">Desired value.</param>
        /// <param name="measurement">Measured value.</param>
        /// <param name="dt">Timestep in seconds. Must be positive.</param>
        /// <returns>Control output, clamped to the output limit.</returns>
        public float Update(float setpoint, float measurement, float dt)
        {
            if (dt <= 0f)
            {
                return _lastOutput;
            }

            float error = setpoint - measurement;
            _lastError = error;

            // ---- Proportional ----
            float p = kp * error;

            // ---- Derivative, on the measurement rather than the error ----
            // Negated because d(error)/dt = -d(measurement)/dt when the setpoint is constant.
            float rawDerivative = 0f;
            if (_hasHistory)
            {
                rawDerivative = -(measurement - _lastMeasurement) / dt;
            }
            _lastMeasurement = measurement;
            _hasHistory = true;

            if (derivativeCutoffHz > 0f)
            {
                // First-order low-pass. The coefficient is derived from the RC form:
                //   alpha = dt / (RC + dt),  RC = 1 / (2*pi*fc)
                // At dt = 10 ms and fc = 25 Hz this gives alpha = 0.61, a usefully gentle filter.
                float rc = 1f / (2f * Mathf.PI * derivativeCutoffHz);
                float alpha = dt / (rc + dt);
                _filteredDerivative += alpha * (rawDerivative - _filteredDerivative);
            }
            else
            {
                _filteredDerivative = rawDerivative;
            }

            float d = kd * _filteredDerivative;

            // ---- Integral, with conditional integration ----
            // Compute the output WITHOUT updating the integral first, so we can tell whether adding
            // to the integral would push us further into saturation. This is the anti-windup scheme
            // that matters; a bare integral clamp alone still winds up to its limit and still
            // overshoots, just by a bounded amount.
            float integralCandidate = _integral;
            if (ki != 0f)
            {
                integralCandidate = _integral + error * dt;
            }

            float iTerm = ki * integralCandidate;
            float integralCap = outputLimit * Mathf.Clamp01(integralLimitFraction);
            iTerm = Mathf.Clamp(iTerm, -integralCap, integralCap);

            float unclamped = p + iTerm + d;
            float clamped = Mathf.Clamp(unclamped, -outputLimit, outputLimit);
            _wasSaturated = !Mathf.Approximately(unclamped, clamped);

            // Accept the new integral only if we are not saturated, or if the error would drive the
            // output back inside the limits. The sign test is what makes this conditional rather
            // than a freeze: an integrator that is frozen outright cannot unwind either, which
            // leaves a steady-state offset the moment saturation clears.
            bool integratingWouldWorsenSaturation =
                _wasSaturated && Mathf.Sign(error) == Mathf.Sign(unclamped);

            if (ki != 0f && !integratingWouldWorsenSaturation)
            {
                _integral = integralCandidate;

                // Keep the stored integral itself bounded, so a very small ki cannot accumulate an
                // enormous raw value that behaves unpredictably if the gain is later raised during
                // live tuning.
                float rawIntegralCap = integralCap / Mathf.Max(0.0001f, Mathf.Abs(ki));
                _integral = Mathf.Clamp(_integral, -rawIntegralCap, rawIntegralCap);
            }

            _lastP = p;
            _lastI = iTerm;
            _lastD = d;
            _lastOutput = clamped;

            return clamped;
        }

        /// <summary>
        /// Clears accumulated state.
        ///
        /// MUST be called whenever the loop is disengaged and re-engaged - on arming, on a mode
        /// change, on a control-source handover. Otherwise the integral accumulated while the
        /// aircraft sat on the ground with an unsatisfiable setpoint is applied the instant the loop
        /// takes authority, and the aircraft lurches. This is a real failure mode on real hardware
        /// and the reason autopilots reset their integrators on mode entry.
        /// </summary>
        public void Reset()
        {
            _integral = 0f;
            _filteredDerivative = 0f;
            _hasHistory = false;
            _lastP = 0f;
            _lastI = 0f;
            _lastD = 0f;
            _lastOutput = 0f;
            _lastError = 0f;
            _wasSaturated = false;
        }

        /// <summary>
        /// Resets while preserving continuity of the measurement history, so re-engaging does not
        /// produce a spurious derivative spike from a stale measurement. Preferred over Reset when
        /// the loop is being re-engaged on a moving aircraft rather than on the ground.
        /// </summary>
        public void ResetPreservingMeasurement(float currentMeasurement)
        {
            Reset();
            _lastMeasurement = currentMeasurement;
            _hasHistory = true;
        }

        /// <summary>
        /// Seeds the integral so the controller starts out producing a known output. Used at takeoff
        /// to preload the altitude loop with the hover thrust the aircraft is known to need, rather
        /// than making the integrator discover it from scratch while the aircraft sags.
        /// </summary>
        public void PresetIntegralForOutput(float desiredOutput)
        {
            if (Mathf.Abs(ki) < 0.0001f)
            {
                return;
            }
            _integral = desiredOutput / ki;
        }
    }
}
