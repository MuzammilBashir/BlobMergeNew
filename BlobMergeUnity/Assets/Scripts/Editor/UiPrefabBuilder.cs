using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace BlobMerge.EditorTools
{
    /// <summary>
    /// Generates the editable Fish UI prefabs (each only when missing, so your edits survive) and wires them into
    /// BoardTheme_Fish:
    ///   Assets/UI/Prefabs/Splash_Fish.prefab, CreatureBar_Fish.prefab, CreatureIcon.prefab, Celebration_Fish.prefab,
    ///   GameOverPopup_Fish.prefab (moved out of the HUD prefab, so it is one asset you can open on its own)
    ///   Assets/Resources/UI/HUD_Fish.prefab: new HUD art + the game-over popup baked in (once: when it has no popup yet)
    /// Sprites drawn in code are saved as PNGs in Assets/UI/Sprites (kept if present, so replacements survive).
    /// Delete a prefab and run the menu again to get the default back.
    /// </summary>
    public static class UiPrefabBuilder
    {
        const string Dir = "Assets/UI/Prefabs", Art = "Assets/Art/UI/";
        const string IconPath = Dir + "/CreatureIcon.prefab", BarPath = Dir + "/CreatureBar_Fish.prefab",
                     SplashPath = Dir + "/Splash_Fish.prefab", CelebrationPath = Dir + "/Celebration_Fish.prefab",
                     GameOverPath = Dir + "/GameOverPopup_Fish.prefab";
        const string FishHudPath = "Assets/Resources/UI/HUD_Fish.prefab", FishThemePath = "Assets/Themes/BoardTheme_Fish.asset";

        [MenuItem("Tools/BlobMerge/Build UI Prefabs (creates missing ones only)")]
        public static void Build()
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontSetup.FontAssetPath);
            if (font == null) { Debug.LogWarning("[BlobMerge] UI prefabs: font not ready"); return; }
            TextStyle.Init(font); // texts reference the outline material asset
            Directory.CreateDirectory(Dir);

            var icon = Ensure<CreatureIconView>(IconPath, p => CreatureBarView.BuildDefaultIcon(p).gameObject);
            var bar = Ensure<CreatureBarView>(BarPath, p => CreatureBarView.BuildDefault(p, icon).gameObject);
            var splash = Ensure<SplashView>(SplashPath, p => SplashView.BuildDefault(p, Load(Art + "splash_bg.jpg"), Load(Art + "logo.png")).gameObject);
            var celebration = Ensure<LevelCompleteView>(CelebrationPath, p => LevelCompleteView.BuildDefault(p).gameObject);

            var theme = AssetDatabase.LoadAssetAtPath<BoardTheme>(FishThemePath);
            BakeFishHud(theme);                        // a brand new HUD gets the popup baked in...
            var gameOver = EnsureGameOverPopup();      // ...and it moves straight out into its own prefab
            if (theme != null)
            {
                if (theme.splashPrefab == null) theme.splashPrefab = splash;
                if (theme.creatureBarPrefab == null) theme.creatureBarPrefab = bar;
                if (theme.celebrationPrefab == null) theme.celebrationPrefab = celebration;
                if (theme.gameOverPrefab == null) theme.gameOverPrefab = gameOver;
                EditorUtility.SetDirty(theme);
            }
            AssetDatabase.SaveAssets();
        }

        static Sprite Load(string path) => AssetDatabase.LoadAssetAtPath<Sprite>(path);

        /// <summary>Loads the prefab, or builds it (under a temporary canvas), saves its code-drawn sprites and saves it.</summary>
        static T Ensure<T>(string path, System.Func<RectTransform, GameObject> build) where T : Component
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;
            var temp = new GameObject("TempCanvas", typeof(RectTransform), typeof(Canvas));
            try
            {
                var go = build((RectTransform)temp.transform);
                go.SetActive(true); // templates are deactivated by the code that uses them
                PersistSprites(go);
                var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
                Debug.Log("[BlobMerge] Created " + path + " — open it to edit.");
                return prefab.GetComponent<T>();
            }
            finally { Object.DestroyImmediate(temp); }
        }

        /// <summary>Every sprite reference to a code-drawn (non-asset) sprite becomes a PNG asset in Assets/UI/Sprites.</summary>
        static void PersistSprites(GameObject go)
        {
            foreach (var c in go.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                var so = new SerializedObject(c);
                var it = so.GetIterator();
                bool changed = false;
                while (it.Next(true))
                {
                    if (it.propertyType != SerializedPropertyType.ObjectReference || !(it.objectReferenceValue is Sprite sp)) continue;
                    if (AssetDatabase.Contains(sp)) continue;
                    if (string.IsNullOrEmpty(sp.name)) { Debug.LogWarning($"[BlobMerge] unnamed code sprite on {c.name}: not saved"); continue; }
                    it.objectReferenceValue = HudPrefabBuilder.SaveSprite(sp, sp.name, sp.border);
                    changed = true;
                }
                if (changed) so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        /// <summary>GameOverPopup_Fish.prefab: the popup moved out of HUD_Fish (your edits come with it) or, failing that,
        /// the code default. The HUD keeps no copy, so there is one asset to open and the theme points at it.</summary>
        static GameOverPopup EnsureGameOverPopup()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameOverPopup>(GameOverPath);
            if (existing != null) return existing;
            if (File.Exists(FishHudPath))
            {
                var root = PrefabUtility.LoadPrefabContents(FishHudPath);
                try
                {
                    var baked = root.GetComponentInChildren<GameOverPopup>(true);
                    if (baked != null)
                    {
                        baked.gameObject.SetActive(true); // Install() hides it; an inactive prefab root never shows
                        var saved = PrefabUtility.SaveAsPrefabAsset(baked.gameObject, GameOverPath);
                        var hud = root.GetComponent<HudView>();
                        if (hud != null) { hud.gameOverPanel = null; hud.gameOverText = null; hud.playAgainButton = null; }
                        Object.DestroyImmediate(baked.gameObject);
                        PrefabUtility.SaveAsPrefabAsset(root, FishHudPath);
                        Debug.Log("[BlobMerge] Moved the game-over popup out of " + FishHudPath + " into " + GameOverPath);
                        return saved.GetComponent<GameOverPopup>();
                    }
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            return Ensure<GameOverPopup>(GameOverPath, p => GameOverPopup.BuildDefault(p,
                Load(Art + "panel_popup.png"), Load(Art + "button_green.png"), Load(Art + "icon_crown.png")).gameObject);
        }

        /// <summary>HUD_Fish: Fish Fantasy art on the score panel / crown / settings button, best score on one line, and
        /// the game-over popup (replaces the old card). Skipped once the HUD has been baked (your edits stay) — the popup
        /// having moved out to its own prefab counts as baked, or it would come back as a second copy.</summary>
        static void BakeFishHud(BoardTheme theme)
        {
            if (!File.Exists(FishHudPath)) return;
            if (theme != null && theme.gameOverPrefab != null) return;
            if (AssetDatabase.LoadAssetAtPath<GameOverPopup>(GameOverPath) != null) return;
            var root = PrefabUtility.LoadPrefabContents(FishHudPath);
            try
            {
                if (root.GetComponentInChildren<GameOverPopup>(true) != null) return;
                var hud = root.GetComponent<HudView>();
                var plate = Load(Art + "score_plate.png");
                if (plate != null && Find(root, "ScorePanel") is RectTransform panel)
                {
                    SetArt(panel, plate);
                    panel.sizeDelta = new Vector2(400f, 400f * plate.rect.height / plate.rect.width);
                    if (Find(panel.gameObject, "Tab") is RectTransform tab)
                    {
                        tab.GetComponent<Image>().enabled = false; // the plate has its own tab; the SCORE label stays
                        tab.anchoredPosition = new Vector2(0f, -4f);
                    }
                    if (hud.scoreText != null) hud.scoreText.rectTransform.anchoredPosition = new Vector2(0f, -12f);
                }
                var crown = Load(Art + "icon_crown.png");
                if (crown != null && Find(root, "Crown") is RectTransform crownRt)
                {
                    SetArt(crownRt, crown);
                    crownRt.sizeDelta = new Vector2(64f, 64f);
                }
                var gear = Load(Art + "btn_settings.png");
                if (gear != null && Find(root, "SettingsButton") is RectTransform gearRt)
                {
                    SetArt(gearRt, gear);
                    gearRt.sizeDelta = new Vector2(100f, 100f);
                    if (Find(gearRt.gameObject, "Gear") is RectTransform icon) icon.gameObject.SetActive(false);
                }
                if (hud.bestText != null) hud.bestText.enableWordWrapping = false; // 5-digit bests wrapped to "4453 / 0"

                if (hud.gameOverPanel != null) Object.DestroyImmediate(hud.gameOverPanel);
                GameOverPopup.BuildInto(hud, Load(Art + "panel_popup.png"), Load(Art + "button_green.png"), crown);
                PrefabUtility.SaveAsPrefabAsset(root, FishHudPath);
                Debug.Log("[BlobMerge] Baked the Fish Fantasy HUD art + game-over popup into " + FishHudPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        static void SetArt(RectTransform rt, Sprite sprite)
        {
            var img = rt.GetComponent<Image>();
            if (img == null) return;
            img.sprite = sprite;
            img.type = Image.Type.Simple;
            img.preserveAspect = true;
            img.color = Color.white;
            foreach (var s in rt.GetComponents<Shadow>()) Object.DestroyImmediate(s, true);
        }

        static RectTransform Find(GameObject root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<RectTransform>(true))
                if (t.name == name) return t;
            return null;
        }
    }
}
