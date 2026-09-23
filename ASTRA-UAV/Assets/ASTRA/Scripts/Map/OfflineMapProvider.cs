using System;
using System.Collections.Generic;
using UnityEngine;
using Astra.Contracts;
using Astra.Core;
using Astra.Core.Geo;
using Astra.Core.Logging;
using Astra.Environment;

namespace Astra.Map
{
    /// <summary>
    /// Zero-dependency procedural environment used as the OFFLINE parachute when the Cesium /
    /// Google Photorealistic 3D Tiles stream is unavailable (no network, no ion token, or exhausted
    /// quota). See <see cref="CesiumMapProvider"/> for the photoreal headline path.
    ///
    /// HONESTY NOTE (do not remove)
    /// ----------------------------
    /// This is a STYLIZED, procedurally generated environment. It is deliberately laid out to evoke
    /// the low-rise campus-in-a-green-suburb character of the BMSIT&M area of north Bangalore
    /// (Avalahalli / Yelahanka), but it is NOT a survey-accurate reconstruction: the building
    /// footprints, heights and road positions are synthetic, not measured. The only way to get the
    /// real, measured geometry of Bangalore is the Cesium provider streaming Google's photogrammetry
    /// (see MAP_SETUP.md). This class exists so a demo still runs when that stream cannot.
    ///
    /// What changed from the earlier version, and why: the previous generator produced a dark,
    /// near-black "tactical" grid of identical glass cubes, which read as a video-game skyline rather
    /// than a real place. This version uses a daytime palette, a realistic asphalt road grid with
    /// lane markings and medians, mixed building typologies (concrete, terracotta, glass, low campus
    /// blocks), and abundant tree canopy - Bangalore is the "Garden City" and its aerial view is
    /// dominated by greenery. A distinct BMSIT&M campus cluster is placed near the launch pad.
    /// </summary>
    [DisallowMultipleComponent]
    public class OfflineMapProvider : MonoBehaviour, IMapDataProvider
    {
        [Header("Site Layout")]
        [SerializeField] private int cityGridSize = 9;
        [SerializeField] private float blockSizeM = 44.0f;
        [SerializeField] private float streetWidthM = 18.0f;
        [SerializeField] private float minBuildingHeightM = 9.0f;
        [SerializeField] private float maxBuildingHeightM = 46.0f;
        [SerializeField] private float usableRadiusM = 1000.0f;
        [SerializeField] private int randomSeed = 42;

        [Header("Greenery (Garden City character)")]
        [SerializeField] private bool generateTrees = true;
        [SerializeField] private int treesPerBlock = 3;

        private GameObject _environmentRoot;
        private readonly List<Bounds> _buildingBounds = new List<Bounds>();
        private bool _isReady;
        private float _loadProgress;
        private string _statusDetail = "Idle";
        private SubsystemStatus _status = SubsystemStatus.Initialising;

        public string Name => "ASTRA Offline City (stylized BMSIT&M / Bangalore)";
        public string ProviderId => "OFFLINE";
        public DataProvenance Provenance => DataProvenance.Simulated;
        public SubsystemStatus Status => _status;
        public bool RequiresNetwork => false;
        public bool IsReady => _isReady;
        public float LoadProgress => _loadProgress;
        public string StatusDetail => _statusDetail;
        public float UsableRadiusM => usableRadiusM;

        public event Action<IMapDataProvider> StatusChanged;

        private void Start()
        {
            Initialise(GeoReference.Instance);
        }

        public void Initialise(GeoReference georeference)
        {
            _statusDetail = "Generating stylized BMSIT&M / Bangalore environment...";
            _loadProgress = 0.2f;

            BuildEnvironment();

            _isReady = true;
            _loadProgress = 1.0f;
            _status = SubsystemStatus.Ok;
            _statusDetail = "Offline environment ready (stylized, zero-dependency).";
            StatusChanged?.Invoke(this);

            EventLog.Info(LogSource.System,
                "Offline environment generated (STYLIZED representation of BMSIT&M area - not survey-accurate).");
        }

        public void Shutdown()
        {
            if (_environmentRoot != null) Destroy(_environmentRoot);
            _isReady = false;
            _status = SubsystemStatus.Offline;
        }

        public void Tick(float deltaTime) { }

        // ----------------------------------------------------------------------------------------
        // Materials
        // ----------------------------------------------------------------------------------------

