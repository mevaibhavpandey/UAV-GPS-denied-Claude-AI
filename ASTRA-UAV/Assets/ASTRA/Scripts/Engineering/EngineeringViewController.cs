using System.Collections.Generic;
using UnityEngine;
using Astra.Drone;

namespace Astra.Engineering
{
    public enum EngineeringViewMode
    {
        Normal = 0,
        TransparentXRay = 1,
        Exploded = 2,
        Wireframe = 3
    }

    /// <summary>
    /// Digital Twin Engineering Inspection View.
    /// Supports exploded views with smooth component separation along radial vectors,
    /// X-Ray transparency shaders, wireframes, and component inspection breakdown.
    /// </summary>
    [DisallowMultipleComponent]
    public class EngineeringViewController : MonoBehaviour
    {
        [Header("State")]
        [SerializeField] private bool isEngineeringViewActive = false;
        [SerializeField] private EngineeringViewMode viewMode = EngineeringViewMode.Normal;
        [SerializeField] private float explosionDistance = 0.45f;
        [SerializeField] private float animationSpeed = 4.0f;

        private class ComponentOffsetRecord
        {
            public Transform Transform;
            public Vector3 BaseLocalPos;
            public Vector3 ExplodedLocalOffset;
        }

        private readonly List<ComponentOffsetRecord> _offsetRecords = new List<ComponentOffsetRecord>();
        private float _currentExplosionFraction = 0f;
        private UavComponent _selectedComponent;

        public bool IsActive => isEngineeringViewActive;
        public EngineeringViewMode ViewMode => viewMode;
        public UavComponent SelectedComponent => _selectedComponent;

        private void Start()
        {
            CacheComponentHierarchy();
        }

        public void CacheComponentHierarchy()
        {
            _offsetRecords.Clear();
            var components = GetComponentsInChildren<UavComponent>(true);

            foreach (var comp in components)
            {
                Transform t = comp.transform;
                Vector3 radialDir = (t.localPosition.sqrMagnitude > 0.001f) ? t.localPosition.normalized : Vector3.up;

                // Motors separate outward, battery drops down, avionics stack moves up
                Vector3 offsetDir = radialDir;
                if (comp.name.Contains("Motor")) offsetDir = (new Vector3(radialDir.x, 0.2f, radialDir.z)).normalized * 1.5f;
                else if (comp.name.Contains("Battery")) offsetDir = Vector3.down * 1.2f;
                else if (comp.name.Contains("Pixhawk") || comp.name.Contains("Pi")) offsetDir = Vector3.up * 1.4f;

                _offsetRecords.Add(new ComponentOffsetRecord
                {
                    Transform = t,
                    BaseLocalPos = t.localPosition,
                    ExplodedLocalOffset = offsetDir * explosionDistance
                });
            }
        }

        private void Update()
        {
            // F6 shortcut toggles Engineering View
            if (Input.GetKeyDown(KeyCode.F6))
            {
                ToggleEngineeringView();
            }

            if (!isEngineeringViewActive) return;

            // Number keys 1-4 toggle engineering visual modes
            if (Input.GetKeyDown(KeyCode.Alpha1)) SetMode(EngineeringViewMode.Normal);
            if (Input.GetKeyDown(KeyCode.Alpha2)) SetMode(EngineeringViewMode.TransparentXRay);
            if (Input.GetKeyDown(KeyCode.Alpha3)) SetMode(EngineeringViewMode.Exploded);
            if (Input.GetKeyDown(KeyCode.Alpha4)) SetMode(EngineeringViewMode.Wireframe);

            // Smoothly animate exploded view separation
            float targetFraction = (viewMode == EngineeringViewMode.Exploded) ? 1.0f : 0.0f;
            _currentExplosionFraction = Mathf.MoveTowards(_currentExplosionFraction, targetFraction, Time.deltaTime * animationSpeed);

            for (int i = 0; i < _offsetRecords.Count; i++)
            {
                var rec = _offsetRecords[i];
                if (rec.Transform != null)
                {
                    rec.Transform.localPosition = rec.BaseLocalPos + rec.ExplodedLocalOffset * _currentExplosionFraction;
                }
            }
        }

        public void ToggleEngineeringView()
        {
            isEngineeringViewActive = !isEngineeringViewActive;
            if (isEngineeringViewActive)
            {
                var cam = FindFirstObjectByType<Astra.Cameras.CameraController>();
                cam?.SetRig(Astra.Cameras.CameraRigMode.Engineering);
            }
        }

        public void SetMode(EngineeringViewMode mode)
        {
            viewMode = mode;
        }

        public void SelectComponent(UavComponent comp)
        {
            _selectedComponent = comp;
        }
    }
}
