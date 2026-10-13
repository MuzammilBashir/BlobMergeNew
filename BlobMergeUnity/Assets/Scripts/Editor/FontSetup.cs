using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace BlobMerge.EditorTools
{
    /// <summary>
    /// Makes sure TextMeshPro essentials are imported and the Barlow Bold SDF font asset exists
    /// (Assets/Resources/Fonts/Barlow-Bold SDF.asset, loaded at runtime by TextStyle), plus an SDF asset for every
    /// other family in Assets/Fonts (<see cref="Extra"/>). Runs automatically after script reloads, and from
    /// Tools > BlobMerge > Setup Project.
    /// </summary>
    [InitializeOnLoad]
    public static class FontSetup
    {
        public const string SourceTtf = "Assets/Fonts/Barlow-Bold.ttf";
        public const string FontAssetPath = "Assets/Resources/Fonts/Barlow-Bold SDF.asset";
        const string TmpSettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";
        const string Dir = "Assets/Resources/Fonts/";
        const string Charset = "0123456789+-%:,.!?ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz ";

        /// <summary>Families to offer besides Barlow: source TTF -> SDF asset. In Resources, so TMP_FontAsset.name or
        /// Resources.Load can reach them at runtime. A variable font comes in at its default instance (no axes).</summary>
        static readonly (string ttf, string asset)[] Extra =
        {
            ("Assets/Fonts/Fredoka-Bold.ttf", Dir + "Fredoka-Bold SDF.asset"),
            ("Assets/Fonts/Nunito-Italic-VariableFont_wght.ttf", Dir + "Nunito-Italic SDF.asset"),
        };

        static int retries;

        static FontSetup()
        {
            if (Application.isBatchMode) return; // batch builds call Ensure() explicitly
            EditorApplication.delayCall += EnsureWithRetry;
        }

        // the essentials import finishes asynchronously in the editor; keep trying for a little while
        static void EnsureWithRetry()
        {
            if (Ensure() == null)
            {
                if (File.Exists(SourceTtf) && retries++ < 20) EditorApplication.delayCall += EnsureWithRetry;
                return;
            }
            HudPrefabBuilder.Build(); // creates the editable HUD prefab/material/sprites if missing
        }

        public static TMP_FontAsset Ensure()
        {
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (existing != null) { EnsureExtras(); return existing; }

            EnsureEssentials();
            if (TMP_Settings.instance == null)
            {
                Debug.Log("[BlobMerge] Waiting for TMP Essential Resources to finish importing before creating the font asset.");
                return null;
            }
            if (!File.Exists(SourceTtf))
            {
                Debug.LogWarning("[BlobMerge] " + SourceTtf + " not found; text will use the TMP default font.");
                return null;
            }
            var fa = Create(SourceTtf, FontAssetPath);
            EnsureExtras();
            return fa;
        }

        /// <summary>SDF assets for the other families in Assets/Fonts (missing ones only).</summary>
        [MenuItem("Tools/BlobMerge/Build Font Assets (creates missing ones only)")]
        public static void EnsureExtras()
        {
            foreach (var f in Extra) Create(f.ttf, f.asset);
        }

        /// <summary>The SDF asset for <paramref name="ttfPath"/>, or the existing one. Dynamic 1024² SDFAA atlas with
        /// generous padding so outlines never clip; the atlas and material live inside the asset.</summary>
        static TMP_FontAsset Create(string ttfPath, string assetPath)
        {
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (existing != null) return existing;
            var ttf = AssetDatabase.LoadAssetAtPath<Font>(ttfPath);
            if (ttf == null) { Debug.LogWarning("[BlobMerge] " + ttfPath + " not found; no font asset made for it."); return null; }

            var fa = TMP_FontAsset.CreateFontAsset(ttf, 90, 12, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            if (fa == null) { Debug.LogError("[BlobMerge] Could not create TMP font asset from " + ttfPath); return null; }
            string name = Path.GetFileNameWithoutExtension(assetPath);
            fa.name = name;
            fa.TryAddCharacters(Charset);

            Directory.CreateDirectory(Path.GetDirectoryName(assetPath));
            AssetDatabase.CreateAsset(fa, assetPath);
            fa.atlasTextures[0].name = name + " Atlas";
            AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
            fa.material.name = name + " Material";
            AssetDatabase.AddObjectToAsset(fa.material, fa);
            EditorUtility.SetDirty(fa);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(assetPath);
            Debug.Log("[BlobMerge] Created " + assetPath);
            return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
        }

        static void EnsureEssentials()
        {
            if (File.Exists(TmpSettingsPath)) return;
            string pkg = Path.GetFullPath("Packages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage");
            if (!File.Exists(pkg)) { Debug.LogError("[BlobMerge] TMP Essential Resources package not found at " + pkg); return; }
            AssetDatabase.ImportPackage(pkg, false);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("[BlobMerge] Imported TMP Essential Resources.");
        }
    }
}
