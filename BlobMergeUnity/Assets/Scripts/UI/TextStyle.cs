using TMPro;
using UnityEngine;

namespace BlobMerge
{
    /// <summary>
    /// One shared TextMeshPro look for every text in the game (HUD, blob numbers, floating scores):
    /// Barlow Bold with a thin dark-blue outline. The look lives in ONE material asset:
    ///   Assets/Resources/Fonts/Barlow-Bold Outline.mat
    /// Edit its Outline colour/thickness (and Face colour/dilate) to restyle all text at once.
    /// </summary>
    public static class TextStyle
    {
        public const string FontResource = "Fonts/Barlow-Bold SDF";
        public const string MaterialResource = "Fonts/Barlow-Bold Outline";

        // defaults used when the material asset doesn't exist yet
        public static readonly Color DefaultOutlineColor = new Color(0.09f, 0.10f, 0.33f, 1f);
        public const float DefaultOutlineWidth = 0.18f;

        public static TMP_FontAsset Font { get; private set; }
        public static Material Material { get; private set; }
        public static FontStyles Style { get; private set; } = FontStyles.Normal;
        public static Color Face { get; private set; } = Color.white;

        /// <summary>
        /// Take the look of an existing text (font, material, face colour, Bold/Italic/…) — used to make blob numbers and
        /// floating scores match the HUD prefab's styled text.
        /// </summary>
        public static void CopyFrom(TMP_Text source)
        {
            if (source == null) return;
            if (source.font != null) Font = source.font;
            if (source.fontSharedMaterial != null) Material = source.fontSharedMaterial;
            Style = source.fontStyle;
            Face = source.color;
        }

        public static void Init(TMP_FontAsset font = null)
        {
            Font = font != null ? font : Resources.Load<TMP_FontAsset>(FontResource);
            if (Font == null) Font = TMP_Settings.defaultFontAsset;
            if (Font == null) { Debug.LogWarning("[BlobMerge] No TMP font asset found."); return; }

            Material = Resources.Load<Material>(MaterialResource);
            if (Material == null)
            {
                Material = new Material(Font.material) { name = "Text Outline (runtime default)" };
                ApplyOutline(Material, DefaultOutlineColor, DefaultOutlineWidth);
            }
        }

        public static void ApplyOutline(Material m, Color color, float width)
        {
            m.EnableKeyword(ShaderUtilities.Keyword_Outline);
            m.SetColor(ShaderUtilities.ID_OutlineColor, color);
            m.SetFloat(ShaderUtilities.ID_OutlineWidth, width);
            m.SetFloat(ShaderUtilities.ID_FaceDilate, width * 0.5f); // keep the white face from thinning
            ShaderUtilities.UpdateShaderRatios(m);
        }

        public static void Apply(TMP_Text t, Color? face = null)
        {
            if (Font == null) Init();
            if (Font != null) t.font = Font;
            if (Material != null) t.fontSharedMaterial = Material;
            t.color = face ?? Face;
            t.fontStyle = Style;
            t.richText = true;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
        }
    }
}
