using UnityEngine;

namespace Astra.Flight
{
    /// <summary>
    /// Converts collective throttle plus roll, pitch and yaw demands into four individual rotor speed
    /// commands for a quadcopter in X configuration.
    ///
    /// MOTOR LAYOUT
    /// ------------
    /// ASTRA uses ArduPilot's QuadX ordering, so that the per-motor readouts here can be compared
    /// directly against Mission Planner's motor test when the real aircraft is built:
    ///
    ///          nose (+Z)
    ///     2 (CW)      0 (CCW)
    ///          \      /
    ///           \    /
    ///            \  /
    ///             ><          -> +X (right)
    ///            /  \
    ///           /    \
    ///          /      \
    ///     1 (CCW)     3 (CW)
    ///
    ///   0 = front-right, counter-clockwise
    ///   1 = rear-left,   counter-clockwise
    ///   2 = front-left,  clockwise
    ///   3 = rear-right,  clockwise
    ///
    /// Diagonal pairs share a rotation direction so their reaction torques cancel in the hover case.
    ///
    /// SIGN CONVENTIONS
    /// ----------------
    ///   roll  positive = roll right, i.e. the right side drops. Achieved by reducing thrust on the
    ///                    right pair (0, 3) and raising it on the left pair (1, 2).
    ///   pitch positive = nose up. Achieved by raising the front pair (0, 2). This is worth stating
    ///                    explicitly because it is a common source of confusion: a quadcopter moves
    ///                    FORWARD by pitching nose DOWN, which means raising the REAR pair.
    ///   yaw   positive = yaw right, i.e. clockwise viewed from above. Achieved by raising the
    ///                    counter-clockwise pair (0, 1), whose reaction torque drags the airframe
    ///                    clockwise.
    ///
    /// SATURATION HANDLING - THE PART THAT MATTERS
    /// -------------------------------------------
    /// The naive mixer computes throttle plus or minus the attitude terms and clamps each motor to
    /// [0,1]. That is wrong in a specific and dangerous way.
    ///
    /// Consider an aircraft at 95% throttle that needs to roll right. The mix asks for 100% on the
    /// left pair and 90% on the right. Clamping caps the left pair at 100%, so instead of a 10%
    /// differential the aircraft gets 10% on one side and 0% on the other - the roll command is
    /// silently halved. Push harder and it disappears entirely: at full throttle the aircraft has no
    /// attitude authority at all, and simply falls out of the sky in whatever attitude it happened to
    /// be in. This is a real accident mode.
    ///
    /// Real autopilots solve it by prioritising. Attitude control is what keeps the aircraft
    /// upright, so it must be preserved even at the cost of the operator's throttle demand: it is
    /// always better to descend while level than to hold altitude while tumbling. The priority order
    /// implemented below, from most to least protected, is:
    ///
    ///   1. Roll and pitch  - sacrificed only as an absolute last resort
    ///   2. Collective throttle - reduced or raised to make room for the attitude terms
    ///   3. Yaw - sacrificed first, because a quadcopter that yaws slowly is merely annoying whereas
    ///            one that cannot hold attitude is falling
    ///
    /// This mirrors ArduPilot's AP_MotorsMatrix behaviour. When the mixer has to give something up it
    /// reports it, so the diagnostics panel can show that the aircraft is at the edge of its control
    /// authority rather than leaving the operator to infer it from sluggish response.
    /// </summary>
    public class MotorMixer
    {
        /// <summary>Number of motors. Fixed at four; a hex or octo layout would need a new matrix.</summary>
        public const int MotorCount = 4;

        private readonly float[] _outputs = new float[MotorCount];

        // ---- Instrumentation ----
        private float _yawScale = 1f;
        private float _attitudeScale = 1f;
        private float _throttleAdjustment;
        private bool _saturated;

        /// <summary>The four rotor speed commands in [0,1], indexed as documented above.</summary>
        public float[] Outputs { get { return _outputs; } }

        /// <summary>
        /// Fraction of the requested yaw authority actually delivered, in [0,1]. Below 1 means yaw
        /// was sacrificed to protect roll and pitch.
        /// </summary>
        public float YawAuthorityDelivered { get { return _yawScale; } }

        /// <summary>
        /// Fraction of the requested roll and pitch authority delivered, in [0,1]. Below 1 means the
        /// aircraft is out of control authority entirely - it cannot achieve the commanded attitude
        /// at any throttle setting. This is the number that should worry an engineer.
        /// </summary>
        public float AttitudeAuthorityDelivered { get { return _attitudeScale; } }

        /// <summary>
        /// How much the collective throttle was moved to make room for the attitude terms. Negative
        /// means throttle was given up, which shows as an unavoidable sink rate during an aggressive
        /// manoeuvre - exactly as on a real aircraft.
        /// </summary>
        public float ThrottleAdjustment { get { return _throttleAdjustment; } }

        /// <summary>True if any demand had to be reduced on the last mix.</summary>
        public bool WasSaturated { get { return _saturated; } }

