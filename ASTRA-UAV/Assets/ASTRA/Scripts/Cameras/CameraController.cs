using UnityEngine;

namespace Astra.Cameras
{
    public enum CameraRigMode
    {
        ChaseFollow = 0,
        FpvCockpit = 1,
        Orbit = 2,
        TopDownMap = 3,
        Engineering = 4,
        Cinematic = 5
    }

    /// <summary>
    /// Multi-rig camera controller providing chase, FPV, orbit, top-down map, and engineering views.
    /// Operates standalone with smooth lerped transforms and Cinemachine fallback compatibility.
    /// </summary>
    [DisallowMultipleComponent]
    public class CameraController : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private Transform targetUav;

        [Header("Active Rig")]
        [SerializeField] private CameraRigMode currentRig = CameraRigMode.ChaseFollow;

        [Header("Chase Settings")]
        [SerializeField] private Vector3 chaseOffset = new Vector3(0f, 2.5f, -6.0f);
        [SerializeField] private float followDamping = 8.0f;
        [SerializeField] private float rotationDamping = 6.0f;

        [Header("FPV Settings")]
        [SerializeField] private Vector3 fpvOffset = new Vector3(0f, 0.12f, 0.25f);

        [Header("Orbit Settings")]
        [SerializeField] private float orbitDistance = 4.5f;
        [SerializeField] private float orbitSpeed = 25.0f;
        [SerializeField] private float orbitElevation = 2.0f;

        [Header("TopDown Settings")]
        [SerializeField] private float topDownAltitude = 120.0f;

        private float _orbitAngle;
        private Camera _cam;

        public CameraRigMode CurrentRig => currentRig;

        private void Awake()
        {
            _cam = GetComponent<Camera>();
            if (_cam == null) _cam = Camera.main;
        }

        private void Start()
        {
            if (targetUav == null)
            {
                var fc = FindFirstObjectByType<Astra.Flight.FlightControlSystem>();
                if (fc != null) targetUav = fc.transform;
            }
        }

        private void Update()
        {
            // Tab cycles camera rigs
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                CycleCamera();
            }
        }

        public void CycleCamera()
        {
            int next = ((int)currentRig + 1) % 6;
            SetRig((CameraRigMode)next);
        }

        public void SetRig(CameraRigMode mode)
        {
            currentRig = mode;
        }

        private void LateUpdate()
        {
            if (targetUav == null) return;

            switch (currentRig)
            {
                case CameraRigMode.ChaseFollow:
                    UpdateChaseCamera();
                    break;
                case CameraRigMode.FpvCockpit:
                    UpdateFpvCamera();
                    break;
                case CameraRigMode.Orbit:
                    UpdateOrbitCamera();
                    break;
                case CameraRigMode.TopDownMap:
                    UpdateTopDownCamera();
                    break;
                case CameraRigMode.Engineering:
                    UpdateEngineeringCamera();
                    break;
                case CameraRigMode.Cinematic:
                    UpdateCinematicCamera();
                    break;
            }
        }

        private void UpdateChaseCamera()
        {
            Vector3 desiredPos = targetUav.position + targetUav.TransformDirection(chaseOffset);
            transform.position = Vector3.Lerp(transform.position, desiredPos, Time.deltaTime * followDamping);

            Quaternion targetRot = Quaternion.LookRotation(targetUav.position + Vector3.up * 0.5f - transform.position);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * rotationDamping);
        }

        private void UpdateFpvCamera()
        {
            transform.position = targetUav.TransformPoint(fpvOffset);
            transform.rotation = targetUav.rotation;
        }

        private void UpdateOrbitCamera()
        {
            _orbitAngle += orbitSpeed * Time.deltaTime;
            float rad = _orbitAngle * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(Mathf.Sin(rad) * orbitDistance, orbitElevation, Mathf.Cos(rad) * orbitDistance);

            transform.position = targetUav.position + offset;
            transform.LookAt(targetUav.position + Vector3.up * 0.3f);
        }

        private void UpdateTopDownCamera()
        {
            Vector3 desiredPos = new Vector3(targetUav.position.x, topDownAltitude, targetUav.position.z);
            transform.position = Vector3.Lerp(transform.position, desiredPos, Time.deltaTime * followDamping);
            transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        }

        private void UpdateEngineeringCamera()
        {
            Vector3 desiredPos = targetUav.position + new Vector3(1.8f, 1.2f, -2.2f);
            transform.position = Vector3.Lerp(transform.position, desiredPos, Time.deltaTime * 6f);
            transform.LookAt(targetUav.position + Vector3.up * 0.2f);
        }

        private void UpdateCinematicCamera()
        {
            _orbitAngle += 8.0f * Time.deltaTime;
            float rad = _orbitAngle * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(Mathf.Sin(rad) * 12.0f, 4.5f + Mathf.Sin(Time.time * 0.5f) * 2f, Mathf.Cos(rad) * 12.0f);

            transform.position = Vector3.Lerp(transform.position, targetUav.position + offset, Time.deltaTime * 3f);
            transform.LookAt(targetUav.position + Vector3.up * 0.5f);
        }
    }
}
