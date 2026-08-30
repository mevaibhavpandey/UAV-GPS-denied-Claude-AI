using UnityEngine;
using Astra.Contracts;
using Astra.Core.Logging;

namespace Astra.Environment
{
    /// <summary>
    /// Dynamic obstacle simulator.
    /// Simulates moving aircraft, dynamic threats, or ground vehicles that cross
    /// the flight corridor to trigger live collision prediction, CPA/TTC analysis,
    /// and dynamic real-time Margasoochi replanning.
    /// </summary>
    [DisallowMultipleComponent]
    public class DynamicObstaclePatrol : MonoBehaviour
    {
        [Header("Patrol Motion")]
        [SerializeField] private Vector3 waypointA = new Vector3(-80f, 35f, 160f);
        [SerializeField] private Vector3 waypointB = new Vector3( 80f, 35f, 160f);
        [SerializeField] private float speedMps = 6.0f;
        [SerializeField] private bool active = true;

        [Header("Visual Warning")]
        [SerializeField] private Color beaconColor = new Color(1.0f, 0.2f, 0.15f);

        private Vector3 _targetPoint;
        private Rigidbody _rb;
        private Light _warningLight;

        public Vector3 Velocity { get; private set; }

        private void Start()
        {
            _rb = GetComponent<Rigidbody>();
            if (_rb == null)
            {
                _rb = gameObject.AddComponent<Rigidbody>();
                _rb.isKinematic = true;
                _rb.useGravity = false;
            }

            transform.position = waypointA;
            _targetPoint = waypointB;

            // Add warning strobe light
            GameObject lightGo = new GameObject("Obstacle_Strobe");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.localPosition = Vector3.up * 0.5f;
            _warningLight = lightGo.AddComponent<Light>();
            _warningLight.type = LightType.Point;
            _warningLight.color = beaconColor;
            _warningLight.range = 12f;
            _warningLight.intensity = 2.5f;

            gameObject.tag = "Obstacle";
        }

        private void Update()
        {
            if (!active) return;

            // Strobe beacon flash
            if (_warningLight != null)
            {
                _warningLight.intensity = (Mathf.Sin(Time.time * 8f) > 0f) ? 3.0f : 0.2f;
            }

            Vector3 toTarget = _targetPoint - transform.position;
            float dist = toTarget.magnitude;

            if (dist < 1.0f)
            {
                _targetPoint = (_targetPoint == waypointA) ? waypointB : waypointA;
                toTarget = _targetPoint - transform.position;
            }

            Vector3 moveDir = toTarget.normalized;
            Velocity = moveDir * speedMps;

            transform.position += Velocity * Time.deltaTime;
            if (moveDir.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(moveDir), Time.deltaTime * 5f);
            }
        }

        public void SetPatrol(Vector3 start, Vector3 end, float speed)
        {
            waypointA = start;
            waypointB = end;
            speedMps = speed;
            transform.position = start;
            _targetPoint = end;
        }
    }
}