        private Material GetOrCreateMat(string matName, Color color, float smoothness, float metallic)
        {
            Shader lit = (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null)
                ? (Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"))
                : (Shader.Find("Standard") ?? Shader.Find("Diffuse") ?? Shader.Find("Unlit/Color"));
            if (lit == null) lit = Shader.Find("Standard") ?? Shader.Find("Diffuse") ?? Shader.Find("Unlit/Color");

            Material mat = new Material(lit) { name = matName, color = color };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            return mat;
        }
        // BUILD_PLACEHOLDER
        // ----------------------------------------------------------------------------------------
        // Environment generation
        // ----------------------------------------------------------------------------------------

        // Daytime "Garden City" palette - warm concrete, terracotta, glass and heavy greenery,
        // rather than the earlier near-black tactical grays that read as a game skybox.
        private Material _matGrass, _matAsphalt, _matLane, _matConcrete, _matTerracotta, _matGlass,
                         _matCampus, _matPad, _matTrunk, _matCanopy, _matThreat;

        private void BuildMaterials()
        {
            _matGrass      = GetOrCreateMat("Mat_Grass",      new Color(0.36f, 0.44f, 0.28f), 0.10f, 0.0f);
            _matAsphalt    = GetOrCreateMat("Mat_Asphalt",    new Color(0.28f, 0.29f, 0.31f), 0.20f, 0.0f);
            _matLane       = GetOrCreateMat("Mat_Lane",       new Color(0.85f, 0.83f, 0.72f), 0.10f, 0.0f);
            _matConcrete   = GetOrCreateMat("Mat_Concrete",   new Color(0.80f, 0.79f, 0.75f), 0.15f, 0.0f);
            _matTerracotta = GetOrCreateMat("Mat_Terracotta", new Color(0.68f, 0.42f, 0.30f), 0.15f, 0.0f);
            _matGlass      = GetOrCreateMat("Mat_Glass",      new Color(0.52f, 0.66f, 0.72f), 0.85f, 0.55f);
            _matCampus     = GetOrCreateMat("Mat_Campus",     new Color(0.86f, 0.74f, 0.52f), 0.15f, 0.0f);
            _matPad        = GetOrCreateMat("Mat_Pad",        new Color(0.20f, 0.55f, 0.38f), 0.35f, 0.0f);
            _matTrunk      = GetOrCreateMat("Mat_Trunk",      new Color(0.32f, 0.24f, 0.16f), 0.10f, 0.0f);
            _matCanopy     = GetOrCreateMat("Mat_Canopy",     new Color(0.24f, 0.40f, 0.20f), 0.05f, 0.0f);
            _matThreat     = GetOrCreateMat("Mat_Threat",     new Color(0.95f, 0.22f, 0.18f), 0.60f, 0.3f);
        }

        private void BuildEnvironment()
        {
            if (_environmentRoot != null) Destroy(_environmentRoot);
            _environmentRoot = new GameObject("ASTRA_Offline_Environment");
            _environmentRoot.transform.SetParent(transform);
            _buildingBounds.Clear();
            BuildMaterials();

            // 1. Grassy ground base (Garden City green rather than tactical black).
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground_Terrain";
            ground.tag = "Terrain";
            ground.transform.SetParent(_environmentRoot.transform);
            ground.transform.localScale = new Vector3(usableRadiusM * 0.2f, 1f, usableRadiusM * 0.2f);
            ground.GetComponent<Renderer>().sharedMaterial = _matGrass;

            BuildUrbanGrid();
            BuildCampusCluster();
            BuildDynamicThreat();
        }
        // GRID_PLACEHOLDER
        private void BuildUrbanGrid()
        {
            float step = blockSizeM + streetWidthM;
            float startOffset = -(cityGridSize * 0.5f) * step;
            System.Random prng = new System.Random(randomSeed);

            // Asphalt road strips along every grid line (both axes) with a dashed centre lane.
            for (int i = 0; i <= cityGridSize; i++)
            {
                float p = startOffset + i * step - streetWidthM * 0.5f;
                MakeRoad(new Vector3(0f, 0.02f, p + streetWidthM * 0.5f),
                         new Vector3(cityGridSize * step + streetWidthM, 1f, streetWidthM), true);
                MakeRoad(new Vector3(p + streetWidthM * 0.5f, 0.02f, 0f),
                         new Vector3(streetWidthM, 1f, cityGridSize * step + streetWidthM), false);
            }

            for (int gx = 0; gx < cityGridSize; gx++)
            {
                for (int gz = 0; gz < cityGridSize; gz++)
                {
                    float bx = startOffset + gx * step + streetWidthM * 0.5f;
                    float bz = startOffset + gz * step + streetWidthM * 0.5f;

                    // Keep the campus / launch-pad core (centre 3x3) clear; campus is placed there.
                    if (Mathf.Abs(gx - cityGridSize / 2) <= 1 && Mathf.Abs(gz - cityGridSize / 2) <= 1)
                        continue;

                    int subBuildings = prng.Next(1, 4);
                    for (int sb = 0; sb < subBuildings; sb++)
                    {
                        float footprint = blockSizeM * (0.28f + (float)prng.NextDouble() * 0.16f);
                        float ox = (float)(prng.NextDouble() - 0.5) * (blockSizeM - footprint);
                        float oz = (float)(prng.NextDouble() - 0.5) * (blockSizeM - footprint);
                        float h = (float)(minBuildingHeightM + prng.NextDouble() * (maxBuildingHeightM - minBuildingHeightM));

                        // Typology by height: low terracotta homes, mid concrete, tall glass.
                        Material mat = h < 16f ? _matTerracotta : (h < 32f ? _matConcrete : _matGlass);
                        MakeBuilding(new Vector3(bx + ox, 0f, bz + oz), footprint, h, footprint * 0.9f, mat, prng);
                    }

                    if (generateTrees) ScatterTrees(bx, bz, prng);
                }
            }
        }
        // MAKERS_PLACEHOLDER
        private void MakeRoad(Vector3 center, Vector3 size, bool alongX)
        {
            GameObject road = GameObject.CreatePrimitive(PrimitiveType.Cube);
            road.name = "Road";
            road.tag = "Terrain";
            road.transform.SetParent(_environmentRoot.transform);
            road.transform.position = new Vector3(center.x, 0.02f, center.z);
            road.transform.localScale = new Vector3(size.x, 0.04f, size.z);
            road.GetComponent<Renderer>().sharedMaterial = _matAsphalt;

            // Dashed centre lane marking.
            float length = alongX ? size.x : size.z;
            int dashes = Mathf.Max(1, (int)(length / 8f));
            for (int d = 0; d < dashes; d++)
            {
                float t = -length * 0.5f + (d + 0.5f) * (length / dashes);
                GameObject lane = GameObject.CreatePrimitive(PrimitiveType.Cube);
                lane.name = "Lane";
                lane.transform.SetParent(road.transform);
                Destroy(lane.GetComponent<Collider>());
                lane.transform.position = alongX
                    ? new Vector3(center.x + t, 0.05f, center.z)
                    : new Vector3(center.x, 0.05f, center.z + t);
                lane.transform.localScale = alongX
                    ? new Vector3(3.2f, 0.02f, 0.35f)
                    : new Vector3(0.35f, 0.02f, 3.2f);
                lane.GetComponent<Renderer>().sharedMaterial = _matLane;
            }
        }

        private void MakeBuilding(Vector3 basePos, float w, float h, float l, Material mat, System.Random prng)
        {
            GameObject bldg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bldg.name = "Building";
            bldg.tag = "Building";
            bldg.transform.SetParent(_environmentRoot.transform);
            bldg.transform.position = new Vector3(basePos.x, h * 0.5f, basePos.z);
            bldg.transform.localScale = new Vector3(w, h, l);
            bldg.GetComponent<Renderer>().sharedMaterial = mat;
            _buildingBounds.Add(new Bounds(bldg.transform.position, bldg.transform.localScale));

            // Rooftop parapet / water tank detail so towers are not featureless boxes.
            if (h > 20f)
            {
                GameObject tank = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                tank.name = "Rooftop_Detail";
                tank.transform.SetParent(bldg.transform);
                Destroy(tank.GetComponent<Collider>());
                tank.transform.localPosition = new Vector3(0.2f, 0.55f, 0.2f);
                tank.transform.localScale = new Vector3(0.18f, 0.06f, 0.18f);
                tank.GetComponent<Renderer>().sharedMaterial = _matConcrete;
            }
        }

        private void ScatterTrees(float bx, float bz, System.Random prng)
        {
            for (int t = 0; t < treesPerBlock; t++)
            {
                float tx = bx + (float)(prng.NextDouble() - 0.5) * blockSizeM;
                float tz = bz + (float)(prng.NextDouble() - 0.5) * blockSizeM;
                float th = 4.5f + (float)prng.NextDouble() * 3.5f;
                MakeTree(new Vector3(tx, 0f, tz), th);
            }
        }

        private void MakeTree(Vector3 basePos, float height)
        {
            GameObject trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            trunk.name = "Tree_Trunk";
            trunk.tag = "Terrain";
            trunk.transform.SetParent(_environmentRoot.transform);
            trunk.transform.position = new Vector3(basePos.x, height * 0.35f, basePos.z);
            trunk.transform.localScale = new Vector3(0.4f, height * 0.35f, 0.4f);
            trunk.GetComponent<Renderer>().sharedMaterial = _matTrunk;

            GameObject canopy = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            canopy.name = "Tree_Canopy";
            canopy.transform.SetParent(trunk.transform);
            canopy.transform.position = new Vector3(basePos.x, height * 0.85f, basePos.z);
            canopy.transform.localScale = new Vector3(height * 0.7f, height * 0.6f, height * 0.7f);
            canopy.GetComponent<Renderer>().sharedMaterial = _matCanopy;
        }
        // CAMPUS_PLACEHOLDER
        /// <summary>
        /// Places a distinct low-rise academic cluster around the origin to stand in for the
        /// BMSIT&M campus, with a green landing pad marker at the exact launch point. Layout is
        /// evocative, not surveyed (see honesty note at the top of the file).
        /// </summary>
        private void BuildCampusCluster()
        {
            // Landing pad marker at origin (kept clear of buildings).
            GameObject pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pad.name = "Launch_Pad_BMSIT";
            pad.tag = "Terrain";
            pad.transform.SetParent(_environmentRoot.transform);
            pad.transform.position = new Vector3(0f, 0.03f, 0f);
            pad.transform.localScale = new Vector3(9f, 0.03f, 9f);
            pad.GetComponent<Renderer>().sharedMaterial = _matPad;

            // Four ochre academic blocks framing a central quad, offset from the pad.
            Vector3[] blocks =
            {
                new Vector3(-34f, 0f, 30f), new Vector3(34f, 0f, 30f),
                new Vector3(-34f, 0f, -30f), new Vector3(34f, 0f, -30f)
            };
            for (int i = 0; i < blocks.Length; i++)
            {
                float h = 14f + i * 2f;
                MakeBuilding(blocks[i], 26f, h, 16f, _matCampus, null);
            }

            // A taller central administrative / library block set back from the quad.
            MakeBuilding(new Vector3(0f, 0f, 62f), 30f, 24f, 20f, _matConcrete, null);
        }

        private void BuildDynamicThreat()
        {
            GameObject threat = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            threat.name = "Dynamic_Threat_Patrol_1";
            threat.tag = "Obstacle";
            threat.transform.SetParent(_environmentRoot.transform);
            threat.transform.position = new Vector3(-80f, 35f, 160f);
            threat.transform.localScale = new Vector3(3.5f, 2.0f, 3.5f);
            threat.GetComponent<Renderer>().sharedMaterial = _matThreat;
            threat.AddComponent<DynamicObstaclePatrol>();
        }

        // ----------------------------------------------------------------------------------------
        // Height sampling
        // ----------------------------------------------------------------------------------------

        public bool SampleTerrainHeight(Vector2 worldXZ, out float worldY)
        {
            worldY = 0f;
            return true;
        }

        public bool SampleSurfaceHeight(Vector2 worldXZ, out float worldY, out ObstacleClass surfaceClass)
        {
            Vector3 pos = new Vector3(worldXZ.x, 0, worldXZ.y);
            surfaceClass = ObstacleClass.Terrain;
            worldY = 0f;

            for (int i = 0; i < _buildingBounds.Count; i++)
            {
                Bounds b = _buildingBounds[i];
                if (b.min.x <= pos.x && pos.x <= b.max.x && b.min.z <= pos.z && pos.z <= b.max.z)
                {
                    worldY = b.max.y;
                    surfaceClass = ObstacleClass.Building;
                    return true;
                }
            }
            return true;
        }

        public void SampleSurfaceHeights(IReadOnlyList<Vector2> worldXZ, float[] resultsY, bool[] resultsValid)
        {
            for (int i = 0; i < worldXZ.Count; i++)
            {
                resultsValid[i] = SampleSurfaceHeight(worldXZ[i], out float y, out _);
                resultsY[i] = y;
            }
        }
    }
}
