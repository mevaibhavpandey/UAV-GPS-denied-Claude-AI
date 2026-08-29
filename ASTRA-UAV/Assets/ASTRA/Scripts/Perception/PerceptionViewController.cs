using System.Collections.Generic;
using UnityEngine;
using Astra.Contracts;
using Astra.Core;
using Astra.Mission;

namespace Astra.Perception
{
    /// <summary>
    /// Synchronized Mode B (Perception View) visualizer.
    /// Overlays monochrome machine-vision representation, obstacle bounding volumes,
    /// LiDAR sensing rays, position uncertainty ellipsoids, and autonomy trajectories.
    /// </summary>
    [DisallowMultipleComponent]
    public class PerceptionViewController : MonoBehaviour
    {
        [Header("Visualization Settings")]
        [SerializeField] private MapViewMode currentMode = MapViewMode.RealWorld;
        [SerializeField] private Color perceptionBgColor = new Color(0.08f, 0.09f, 0.11f);
        [SerializeField] private Color rayColor = new Color(0.2f, 0.85f, 0.95f, 0.4f);
        [SerializeField] private Color obstacleBoxColor = new Color(0.98f, 0.35f, 0.25f, 0.8f);
        [SerializeField] private Color trajectoryColor = new Color(0.3f, 0.95f, 0.45f, 0.9f);
        [SerializeField] private Color uncertaintyColor = new Color(0.95f, 0.75f, 0.2f, 0.35f);

        [Header("References")]
        [SerializeField] private RaycastObstacleDetector detector;
        [SerializeField] private AutonomyController autonomyController;
        [SerializeField] private Camera mainCamera;

        public MapViewMode CurrentMode => currentMode;

        private void Awake()
        {
            if (detector == null) detector = FindFirstObjectByType<RaycastObstacleDetector>();
            if (autonomyController == null) autonomyController = FindFirstObjectByType<AutonomyController>();
            if (mainCamera == null) mainCamera = Camera.main;
        }

        private void Update()
        {
            // F5 shortcut toggles between Real World View and Perception View
            if (Input.GetKeyDown(KeyCode.F5))
            {
                ToggleViewMode();
            }
        }

        public void ToggleViewMode()
        {
            SetViewMode(currentMode == MapViewMode.RealWorld ? MapViewMode.Perception : MapViewMode.RealWorld);
        }

        public void SetViewMode(MapViewMode mode)
        {
            if (currentMode != mode)
            {
                currentMode = mode;
                AstraEvents.RaiseMapViewModeChanged(mode);
            }
        }

        private void OnDrawGizmos()
        {
            if (currentMode != MapViewMode.Perception && !Application.isPlaying) return;

            // 1. Draw Trajectory Line
            if (autonomyController != null && autonomyController.CurrentPlannedPath != null)
            {
                var path = autonomyController.CurrentPlannedPath;
                Gizmos.color = trajectoryColor;
                for (int i = 1; i < path.Count; i++)
                {
                    Gizmos.DrawLine(path[i - 1], path[i]);
                    Gizmos.DrawWireSphere(path[i], 0.3f);
                }
            }

            // 2. Draw Tracked Obstacle Bounding Volumes
            if (detector != null && detector.Obstacles != null)
            {
                var obstacles = detector.Obstacles;
                for (int i = 0; i < obstacles.Count; i++)
                {
                    var obs = obstacles[i];
                    Gizmos.color = obstacleBoxColor;
                    Gizmos.DrawWireCube(obs.Centre, obs.HalfExtents * 2.0f);
                    Gizmos.DrawLine(transform.position, obs.ClosestPoint);
                }
            }

            // 3. Draw Localization Uncertainty Ellipsoid
            ILocalizationProvider loc = AstraServices.Get<ILocalizationProvider>();
            if (loc != null && loc.CurrentEstimate.IsValid)
            {
                Gizmos.color = uncertaintyColor;
                Vector3 estPos = loc.CurrentEstimate.Position;
                Vector3 stdDev = loc.CurrentEstimate.PositionStdDev;
                Gizmos.DrawWireCube(estPos, stdDev * 2.0f);
            }
        }
    }
}
