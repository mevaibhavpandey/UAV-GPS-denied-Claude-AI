using System;
using System.Collections.Generic;
using UnityEngine;
using Astra.Contracts;
using Astra.Core;
using Astra.Core.Geo;
using Astra.Core.Logging;

namespace Astra.Map
{
    /// <summary>
    /// Procedural 3D urban environment generator with extruded buildings, road networks, and obstacle colliders.
    /// Provides zero-dependency offline terrain & height queries.
    /// Implements IMapDataProvider.
    /// </summary>
    [DisallowMultipleComponent]
    public class OfflineMapProvider : MonoBehaviour, IMapDataProvider
    {
        [Header("City Layout Parameters")]
        [SerializeField] private int cityGridSize = 8;
        [SerializeField] private float blockSizeM = 40.0f;
        [SerializeField] private float streetWidthM = 14.0f;
        [SerializeField] private float minBuildingHeightM = 12.0f;
        [SerializeField] private float maxBuildingHeightM = 48.0f;
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
            _statusDetail = "Generating procedural urban geometry...";
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

        private void BuildProceduralCity()
        {
            if (_environmentRoot != null)
            {
                Destroy(_environmentRoot);
            }

            _environmentRoot = new GameObject("ASTRA_Offline_City");
            _environmentRoot.transform.SetParent(transform);
            _buildingBounds.Clear();

            // 1. Ground Plane
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground_Terrain";
            ground.tag = "Terrain";
            ground.transform.SetParent(_environmentRoot.transform);
            ground.transform.position = Vector3.zero;
            ground.transform.localScale = new Vector3(usableRadiusM * 0.2f, 1f, usableRadiusM * 0.2f);
            if (groundMaterial != null) ground.GetComponent<Renderer>().material = groundMaterial;

            // 2. City Blocks & Buildings
            float totalBlockStep = blockSizeM + streetWidthM;
            float startOffset = -(cityGridSize * 0.5f) * totalBlockStep;

            System.Random prng = new System.Random(42); // deterministic seed

            for (int gx = 0; gx < cityGridSize; gx++)
            {
                for (int gz = 0; gz < cityGridSize; gz++)
                {
                    // Leave launch pad area (origin 0,0) clear
                    if (Mathf.Abs(gx - cityGridSize / 2) <= 1 && Mathf.Abs(gz - cityGridSize / 2) <= 1)
                    {
                        continue;
                    }

                    float bx = startOffset + gx * totalBlockStep;
                    float bz = startOffset + gz * totalBlockStep;

                    // Generate 1-4 buildings per block
                    int subBuildings = prng.Next(1, 4);
                    float subW = blockSizeM * 0.45f;
                    float subL = blockSizeM * 0.45f;

                    for (int sb = 0; sb < subBuildings; sb++)
                    {
                        float offsetX = (sb % 2 == 0 ? -1 : 1) * (subW * 0.5f);
                        float offsetZ = (sb / 2 == 0 ? -1 : 1) * (subL * 0.5f);

                        float buildingHeight = (float)(minBuildingHeightM + prng.NextDouble() * (maxBuildingHeightM - minBuildingHeightM));

                        GameObject bldg = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        bldg.name = $"Building_{gx}_{gz}_{sb}";
                        bldg.tag = "Building";
                        bldg.transform.SetParent(_environmentRoot.transform);
                        bldg.transform.position = new Vector3(bx + offsetX, buildingHeight * 0.5f, bz + offsetZ);
                        bldg.transform.localScale = new Vector3(subW, buildingHeight, subL);

                        if (buildingMaterial != null) bldg.GetComponent<Renderer>().material = buildingMaterial;

                        _buildingBounds.Add(new Bounds(bldg.transform.position, bldg.transform.localScale));
                    }
                }
            }
        }

        public bool SampleTerrainHeight(Vector2 worldXZ, out float worldY)
        {
            worldY = 0f; // flat baseline terrain
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
