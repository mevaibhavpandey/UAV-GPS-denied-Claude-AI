using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Astra.Core.Logging;

namespace Astra.Core.Geo
{
    /// <summary>
    /// Floating-origin / world-rebasing manager (ASTRA spec: large-scale map, precision).
    ///
    /// THE PROBLEM. Unity world space is single-precision. At a few hundred metres a float still
    /// resolves millimetres, but by 8-10 km it resolves only centimetres, and by tens of kilometres
    /// the residual quantisation is large enough to make a rigidbody visibly jitter and the physics
    /// integrator misbehave. The spec asks for missions spanning 1-25 km, which is squarely in the
    /// range where this bites. Anchoring the geodetic origin near the site (GeoReference) fixes the
    /// STARTING precision, but an aircraft that then flies 20 km from that origin has the same problem
    /// again.
    ///
    /// THE FIX. Keep the aircraft near the Unity origin by periodically translating the ENTIRE world -
    /// every root object, the aircraft included - back toward (0,0,0), and moving the geodetic origin
    /// by the equal and opposite amount so that latitude/longitude are completely unchanged. Because
    /// every object moves by the same vector, all relative geometry is preserved: nothing appears to
    /// move, the camera framing is identical, and - crucially - every control error the flight
    /// controller computes is unchanged, so the rebase cannot perturb the flight (the cached setpoints
    /// are shifted by the same delta via AstraEvents.WorldRebased). This is the standard large-world
    /// technique; Unity's own documentation recommends it and Cesium implements the same idea.
    ///
    /// SAFETY / SCOPE. The default threshold is deliberately large (2 km) so this NEVER triggers in
    /// the current campus demo, which operates within a few hundred metres - the working, tuned demo
    /// is therefore completely unaffected. It only begins to act on the large-extent missions the spec
    /// asks about. It holds no control authority and never sets the aircraft's position relative to
    /// the world; it only translates the shared frame. Self-installing; does not touch the GCS.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FloatingOriginManager : MonoBehaviour
    {
        [Tooltip("Horizontal distance of the focus object from the Unity origin, in metres, that " +
                 "triggers a rebase. Large by default so the short-range campus demo never rebases; " +
                 "lower it for genuinely large missions.")]
        [SerializeField] private float rebaseThresholdM = 2000f;

        [Tooltip("If true, the manager finds and follows the flight controller's aircraft as the " +
                 "focus. If false, assign a focus transform explicitly.")]
        [SerializeField] private bool autoFindFocus = true;

        [SerializeField] private Transform focus;

        [Tooltip("Snap the rebase shift to whole metres. Keeps the geodetic origin's fractional part " +
                 "stable and avoids accumulating tiny offsets over many rebases.")]
        [SerializeField] private bool snapToMetre = true;

        private float _totalShiftMagnitude;
        private int _rebaseCount;

        /// <summary>Total distance the world has been shifted over the run, metres. Diagnostic.</summary>
        public float TotalShiftMagnitude { get { return _totalShiftMagnitude; } }

        /// <summary>Number of rebases performed this run. Diagnostic.</summary>
        public int RebaseCount { get { return _rebaseCount; } }

        // ====================================================================================
        // SELF-INSTALL
        // ====================================================================================
        // Consistent with the rest of ASTRA's diagnostic/infrastructure components: a single
        // instance installs itself after the scene loads, so no prefab wiring is required and the
        // working demo scene is not modified on disk.

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
#if UNITY_2023_1_OR_NEWER
            if (FindAnyObjectByType<FloatingOriginManager>() != null) return;
#else
            if (FindObjectOfType<FloatingOriginManager>() != null) return;
#endif
            new GameObject("ASTRA_FloatingOrigin").AddComponent<FloatingOriginManager>();
        }

        // ====================================================================================
        // FOCUS RESOLUTION
        // ====================================================================================