        /// <summary>
        /// Mixes the demands into four rotor speed commands.
        /// </summary>
        /// <param name="throttle">Collective rotor speed in [0,1].</param>
        /// <param name="roll">Roll demand, roughly [-1,1]. Positive rolls right.</param>
        /// <param name="pitch">Pitch demand, roughly [-1,1]. Positive pitches nose up.</param>
        /// <param name="yaw">Yaw demand, roughly [-1,1]. Positive yaws right.</param>
        public void Mix(float throttle, float roll, float pitch, float yaw)
        {
            throttle = Mathf.Clamp01(throttle);

            _yawScale = 1f;
            _attitudeScale = 1f;
            _throttleAdjustment = 0f;
            _saturated = false;

            // ---- Step 1: how much headroom do the roll and pitch terms need? ----
            // Each motor receives some combination of +/- roll and +/- pitch. The worst case for any
            // single motor is the sum of the magnitudes, since the matrix gives every motor a unit
            // coefficient on both axes.
            float rollPitchSpread = Mathf.Abs(roll) + Mathf.Abs(pitch);

            // If roll and pitch alone need more than the full [0,1] range, the aircraft physically
            // cannot deliver the commanded attitude at any throttle. Scale them down together so the
            // RATIO between them is preserved - scaling them independently would rotate the
            // commanded tilt direction, sending the aircraft somewhere other than where the
            // controller asked, which is worse than a weaker response in the right direction.
            if (rollPitchSpread > 1f)
            {
                _attitudeScale = 1f / rollPitchSpread;
                roll *= _attitudeScale;
                pitch *= _attitudeScale;
                rollPitchSpread = 1f;
                _saturated = true;
            }

            // ---- Step 2: yaw gets whatever range is left ----
            // Yaw is the first thing sacrificed. A slow yaw response is a nuisance; losing roll or
            // pitch authority is a crash.
            float yawHeadroom = Mathf.Max(0f, 1f - rollPitchSpread);
            if (Mathf.Abs(yaw) > yawHeadroom)
            {
                if (Mathf.Abs(yaw) > 0.0001f)
                {
                    _yawScale = yawHeadroom / Mathf.Abs(yaw);
                    yaw *= _yawScale;
                }
                else
                {
                    _yawScale = 1f;
                }
                _saturated = true;
            }

            // ---- Step 3: raw mix ----
            // Matrix rows, in the documented motor order.
            _outputs[0] = throttle - roll + pitch + yaw;   // front-right, CCW
            _outputs[1] = throttle + roll - pitch + yaw;   // rear-left,   CCW
            _outputs[2] = throttle + roll + pitch - yaw;   // front-left,  CW
            _outputs[3] = throttle - roll - pitch - yaw;   // rear-right,  CW

            // ---- Step 4: shift the collective so the whole set fits in [0,1] ----
            // Because every attitude term appears with opposite signs on opposite motors, shifting
            // all four by the same amount changes the collective thrust WITHOUT disturbing any of the
            // differentials. That is what makes it possible to preserve attitude authority at the
            // cost of throttle, and it is the key insight the naive clamping mixer misses.
            float minOut = _outputs[0];
            float maxOut = _outputs[0];
            for (int i = 1; i < MotorCount; i++)
            {
                if (_outputs[i] < minOut) minOut = _outputs[i];
                if (_outputs[i] > maxOut) maxOut = _outputs[i];
            }

            float shift = 0f;
            if (maxOut > 1f)
            {
                shift = 1f - maxOut;          // negative: give up throttle
            }
            else if (minOut < 0f)
            {
                shift = -minOut;              // positive: add throttle so nothing goes negative
            }

            if (Mathf.Abs(shift) > 0.0001f)
            {
                for (int i = 0; i < MotorCount; i++)
                {
                    _outputs[i] += shift;
                }
                _throttleAdjustment = shift;
                _saturated = true;
            }

            // ---- Step 5: final clamp ----
            // After the shift the set should already fit. This clamp catches the residual case where
            // it does not - specifically where the spread genuinely exceeds the available range - and
            // guarantees a valid command reaches the ESCs. Reaching here with a real clamp still
            // happening means the aircraft is beyond its control authority, which the flags above
            // report rather than hide.
            for (int i = 0; i < MotorCount; i++)
            {
                float before = _outputs[i];
                _outputs[i] = Mathf.Clamp01(before);
                if (!Mathf.Approximately(before, _outputs[i]))
                {
                    _saturated = true;
                }
            }
        }

        /// <summary>
        /// Sets all outputs to zero. Used on disarm.
        /// </summary>
        public void Zero()
        {
            for (int i = 0; i < MotorCount; i++)
            {
                _outputs[i] = 0f;
            }
            _yawScale = 1f;
            _attitudeScale = 1f;
            _throttleAdjustment = 0f;
            _saturated = false;
        }

        /// <summary>
        /// Human-readable saturation summary for the diagnostics panel. Returns an empty string when
        /// the mixer is delivering everything it was asked for, so the panel stays quiet in normal
        /// flight and only speaks up when there is something to say.
        /// </summary>
        public string DescribeSaturation()
        {
            if (!_saturated)
            {
                return string.Empty;
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            if (_attitudeScale < 0.999f)
            {
                sb.Append("ATTITUDE AUTHORITY LIMITED (");
                sb.Append((_attitudeScale * 100f).ToString("F0"));
                sb.Append("%) ");
            }

            if (_yawScale < 0.999f)
            {
                sb.Append("yaw reduced to ");
                sb.Append((_yawScale * 100f).ToString("F0"));
                sb.Append("% ");
            }

            if (_throttleAdjustment < -0.001f)
            {
                sb.Append("throttle reduced ");
                sb.Append((-_throttleAdjustment * 100f).ToString("F0"));
                sb.Append("% to preserve attitude ");
            }
            else if (_throttleAdjustment > 0.001f)
            {
                sb.Append("throttle raised ");
                sb.Append((_throttleAdjustment * 100f).ToString("F0"));
                sb.Append("% to avoid motor stop ");
            }

            if (sb.Length == 0)
            {
                sb.Append("motor command clamped");
            }

            return sb.ToString().TrimEnd();
        }
    }
}
