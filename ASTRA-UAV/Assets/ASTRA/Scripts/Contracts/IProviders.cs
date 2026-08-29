using UnityEngine;

namespace Astra.Contracts
{
    /// <summary>
    /// Supplies raw sensor measurements.
    ///
    /// This is the seam at which the simulator is replaced by real hardware. In SIMULATION mode a
    /// SimulatedSensorSuite synthesises these samples from Unity's physics state and adds
    /// configurable noise. In HARDWARE mode a MavlinkSensorProvider would read them from a Pixhawk
    /// over MAVLink. Nothing downstream of this interface knows or cares which is active.
    ///
    /// Every getter returns a struct carrying its own IsValid flag rather than throwing, because a
    /// sensor dropping out is a normal operating condition on an aircraft, not an exceptional one.
    /// </summary>
    public interface ISensorProvider
    {
        /// <summary>Display name, e.g. "Simulated IMU Suite" or "Pixhawk 2.4.8 (MAVLink)".</summary>
        string Name { get; }

        /// <summary>Where this data comes from. Drives the UI provenance badge.</summary>
        DataProvenance Provenance { get; }

        /// <summary>Overall health of the sensor suite.</summary>
        SubsystemStatus Status { get; }

        /// <summary>True when GNSS is available and being published. False in GPS-denied mode.</summary>
        bool GpsAvailable { get; }

        ImuSample ReadImu();
        GpsFix ReadGps();
        BarometerSample ReadBarometer();
        MagnetometerSample ReadMagnetometer();

        /// <summary>
        /// Called once at startup. Returns false if the sensor suite cannot be brought up, in
        /// which case preflight must fail rather than proceeding with dead sensors.
        /// </summary>
        bool Initialise();

        /// <summary>
        /// Advances the provider. Called from FixedUpdate so that simulated sensor noise is
        /// generated at a fixed rate, which keeps results reproducible across machines with
        /// different frame rates - important for a demonstration that must behave identically
        /// every time it is run.
        /// </summary>
        void Tick(float fixedDeltaTime);

        /// <summary>
        /// Forces GNSS on or off. This is how the GPS-denied scenario is triggered: the sensor
        /// provider simply stops publishing fixes, and the localisation stack has to cope. That is
        /// a more honest demonstration than special-casing a "gps denied mode" flag throughout the
        /// navigation code, because it exercises the same failure path a real outage would.
        /// </summary>
        void SetGpsEnabled(bool enabled);
    }

    /// <summary>
    /// Produces a fused estimate of where the vehicle is and how it is moving.
    ///
    /// Two implementations ship with ASTRA:
    ///   GnssBaroLocalizationProvider - GNSS + barometer + magnetometer, the normal case.
    ///   VisualInertialLocalizationProvider - a DEMONSTRATION stand-in for visual-inertial
    ///       odometry used when GNSS is unavailable. It is emphatically NOT an implementation of
    ///       VIO or SLAM; see the class documentation and Docs/08-GPS-Denied-Navigation.md.
    ///
    /// A future implementation could wrap ORB-SLAM3, VINS-Fusion, OpenVINS or RTAB-Map over a ROS 2
    /// bridge without any change to consumers of this interface.
    /// </summary>
    public interface ILocalizationProvider
    {
        string Name { get; }
        DataProvenance Provenance { get; }
        SubsystemStatus Status { get; }

        /// <summary>The current best estimate of vehicle state.</summary>
        PoseEstimate CurrentEstimate { get; }

        /// <summary>
        /// True when the estimator considers itself initialised and constrained. A visual
        /// estimator needs to observe parallax before it can produce a scaled estimate, so there
        /// is a genuine initialisation period during which navigation must not rely on it.
        /// </summary>
        bool IsConverged { get; }

        /// <summary>
        /// Metres of accumulated position error the estimator believes it has drifted since its
        /// last absolute fix. Zero for a GNSS estimator with a lock; grows monotonically for dead
        /// reckoning. Displayed prominently in GPS-denied mode because unbounded drift is the
        /// central limitation of odometry and hiding it would be dishonest.
        /// </summary>
        float AccumulatedDriftM { get; }

        bool Initialise(Vector3 knownStartPosition, Quaternion knownStartOrientation);
        void Tick(float fixedDeltaTime, ISensorProvider sensors);

        /// <summary>
        /// Resets the estimator to a known pose. On real hardware this corresponds to a loop
        /// closure or an operator-supplied fix.
        /// </summary>
        void ResetTo(Vector3 position, Quaternion orientation);
    }

    /// <summary>
    /// Publishes telemetry to consumers, chiefly the ground station UI.
    ///
    /// Kept as an interface so that a future MavlinkTelemetryProvider can feed the same GCS panels
    /// from a real aircraft, meaning the ground station developed for this simulator becomes the
    /// ground station for the physical UAV rather than being thrown away.
    /// </summary>
    public interface ITelemetryProvider
    {
        string Name { get; }
        DataProvenance Provenance { get; }
        TelemetrySnapshot Current { get; }

        /// <summary>Link quality in [0,1]. Always 1 in pure simulation; meaningful over a radio.</summary>
        float LinkQuality { get; }

        /// <summary>Round-trip latency in milliseconds. Zero in simulation.</summary>
        float LatencyMs { get; }
    }
}
