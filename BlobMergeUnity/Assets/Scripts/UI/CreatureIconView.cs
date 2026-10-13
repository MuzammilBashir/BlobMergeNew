using UnityEngine;
using UnityEngine.UI;

namespace BlobMerge
{
    /// <summary>
    /// One creature-bar icon (CreatureIcon.prefab): outline circle on the root, then ring, fill and the fish. Edit the
    /// prefab freely; the bar recolours ring/fill/fish per state (colours on the bar prefab) and swaps the fish sprite.
    /// </summary>
    public class CreatureIconView : MonoBehaviour
    {
        public Image edge, ring, fill, fish;

        public RectTransform Rect => (RectTransform)transform;
    }
}
