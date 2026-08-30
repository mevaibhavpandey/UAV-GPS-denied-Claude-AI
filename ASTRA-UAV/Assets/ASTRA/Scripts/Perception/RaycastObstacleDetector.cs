using System;
using System.Collections.Generic;
using UnityEngine;
using Astra.Contracts;
using Astra.Core;
using Astra.Core.Logging;

namespace Astra.Perception
{
    /// <summary>
    /// Multi-beam LiDAR and depth camera simulation via physics raycasts.
    /// Clusters returns into tracked 3D obstacles with velocity estimation and extent.
    /// Implements IObstacleDetector.
    /// </summary>
    [DisallowMultipleComponent]
    public class RaycastObstacleDetector : MonoBehaviour, IObstacleDetector
    {
        [Header("Sensor Characteristics")]
        [SerializeField] private float maxRangeM = 60.0f;
        [SerializeField] private float horizontalFovDeg = 120.0f;
        [SerializeField] private float verticalFovDeg = 45.0f;
        [SerializeField] private int horizontalRays = 16;
        [SerializeField] private int verticalRays = 8;
        [SerializeField] private LayerMask obstacleLayerMask = ~0;

        [Header("Status")]
        [SerializeField] private SubsystemStatus status = SubsystemStatus.Initialising;

        private readonly List<ObstacleReading> _trackedObstacles = new List<ObstacleReading>();
        private readonly Dictionary<int, ObstacleTrackInternal> _activeTracks = new Dictionary<int, ObstacleTrackInternal>();
        private int _nextTrackId = 1;
        private int _lastSampleCount;
        private float _lastScanDurationMs;

        public string Name => "Simulated 3D LiDAR / Depth Sensor";
        public DataProvenance Provenance => DataProvenance.Simulated;
        public SubsystemStatus Status => status;
        public float MaxRangeM => maxRangeM;
        public float HorizontalFovDeg => horizontalFovDeg;
        public float VerticalFovDeg => verticalFovDeg;
        public IReadOnlyList<ObstacleReading> Obstacles => _trackedObstacles;
        public int LastScanSampleCount => _lastSampleCount;
        public float LastScanDurationMs => _lastScanDurationMs;

        private class ObstacleTrackInternal
        {
            public int TrackId;
            public Vector3 ClosestPoint;
            public Vector3 Centre;
            public Vector3 HalfExtents;
            public float DistanceM;
            public float RelativeBearingDeg;
            public Vector3 Velocity;
            public bool VelocityIsReliable;
            public int ObservationCount;
            public double LastSeen;
            public ObstacleClass Class;
            public GameObject HitObject;
        }

        private void Start()
        {
            Initialise();
            AstraServices.Register<IObstacleDetector>(this);
        }

        private void OnDestroy()
        {
            AstraServices.UnregisterIfCurrent<IObstacleDetector>(this);
        }

        public void Initialise()
        {
            _trackedObstacles.Clear();
            _activeTracks.Clear();
            status = SubsystemStatus.Ok;
        }

        public void Reset()
        {
            _trackedObstacles.Clear();
            _activeTracks.Clear();
        }

