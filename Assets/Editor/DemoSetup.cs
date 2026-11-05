using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RemoteConfigPractice.Editor
{
    // Демо-сцена и билд. Сцена уже лежит в репозитории; пункт меню пересоздаёт её с нуля.
    // Из командной строки: Unity.exe -batchmode -quit -projectPath . -executeMethod RemoteConfigPractice.Editor.DemoSetup.BuildWindows
    public static class DemoSetup
    {
        const string ScenePath = "Assets/Scenes/RemoteConfigDemo.unity";
        const string MaterialFolder = "Assets/Materials";
        const string WindowsBuild = "Builds/Windows/RemoteConfigPractice.exe";

        static readonly (string id, PrimitiveType shape, Color color)[] Arsenal =
        {
            ("pistol", PrimitiveType.Cube, new Color(1f, 0.56f, 0.2f)),
            ("rifle", PrimitiveType.Capsule, new Color(0.2f, 0.78f, 0.7f)),
            ("shotgun", PrimitiveType.Cylinder, new Color(0.62f, 0.45f, 0.95f)),
            ("sniper", PrimitiveType.Sphere, new Color(0.35f, 0.6f, 1f))
        };

        [MenuItem("Remote Config/Recreate Demo Scene", priority = 1)]
        public static void CreateDemo()
        {
            PlayerSettings.companyName = "KTSHKI";
            PlayerSettings.productName = "RemoteConfigPractice";
            PlayerSettings.bundleVersion = "1.0";
            PlayerSettings.runInBackground = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = true;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)) { tag = "MainCamera" };
            camera.transform.position = new Vector3(0f, 0f, -9f);
            var view = camera.GetComponent<Camera>();
            view.clearFlags = CameraClearFlags.SolidColor;
            view.backgroundColor = new Color(0.12f, 0.14f, 0.2f);

            var sun = new GameObject("Directional Light", typeof(Light));
            sun.GetComponent<Light>().type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(40f, -35f, 0f);

            var weapons = new GameObject("Weapons");
            for (int i = 0; i < Arsenal.Length; i++)
            {
                var (id, shape, color) = Arsenal[i];
                var weapon = GameObject.CreatePrimitive(shape);
                weapon.name = $"Weapon_{id}";
                weapon.transform.SetParent(weapons.transform);
                weapon.transform.position = new Vector3((i - 1.5f) * 3.2f, 3.1f, 0f);
                weapon.transform.localScale = shape == PrimitiveType.Capsule || shape == PrimitiveType.Cylinder
                    ? new Vector3(0.75f, 0.55f, 0.75f)
                    : Vector3.one * 1.05f;
                Object.DestroyImmediate(weapon.GetComponent<Collider>());
                weapon.GetComponent<Renderer>().sharedMaterial = ColorMaterial(id, color);
                weapon.AddComponent<Weapon>().id = id;
            }

            var loader = new GameObject("Remote Config", typeof(RemoteConfigLoader)).GetComponent<RemoteConfigLoader>();
            loader.gameObject.AddComponent<ConfigDebugPanel>().loader = loader;

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log($"[DemoSetup] Scene saved: {ScenePath}");
        }

        [MenuItem("Remote Config/Build Windows", priority = 20)]
        public static void BuildWindows()
        {
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = WindowsBuild,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            });
            var summary = report.summary;
            Debug.Log($"[DemoSetup] Build {summary.result}: {summary.outputPath}, {summary.totalSize / 1048576f:0.0} MB, " +
                      $"{summary.totalTime.TotalSeconds:0} s");
            if (Application.isBatchMode && summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }

        [MenuItem("Remote Config/Show Local Copy Folder", priority = 40)]
        static void ShowLocalCopy()
        {
            string folder = Path.GetDirectoryName(ConfigCache.InPersistentData().FilePath);
            Directory.CreateDirectory(folder);
            EditorUtility.RevealInFinder(folder);
        }

        [MenuItem("Remote Config/Delete Local Copy", priority = 41)]
        static void DeleteLocalCopy()
        {
            var cache = ConfigCache.InPersistentData();
            Debug.Log(cache.Delete() ? $"[RemoteConfig] Local copy deleted: {cache.FilePath}" : "[RemoteConfig] There is no local copy");
        }

        static Material ColorMaterial(string id, Color color)
        {
            string path = $"{MaterialFolder}/{id}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Directory.CreateDirectory(MaterialFolder);
                material = new Material(Shader.Find("Standard"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.color = color;
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
