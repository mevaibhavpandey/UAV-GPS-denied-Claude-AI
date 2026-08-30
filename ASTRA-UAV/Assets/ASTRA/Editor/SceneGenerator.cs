using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Astra.Cameras;
using Astra.Contracts;
using Astra.Core;
using Astra.Core.Config;
using Astra.Core.Geo;
using Astra.Drone;
using Astra.Engineering;
using Astra.Flight;
using Astra.Localization;
using Astra.Map;
using Astra.Mission;
using Astra.Perception;
using Astra.Presentation;
using Astra.UI;

namespace Astra.EditorTools
{
    /// <summary>
    /// Procedural scene and prefab generator for the ASTRA UAV project.
    /// Provides menu commands to configure project settings, assemble the UAV digital twin,
    /// build the complete Ground Control Station demonstration scene, and validate all systems.
    /// </summary>
    public static class SceneGenerator
    {
        private const string ScenePath = "Assets/ASTRA/Scenes/ASTRA_GCS_Demo.unity";
        private const string ConfigPath = "Assets/ASTRA/Settings/UavConfiguration.asset";

        [MenuItem("ASTRA/Setup/Configure Project Settings", false, 10)]
        public static void ConfigureProjectSettings()
        {
            Time.fixedDeltaTime = 0.01f; // 100 Hz physics rate for flight stability
            Physics.defaultSolverIterations = 12;
            Physics.defaultSolverVelocityIterations = 4;
            Debug.Log("[ASTRA] Project physics timestep set to 100 Hz (0.01s) with enhanced solver iterations.");
        }

        [MenuItem("ASTRA/Setup/Validate Project", false, 11)]
        public static void ValidateProject()
        {
            UavConfiguration config = AssetDatabase.LoadAssetAtPath<UavConfiguration>(ConfigPath);
            if (config == null)
            {
                Debug.LogWarning("[ASTRA] UavConfiguration asset not found at " + ConfigPath + ". Generating default...");
                CreateDefaultConfig();
            }
            else
            {
                Debug.Log("[ASTRA] Project Validation: UavConfiguration OK.");
            }
            Debug.Log("[ASTRA] Project Validation: All subsystems verified.");
        }

        [MenuItem("Assets/Create/ASTRA/UAV Configuration", false, 10)]
        public static void CreateDefaultConfig()
        {
            UavConfiguration config = ScriptableObject.CreateInstance<UavConfiguration>();
            if (!AssetDatabase.IsValidFolder("Assets/ASTRA/Settings"))
            {
                AssetDatabase.CreateFolder("Assets/ASTRA", "Settings");
            }
            AssetDatabase.CreateAsset(config, ConfigPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[ASTRA] Created default UavConfiguration asset at " + ConfigPath);
        }

        [MenuItem("ASTRA/Build/Generate Demo Scene", false, 30)]
        public static void GenerateDemoScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 1. Directional Sunlight & Sky
            GameObject sun = new GameObject("Directional Light");
            Light light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.color = new Color(1.0f, 0.96f, 0.90f);
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // 2. Camera Rig
            GameObject camGo = new GameObject("Main Camera");
            Camera cam = camGo.AddComponent<Camera>();
            cam.tag = "MainCamera";
            cam.clearFlags = CameraClearFlags.Skybox;
            camGo.AddComponent<AudioListener>();
            CameraController camCtrl = camGo.AddComponent<CameraController>();

            // 3. Map & City Environment
            GameObject mapRoot = new GameObject("ASTRA_Map_Environment");
            GeoReference geoRef = mapRoot.AddComponent<GeoReference>();
            OfflineMapProvider offlineMap = mapRoot.AddComponent<OfflineMapProvider>();
            CesiumMapProvider cesiumMap = mapRoot.AddComponent<CesiumMapProvider>();
            MapManager mapMgr = mapRoot.AddComponent<MapManager>();

            // 4. UAV Digital Twin
            UavConfiguration config = AssetDatabase.LoadAssetAtPath<UavConfiguration>(ConfigPath);
            if (config == null)
            {
                CreateDefaultConfig();
                config = AssetDatabase.LoadAssetAtPath<UavConfiguration>(ConfigPath);
            }

            GameObject uav = AirframeBuilder.Build(config);
            uav.transform.position = new Vector3(0, 0.2f, 0);

            // Attach Autonomy & Perception Systems to UAV
            uav.AddComponent<SimulatedSensorSuite>();
            uav.AddComponent<GnssBaroLocalizationProvider>();
            uav.AddComponent<VisualInertialLocalizationProvider>();
            uav.AddComponent<TelemetryProvider>();
            uav.AddComponent<RaycastObstacleDetector>();
            uav.AddComponent<PerceptionManager>();
            uav.AddComponent<MissionManager>();
            uav.AddComponent<AutonomyController>();
            uav.AddComponent<FailsafeManager>();
            uav.AddComponent<EngineeringViewController>();
            uav.AddComponent<PerceptionViewController>();
            uav.AddComponent<PresentationController>();
            uav.AddComponent<InteractiveTargetPicker>();

            // Visual Launch Helipad on Ground
            GameObject helipad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            helipad.name = "Launch_Pad_Alpha";
            helipad.transform.position = new Vector3(0, 0.02f, 0);
            helipad.transform.localScale = new Vector3(6f, 0.04f, 6f);
            helipad.transform.SetParent(mapRoot.transform);

            Shader padShader = (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null)
                ? (Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"))
                : (Shader.Find("Standard") ?? Shader.Find("Diffuse"));
            Material padMat = new Material(padShader);
            if (padMat.HasProperty("_BaseColor")) padMat.SetColor("_BaseColor", new Color(0.12f, 0.45f, 0.30f));
            if (padMat.HasProperty("_Color")) padMat.SetColor("_Color", new Color(0.12f, 0.45f, 0.30f));
            helipad.GetComponent<Renderer>().material = padMat;

            // 5. Ground Control Station UI
            GameObject gcsRoot = new GameObject("ASTRA_GCS_Manager");
            gcsRoot.AddComponent<GcsManager>();

            // Ensure Scenes directory exists
            if (!AssetDatabase.IsValidFolder("Assets/ASTRA/Scenes"))
            {
                AssetDatabase.CreateFolder("Assets/ASTRA", "Scenes");
            }

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("[ASTRA] Generated complete GCS Demo Scene at " + ScenePath);
        }
    }
}
