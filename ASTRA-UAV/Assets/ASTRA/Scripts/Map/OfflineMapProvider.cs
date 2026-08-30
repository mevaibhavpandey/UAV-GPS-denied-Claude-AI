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
    /// Procedural 3D urban environment generator with extruded buildings, road networks, and obstacle colliders.
    /// Provides zero-dependency offline terrain & height queries with high-aesthetic modern styling.
    /// Implements IMapDataProvider.
    /// </summary>
    [DisallowMultipleComponent]
    public class OfflineMapProvider : MonoBehaviour, IMapDataProvider
    {
        [Header("City Layout Parameters")]
        [SerializeField] private int cityGridSize = 8;
        [SerializeField] private float blockSizeM = 42.0f;
        [SerializeField] private float streetWidthM = 16.0f;
        [SerializeField] private float minBuildingHeightM = 12.0f;
        [SerializeField] private float maxBuildingHeightM = 52.0f;
        [SerializeField] private float usableRadiusM = 1000.0f;

        [Header("Visual Styling")]
        [SerializeField] private Material buildingMaterial;
        [SerializeField] private Material roadMaterial;
        [SerializeField] private Material groundMaterial;

        private GameObject _environmentRoot;
        private readonly List<Bounds> _buildingBounds = new List<Bounds>();
        private bool _isReady;
        private float _loadProgress;
        private string _statusDetail = "Idle";
        private SubsystemStatus _status = SubsystemStatus.Initialising;

        public string Name => "ASTRA Procedural Offline City Provider";
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
            _statusDetail = "Generating procedural urban geometry & road network...";
            _loadProgress = 0.2f;

            BuildProceduralCity();

            _isReady = true;
            _loadProgress = 1.0f;
            _status = SubsystemStatus.Ok;
            _statusDetail = "Offline procedural city ready (Zero-dependency offline mode).";
            StatusChanged?.Invoke(this);

            EventLog.Info(LogSource.System, "Offline procedural 3D map environment generated successfully.");
        }

        public void Shutdown()
        {
            if (_environmentRoot != null)
            {
                Destroy(_environmentRoot);
            }
            _isReady = false;
            _status = SubsystemStatus.Offline;
        }

        public void Tick(float deltaTime) { }

        private Material GetOrCreateMat(string name, Color color, float smoothness, float metallic)
        {
            Shader lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material mat = new Material(lit);
            mat.name = name;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            return mat;
        }

        private void BuildProceduralCity()
        {
            if (_environmentRoot != null)
            {
                Destroy(_environmentRoot);
            }

            _environmentRoot = new GameObject("ASTRA_Offline_City");
            _environmentRoot.transform.SetParent(transform);
            _buildingBounds.Clear();

            // Tactical Materials
            Material groundMat = groundMaterial ?? GetOrCreateMat("Mat_Ground", new Color(0.08f, 0.10f, 0.12f), 0.15f, 0.05f);
            Material asphaltMat = roadMaterial ?? GetOrCreateMat("Mat_Asphalt", new Color(0.12f, 0.14f, 0.16f), 0.25f, 0.05f);
            Material bldgDarkMat = buildingMaterial ?? GetOrCreateMat("Mat_BldgDark", new Color(0.15f, 0.17f, 0.20f), 0.40f, 0.20f);
            Material bldgGlassMat = GetOrCreateMat("Mat_BldgGlass", new Color(0.10f, 0.20f, 0.28f), 0.85f, 0.70f);
            Material helipadMat = GetOrCreateMat("Mat_Helipad", new Color(0.18f, 0.55f, 0.40f), 0.50f, 0.10f);

            // 1. Ground Base Terrain
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground_Terrain";
            ground.tag = "Terrain";
            ground.transform.SetParent(_environmentRoot.transform);
            ground.transform.position = Vector3.zero;
            ground.transform.localScale = new Vector3(usableRadiusM * 0.2f, 1f, usableRadiusM * 0.2f);
            ground.GetComponent<Renderer>().material = groundMat;

            // 2. City Grid & Buildings
            float totalBlockStep = blockSizeM + streetWidthM;
            float startOffset = -(cityGridSize * 0.5f) * totalBlockStep;

            System.Random prng = new System.Random(42); // deterministic seed

            for (int gx = 0; gx < cityGridSize; gx++)
            {
                for (int gz = 0; gz < cityGridSize; gz++)
                {
                    float bx = startOffset + gx * totalBlockStep;
                    float bz = startOffset + gz * totalBlockStep;

                    // Leave launch pad area (origin 0,0) clear for takeoff / landing
                    if (Mathf.Abs(gx - cityGridSize / 2) <= 1 && Mathf.Abs(gz - cityGridSize / 2) <= 1)
                    {
                        continue;
                    }

                    // Road Block Plate (Asphalt)
                    GameObject roadBlock = GameObject.CreatePrimitive(PrimitiveType.Plane);
                    roadBlock.name = $"Road_{gx}_{gz}";
                    roadBlock.tag = "Terrain";
                    roadBlock.transform.SetParent(_environmentRoot.transform);
                    roadBlock.transform.position = new Vector3(bx, 0.01f, bz);
                    roadBlock.transform.localScale = new Vector3(totalBlockStep * 0.1f, 1f, totalBlockStep * 0.1f);
                    roadBlock.GetComponent<Renderer>().material = asphaltMat;

                    // Generate 1-3 buildings per block
                    int subBuildings = prng.Next(1, 3);
                    float subW = blockSizeM * 0.44f;
                    float subL = blockSizeM * 0.44f;

                    for (int sb = 0; sb < subBuildings; sb++)
                    {
                        float offsetX = (sb % 2 == 0 ? -1 : 1) * (subW * 0.52f);
                        float offsetZ = (sb / 2 == 0 ? -1 : 1) * (subL * 0.52f);

                        float buildingHeight = (float)(minBuildingHeightM + prng.NextDouble() * (maxBuildingHeightM - minBuildingHeightM));

                        GameObject bldg = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        bldg.name = $"Building_{gx}_{gz}_{sb}";
                        bldg.tag = "Building";
                        bldg.transform.SetParent(_environmentRoot.transform);
                        bldg.transform.position = new Vector3(bx + offsetX, buildingHeight * 0.5f, bz + offsetZ);
                        bldg.transform.localScale = new Vector3(subW, buildingHeight, subL);

                        Material mat = (sb % 2 == 0) ? bldgDarkMat : bldgGlassMat;
                        bldg.GetComponent<Renderer>().material = mat;

                        _buildingBounds.Add(new Bounds(bldg.transform.position, bldg.transform.localScale));

                        // Add rooftop helipad on selected tall buildings
                        if (buildingHeight > 30.0f && (gx + gz) % 3 == 0)
                        {
                            GameObject pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                            pad.name = $"Rooftop_Helipad_{gx}_{gz}";
                            pad.tag = "Terrain";
                            pad.transform.SetParent(bldg.transform);
                            pad.transform.localPosition = new Vector3(0f, 0.505f, 0f);
                            pad.transform.localScale = new Vector3(0.75f, 0.01f, 0.75f);
                            pad.GetComponent<Renderer>().material = helipadMat;
                        }
                    }
                }
            }

            // 3. Dynamic Obstacle Patrol Entity (Cross-corridor moving threat)
            GameObject obstacleDrone = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            obstacleDrone.name = "Dynamic_Threat_Patrol_1";
            obstacleDrone.tag = "Obstacle";
            obstacleDrone.transform.SetParent(_environmentRoot.transform);
            obstacleDrone.transform.position = new Vector3(-80f, 35f, 160f);
            obstacleDrone.transform.localScale = new Vector3(3.5f, 2.0f, 3.5f);
            Material threatMat = GetOrCreateMat("Mat_Threat", new Color(0.95f, 0.22f, 0.18f), 0.75f, 0.5f);
            obstacleDrone.GetComponent<Renderer>().material = threatMat;
            obstacleDrone.AddComponent<DynamicObstaclePatrol>();
        }

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
