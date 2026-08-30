using System.Collections.Generic;
using UnityEngine;
using Astra.Flight;

namespace Astra.Drone
{
    /// <summary>
    /// Renders a smooth glowing flight trail behind the UAV using a LineRenderer.
    /// Shows actual vs estimated position when GPS-denied mode is active.
    /// </summary>
    [DisallowMultipleComponent]
    public class FlightTrailRenderer : MonoBehaviour
    {
        [Header("Trail Settings")]
        [SerializeField] private int   maxPoints       = 400;
        [SerializeField] private float recordIntervalS = 0.08f;
        [SerializeField] private float trailWidth      = 0.06f;
        [SerializeField] private Color trailColorStart = new Color(0.2f, 0.85f, 1.0f, 0.9f);
        [SerializeField] private Color trailColorEnd   = new Color(0.1f, 0.4f,  0.8f, 0.0f);

        [Header("GPS-Denied Estimated Trail")]
        [SerializeField] private Color estColorStart   = new Color(1.0f, 0.75f, 0.1f, 0.9f);
        [SerializeField] private Color estColorEnd     = new Color(0.8f, 0.4f,  0.0f, 0.0f);

        private LineRenderer _actualTrail;
        private LineRenderer _estTrail;
        private readonly Queue<Vector3> _actualPoints = new Queue<Vector3>();
        private readonly Queue<Vector3> _estPoints    = new Queue<Vector3>();
        private float _timer;
        private FlightControlSystem _fc;

        private void Awake()
        {
            _fc = GetComponent<FlightControlSystem>();
            _actualTrail = BuildLine("Trail_Actual", trailColorStart, trailColorEnd, trailWidth);
            _estTrail    = BuildLine("Trail_Estimated", estColorStart, estColorEnd, trailWidth * 0.7f);
        }

        private LineRenderer BuildLine(string goName, Color c0, Color c1, float w)
        {
            var go = new GameObject(goName);
            go.transform.SetParent(null, false);

            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace    = true;
            lr.positionCount    = 0;
            lr.startWidth       = w;
            lr.endWidth         = 0f;
            lr.numCapVertices   = 4;
            lr.numCornerVertices= 4;

            Shader lit = (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null)
                ? (Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Sprites/Default"))
                : (Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Particles/Standard Unlit"));
            if (lit != null)
            {
                var mat = new Material(lit);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c0);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", c0);
                lr.material = mat;
            }

            var grad = new Gradient();
            grad.SetKeys(
                new[]{ new GradientColorKey(c0, 0f), new GradientColorKey(c1, 1f) },
                new[]{ new GradientAlphaKey(c0.a, 0f), new GradientAlphaKey(0f, 1f) });
            lr.colorGradient = grad;
            return lr;
        }

        private void Update()
        {
            _timer += Time.deltaTime;
            if (_timer < recordIntervalS) return;
            _timer = 0f;

            bool flying = _fc != null && _fc.IsArmed;
            if (!flying) return;

            Vector3 pos = transform.position;
            Enqueue(_actualPoints, pos, maxPoints);
            ApplyToLineRenderer(_actualTrail, _actualPoints);

            // Estimated position trail (from localization provider)
            var loc = Astra.Core.AstraServices.Get<Astra.Contracts.ILocalizationProvider>();
            if (loc != null && loc.CurrentEstimate.IsValid)
            {
                Vector3 est = loc.CurrentEstimate.Position;
                Enqueue(_estPoints, est, maxPoints);
                ApplyToLineRenderer(_estTrail, _estPoints);
                _estTrail.gameObject.SetActive(true);
            }
            else
            {
                _estTrail.gameObject.SetActive(false);
            }
        }

        private static void Enqueue(Queue<Vector3> q, Vector3 v, int max)
        {
            q.Enqueue(v);
            while (q.Count > max) q.Dequeue();
        }

        private static void ApplyToLineRenderer(LineRenderer lr, Queue<Vector3> q)
        {
            var arr = q.ToArray();
            lr.positionCount = arr.Length;
            lr.SetPositions(arr);
        }

        public void ClearTrails()
        {
            _actualPoints.Clear();
            _estPoints.Clear();
            if (_actualTrail) _actualTrail.positionCount = 0;
            if (_estTrail)    _estTrail.positionCount    = 0;
        }
    }
}
