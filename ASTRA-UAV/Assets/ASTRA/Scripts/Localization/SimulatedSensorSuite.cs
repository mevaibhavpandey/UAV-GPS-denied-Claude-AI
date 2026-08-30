using System;
using UnityEngine;
using Astra.Contracts;
using Astra.Core;
using Astra.Core.Config;
using Astra.Core.Logging;
using Astra.Flight;

namespace Astra.Localization
{
    /// <summary>
    /// Simulated sensor suite that synthesizes raw IMU, GNSS, Barometer, and Magnetometer samples
    /// from physics state, adding realistic noise and bias characteristics.
    /// Implements ISensorProvider.
    /// </summary>
    [DisallowMultipleComponent]
    public class SimulatedSensorSuite : MonoBehaviour, ISensorProvider
    {
        [Header("References")]
        [SerializeField] private QuadcopterPhysics physics;
        [SerializeField] private Rigidbody droneRigidbody;
        [SerializeField] private UavConfiguration config;

        [Header("GNSS Settings")]
        [SerializeField] private bool gpsAvailable = true;
        [SerializeField] private int nominalSatellites = 14;
        [SerializeField] private float nominalHdop = 0.9f;
        [SerializeField] private float nominalVdop = 1.2f;
        [SerializeField] private float gpsNoiseStdDevM = 0.5f;

        [Header("IMU Noise & Bias")]
        [SerializeField] private float accelNoiseStdDev = 0.05f; // m/s^2
        [SerializeField] private float gyroNoiseStdDev = 0.005f;  // rad/s
        [SerializeField] private Vector3 gyroBias = new Vector3(0.001f, -0.002f, 0.001f);

        [Header("Barometer Settings")]
        [SerializeField] private float seaLevelPressurePa = 101325f;
        [SerializeField] private float baroNoiseStdDevM = 0.15f;

        [Header("Status")]
        [SerializeField] private SubsystemStatus status = SubsystemStatus.Initialising;

        private ImuSample _lastImu;
        private GpsFix _lastGps;
        private BarometerSample _lastBaro;
        private MagnetometerSample _lastMag;
        private double _simTime;
        private Vector3 _prevVelocity;

        public string Name => "Simulated Sensor Suite (Pixhawk 2.4.8)";
        public DataProvenance Provenance => DataProvenance.Simulated;
        public SubsystemStatus Status => status;
        public bool GpsAvailable => gpsAvailable;

        private void Awake()
        {
            if (physics == null) physics = GetComponent<QuadcopterPhysics>();
            if (droneRigidbody == null) droneRigidbody = GetComponent<Rigidbody>();
        }

        private void Start()
        {
            Initialise();
            AstraServices.Register<ISensorProvider>(this);
        }

        private void OnDestroy()
        {
            AstraServices.UnregisterIfCurrent<ISensorProvider>(this);
        }

        public bool Initialise()
        {
            _simTime = 0;
            _prevVelocity = Vector3.zero;
            status = SubsystemStatus.Ok;
            AstraEvents.RaiseGpsAvailabilityChanged(gpsAvailable);
            return true;
        }

