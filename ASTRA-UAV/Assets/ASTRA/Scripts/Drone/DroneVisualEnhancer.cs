using System.Collections;
using UnityEngine;
using Astra.Flight;

namespace Astra.Drone
{
    /// <summary>
    /// Runtime visual enhancer for the ASTRA UAV digital twin.
    /// Forces tactical matte-black materials on ALL renderers at Start(),
    /// adds blinking navigation lights, and propwash VFX per motor.
    /// This resolves the pink/magenta missing-material issue at runtime.
    /// </summary>
    [DisallowMultipleComponent]
    public class DroneVisualEnhancer : MonoBehaviour
    {
        [Header("Body Colour")]
        [SerializeField] private Color bodyColour        = new Color(0.030f, 0.030f, 0.032f);
        [SerializeField] private Color armColour         = new Color(0.040f, 0.040f, 0.042f);
        [SerializeField] private Color motorColour       = new Color(0.060f, 0.060f, 0.065f);
        [SerializeField] private Color propColour        = new Color(0.025f, 0.025f, 0.027f);
        [SerializeField] private Color gearColour        = new Color(0.055f, 0.055f, 0.060f);
        [SerializeField] private float bodySmoothness    = 0.18f;
        [SerializeField] private float motorSmoothness   = 0.68f;
        [SerializeField] private float motorMetallic     = 0.82f;

        [Header("Navigation Lights")]
        [SerializeField] private bool  enableNavLights   = true;
        [SerializeField] private float navLightRange     = 4.0f;
        [SerializeField] private float navLightIntensity = 1.8f;
        [SerializeField] private float antiCollisionHz   = 1.2f;

        [Header("Propwash VFX")]
        [SerializeField] private bool  enablePropwash    = true;
        [SerializeField] private float maxPropwashRate   = 180f;

        private FlightControlSystem _fc;
        private MotorUnit[]         _motors;
        private Light               _navRed, _navGreen, _navWhite, _armedBeacon;
        private ParticleSystem[]    _propwash;
        private Material            _matBody, _matArm, _matMotor, _matProp, _matGear;

        private void Awake()
        {
            _fc     = GetComponent<FlightControlSystem>();
            _motors = GetComponentsInChildren<MotorUnit>(true);
        }

        private void Start()
        {
            CreateRuntimeMaterials();
            ApplyMaterialsToAllRenderers();
            if (enableNavLights) BuildNavLights();
            if (enablePropwash)  BuildPropwash();
            StartCoroutine(AntiCollisionBlink());
        }

        private void Update()
        {
            UpdateNavLights();
            UpdatePropwash();
            UpdateMotorGlow();
        }

        // ---- Materials ----

        private void CreateRuntimeMaterials()
        {
            Shader lit = (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null)
                ? (Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"))
                : (Shader.Find("Standard") ?? Shader.Find("Diffuse") ?? Shader.Find("Unlit/Color"));
            if (lit == null) lit = Shader.Find("Standard") ?? Shader.Find("Diffuse");

            _matBody  = MakeMat(lit, bodyColour,   bodySmoothness, 0.05f);
            _matArm   = MakeMat(lit, armColour,    bodySmoothness, 0.05f);
            _matMotor = MakeMat(lit, motorColour,  motorSmoothness, motorMetallic);
            _matProp  = MakeMat(lit, propColour,   0.30f, 0.05f);
            _matGear  = MakeMat(lit, gearColour,   bodySmoothness, 0.10f);
        }

        private static Material MakeMat(Shader s, Color c, float smooth, float metallic)
        {
            var m = new Material(s) { enableInstancing = true };
            if (m.HasProperty("_BaseColor"))  m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color"))      m.SetColor("_Color",     c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
            if (m.HasProperty("_Metallic"))   m.SetFloat("_Metallic",   metallic);
            return m;
        }

        private void ApplyMaterialsToAllRenderers()
        {
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            {
                string n = r.gameObject.name.ToLowerInvariant();
                Material chosen;
                if      (n.Contains("blade") || n.Contains("hub") || n.Contains("blur") || n.Contains("prop"))
                    chosen = _matProp;
                else if (n.Contains("motor") || n.Contains("esc"))
                    chosen = _matMotor;
                else if (n.Contains("leg") || n.Contains("gear"))
                    chosen = _matGear;
                else if (n.Contains("arm_"))
                    chosen = _matArm;
                else
                    chosen = _matBody;

                var slots = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < slots.Length; i++) slots[i] = chosen;
                r.sharedMaterials = slots;
            }
        }

        // ---- Nav Lights ----

