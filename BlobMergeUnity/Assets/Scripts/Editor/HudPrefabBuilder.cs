using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace BlobMerge.EditorTools
{
    /// <summary>
    /// Generates the editable HUD assets:
    ///   Assets/Resources/UI/HUD.prefab              (the HUD — open it in Prefab Mode to edit)
    ///   Assets/UI/Sprites/*.png                      (rounded panel, crown, gear — replace with your own art)
    ///   Assets/Resources/Fonts/Barlow-Bold Outline.mat (text look for ALL text: outline colour/thickness)
    /// Existing files are never overwritten except by the explicit "Rebuild" menu item.
    /// </summary>
    public static class HudPrefabBuilder
    {
        public const string PrefabPath = "Assets/Resources/UI/HUD.prefab";
        public const string MaterialPath = "Assets/Resources/Fonts/Barlow-Bold Outline.mat";
        const string SpriteDir = "Assets/UI/Sprites";

        [MenuItem("Tools/BlobMerge/Rebuild HUD Prefab (overwrites your HUD edits)")]
        static void RebuildMenu()
        {
            if (File.Exists(PrefabPath) && !EditorUtility.DisplayDialog("Rebuild HUD prefab?",
                    "This replaces " + PrefabPath + " with the default layout. Your edits to it will be lost.", "Rebuild", "Cancel"))
                return;
            Build(true);
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        }

        /// <summary>Creates any missing HUD assets. Returns the prefab's HudView (or null if the font isn't ready yet).</summary>
        public static HudView Build(bool overwritePrefab = false)
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontSetup.FontAssetPath);
            if (font == null) return null;

            EnsureOutlineMaterial(font);
            if (!overwritePrefab && File.Exists(PrefabPath))
                return AssetDatabase.LoadAssetAtPath<HudView>(PrefabPath);

            var rounded = SaveSprite(UiSprites.RoundedRect, "RoundedRect", new Vector4(26, 26, 26, 26));
            var crown = SaveSprite(UiSprites.Crown, "Crown", Vector4.zero);
            var gear = SaveSprite(UiSprites.Gear, "Gear", Vector4.zero);

            TextStyle.Init(font); // picks up the material asset so prefab texts reference it
            var hud = HudView.Build(rounded, crown, gear);
            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            var prefab = PrefabUtility.SaveAsPrefabAsset(hud.gameObject, PrefabPath);
            Object.DestroyImmediate(hud.gameObject);
            Debug.Log("[BlobMerge] Created " + PrefabPath + " — open it to edit the HUD.");
            return prefab.GetComponent<HudView>();
        }

        static void EnsureOutlineMaterial(TMP_FontAsset font)
        {
            if (File.Exists(MaterialPath)) return;
            var m = new Material(font.material) { name = "Barlow-Bold Outline" };
            TextStyle.ApplyOutline(m, TextStyle.DefaultOutlineColor, TextStyle.DefaultOutlineWidth);
            Directory.CreateDirectory(Path.GetDirectoryName(MaterialPath));
            AssetDatabase.CreateAsset(m, MaterialPath);
            AssetDatabase.SaveAssets();
        }

        /// <summary>Writes a generated sprite to a PNG asset (kept if it already exists, so your replacements survive).</summary>
        static Sprite SaveSprite(Sprite generated, string name, Vector4 border)
        {
            Directory.CreateDirectory(SpriteDir);
            string path = SpriteDir + "/" + name + ".png";
            if (!File.Exists(path))
            {
                File.WriteAllBytes(path, generated.texture.EncodeToPNG());
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.spritePixelsPerUnit = 100;
                ti.spriteBorder = border;
                ti.alphaIsTransparency = true;
                ti.mipmapEnabled = false;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
    }
}
