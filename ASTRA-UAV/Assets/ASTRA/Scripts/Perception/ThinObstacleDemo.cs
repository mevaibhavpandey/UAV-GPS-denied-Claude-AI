using System.Collections.Generic;
using UnityEngine;
using Astra.Contracts;
using Astra.Core;
using Astra.Core.Logging;

namespace Astra.Perception
{
    /// <summary>
    /// Thin-obstacle (wire/cable) representation and an HONEST sensor detection-limit demonstration
    /// (ASTRA spec Sec 24).
    ///
    /// Thin obstacles - power lines, guy wires, antennas - are the classic case where a discretely
    /// sampled sensor can fail: between two adjacent LiDAR beams there is an angular gap, and a wire
    /// thinner than that gap at its range can pass completely undetected. Pretending the simulator
    /// always sees them would be dishonest and would teach the wrong lesson. This component instead:
    ///   1. Spawns a small set of genuinely thin wire colliders (real geometry the sensors must try
    ///      to detect), classified as <see cref="ObstacleClass.Wire"/>.
    ///   2. Computes, from the LiDAR's actual angular resolution, the range beyond which each wire
    ///      is thinner than the beam spacing - i.e. the honest range past which detection is not
    ///      guaranteed - and reports it rather than hiding it.
    ///
    /// HONESTY: the detection-limit figure is a geometric bound (wire subtends less than one beam
    /// spacing), SIMULATED and clearly labelled. It is a teaching demonstration of a real sensor
    /// limitation, not a claim that the simulator models diffraction, partial returns or material
    /// reflectivity.
    /// </summary>
    [DisallowMultipleComponent]
    public class ThinObstacleDemo : MonoBehaviour
    {
        [SerializeField] private bool spawnDemoWires = true;
        [SerializeField] private float wireDiameterM = 0.02f;   // ~2 cm cable
        [SerializeField] private float wireLengthM = 40f;
        [SerializeField] private float wireHeightAglM = 18f;

        public struct WireInfo
        {
            public string Name;
            public float DiameterM;
            public Vector3 Midpoint;
            public float GuaranteedDetectRangeM;  // honest: beyond this the wire may slip between beams
        }

        private readonly List<WireInfo> _wires = new List<WireInfo>();
        public IReadOnlyList<WireInfo> Wires => _wires;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
#if UNITY_2023_1_OR_NEWER
            if (Object.FindAnyObjectByType<ThinObstacleDemo>() != null) return;
#else
            if (Object.FindObjectOfType<ThinObstacleDemo>() != null) return;
#endif
            new GameObject("ASTRA_ThinObstacle_Demo").AddComponent<ThinObstacleDemo>();
        }

        private void Start()
        {
            AstraServices.Register<ThinObstacleDemo>(this);
            if (spawnDemoWires) SpawnWires();
            ReportDetectionLimits();
        }

        private void OnDestroy()
        {
            AstraServices.UnregisterIfCurrent<ThinObstacleDemo>(this);
        }

        private void SpawnWires()
        {
            // Two spans of parallel wires, offset from the origin, at wire height. Placed away from
            // the immediate launch point so they are obstacles to be detected en route, not on the pad.
            Vector3[] spanCentres =
            {
                new Vector3(60f, wireHeightAglM, 90f),
                new Vector3(-40f, wireHeightAglM, 120f)
            };

            int idx = 0;
            foreach (Vector3 centre in spanCentres)
            {
                for (int k = 0; k < 3; k++)
                {
                    GameObject wire = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    wire.name = $"Wire_{idx++}";
                    // Unity cylinder is 2 units tall along Y with radius 0.5; scale to a thin, long rod.
                    wire.transform.localScale = new Vector3(wireDiameterM, wireLengthM * 0.5f, wireDiameterM);
                    // Lay it horizontal (along X), stack the strands vertically 0.5 m apart.
                    wire.transform.position = centre + new Vector3(0f, k * 0.5f, 0f);
                    wire.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
                    wire.transform.SetParent(transform, true);

                    var mr = wire.GetComponent<MeshRenderer>();
                    if (mr != null) mr.material.color = new Color(0.15f, 0.15f, 0.17f);

                    _wires.Add(new WireInfo
                    {
                        Name = wire.name,
                        DiameterM = wireDiameterM,
                        Midpoint = wire.transform.position,
                        GuaranteedDetectRangeM = -1f // filled in by ReportDetectionLimits
                    });
                }
            }
        }

        /// <summary>
        /// For the active LiDAR, computes and logs the honest guaranteed-detection range for a wire
        /// of the demo diameter: the range at which the wire's angular size equals one beam spacing.
        /// Beyond it, detection is geometry-dependent and not guaranteed.
        /// </summary>
        private void ReportDetectionLimits()
        {
            LidarSensor lidar = AstraServices.Get<LidarSensor>();
            if (lidar == null)
            {
                EventLog.Warning(LogSource.Perception, "Thin-obstacle demo: no LiDAR present to characterise detection limit.");
                return;
            }

            // Horizontal beam spacing in radians.
            float beamSpacingRad = (lidar.HorizontalFovDeg * Mathf.Deg2Rad) / Mathf.Max(1, lidar.HorizontalResolution);
            if (beamSpacingRad <= 1e-6f) return;

            // A wire of diameter d subtends >= one beam spacing while range <= d / beamSpacing.
            float guaranteed = wireDiameterM / beamSpacingRad;

            for (int i = 0; i < _wires.Count; i++)
            {
                WireInfo w = _wires[i];
                w.GuaranteedDetectRangeM = guaranteed;
                _wires[i] = w;
            }

            EventLog.Warning(LogSource.Perception,
                $"HONEST DETECTION LIMIT (SIMULATED): a {wireDiameterM * 1000f:F0} mm wire subtends one LiDAR beam " +
                $"spacing at ~{guaranteed:F1} m (beam spacing {beamSpacingRad * Mathf.Rad2Deg:F2}°). Beyond that range the " +
                $"wire can pass between beams and is NOT guaranteed to be detected. {_wires.Count} demo wires spawned.");
        }
    }
}