        private void ResolveFocus()
        {
            if (focus != null || !autoFindFocus) return;

            // The aircraft is whatever carries the flight controller. Resolve via the service
            // registry first (cheap, authoritative), fall back to a scene search.
            var fc = Astra.Core.AstraServices.Get<Astra.Contracts.IFlightController>()
                     as MonoBehaviour;
#if UNITY_2023_1_OR_NEWER
            if (fc == null) fc = FindAnyObjectByType<Astra.Flight.FlightControlSystem>();
#else
            if (fc == null) fc = FindObjectOfType<Astra.Flight.FlightControlSystem>();
#endif
            if (fc != null) focus = fc.transform;
        }

        // ====================================================================================
        // REBASE CHECK
        // ====================================================================================
        // Runs in LateUpdate so it sees the aircraft's final position for the frame, after both
        // physics and any camera work. The check itself is a cheap squared-distance compare; the
        // expensive part (translating the world) happens only on the rare frames a rebase fires.

        private void LateUpdate()
        {
            ResolveFocus();
            if (focus == null) return;

            Vector3 p = focus.position;
            float horizontalSqr = p.x * p.x + p.z * p.z;
            if (horizontalSqr < rebaseThresholdM * rebaseThresholdM) return;

            // Shift the world so the focus returns to the vertical axis through the origin. Only
            // the horizontal plane is rebased: altitude is already origin-relative and small, and
            // moving it would complicate the AGL/altitude bookkeeping for no precision benefit.
            Vector3 shift = new Vector3(-p.x, 0f, -p.z);
            if (snapToMetre)
            {
                shift.x = Mathf.Round(shift.x);
                shift.z = Mathf.Round(shift.z);
            }
            if (shift.sqrMagnitude < 1f) return; // nothing meaningful to do

            PerformRebase(shift);
        }

        // ====================================================================================
        // THE REBASE
        // ====================================================================================

        private void PerformRebase(Vector3 shift)
        {
            // 1. Move the geodetic origin by the equal-and-opposite amount FIRST, so geographic
            //    coordinates are continuous across the translation. The point that will sit at
            //    Unity (0,0,0) after the shift is the one currently at -shift; its geodetic
            //    coordinate becomes the new origin.
            GeoReference geo = GeoReference.Instance;
            if (geo != null)
            {
                GeoCoordinate newOrigin = GeoMath.UnityToGeodetic(-shift, geo.Origin);
                geo.RebaseOrigin(newOrigin);
            }

            // 2. Translate every root transform in every loaded scene by the shift. Iterating
            //    roots (not every transform) is correct and cheap: children move with their
            //    parents. Skipping this manager's own GameObject is unnecessary - it has no
            //    spatial meaning - but harmless either way.
            int sceneCount = SceneManager.sceneCount;
            for (int si = 0; si < sceneCount; si++)
            {
                Scene scene = SceneManager.GetSceneAt(si);
                if (!scene.isLoaded) continue;
                scene.GetRootGameObjects(_rootBuffer);
                for (int i = 0; i < _rootBuffer.Count; i++)
                {
                    Transform t = _rootBuffer[i].transform;
                    t.position += shift;
                }
            }

            // 3. Announce the shift so subsystems that CACHE Unity-space positions (the flight
            //    controller's setpoints, the planner's path nodes, obstacle tracks) add the same
            //    delta and keep pointing at the same physical points. Because setpoint and
            //    measurement move together, the flight controller's error terms are unchanged and
            //    the rebase is invisible to the control law - it cannot perturb the flight.
            _rebaseCount++;
            _totalShiftMagnitude += shift.magnitude;
            Astra.Core.AstraEvents.RaiseWorldRebased(shift);

            EventLog.Info(LogSource.System, string.Format(
                "Floating origin: world rebased by ({0:F0}, {1:F0}) m to preserve float precision " +
                "(rebase #{2}). Geographic coordinates unchanged.", shift.x, shift.z, _rebaseCount));
        }

        private readonly List<GameObject> _rootBuffer = new List<GameObject>(64);
    }
}

