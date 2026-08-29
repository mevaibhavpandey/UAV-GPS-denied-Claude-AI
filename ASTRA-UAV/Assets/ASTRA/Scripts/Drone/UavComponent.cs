using UnityEngine;
using Astra.Contracts;

namespace Astra.Drone
{
    /// <summary>
    /// Which subsystem a physical component belongs to. Drives grouping in the engineering view and
    /// colouring in the power-flow and data-flow overlays.
    /// </summary>
    public enum ComponentCategory
    {
        Structure = 0,
        Propulsion = 1,
        Power = 2,
        FlightControl = 3,
        Compute = 4,
        Navigation = 5,
        Communication = 6,
        Payload = 7,
        LandingGear = 8
    }

    /// <summary>
    /// How real a given component is.
    ///
    /// This enum exists to satisfy a hard project constraint: the demonstration must not present
    /// anything as further along than it is. Every part of the airframe carries one of these labels,
    /// the engineering view renders it next to the part name, and there is no code path that displays
    /// a component without displaying its status. Overstating maturity would require deleting a field,
    /// not merely forgetting to add one.
    /// </summary>
    public enum ImplementationStatus
    {
        /// <summary>Physically present on the real airframe and modelled here. The simulated behaviour
        /// stands in for the real part.</summary>
        Simulated = 0,

        /// <summary>Present on the airframe, but what the simulator does with it is a simplified
        /// stand-in rather than the real algorithm. Must be labelled DEMONSTRATION.</summary>
        Demonstration = 1,

        /// <summary>Not on the airframe and not purchased. A mounting provision and a software
        /// interface exist; nothing else does. Must be labelled FUTURE HARDWARE.</summary>
        FutureHardware = 2,

        /// <summary>Genuinely implemented and working in the real system, not simulated.</summary>
        RealImplementation = 3
    }

    /// <summary>
    /// Tags one physical part of the UAV digital twin.
    ///
    /// Every named component on the airframe carries this so that three things become possible without
    /// any hard-coded lists:
    ///
    ///   - the engineering view can enumerate, label, isolate and explode the airframe;
    ///   - the diagnostics panel can report per-component status;
    ///   - the honesty labelling is attached to the part itself rather than to a UI string that
    ///     somebody has to remember to keep truthful.
    ///
    /// SPECIFICATION HONESTY
    /// ---------------------
    /// The mass and the notes on each component are ENGINEERING ESTIMATES entered when the airframe was
    /// modelled. None of them has been verified against a manufacturer datasheet or a scale. They are
    /// labelled as estimates in the inspector and in the UI, and the verification worklist in
    /// Docs/10-UAV-Hardware-Layout.md lists what needs measuring. A number that looks precise is not
    /// the same as a number that is correct, and this field being populated is not evidence that it is.
    /// </summary>
    [DisallowMultipleComponent]
    public class UavComponent : MonoBehaviour
    {
        [Header("Identity")]
        [Tooltip("Name shown in the engineering view and the component list.")]
        [SerializeField] private string displayName = "Unnamed component";

        [Tooltip("Manufacturer part designation, where one is known. Leave blank rather than guessing.")]
        [SerializeField] private string partDesignation = "";

        [SerializeField] private ComponentCategory category = ComponentCategory.Structure;

        [Header("Honesty labelling")]
        [Tooltip("How real this component is. Rendered as a badge next to the name; there is no way " +
                 "to display the component without it.")]
        [SerializeField] private ImplementationStatus status = ImplementationStatus.Simulated;

        [Header("Specifications - ALL ESTIMATES")]
        [Tooltip("[ESTIMATE] Mass in grams. Not weighed, not taken from a datasheet. Present so the " +
                 "mass budget can be discussed, not so it can be relied upon.")]
        [SerializeField] private float massGrams;

        [Tooltip("[ESTIMATE] Continuous current draw in amperes, where the component draws power. " +
                 "Zero for passive structure.")]
        [SerializeField] private float currentDrawA;

        [Tooltip("What this component does and why it is on the aircraft. Shown in the engineering " +
                 "view when the component is selected. Write for an examiner who knows engineering " +
                 "but not this specific airframe.")]
        [TextArea(2, 6)]
        [SerializeField] private string description = "";

        [Tooltip("Anything about this component that is unverified, uncertain or a known concern. " +
                 "Surfaced in the engineering view. Leaving a real concern out of this field would " +
                 "defeat the purpose of having it.")]
        [TextArea(2, 5)]
        [SerializeField] private string engineeringNotes = "";

        [Header("Exploded view")]
        [Tooltip("Direction this part moves when the airframe is exploded, in local airframe space. " +
                 "Zero means the builder derives it from the part's offset from the airframe centre, " +
                 "which is right for most parts and wrong for anything mounted on the centre line.")]
        [SerializeField] private Vector3 explodeDirection = Vector3.zero;

        [Tooltip("How far this part travels at full explode, metres.")]
        [SerializeField] private float explodeDistanceM = 0.25f;

        // ---- Captured at Awake so the exploded view has somewhere to return to ----
        private Vector3 _restLocalPosition;
        private bool _restCaptured;
        private Renderer[] _renderers;

        // ====================================================================================
        // ACCESSORS
        // ====================================================================================

