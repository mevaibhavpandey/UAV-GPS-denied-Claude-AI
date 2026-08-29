using System;
using System.Collections.Generic;
using UnityEngine;
using Astra.Contracts;
using Astra.Core.Logging;

namespace Astra.Core
{
    /// <summary>
    /// The service registry: a single place where subsystems are registered and looked up by
    /// interface.
    ///
    /// WHY NOT JUST WIRE REFERENCES IN THE INSPECTOR
    /// ---------------------------------------------
    /// Inspector wiring is fine for a fixed set of objects, and ASTRA uses it where the relationship
    /// is genuinely fixed. It breaks down for the specific thing this project must do: swap an
    /// implementation at runtime. When the operator presses F9 to fall back from Cesium to the
    /// offline map, or when GPS drops out and localisation switches from a GPS-aided estimator to
    /// dead reckoning, a serialised reference cannot follow that change. Every consumer would need
    /// to be found and re-pointed.
    ///
    /// So consumers ask the registry for the interface, and the registry decides which concrete
    /// object answers. Swapping an implementation becomes one call rather than a scene-wide rewire,
    /// and the hardware abstraction seam the specification asks for becomes real rather than
    /// aspirational: the day a MAVLink flight controller arrives, it registers itself as
    /// IFlightController and nothing else in the project changes.
    ///
    /// DELIBERATE LIMITATIONS
    /// ----------------------
    /// This is not a dependency injection container and should not grow into one. No lifetime
    /// management, no automatic construction, no scopes. It is a dictionary from Type to object with
    /// logging and a reset hook. That is all this project needs, and a heavier container would add
    /// indirection that makes the flight loop harder to reason about.
    ///
    /// THREADING: main thread only. Unity's object model is not thread-safe and neither is this.
    /// </summary>
    public static class AstraServices
    {
        private static readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();

        /// <summary>
        /// Raised whenever a service is registered or replaced. Consumers that cache a reference
        /// should subscribe and refresh, or simply call Get every time - the dictionary lookup is
        /// cheap enough that caching is rarely worth the staleness risk.
        /// </summary>
        public static event Action<Type> ServiceChanged;

        /// <summary>
        /// Registers an implementation for interface T, replacing any existing one.
        /// </summary>
        public static void Register<T>(T implementation) where T : class
        {
            if (implementation == null)
            {
                Debug.LogError("[AstraServices] Refusing to register null for " + typeof(T).Name +
                               ". Use Unregister if removal was intended.");
                return;
            }

            Type key = typeof(T);
            object existing;
            bool replacing = _services.TryGetValue(key, out existing);

            _services[key] = implementation;

            string implName = DescribeImplementation(implementation);

            if (replacing)
            {
                EventLog.Info(LogSource.System,
                    key.Name + " provider switched to " + implName);
            }
            else
            {
                EventLog.Info(LogSource.System,
                    key.Name + " registered: " + implName);
            }

            RaiseServiceChanged(key);
        }

        /// <summary>
        /// Registers only if nothing is registered yet. Useful for a default provider that should
        /// not stamp on a deliberately chosen one, regardless of Awake ordering.
        /// </summary>
        public static bool RegisterIfAbsent<T>(T implementation) where T : class
        {
            if (_services.ContainsKey(typeof(T)))
            {
                return false;
            }
            Register(implementation);
            return true;
        }

        /// <summary>
        /// Returns the registered implementation of T, or null if none.
        ///
        /// Returns null rather than throwing, and rather than silently constructing a default,
        /// because a missing service during bring-up is a normal transient condition. Callers that
        /// genuinely cannot proceed should use Require instead, which fails loudly.
        /// </summary>
        public static T Get<T>() where T : class
        {
            object found;
            if (_services.TryGetValue(typeof(T), out found))
            {
                return found as T;
            }
            return null;
        }

        /// <summary>
        /// Returns the registered implementation of T, logging an error if absent. Use where the
        /// caller cannot function without it, so the failure surfaces in the console and the event
        /// log instead of appearing later as a NullReferenceException with no explanation.
        /// </summary>
        public static T Require<T>() where T : class
        {
            T service = Get<T>();
            if (service == null)
            {
                string message = "Required service " + typeof(T).Name + " is not registered. " +
                                 "Check that the ASTRA bootstrap object exists in the scene and " +
                                 "that its initialisation order is correct.";
                Debug.LogError("[AstraServices] " + message);
                EventLog.Error(LogSource.System, message);
            }
            return service;
        }

