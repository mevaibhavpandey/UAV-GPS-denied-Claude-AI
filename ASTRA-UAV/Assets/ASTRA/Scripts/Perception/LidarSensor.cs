using System.Collections.Generic;
using UnityEngine;
using Astra.Contracts;
using Astra.Core;
using Astra.Core.Logging;

namespace Astra.Perception
{
    /// <summary>
    /// Dedicated configurable LiDAR sensor model (ASTRA spec Sec 13-15, 54).
    ///
    /// The project already has <see cref="RaycastObstacleDetector"/>, which raycasts and CLUSTERS
    /// returns into tracked obstacles for the avoidance stack. This component is the complementary
    /// thing the spec asks for and that the detector is not: an explicit, operator-configurable
    /// LiDAR SENSOR that exposes range, field of view, horizontal + vertical angular resolution,
    /// scan rate, range noise, dropout probability and a vertical blind-spot, and produces a raw
    /// POINT CLOUD plus honest scan statistics for a sensor panel. It does not replace or duplicate
    /// the detector (Sec 53) - the detector remains the perception consumer; this is the sensor a
    /// real LiDAR driver would map onto, and its point cloud is what a point-cloud visualiser reads.
    ///
    /// HONESTY: every point is SIMULATED from Unity physics raycasts. The noise and dropout are a
    /// plausible parametric model, NOT a calibrated model of any specific LiDAR unit; they exist to
    /// demonstrate that downstream code tolerates imperfect returns, not to claim sensor fidelity.
    /// </summary>
    [DisallowMultipleComponent]
    public class LidarSensor : MonoBehaviour
    {
        [Header("Sensor Characteristics (operator-configurable)")]
        [SerializeField] private float maxRangeM = 80.0f;
        [SerializeField] private float minRangeM = 0.5f;
        [SerializeField] private float horizontalFovDeg = 360.0f;
        [SerializeField] private float verticalFovDeg = 30.0f;
        [SerializeField] private int horizontalResolution = 72;  // beams per revolution slice
        [SerializeField] private int verticalChannels = 16;      // e.g. a 16-line LiDAR
        [SerializeField] private float scanRateHz = 10.0f;
        [SerializeField] private LayerMask layerMask = ~0;

        [Header("Imperfection model (SIMULATED, not calibrated)")]
        [Tooltip("1-sigma range noise in metres, added to every return.")]
        [SerializeField] private float rangeNoiseSigmaM = 0.03f;
        [Tooltip("Probability in [0,1] that any given beam returns nothing even when it hit something.")]
        [SerializeField] private float dropoutProbability = 0.02f;
        [Tooltip("Half-angle of a vertical blind cone directly below the sensor, degrees. Real spinning LiDARs are blind under the base.")]
        [SerializeField] private float lowerBlindConeDeg = 8.0f;

        [Header("Status")]
        [SerializeField] private SubsystemStatus status = SubsystemStatus.Initialising;

        public string Name => "Configurable 3D LiDAR (SIMULATED)";
        public DataProvenance Provenance => DataProvenance.Simulated;
        public SubsystemStatus Status => status;

        // Configurable getters/setters for the panel and the setup screen.
        public float MaxRangeM { get => maxRangeM; set => maxRangeM = Mathf.Max(1f, value); }
        public float HorizontalFovDeg { get => horizontalFovDeg; set => horizontalFovDeg = Mathf.Clamp(value, 1f, 360f); }
        public float VerticalFovDeg { get => verticalFovDeg; set => verticalFovDeg = Mathf.Clamp(value, 1f, 180f); }
        public int HorizontalResolution { get => horizontalResolution; set => horizontalResolution = Mathf.Clamp(value, 4, 720); }
        public int VerticalChannels { get => verticalChannels; set => verticalChannels = Mathf.Clamp(value, 1, 128); }
        public float ScanRateHz { get => scanRateHz; set => scanRateHz = Mathf.Clamp(value, 1f, 40f); }
        public float RangeNoiseSigmaM { get => rangeNoiseSigmaM; set => rangeNoiseSigmaM = Mathf.Max(0f, value); }
        public float DropoutProbability { get => dropoutProbability; set => dropoutProbability = Mathf.Clamp01(value); }

        // Live stats surfaced to the GCS LiDAR panel.
        public int LastPointCount { get; private set; }
        public int LastBeamsCast { get; private set; }
        public float LastScanDurationMs { get; private set; }
        public float LastReturnRatePercent { get; private set; }
        public float NearestReturnM { get; private set; }