        public string DisplayName { get { return displayName; } }
        public string PartDesignation { get { return partDesignation; } }
        public ComponentCategory Category { get { return category; } }
        public ImplementationStatus Status { get { return status; } }
        public float MassGrams { get { return massGrams; } }
        public float CurrentDrawA { get { return currentDrawA; } }
        public string Description { get { return description; } }
        public string EngineeringNotes { get { return engineeringNotes; } }
        public bool HasEngineeringNotes { get { return !string.IsNullOrEmpty(engineeringNotes); } }
        public Vector3 RestLocalPosition { get { return _restLocalPosition; } }

        /// <summary>
        /// The badge text shown next to this component's name. Deliberately not optional.
        /// </summary>
        public string StatusBadge
        {
            get
            {
                switch (status)
                {
                    case ImplementationStatus.Simulated: return "SIMULATED";
                    case ImplementationStatus.Demonstration: return "DEMONSTRATION";
                    case ImplementationStatus.FutureHardware: return "FUTURE HARDWARE";
                    case ImplementationStatus.RealImplementation: return "IMPLEMENTED";
                    default: return "UNKNOWN";
                }
            }
        }

        /// <summary>
        /// Badge colour. Future hardware gets a distinctly cool grey-blue and demonstration an amber,
        /// so that a viewer scanning the airframe can see at a glance which parts do not exist yet
        /// without reading a single label.
        /// </summary>
        public Color StatusColour
        {
            get
            {
                switch (status)
                {
                    case ImplementationStatus.Simulated: return new Color(0.98f, 0.71f, 0.20f);
                    case ImplementationStatus.Demonstration: return new Color(0.98f, 0.55f, 0.20f);
                    case ImplementationStatus.FutureHardware: return new Color(0.45f, 0.55f, 0.70f);
                    case ImplementationStatus.RealImplementation: return new Color(0.30f, 0.85f, 0.45f);
                    default: return Color.grey;
                }
            }
        }

        /// <summary>
        /// Maps the component's own status onto the provenance enum the rest of the UI uses, so a
        /// component and a data provider are badged consistently. Without this the same concept would
        /// be expressed two ways and would eventually disagree.
        /// </summary>
        public DataProvenance ToProvenance()
        {
            switch (status)
            {
                case ImplementationStatus.Simulated: return DataProvenance.Simulated;
                case ImplementationStatus.Demonstration: return DataProvenance.Demonstration;
                case ImplementationStatus.FutureHardware: return DataProvenance.FutureHardware;
                case ImplementationStatus.RealImplementation: return DataProvenance.Hardware;
                default: return DataProvenance.Simulated;
            }
        }

        // ====================================================================================
        // LIFECYCLE
        // ====================================================================================

        private void Awake()
        {
            CaptureRestPose();
            _renderers = GetComponentsInChildren<Renderer>(true);
        }

        public void CaptureRestPose()
        {
            if (_restCaptured)
            {
                return;
            }
            _restLocalPosition = transform.localPosition;
            _restCaptured = true;
        }

        // ====================================================================================
        // ENGINEERING VIEW SUPPORT
        // ====================================================================================

        /// <summary>
        /// Direction this part travels when exploded, in the parent's local space, normalised.
        /// Derived from the offset from the airframe centre when not explicitly set, which spreads the
        /// airframe outward in the way a hand-drawn exploded diagram does.
        /// </summary>
        public Vector3 ResolveExplodeDirection()
        {
            if (explodeDirection.sqrMagnitude > 0.0001f)
            {
                return explodeDirection.normalized;
            }

            CaptureRestPose();

            Vector3 offset = _restLocalPosition;
            if (offset.sqrMagnitude < 0.0004f)
            {
                // Mounted on or very near the centre line, so there is no outward direction to infer.
                // Send it straight up, which separates it from the frame without colliding with the
                // parts that do have a lateral direction.
                return Vector3.up;
            }
            return offset.normalized;
        }

        /// <summary>
        /// Positions the part for an exploded view. A fraction of 0 is the assembled position and 1 is
        /// fully exploded, so the caller can animate it.
        /// </summary>
        public void ApplyExplode(float fraction)
        {
            CaptureRestPose();
            transform.localPosition = _restLocalPosition +
                ResolveExplodeDirection() * explodeDistanceM * Mathf.Clamp01(fraction);
        }

        /// <summary>
        /// Shows or hides the part's geometry without disabling the GameObject.
        ///
        /// The distinction matters: disabling the GameObject would also stop the part's behaviour, so
        /// isolating a motor in the component view would silently stop that motor producing thrust and
        /// the aircraft would tip over while the operator was looking at it. Toggling renderers keeps
        /// the simulation running underneath the visualisation, which is the whole point of a digital
        /// twin as opposed to a diagram.
        /// </summary>
        public void SetVisible(bool visible)
        {
            if (_renderers == null)
            {
                _renderers = GetComponentsInChildren<Renderer>(true);
            }
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] != null)
                {
                    _renderers[i].enabled = visible;
                }
            }
        }

        /// <summary>
        /// Fills in the fields. Used by the airframe builder so the component metadata lives in one
        /// place in the builder rather than being typed into the inspector part by part, where it
        /// would drift out of date.
        /// </summary>
        public void Configure(string name, string designation, ComponentCategory componentCategory,
                              ImplementationStatus implementationStatus, float mass,
                              float current, string desc, string notes)
        {
            displayName = name;
            partDesignation = designation;
            category = componentCategory;
            status = implementationStatus;
            massGrams = mass;
            currentDrawA = current;
            description = desc;
            engineeringNotes = notes;
        }

        public void SetExplode(Vector3 direction, float distanceM)
        {
            explodeDirection = direction;
            explodeDistanceM = distanceM;
        }
    }
}