        public void Scan(Vector3 sensorPosition, Quaternion sensorOrientation, float fixedDeltaTime)
        {
            float startTime = Time.realtimeSinceStartup;
            _lastSampleCount = 0;

            HashSet<int> seenTracksThisFrame = new HashSet<int>();
            double now = Time.timeAsDouble;

            float hStep = horizontalFovDeg / Mathf.Max(1, horizontalRays - 1);
            float vStep = verticalFovDeg / Mathf.Max(1, verticalRays - 1);
            float hStart = -horizontalFovDeg * 0.5f;
            float vStart = -verticalFovDeg * 0.5f;

            for (int v = 0; v < verticalRays; v++)
            {
                float pitch = vStart + v * vStep;
                for (int h = 0; h < horizontalRays; h++)
                {
                    float yaw = hStart + h * hStep;
                    Quaternion rayRot = sensorOrientation * Quaternion.Euler(pitch, yaw, 0f);
                    Vector3 rayDir = rayRot * Vector3.forward;

                    _lastSampleCount++;

                    RaycastHit hit;
                    if (Physics.Raycast(sensorPosition, rayDir, out hit, maxRangeM, obstacleLayerMask))
                    {
                        // Ignore own UAV colliders
                        if (hit.collider.transform.IsChildOf(transform) || hit.collider.transform == transform)
                            continue;

                        // Ignore flat ground surface directly underneath (landing pad / ground plane)
                        if (hit.point.y < 0.35f && hit.normal.y > 0.6f)
                            continue;

                        // Ignore hits too close to sensor (< 0.7m) to prevent self-shadowing
                        if (hit.distance < 0.7f)
                            continue;

                        GameObject hitGo = hit.collider.gameObject;
                        int trackKey = hitGo.GetInstanceID();

                        ObstacleClass obsClass = ClassifyCollider(hit.collider);
                        float dist = hit.distance;
                        Vector3 forwardVec = sensorOrientation * Vector3.forward;
                        Vector3 toHit = (hit.point - sensorPosition).normalized;
                        float bearing = Vector3.SignedAngle(forwardVec, toHit, Vector3.up);

                        Bounds bounds = hit.collider.bounds;

                        if (_activeTracks.TryGetValue(trackKey, out ObstacleTrackInternal track))
                        {
                            Vector3 prevCentre = track.Centre;
                            track.ClosestPoint = hit.point;
                            track.Centre = bounds.center;
                            track.HalfExtents = bounds.extents;
                            track.DistanceM = dist;
                            track.RelativeBearingDeg = bearing;

                            if (fixedDeltaTime > 0.0001f)
                            {
                                Vector3 instantVel = (track.Centre - prevCentre) / fixedDeltaTime;
                                track.Velocity = Vector3.Lerp(track.Velocity, instantVel, 0.3f);
                            }

                            track.ObservationCount++;
                            track.VelocityIsReliable = track.ObservationCount >= 5;
                            track.LastSeen = now;
                            track.Class = obsClass;
                            seenTracksThisFrame.Add(trackKey);
                        }
                        else
                        {
                            track = new ObstacleTrackInternal
                            {
                                TrackId = _nextTrackId++,
                                ClosestPoint = hit.point,
                                Centre = bounds.center,
                                HalfExtents = bounds.extents,
                                DistanceM = dist,
                                RelativeBearingDeg = bearing,
                                Velocity = Vector3.zero,
                                VelocityIsReliable = false,
                                ObservationCount = 1,
                                LastSeen = now,
                                Class = obsClass,
                                HitObject = hitGo
                            };
                            _activeTracks[trackKey] = track;
                            seenTracksThisFrame.Add(trackKey);

                            ObstacleReading reading = ToReading(track);
                            AstraEvents.RaiseObstacleDetected(reading);
                        }
                    }
                }
            }

            // Remove expired tracks (not seen for > 1.0 second)
            List<int> toRemove = new List<int>();
            foreach (var kvp in _activeTracks)
            {
                if (now - kvp.Value.LastSeen > 1.0)
                {
                    toRemove.Add(kvp.Key);
                }
            }

            foreach (int key in toRemove)
            {
                int trackId = _activeTracks[key].TrackId;
                _activeTracks.Remove(key);
                AstraEvents.RaiseObstacleLost(trackId);
            }

            // Rebuild public readonly list
            _trackedObstacles.Clear();
            foreach (var track in _activeTracks.Values)
            {
                _trackedObstacles.Add(ToReading(track));
            }

            _lastScanDurationMs = (Time.realtimeSinceStartup - startTime) * 1000.0f;
        }

        private ObstacleReading ToReading(ObstacleTrackInternal track)
        {
            return new ObstacleReading
            {
                TrackId = track.TrackId,
                ClosestPoint = track.ClosestPoint,
                Centre = track.Centre,
                HalfExtents = track.HalfExtents,
                DistanceM = track.DistanceM,
                RelativeBearingDeg = track.RelativeBearingDeg,
                Velocity = track.Velocity,
                VelocityIsReliable = track.VelocityIsReliable,
                ObservationCount = track.ObservationCount,
                LastSeen = track.LastSeen,
                SensorName = Name,
                Class = track.Class
            };
        }

        private ObstacleClass ClassifyCollider(Collider col)
        {
            string tag = col.tag;
            string name = col.name.ToLower();

            if (tag == "Building" || name.Contains("building") || name.Contains("structure"))
                return ObstacleClass.Building;
            if (tag == "Terrain" || name.Contains("terrain") || name.Contains("ground"))
                return ObstacleClass.Terrain;
            if (name.Contains("tree") || name.Contains("veg"))
                return ObstacleClass.Vegetation;
            if (name.Contains("pole") || name.Contains("tower"))
                return ObstacleClass.Pole;
            if (name.Contains("vehicle") || name.Contains("car") || name.Contains("truck"))
                return ObstacleClass.GroundVehicle;
            if (name.Contains("drone") || name.Contains("uav") || name.Contains("aircraft"))
                return ObstacleClass.Aircraft;
            if (name.Contains("bird"))
                return ObstacleClass.Bird;

            return ObstacleClass.Building;
        }
    }
}
