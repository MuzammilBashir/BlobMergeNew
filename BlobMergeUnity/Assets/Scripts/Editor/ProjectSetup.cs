using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BlobMerge.EditorTools
{
    public static class ProjectSetup
    {
        const string ScenePath = "Assets/Scenes/Game.unity";
        const string HexaScenePath = "Assets/Scenes/Hexa.unity";
        const string FishScenePath = "Assets/Scenes/FishFantasy.unity";
        const string FishHudPath = "Assets/Resources/UI/HUD_Fish.prefab";
        const string ThemeDir = "Assets/Themes";
        const string HudPath = "Assets/Resources/UI/HUD.prefab";
        const string HexaHudPath = "Assets/Resources/UI/HUD_Hexa.prefab";

        /// <summary>Scene chosen on the command line with "-blobscene Game|Hexa" (default Game).</summary>
        static string SceneFromArgs(out string variant)
        {
            variant = "Game";
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++)
                if (a[i] == "-blobscene") variant = a[i + 1];
            return variant == "Hexa" ? HexaScenePath : variant == "Fish" || variant == "FishFantasy" ? FishScenePath : ScenePath;
        }

        /// <summary>Loads a theme asset, creating it with the default (purple) colours if missing. Never overwrites.</summary>
        static BoardTheme EnsureTheme(string name)
        {
            Directory.CreateDirectory(ThemeDir);
            string path = $"{ThemeDir}/{name}.asset";
            var t = AssetDatabase.LoadAssetAtPath<BoardTheme>(path);
            if (t != null) return t;
            t = ScriptableObject.CreateInstance<BoardTheme>();
            AssetDatabase.CreateAsset(t, path);
            return t;
        }

        /// <summary>Hexa's own HUD: a one-time copy of the main HUD prefab. Never overwrites.</summary>
        static HudView EnsureHexaHud(HudView mainHud)
        {
            var hud = AssetDatabase.LoadAssetAtPath<HudView>(HexaHudPath);
            if (hud != null) return hud;
            if (!AssetDatabase.CopyAsset(HudPath, HexaHudPath)) return mainHud;
            AssetDatabase.ImportAsset(HexaHudPath);
            return AssetDatabase.LoadAssetAtPath<HudView>(HexaHudPath);
        }

        /// <summary>The Fish scene's own HUD: a one-time copy of the main HUD prefab. Never overwrites.</summary>
        static HudView EnsureFishHud(HudView mainHud)
        {
            var hud = AssetDatabase.LoadAssetAtPath<HudView>(FishHudPath);
            if (hud != null) return hud;
            if (!AssetDatabase.CopyAsset(HudPath, FishHudPath)) return mainHud;
            AssetDatabase.ImportAsset(FishHudPath);
            return AssetDatabase.LoadAssetAtPath<HudView>(FishHudPath);
        }

        static void CreateScene(string path, Material mat, TMPro.TMP_FontAsset font, HudView hud, BoardTheme theme, bool hexa, bool fish = false)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            camGo.AddComponent<UniversalAdditionalCameraData>();
            camGo.transform.position = new Vector3(0, 0, -10);

            var gameGo = new GameObject("Game");
            gameGo.AddComponent<AimController>();
            var gc = gameGo.AddComponent<GameController>();
            gc.material = mat;
            gc.font = font;
            gc.hudPrefab = hud;
            gc.boardTheme = theme;
            if (hexa) gc.ApplyHexaPreset();
            if (fish) gc.ApplyFishPreset();
            gameGo.AddComponent<DebugAutopilot>();
            EditorSceneManager.SaveScene(scene, path);
        }

        [MenuItem("Tools/BlobMerge/Setup Project (URP, Android, Scene)")]
        public static void Run()
        {
            Directory.CreateDirectory("Assets/Settings");
            Directory.CreateDirectory("Assets/Materials");
            Directory.CreateDirectory("Assets/Scenes");

            // ---- URP ----
            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/URP_Renderer.asset");
            if (rendererData == null)
            {
                rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rendererData, "Assets/Settings/URP_Renderer.asset");
            }
            var rp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/URP_Asset.asset");
            if (rp == null)
            {
                rp = UniversalRenderPipelineAsset.Create(rendererData);
                AssetDatabase.CreateAsset(rp, "Assets/Settings/URP_Asset.asset");
            }
            rp.supportsHDR = false;
            rp.msaaSampleCount = 4;
            rp.shadowDistance = 0f;
            EditorUtility.SetDirty(rp);
            GraphicsSettings.defaultRenderPipeline = rp;
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = rp;
            }

            // ---- material ----
            AssetDatabase.DeleteAsset("Assets/Materials/BlobUnlit.mat"); // legacy
            var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/VertexColor.mat");
            if (mat == null)
            {
                mat = new Material(Shader.Find("BlobMerge/VertexColor"));
                AssetDatabase.CreateAsset(mat, "Assets/Materials/VertexColor.mat");
            }

            // ---- player settings (Android, portrait) ----
            PlayerSettings.companyName = "Mindstorm Studios";
            PlayerSettings.productName = "BlobMerge";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.mindstormstudios.blobmerge");
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;

            // ---- text: TMP essentials + Barlow Bold SDF font asset ----
            var font = FontSetup.Ensure();
            var hudPrefab = HudPrefabBuilder.Build(); // keeps an existing (edited) HUD prefab

            // ---- scenes: Game (blobs) and Hexa (hexagons), each with its own settings, HUD and theme ----
            var purple = EnsureTheme("BoardTheme_Purple");
            var hexaTheme = EnsureTheme("BoardTheme_Hexa");
            var hexaHud = EnsureHexaHud(hudPrefab);
            CreateScene(HexaScenePath, mat, font, hexaHud, hexaTheme, true);
            if (!File.Exists(FishScenePath)) // Fish: generated once, then it's the user's scene (never regenerated)
                CreateScene(FishScenePath, mat, font, EnsureFishHud(hudPrefab), EnsureTheme("BoardTheme_Fish"), false, true);
            CreateScene(ScenePath, mat, font, hudPrefab, purple, false);
            UiPrefabBuilder.Build(); // Fish UI prefabs (missing ones only) + HUD_Fish art / popup bake
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ScenePath, true),
                new EditorBuildSettingsScene(HexaScenePath, true),
                new EditorBuildSettingsScene(FishScenePath, true)
            };

            AssetDatabase.SaveAssets();
            Debug.Log("[BlobMerge] Setup complete.");
        }

        /// <summary>Desktop build used for automated visual checks (-blobauto / -blobshot).</summary>
        public static void BuildWindowsTest()
        {
            Run(); // regenerate the scenes so code defaults are used
            PlayerSettings.runInBackground = true; // the bot keeps playing when the window isn't focused
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { SceneFromArgs(out _) },
                locationPathName = "Builds/WinTest/BlobMerge.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            });
            Debug.Log("[BlobMerge] Win build result: " + report.summary.result);
        }

        /// <summary>CI-style: regenerate the scene then build the APK.</summary>
        public static void SetupAndBuildAndroid()
        {
            Run();
            BuildAndroid();
        }

        [MenuItem("Tools/BlobMerge/Build Android APK")]
        public static void BuildAndroid()
        {
            Directory.CreateDirectory("Builds");
            string scene = SceneFromArgs(out string variant);
            bool hexa = scene == HexaScenePath, fishV = scene == FishScenePath;
            // variants install side by side: own app id + name, restored after the build
            string id = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android), product = PlayerSettings.productName;
            var oldIcons = PlayerSettings.GetIcons(NamedBuildTarget.Unknown, IconKind.Any);
            if (hexa || fishV)
            {
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, id + (hexa ? ".hexa" : ".fish"));
                // Fish ships under its brand: "Fish Fantasy" + the whale icon (default icon, Fish build only)
                PlayerSettings.productName = hexa ? product + " Hexa" : "Fish Fantasy";
                var icon = fishV ? AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Icons/FishFantasy_AppIcon.png") : null;
                if (icon != null) PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
            }
            try
            {
                var opts = new BuildPlayerOptions
                {
                    scenes = new[] { scene },
                    locationPathName = hexa ? "Builds/BlobMerge_Hexa.apk" : fishV ? "Builds/BlobMerge_Fish.apk" : "Builds/BlobMerge.apk",
                    target = BuildTarget.Android,
                    options = BuildOptions.None
                };
                var report = BuildPipeline.BuildPlayer(opts);
                Debug.Log($"[BlobMerge] Build result ({variant}): " + report.summary.result);
            }
            finally
            {
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, id);
                PlayerSettings.productName = product;
                PlayerSettings.SetIcons(NamedBuildTarget.Unknown, oldIcons, IconKind.Any);
            }
        }
    }
}
