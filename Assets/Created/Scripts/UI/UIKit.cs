using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Delver.UI
{
    /// <summary>
    /// Builders for the runtime UI. Every screen in the game is constructed from code rather
    /// than authored as a prefab, so there is nothing to re-wire when a script changes and no
    /// prefab to merge-conflict over.
    /// </summary>
    public static class UIKit
    {
        // Palette. Kept here so the lobby and the character sheet cannot drift apart.
        public static readonly Color Ink       = new Color32(0xE3, 0xE8, 0xE3, 0xFF);
        public static readonly Color InkSoft   = new Color32(0xBC, 0xC4, 0xBD, 0xFF);
        public static readonly Color Muted     = new Color32(0x8B, 0x96, 0x8D, 0xFF);
        public static readonly Color Torch     = new Color32(0xE0, 0x8A, 0x46, 0xFF);
        public static readonly Color Arcane    = new Color32(0x78, 0xAE, 0xC3, 0xFF);
        public static readonly Color Panel     = new Color32(0x17, 0x1B, 0x19, 0xF2);
        public static readonly Color PanelSunk = new Color32(0x10, 0x14, 0x13, 0xFF);
        public static readonly Color Rule      = new Color32(0x2B, 0x32, 0x2D, 0xFF);
        public static readonly Color Scrim     = new Color32(0x0A, 0x0C, 0x0B, 0xC4);
        public static readonly Color Ready     = new Color32(0x71, 0xA8, 0x76, 0xFF);

        private static TMP_FontAsset _font;

        /// <summary>TMP's default font, resolved once. Null is survivable - TMP falls back itself.</summary>
        public static TMP_FontAsset Font
        {
            get
            {
                if (_font == null)
                {
                    _font = TMP_Settings.defaultFontAsset;
                    if (_font == null)
                        _font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
                }
                return _font;
            }
        }

        /// <summary>A full-screen overlay canvas.</summary>
        public static Canvas CreateOverlayCanvas(string name, int sortOrder, Transform parent = null)
        {
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            if (parent != null) go.transform.SetParent(parent, false);

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortOrder;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            return canvas;
        }

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>A solid block of colour. Panels, rules and fills alike.</summary>
        public static Image Block(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        public static TextMeshProUGUI Text(string name, Transform parent, string content,
            float size, Color color, FontStyles style = FontStyles.Normal,
            TextAlignmentOptions align = TextAlignmentOptions.TopLeft)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);

            var t = go.GetComponent<TextMeshProUGUI>();
            if (Font != null) t.font = Font;
            t.text = content;
            t.fontSize = size;
            t.color = color;
            t.fontStyle = style;
            t.alignment = align;
            t.raycastTarget = false;
            t.overflowMode = TextOverflowModes.Overflow;
            return t;
        }

        /// <summary>An uppercase, letter-spaced caption. The game's label voice.</summary>
        public static TextMeshProUGUI Label(string name, Transform parent, string content, Color color)
        {
            var t = Text(name, parent, content.ToUpperInvariant(), 15f, color, FontStyles.Bold);
            t.characterSpacing = 12f;
            return t;
        }

        public static Button TextButton(string name, Transform parent, string content, Color accent, out TextMeshProUGUI label)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            var img = go.GetComponent<Image>();
            img.color = PanelSunk;

            var btn = go.GetComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.35f, 1.35f, 1.35f, 1f);
            colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
            colors.selectedColor = Color.white;
            colors.fadeDuration = 0.08f;
            btn.colors = colors;

            var stripe = Block("Accent", go.transform, accent);
            var srt = stripe.rectTransform;
            srt.anchorMin = new Vector2(0f, 0f);
            srt.anchorMax = new Vector2(0f, 1f);
            srt.pivot = new Vector2(0f, 0.5f);
            srt.sizeDelta = new Vector2(3f, 0f);
            srt.anchoredPosition = Vector2.zero;

            label = Text("Label", go.transform, content.ToUpperInvariant(), 20f, Ink,
                FontStyles.Bold, TextAlignmentOptions.Center);
            label.characterSpacing = 8f;
            Stretch(label.rectTransform, 14, 0, 8, 0);

            return btn;
        }

        /// <summary>Anchors a rect to fill its parent, inset by the given padding.</summary>
        public static void Stretch(RectTransform rt, float left, float top, float right, float bottom)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        /// <summary>Places a fixed-size rect against an anchor point.</summary>
        public static void Anchor(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 offset, Vector2 size)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = offset;
            rt.sizeDelta = size;
        }

        /// <summary>A horizontal rule pinned to the top of its parent.</summary>
        public static Image TopRule(Transform parent, Color color, float thickness = 1f)
        {
            var img = Block("Rule", parent, color);
            var rt = img.rectTransform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, thickness);
            rt.anchoredPosition = Vector2.zero;
            return img;
        }

        public static VerticalLayoutGroup VerticalList(RectTransform rt, float spacing, RectOffset padding = null)
        {
            var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = padding ?? new RectOffset(0, 0, 0, 0);
            v.childControlWidth = true;
            v.childControlHeight = false;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            v.childAlignment = TextAnchor.UpperLeft;
            return v;
        }

        public static LayoutElement FixedHeight(GameObject go, float height)
        {
            var le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;
            return le;
        }

        /// <summary>Ability modifier in the D&amp;D sense: (score - 10) / 2, rounded down.</summary>
        public static int Modifier(int score)
        {
            return Mathf.FloorToInt((score - 10) / 2f);
        }

        public static string SignedModifier(int score)
        {
            int m = Modifier(score);
            return m >= 0 ? "+" + m : m.ToString();
        }
    }
}