        public void Tick(float fixedDeltaTime)
        {
            _simTime += fixedDeltaTime;

            Vector3 pos = droneRigidbody != null ? droneRigidbody.position : transform.position;
            Vector3 vel = droneRigidbody != null ? droneRigidbody.linearVelocity : Vector3.zero;
            Quaternion rot = droneRigidbody != null ? droneRigidbody.rotation : transform.rotation;
            Vector3 angVel = droneRigidbody != null ? droneRigidbody.angularVelocity : Vector3.zero;

            // 1. Synthesize IMU: Specific force = body_accel - body_gravity
            Vector3 gravityWorld = Physics.gravity;
            Vector3 accelWorld = fixedDeltaTime > 0.0001f ? (vel - _prevVelocity) / fixedDeltaTime : Vector3.zero;
            _prevVelocity = vel;

            Vector3 specificForceWorld = accelWorld - gravityWorld;
            Vector3 specificForceBody = Quaternion.Inverse(rot) * specificForceWorld;

            // Add Gaussian noise
            Vector3 accelNoise = new Vector3(GaussianNoise(0, accelNoiseStdDev), GaussianNoise(0, accelNoiseStdDev), GaussianNoise(0, accelNoiseStdDev));
            Vector3 gyroNoise = new Vector3(GaussianNoise(0, gyroNoiseStdDev), GaussianNoise(0, gyroNoiseStdDev), GaussianNoise(0, gyroNoiseStdDev));

            _lastImu = new ImuSample
            {
                Acceleration = specificForceBody + accelNoise,
                AngularVelocity = Quaternion.Inverse(rot) * angVel + gyroBias + gyroNoise,
                TemperatureC = 38.5f + Mathf.Sin((float)_simTime * 0.05f) * 1.5f,
                Timestamp = _simTime,
                IsValid = true
            };

            // 2. Synthesize GNSS
            if (gpsAvailable)
            {
                Vector3 noise = new Vector3(GaussianNoise(0, gpsNoiseStdDevM), 0, GaussianNoise(0, gpsNoiseStdDevM));
                Vector3 noisyPos = pos + noise;

                _lastGps = new GpsFix
                {
                    Latitude = 13.0827 + (noisyPos.z / 111319.5),
                    Longitude = 77.5877 + (noisyPos.x / (111319.5 * Mathf.Cos(13.0827f * Mathf.Deg2Rad))),
                    Altitude = noisyPos.y + 920.0, // MSL altitude Bangalore ~920m
                    SatelliteCount = nominalSatellites + UnityEngine.Random.Range(-1, 2),
                    Hdop = nominalHdop + Mathf.Abs(GaussianNoise(0, 0.1f)),
                    Vdop = nominalVdop + Mathf.Abs(GaussianNoise(0, 0.15f)),
                    VelocityEnu = vel,
                    FixType = 3, // 3D Fix
                    Timestamp = _simTime
                };
            }
            else
            {
                _lastGps = new GpsFix
                {
                    FixType = 0, // No fix
                    SatelliteCount = 0,
                    Hdop = 99.9f,
                    Vdop = 99.9f,
                    Timestamp = _simTime
                };
            }

            // 3. Synthesize Barometer
            float baroNoise = GaussianNoise(0, baroNoiseStdDevM);
            float currentAlt = pos.y + baroNoise;
            float pressure = seaLevelPressurePa * Mathf.Pow(1f - (0.0065f * currentAlt / 288.15f), 5.255f);

            _lastBaro = new BarometerSample
            {
                PressurePa = pressure,
                AltitudeM = currentAlt,
                TemperatureC = 25f - (0.0065f * currentAlt),
                Timestamp = _simTime,
                IsValid = true
            };

            // 4. Synthesize Magnetometer (North = Z axis)
            Vector3 northWorld = Vector3.forward;
            Vector3 magBody = Quaternion.Inverse(rot) * (northWorld * 45f + Vector3.down * 15f);
            float heading = rot.eulerAngles.y;

            _lastMag = new MagnetometerSample
            {
                FieldUt = magBody,
                HeadingDegrees = heading,
                Timestamp = _simTime,
                IsValid = true
            };
        }

        public ImuSample ReadImu() => _lastImu;
        public GpsFix ReadGps() => _lastGps;
        public BarometerSample ReadBarometer() => _lastBaro;
        public MagnetometerSample ReadMagnetometer() => _lastMag;

        public void SetGpsEnabled(bool enabled)
        {
            if (gpsAvailable != enabled)
            {
                gpsAvailable = enabled;
                AstraEvents.RaiseGpsAvailabilityChanged(enabled);
                EventLog.Warning(LogSource.Navigation, enabled ? "GNSS signal acquired. 3D Lock." : "GNSS signal lost! Entering GPS-Denied operation.");
            }
        }

        private float GaussianNoise(float mean, float stdDev)
        {
            float u1 = 1.0f - UnityEngine.Random.value;
            float u2 = 1.0f - UnityEngine.Random.value;
            float randStdNormal = Mathf.Sqrt(-2.0f * Mathf.Log(u1)) * Mathf.Sin(2.0f * Mathf.PI * u2);
            return mean + stdDev * randStdNormal;
        }
    }
}