        public static bool Has<T>() where T : class
        {
            return _services.ContainsKey(typeof(T));
        }

        public static void Unregister<T>() where T : class
        {
            Type key = typeof(T);
            if (_services.Remove(key))
            {
                EventLog.Info(LogSource.System, key.Name + " unregistered");
                RaiseServiceChanged(key);
            }
        }

        /// <summary>
        /// Removes a specific instance only if it is the one currently registered. This is what
        /// OnDestroy should call: a component being torn down must not unregister a replacement that
        /// has already taken over, which is a real hazard during a provider swap because Unity's
        /// destruction is deferred to the end of the frame.
        /// </summary>
        public static void UnregisterIfCurrent<T>(T implementation) where T : class
        {
            object current;
            if (_services.TryGetValue(typeof(T), out current) &&
                ReferenceEquals(current, implementation))
            {
                Unregister<T>();
            }
        }

        /// <summary>
        /// Clears the registry. Called on scene teardown and mission restart.
        /// </summary>
        public static void Clear()
        {
            _services.Clear();
        }

        /// <summary>
        /// A human-readable dump of what is currently registered, with provenance where the service
        /// declares it. Shown by the diagnostics panel, and the fastest way to answer an evaluator
        /// who asks "so what is actually simulated here?" - the answer is on screen rather than in a
        /// claim.
        /// </summary>
        public static string DescribeAll()
        {
            if (_services.Count == 0)
            {
                return "No services registered.";
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (KeyValuePair<Type, object> kv in _services)
            {
                sb.Append(kv.Key.Name);
                sb.Append("  ->  ");
                sb.AppendLine(DescribeImplementation(kv.Value));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Builds a description of an implementation, including its declared provenance if it has
        /// one. Provenance is read off the object rather than passed in, so a service cannot be
        /// registered under a more flattering label than it declares for itself.
        /// </summary>
        private static string DescribeImplementation(object implementation)
        {
            if (implementation == null)
            {
                return "<null>";
            }

            string name = implementation.GetType().Name;

            ISensorProvider sensor = implementation as ISensorProvider;
            if (sensor != null)
            {
                return name + " [" + DataProvenanceLabels.ToBadge(sensor.Provenance) + "]";
            }

            ILocalizationProvider localization = implementation as ILocalizationProvider;
            if (localization != null)
            {
                return name + " [" + DataProvenanceLabels.ToBadge(localization.Provenance) + "]";
            }

            IObstacleDetector detector = implementation as IObstacleDetector;
            if (detector != null)
            {
                return name + " [" + DataProvenanceLabels.ToBadge(detector.Provenance) + "]";
            }

            IMapDataProvider map = implementation as IMapDataProvider;
            if (map != null)
            {
                return name + " [" + DataProvenanceLabels.ToBadge(map.Provenance) + "]";
            }

            ITelemetryProvider telemetry = implementation as ITelemetryProvider;
            if (telemetry != null)
            {
                return name + " [" + DataProvenanceLabels.ToBadge(telemetry.Provenance) + "]";
            }

            return name;
        }

        private static void RaiseServiceChanged(Type key)
        {
            Action<Type> handler = ServiceChanged;
            if (handler == null)
            {
                return;
            }

            // A throwing subscriber must not prevent other subscribers from being notified, and must
            // not abort the registration that triggered this.
            Delegate[] targets = handler.GetInvocationList();
            for (int i = 0; i < targets.Length; i++)
            {
                try
                {
                    ((Action<Type>)targets[i])(key);
                }
                catch (Exception e)
                {
                    Debug.LogError("[AstraServices] ServiceChanged subscriber threw: " + e);
                }
            }
        }

        /// <summary>
        /// Unity does not clear static state between play sessions when domain reload is disabled,
        /// which is the default in Unity 6 for faster iteration. Without this, entering play mode a
        /// second time finds stale references to destroyed objects and produces errors that look
        /// like real bugs but only occur in the editor. Resetting at SubsystemRegistration - the
        /// earliest available point, before any scene loads - removes the whole class of problem.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            ServiceChanged = null;
            _services.Clear();
        }
    }
}
