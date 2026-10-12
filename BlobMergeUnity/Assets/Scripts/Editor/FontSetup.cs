using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace BlobMerge.EditorTools
{
    /// <summary>
    /// Makes sure TextMeshPro essentials are imported and the Barlow Bold SDF font asset exists
    /// (Assets/Resources/Fonts/Barlow-Bold SDF.asset, loaded at runtime by TextStyle). Runs automatically
    /// after script reloads, and from Tools > BlobMerge > Setup Project.
    /// </summary>
    [InitializeOnLoad]
    public static class FontSetup
    {
        public const string SourceTtf = "Assets/Fonts/Barlow-Bold.ttf";
        public const string FontAssetPath = "Assets/Resources/Fonts/Barlow-Bold SDF.asset";
        const string TmpSettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";

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
            if (existing != null) return existing;

            EnsureEssentials();
            if (TMP_Settings.instance == null)
            {
                Debug.Log("[BlobMerge] Waiting for TMP Essential Resources to finish importing before creating the font asset.");
                return null;
            }

            var ttf = AssetDatabase.LoadAssetAtPath<Font>(SourceTtf);
            if (ttf == null)
            {
                Debug.LogWarning("[BlobMerge] " + SourceTtf + " not found; text will use the TMP default font.");
                return null;
            }

            // dynamic SDF atlas; generous padding so the outline never clips
            var fa = TMP_FontAsset.CreateFontAsset(ttf, 90, 12, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            if (fa == null) { Debug.LogError("[BlobMerge] Could not create TMP font asset from " + SourceTtf); return null; }
            fa.name = "Barlow-Bold SDF";
            fa.TryAddCharacters("0123456789+-%:,.!?ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz ");

            Directory.CreateDirectory(Path.GetDirectoryName(FontAssetPath));
            AssetDatabase.CreateAsset(fa, FontAssetPath);
            fa.atlasTextures[0].name = "Barlow-Bold Atlas";
            AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
            fa.material.name = "Barlow-Bold Material";
            AssetDatabase.AddObjectToAsset(fa.material, fa);
            EditorUtility.SetDirty(fa);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(FontAssetPath);
            Debug.Log("[BlobMerge] Created " + FontAssetPath);
            return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
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