        /// <summary>
        /// The most recent point cloud in WORLD space. Owned by the sensor and reused between scans;
        /// callers must copy if they need to retain it (avoids per-scan allocation).
        /// </summary>
        public IReadOnlyList<Vector3> PointCloud => _points;

        private readonly List<Vector3> _points = new List<Vector3>();
        private float _lastScanTime;

        // -------- self-install bootstrap (no scene editing needed) --------
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindAny() != null) return;

            // Prefer mounting on the flight controller's GameObject (the airframe) so the sensor
            // rides with the vehicle; fall back to a standalone object if none is present yet.
            var fc = FindType<Astra.Flight.FlightControlSystem>();
            GameObject host = fc != null ? fc.gameObject : new GameObject("ASTRA_LiDAR");
            if (host.GetComponent<LidarSensor>() == null) host.AddComponent<LidarSensor>();
        }

        private static LidarSensor FindAny()
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindAnyObjectByType<LidarSensor>();
#else
            return Object.FindObjectOfType<LidarSensor>();
#endif
        }

        private static T FindType<T>() where T : Object
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindFirstObjectByType<T>();
#else
            return Object.FindObjectOfType<T>();
#endif
        }

        private void Start()
        {
            status = SubsystemStatus.Ok;
            AstraServices.Register<LidarSensor>(this);
            EventLog.Info(LogSource.Sensors,
                $"LiDAR online (SIMULATED): {verticalChannels}ch x {horizontalResolution} @ {scanRateHz:F0} Hz, {maxRangeM:F0} m range.");
        }

        private void OnDestroy()
        {
            AstraServices.UnregisterIfCurrent<LidarSensor>(this);
        }

        private void FixedUpdate()
        {
            if (status != SubsystemStatus.Ok) return;
            if (Time.time - _lastScanTime < 1.0f / Mathf.Max(1f, scanRateHz)) return;
            _lastScanTime = Time.time;
            Scan(transform.position, transform.rotation);
        }

        /// <summary>Casts one full frame of beams and rebuilds the point cloud with the noise/dropout model.</summary>
        public void Scan(Vector3 origin, Quaternion orientation)
        {
            float t0 = Time.realtimeSinceStartup;
            _points.Clear();
            int beams = 0;
            int hits = 0;
            float nearest = float.PositiveInfinity;

            float hStep = horizontalFovDeg / Mathf.Max(1, horizontalResolution);
            float vStep = verticalFovDeg / Mathf.Max(1, verticalChannels - 1);
            float hStart = -horizontalFovDeg * 0.5f;
            float vStart = -verticalFovDeg * 0.5f;

            for (int vc = 0; vc < verticalChannels; vc++)
            {
                float pitch = vStart + vc * vStep;

                // Honest blind spot: skip beams that fall inside the lower blind cone.
                if (pitch < -(90f - lowerBlindConeDeg)) continue;

                for (int hc = 0; hc < horizontalResolution; hc++)
                {
                    float yaw = hStart + hc * hStep;
                    beams++;

                    // Random dropout: the beam is lost regardless of geometry.
                    if (Random.value < dropoutProbability) continue;

                    Vector3 dir = orientation * Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;
                    if (Physics.Raycast(origin, dir, out RaycastHit hit, maxRangeM, layerMask))
                    {
                        // Ignore own airframe.
                        if (hit.collider.transform == transform || hit.collider.transform.IsChildOf(transform))
                            continue;

                        float range = hit.distance + RandomGaussian() * rangeNoiseSigmaM;
                        if (range < minRangeM) continue;

                        Vector3 p = origin + dir * range;
                        _points.Add(p);
                        hits++;
                        if (range < nearest) nearest = range;
                    }
                }
            }

            LastBeamsCast = beams;
            LastPointCount = hits;
            LastReturnRatePercent = beams > 0 ? 100f * hits / beams : 0f;
            NearestReturnM = float.IsPositiveInfinity(nearest) ? -1f : nearest;
            LastScanDurationMs = (Time.realtimeSinceStartup - t0) * 1000.0f;
        }

        /// <summary>Box-Muller standard normal sample (mean 0, sigma 1).</summary>
        private static float RandomGaussian()
        {
            float u1 = Mathf.Max(1e-6f, Random.value);
            float u2 = Random.value;
            return Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(2f * Mathf.PI * u2);
        }
    }
}
