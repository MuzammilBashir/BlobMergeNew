using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AdSim.EditorTools
{
    /// <summary>
    /// Runs an <see cref="AdRequest"/>: opens the scene, enters play mode, attaches the request's
    /// <see cref="AdDirector"/>, records, then leaves play mode (and quits in batchmode).
    /// Batchmode (no -quit, no -nographics):
    ///   Unity -batchmode -projectPath . -executeMethod AdSim.EditorTools.AdSimLauncher.RunFromCommandLine
    ///         -adRequest Logs/adsim/request.json -logFile Logs/adsim.log
    /// The request survives the play-mode domain reload through SessionState.
    /// </summary>
    [InitializeOnLoad]
    public static class AdSimLauncher
    {
        const string RequestKey = "AdSim.Request", BatchKey = "AdSim.Batch", ResultKey = "AdSim.Result";

        static AdSimLauncher()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        public static void RunFromCommandLine()
        {
            string path = Arg("-adRequest");
            if (path == null || !File.Exists(path))
            {
                Debug.LogError("[AdSim] -adRequest <request.json> missing");
                EditorApplication.Exit(1);
                return;
            }
            SessionState.SetBool(BatchKey, Application.isBatchMode);
            if (!Launch(File.ReadAllText(path)) && Application.isBatchMode) EditorApplication.Exit(1);
        }

        /// <summary>Start a take from request JSON. Returns false if the request is unusable.</summary>
        public static bool Launch(string requestJson)
        {
            var req = JsonUtility.FromJson<AdRequest>(requestJson);
            if (req == null || string.IsNullOrEmpty(req.scene) || string.IsNullOrEmpty(req.director))
            {
                Debug.LogError("[AdSim] request needs scene + director");
                return false;
            }
            var hand = LoadJson<HandProfile>(req.hand);
            if (hand == null) { Debug.LogError("[AdSim] hand profile not found: " + req.hand); return false; }
            PrepareTexture(hand.idleTexture);
            PrepareTexture(hand.clickTexture);
            if (EditorApplication.isPlaying) { Debug.LogError("[AdSim] leave play mode first"); return false; }
            EditorSceneManager.OpenScene(req.scene, OpenSceneMode.Single);
            SessionState.SetString(RequestKey, requestJson);
            EditorApplication.EnterPlaymode();
            return true;
        }

        static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                string json = SessionState.GetString(RequestKey, "");
                if (string.IsNullOrEmpty(json)) return;
                SessionState.EraseString(RequestKey);
                if (!Attach(json)) EditorApplication.ExitPlaymode();
            }
            else if (change == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(BatchKey, false))
            {
                SessionState.EraseBool(BatchKey);
                EditorApplication.Exit(SessionState.GetBool(ResultKey, false) ? 0 : 3);
            }
        }

        static bool Attach(string json)
        {
            var req = JsonUtility.FromJson<AdRequest>(json);
            var type = Type.GetType(req.director);
            if (type == null || !typeof(AdDirector).IsAssignableFrom(type))
            {
                Debug.LogError("[AdSim] director type not found: " + req.director);
                return false;
            }
            var persona = Persona.FromJson(File.Exists(req.persona ?? "") ? File.ReadAllText(req.persona) : null);
            if (!File.Exists(req.persona ?? "")) Debug.LogWarning("[AdSim] persona not found, using defaults: " + req.persona);
            var hand = LoadJson<HandProfile>(req.hand);
            var idle = AssetDatabase.LoadAssetAtPath<Texture2D>(hand.idleTexture);
            var click = AssetDatabase.LoadAssetAtPath<Texture2D>(hand.clickTexture);
            if (idle == null) { Debug.LogError("[AdSim] hand texture not found: " + hand.idleTexture); return false; }

            var go = new GameObject("AdSim Director");
            var director = (AdDirector)go.AddComponent(type);
            director.Finished += ok =>
            {
                SessionState.SetBool(ResultKey, ok);
                EditorApplication.ExitPlaymode();
            };
            director.Begin(req, persona, hand, idle, click);
            return true;
        }

        /// <summary>Hand art must be uncompressed, unscaled (NPOT kept), without mips, alpha as transparency.</summary>
        static void PrepareTexture(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return;
            AssetDatabase.ImportAsset(assetPath);
            if (!(AssetImporter.GetAtPath(assetPath) is TextureImporter ti)) return;
            bool dirty = ti.textureType != TextureImporterType.Default || ti.mipmapEnabled ||
                         ti.npotScale != TextureImporterNPOTScale.None || !ti.alphaIsTransparency ||
                         ti.textureCompression != TextureImporterCompression.Uncompressed ||
                         ti.wrapMode != TextureWrapMode.Clamp;
            if (!dirty) return;
            ti.textureType = TextureImporterType.Default;
            ti.mipmapEnabled = false;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.alphaIsTransparency = true;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Bilinear;
            ti.SaveAndReimport();
        }

        static T LoadJson<T>(string path) where T : class =>
            !string.IsNullOrEmpty(path) && File.Exists(path) ? JsonUtility.FromJson<T>(File.ReadAllText(path)) : null;

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