        private void BuildNavLights()
        {
            _navRed    = MakeLight("NavLight_Port",       Color.red,                  new Vector3(-0.30f, 0.05f, 0.30f));
            _navGreen  = MakeLight("NavLight_Starboard",  Color.green,                new Vector3( 0.30f, 0.05f, 0.30f));
            _navWhite  = MakeLight("NavLight_AntiCol",    Color.white,                new Vector3( 0.00f, 0.05f,-0.30f));
            _armedBeacon = MakeLight("ArmedBeacon",       new Color(0.0f, 0.7f, 1f),  new Vector3( 0.00f, 0.14f, 0.00f));
            _armedBeacon.range     = 2.5f;
            _armedBeacon.intensity = 0f;
        }

        private Light MakeLight(string goName, Color c, Vector3 localPos)
        {
            var go = new GameObject(goName);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;
            var l = go.AddComponent<Light>();
            l.type      = LightType.Point;
            l.color     = c;
            l.range     = navLightRange;
            l.intensity = navLightIntensity;
            l.shadows   = LightShadows.None;
            return l;
        }

        private void UpdateNavLights()
        {
            if (!enableNavLights || _navRed == null) return;
            bool armed = _fc != null && _fc.IsArmed;
            _navRed.intensity   = armed ? navLightIntensity : 0f;
            _navGreen.intensity = armed ? navLightIntensity : 0f;
            if (_armedBeacon != null)
                _armedBeacon.intensity = armed ? 0.8f + 0.4f * Mathf.Sin(Time.time * 6f) : 0f;
        }

        private IEnumerator AntiCollisionBlink()
        {
            float half = 0.5f / Mathf.Max(0.1f, antiCollisionHz);
            while (true)
            {
                if (_navWhite != null && _fc != null && _fc.IsArmed)
                    _navWhite.intensity = navLightIntensity * 2.5f;
                yield return new WaitForSeconds(half * 0.12f);
                if (_navWhite != null) _navWhite.intensity = 0f;
                yield return new WaitForSeconds(half * 0.88f);
            }
        }

        // ---- Propwash ----

        private void BuildPropwash()
        {
            _propwash = new ParticleSystem[_motors.Length];
            for (int i = 0; i < _motors.Length; i++)
            {
                if (_motors[i] == null) continue;
                var go = new GameObject("Propwash_" + (i + 1));
                go.transform.SetParent(_motors[i].transform, false);
                go.transform.localPosition = new Vector3(0f, -0.05f, 0f);
                go.transform.localRotation = Quaternion.Euler(180f, 0f, 0f);

                var ps   = go.AddComponent<ParticleSystem>();
                var main = ps.main;
                main.startSpeed    = new ParticleSystem.MinMaxCurve(1.5f, 4.5f);
                main.startSize     = new ParticleSystem.MinMaxCurve(0.04f, 0.20f);
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.28f);
                main.startColor    = new ParticleSystem.MinMaxGradient(
                    new Color(1f, 1f, 1f, 0.07f), new Color(0.9f, 0.95f, 1f, 0.00f));
                main.simulationSpace  = ParticleSystemSimulationSpace.World;
                main.maxParticles     = 250;
                main.gravityModifier  = 0.04f;

                var em = ps.emission;
                em.rateOverTime = 0f;

                var sh = ps.shape;
                sh.shapeType = ParticleSystemShapeType.Cone;
                sh.angle     = 20f;
                sh.radius    = 0.14f;

                var col = ps.colorOverLifetime;
                col.enabled = true;
                var grad = new Gradient();
                grad.SetKeys(
                    new[]{ new GradientColorKey(Color.white,0f), new GradientColorKey(Color.white,1f) },
                    new[]{ new GradientAlphaKey(0.07f, 0f),     new GradientAlphaKey(0.0f, 1f) });
                col.color = new ParticleSystem.MinMaxGradient(grad);

                _propwash[i] = ps;
            }
        }

        private void UpdatePropwash()
        {
            if (!enablePropwash || _propwash == null) return;
            for (int i = 0; i < _propwash.Length; i++)
            {
                if (_propwash[i] == null || i >= _motors.Length || _motors[i] == null) continue;
                float frac = Mathf.Clamp01(_motors[i].Rpm / 8000f);
                var em = _propwash[i].emission;
                em.rateOverTime = frac * maxPropwashRate;
            }
        }

        // ---- Motor Glow ----

        private void UpdateMotorGlow()
        {
            if (_matMotor == null || !_matMotor.HasProperty("_EmissionColor")) return;
            float total = 0f;
            if (_motors != null) foreach (var m in _motors) if (m != null) total += m.Rpm;
            float avg  = _motors != null && _motors.Length > 0 ? total / _motors.Length : 0f;
            float glow = Mathf.Clamp01(avg / 7000f);
            Color emissive = new Color(glow * 0.10f, glow * 0.035f, 0f);
            _matMotor.SetColor("_EmissionColor", emissive);
            if (glow > 0.01f && !_matMotor.IsKeywordEnabled("_EMISSION"))
                _matMotor.EnableKeyword("_EMISSION");
        }
    }
}
