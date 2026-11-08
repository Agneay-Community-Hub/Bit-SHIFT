// Command-line entry points. Examples (from the repo root):
//   Unity.exe -batchmode -quit -projectPath . -executeMethod BitShift.EditorTools.BuildScript.Verify
//   Unity.exe -batchmode -quit -projectPath . -executeMethod BitShift.EditorTools.BuildScript.BuildWebGL
//   Unity.exe -batchmode -quit -projectPath . -executeMethod BitShift.EditorTools.BuildScript.BuildWindows
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BitShift.EditorTools
{
    public static class BuildScript
    {
        const string ScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("BitShift/Create Main Scene")]
        public static void CreateScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.027f, 0.04f, 0.063f);
            camGo.transform.position = new Vector3(0, 0, -10);
            camGo.AddComponent<AudioListener>();
            new GameObject("BitShift").AddComponent<Game>();
            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[BitShift] Scene created at " + ScenePath);
        }

        static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "BitShift";
            PlayerSettings.productName = "BITSHIFT";
            PlayerSettings.runInBackground = true;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.SplashScreen.showUnityLogo = false;
            PlayerSettings.defaultWebScreenWidth = 1280;
            PlayerSettings.defaultWebScreenHeight = 720;
            // Ship uncompressed: gzip + JS fallback can hang on hosts like itch.io's CDN, and those hosts
            // compress files in transit anyway, so the download size barely changes.
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.decompressionFallback = false;
            PlayerSettings.WebGL.template = "PROJECT:BitShift";
        }

        static void Build(BuildTarget target, BuildTargetGroup group, string path)
        {
            CreateScene();
            ConfigurePlayer();
            if (EditorUserBuildSettings.activeBuildTarget != target)
                EditorUserBuildSettings.SwitchActiveBuildTarget(group, target);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = path,
                target = target,
                options = BuildOptions.None
            });
            var s = report.summary;
            Debug.Log($"[BitShift] Build {target}: {s.result}, {s.totalSize / 1024} KB, {s.totalErrors} errors -> {path}");
            if (Application.isBatchMode) EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
        }

        [MenuItem("BitShift/Build WebGL")]
        public static void BuildWebGL() => Build(BuildTarget.WebGL, BuildTargetGroup.WebGL, "Builds/WebGL");

        [MenuItem("BitShift/Build Windows")]
        public static void BuildWindows() =>
            Build(BuildTarget.StandaloneWindows64, BuildTargetGroup.Standalone, "Builds/Windows/BITSHIFT.exe");

        /// Replays the solver's solutions (tools/solutions.txt) through the C# rules to prove both agree.
        [MenuItem("BitShift/Verify Levels")]
        public static void Verify()
        {
            var levels = Sim.Parse(File.ReadAllText("Assets/Resources/levels.txt"));
            var lines = File.ReadAllLines("tools/solutions.txt").Where(l => l.Contains(":")).ToArray();
            int failures = 0;
            foreach (var line in lines)
            {
                var title = line.Substring(0, line.IndexOf(':')).Trim();
                var moves = line.Substring(line.IndexOf(':') + 1).Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                var L = levels.FirstOrDefault(l => l.title == title);
                if (L == null) { Debug.LogError("[BitShift] unknown level " + title); failures++; continue; }
                var st = Sim.Initial(L);
                bool won = false;
                foreach (var m in moves)
                {
                    int a = m switch
                    {
                        "U" => 0, "R" => 1, "D" => 2, "L" => 3,
                        "shl" => 4 + (int)Op.Shl, "shr" => 4 + (int)Op.Shr, "rol" => 4 + (int)Op.Rol, _ => 4 + (int)Op.Ror
                    };
                    if (Sim.Step(L, st, a).ev == Ev.Win) won = true;
                }
                bool ok = won && st.actions == L.par;
                if (!ok) failures++;
                Debug.Log($"[BitShift] {(ok ? "OK  " : "FAIL")} {title}: won={won} actions={st.actions} par={L.par}");
            }
            Debug.Log($"[BitShift] Verify finished: {lines.Length - failures}/{lines.Length} levels agree with the solver");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }
    }
}
