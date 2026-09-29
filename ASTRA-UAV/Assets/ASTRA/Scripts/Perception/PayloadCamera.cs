using System.Collections.Generic;
using UnityEngine;
using Astra.Contracts;
using Astra.Core;
using Astra.Core.Logging;

namespace Astra.Perception
{
    /// <summary>
    /// Onboard payload camera sensor model (ASTRA spec Sec 12).
    ///
    /// This is the payload SENSOR the spec asks for, distinct from the existing camera-rig/view
    /// controller (which frames the scene for the operator). It models a gimbo-mounted imaging
    /// payload with a configurable focal/FOV, resolution, a switchable imaging mode
    /// (RGB / Stereo / Depth / Thermal), a frustum, and a simple per-mode noise/limit model. It
    /// reports which tracked obstacles fall inside its frustum and within its usable range, which is
    /// what a real detection payload would hand to perception.
    ///
    /// HONESTY: this SIMULATES a payload's geometry and coverage. It does NOT run image formation,
    /// stereo matching, monocular depth or a thermal radiometric model - the mode only changes the
    /// declared range/limits and the badge. Presenting it as real imaging would overstate it.
    /// </summary>
    [DisallowMultipleComponent]
    public class PayloadCamera : MonoBehaviour
    {
        public enum CameraMode { Rgb = 0, Stereo = 1, Depth = 2, Thermal = 3 }

        [Header("Optics (operator-configurable)")]
        [SerializeField] private CameraMode mode = CameraMode.Rgb;
        [SerializeField] private float horizontalFovDeg = 69f;   // ~ a common wide RGB payload
        [SerializeField] private float aspect = 16f / 9f;
        [SerializeField] private int pixelWidth = 1920;
        [SerializeField] private int pixelHeight = 1080;
        [SerializeField] private float nearM = 0.3f;
        [SerializeField] private float farM = 120f;

        [Header("Imperfection model (SIMULATED, not calibrated)")]
        [Tooltip("Relative intensity/measurement noise, 1-sigma, in [0,1].")]
        [SerializeField] private float noiseSigma = 0.02f;

        [Header("Status")]
        [SerializeField] private SubsystemStatus status = SubsystemStatus.Initialising;

        public string Name => $"Payload Camera [{mode}] (SIMULATED)";
        public DataProvenance Provenance => DataProvenance.Simulated;
        public SubsystemStatus Status => status;

        public CameraMode Mode { get => mode; set => mode = value; }
        public float HorizontalFovDeg { get => horizontalFovDeg; set => horizontalFovDeg = Mathf.Clamp(value, 5f, 170f); }
        public float VerticalFovDeg => 2f * Mathf.Atan(Mathf.Tan(horizontalFovDeg * 0.5f * Mathf.Deg2Rad) / Mathf.Max(0.01f, aspect)) * Mathf.Rad2Deg;
        public int PixelWidth => pixelWidth;
        public int PixelHeight => pixelHeight;
        public float NoiseSigma { get => noiseSigma; set => noiseSigma = Mathf.Clamp01(value); }

        /// <summary>Usable range depends on the mode: depth/stereo fall off sooner than RGB/thermal.</summary>
        public float UsableRangeM
        {
            get
            {
                switch (mode)
                {
                    case CameraMode.Stereo: return Mathf.Min(farM, 40f);   // stereo baseline limits range
                    case CameraMode.Depth: return Mathf.Min(farM, 25f);    // active depth even shorter
                    case CameraMode.Thermal: return farM;                  // thermal sees far but low-res
                    default: return farM;                                  // RGB
                }
            }
        }

        // Live coverage stats for the sensor panel.
        public int ObstaclesInFrustum { get; private set; }
        public float NearestInFrustumM { get; private set; } = -1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindAny() != null) return;
            var fc = FindType<Astra.Flight.FlightControlSystem>();
            GameObject host = fc != null ? fc.gameObject : new GameObject("ASTRA_Payload_Camera");
            if (host.GetComponent<PayloadCamera>() == null) host.AddComponent<PayloadCamera>();
        }

        private static PayloadCamera FindAny()
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindAnyObjectByType<PayloadCamera>();
#else
            return Object.FindObjectOfType<PayloadCamera>();
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
            AstraServices.Register<PayloadCamera>(this);
            EventLog.Info(LogSource.Sensors, $"Payload camera online (SIMULATED): {mode}, {pixelWidth}x{pixelHeight}, HFOV {horizontalFovDeg:F0}°.");
        }

        private void OnDestroy()
        {
            AstraServices.UnregisterIfCurrent<PayloadCamera>(this);
        }

        private void FixedUpdate()
        {
            if (status != SubsystemStatus.Ok) return;

            IObstacleDetector det = AstraServices.Get<IObstacleDetector>();
            int inFov = 0;
            float nearest = float.PositiveInfinity;
            if (det != null)
            {
                var obs = det.Obstacles;
                for (int i = 0; i < obs.Count; i++)
                {
                    if (IsInFrustum(obs[i].Centre, out float range))
                    {
                        inFov++;
                        if (range < nearest) nearest = range;
                    }
                }
            }
            ObstaclesInFrustum = inFov;
            NearestInFrustumM = float.IsPositiveInfinity(nearest) ? -1f : nearest;
        }

        /// <summary>True if a world point lies inside the camera frustum and within usable range.</summary>
        public bool IsInFrustum(Vector3 world, out float rangeM)
        {
            Vector3 local = transform.InverseTransformPoint(world);
            rangeM = local.z;
            if (local.z < nearM || local.z > UsableRangeM) return false;

            float halfH = Mathf.Tan(horizontalFovDeg * 0.5f * Mathf.Deg2Rad) * local.z;
            float halfV = Mathf.Tan(VerticalFovDeg * 0.5f * Mathf.Deg2Rad) * local.z;
            return Mathf.Abs(local.x) <= halfH && Mathf.Abs(local.y) <= halfV;
        }
    }
}
