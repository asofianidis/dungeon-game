using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Depthbound.EditorTools
{
    public static class ProjectSetup
    {
        const string ScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("Depthbound/Setup Main Scene")]
        public static void SetupScene()
        {
            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 8.5f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.02f, 0.03f);
            camGo.transform.position = new Vector3(0, 0, -10);
            new GameObject("Depthbound").AddComponent<GameManager>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.productName = "Depthbound";
            PlayerSettings.companyName = "Depthbound";
            AssetDatabase.SaveAssets();
            Debug.Log("Depthbound: main scene created at " + ScenePath);
        }

        [MenuItem("Depthbound/Build Windows")]
        public static void BuildWindows()
        {
            if (!File.Exists(ScenePath)) SetupScene();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/Windows/Depthbound.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });
            Debug.Log("Depthbound build result: " + report.summary.result);
            if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
        }
    }
}
