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
    /// Renders via GL immediate mode so it is visible in Game View and standalone builds.
    /// </summary>
    [DisallowMultipleComponent]
    public class PerceptionViewController : MonoBehaviour
    {
        [Header("Visualization Settings")]
        [SerializeField] private MapViewMode currentMode = MapViewMode.RealWorld;
        [SerializeField] private Color perceptionBgColor = new Color(0.05f, 0.07f, 0.09f);
        [SerializeField] private Color rayColor = new Color(0.15f, 0.85f, 0.95f, 0.35f);
        [SerializeField] private Color obstacleBoxColor = new Color(0.98f, 0.30f, 0.20f, 0.9f);
        [SerializeField] private Color trajectoryColor = new Color(0.20f, 0.95f, 0.45f, 0.95f);
        [SerializeField] private Color uncertaintyColor = new Color(0.95f, 0.75f, 0.15f, 0.5f);

        [Header("References")]
        [SerializeField] private RaycastObstacleDetector detector;
        [SerializeField] private AutonomyController autonomyController;
        [SerializeField] private Camera mainCamera;

        private Material _glLineMat;
        private Color _originalBgColor;
        private CameraClearFlags _originalClearFlags;

        public MapViewMode CurrentMode => currentMode;

        private void Awake()
        {
            if (detector == null) detector = FindFirstObjectByType<RaycastObstacleDetector>();
            if (autonomyController == null) autonomyController = FindFirstObjectByType<AutonomyController>();
            if (mainCamera == null) mainCamera = Camera.main;

            if (mainCamera != null)
            {
                _originalBgColor = mainCamera.backgroundColor;
                _originalClearFlags = mainCamera.clearFlags;
            }
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
                if (mainCamera == null) mainCamera = Camera.main;

                if (mainCamera != null)
                {
                    if (currentMode == MapViewMode.Perception)
                    {
                        mainCamera.backgroundColor = perceptionBgColor;
                    }
                    else
                    {
                        mainCamera.backgroundColor = _originalBgColor;
                    }
                }

                AstraEvents.RaiseMapViewModeChanged(mode);
            }
        }

        private void EnsureGLMaterial()
        {
            if (_glLineMat == null)
            {
                Shader shader = Shader.Find("Hidden/Internal-Colored");
                if (shader != null)
                {
                    _glLineMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                    _glLineMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    _glLineMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    _glLineMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                    _glLineMat.SetInt("_ZWrite", 0);
                    _glLineMat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                }
            }
        }

        private void OnRenderObject()
        {
            if (currentMode != MapViewMode.Perception) return;

            EnsureGLMaterial();
            if (_glLineMat == null) return;

            _glLineMat.SetPass(0);
            GL.PushMatrix();

            // 1. Draw Planned Trajectory Path
            if (autonomyController != null && autonomyController.CurrentPlannedPath != null)
            {
                var path = autonomyController.CurrentPlannedPath;
                if (path.Count > 1)
                {
                    GL.Begin(GL.LINES);
                    GL.Color(trajectoryColor);
                    for (int i = 1; i < path.Count; i++)
                    {
                        GL.Vertex(path[i - 1]);
                        GL.Vertex(path[i]);
                    }
                    GL.End();
                }
            }

            // 2. Draw Tracked Obstacle 3D Wireframe Boxes
            if (detector != null && detector.Obstacles != null)
            {
                var obstacles = detector.Obstacles;
                Vector3 uavPos = transform.position;

                GL.Begin(GL.LINES);
                for (int i = 0; i < obstacles.Count; i++)
                {
                    var obs = obstacles[i];
                    GL.Color(obstacleBoxColor);
                    DrawGLWireCube(obs.Centre, obs.HalfExtents);

                    // Swept LiDAR ray to obstacle
                    GL.Color(rayColor);
                    GL.Vertex(uavPos);
                    GL.Vertex(obs.ClosestPoint);
                }
                GL.End();
            }

            // 3. Draw Localization Uncertainty
            ILocalizationProvider loc = AstraServices.Get<ILocalizationProvider>();
            if (loc != null && loc.CurrentEstimate.IsValid)
            {
                GL.Begin(GL.LINES);
                GL.Color(uncertaintyColor);
                DrawGLWireCube(loc.CurrentEstimate.Position, loc.CurrentEstimate.PositionStdDev);
                GL.End();
            }

            GL.PopMatrix();
        }

        private void DrawGLWireCube(Vector3 center, Vector3 halfExtents)
        {
            Vector3 min = center - halfExtents;
            Vector3 max = center + halfExtents;

            // Bottom square
            GL.Vertex3(min.x, min.y, min.z); GL.Vertex3(max.x, min.y, min.z);
            GL.Vertex3(max.x, min.y, min.z); GL.Vertex3(max.x, min.y, max.z);
            GL.Vertex3(max.x, min.y, max.z); GL.Vertex3(min.x, min.y, max.z);
            GL.Vertex3(min.x, min.y, max.z); GL.Vertex3(min.x, min.y, min.z);

            // Top square
            GL.Vertex3(min.x, max.y, min.z); GL.Vertex3(max.x, max.y, min.z);
            GL.Vertex3(max.x, max.y, min.z); GL.Vertex3(max.x, max.y, max.z);
            GL.Vertex3(max.x, max.y, max.z); GL.Vertex3(min.x, max.y, max.z);
            GL.Vertex3(min.x, max.y, max.z); GL.Vertex3(min.x, max.y, min.z);

            // 4 vertical edges
            GL.Vertex3(min.x, min.y, min.z); GL.Vertex3(min.x, max.y, min.z);
            GL.Vertex3(max.x, min.y, min.z); GL.Vertex3(max.x, max.y, min.z);
            GL.Vertex3(max.x, min.y, max.z); GL.Vertex3(max.x, max.y, max.z);
            GL.Vertex3(min.x, min.y, max.z); GL.Vertex3(min.x, max.y, max.z);
        }

        private void OnDrawGizmos()
        {
            if (currentMode != MapViewMode.Perception && !Application.isPlaying) return;

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
        }
    }
}
