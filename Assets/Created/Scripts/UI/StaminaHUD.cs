using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Delver.Game;
using Delver.Movement;

namespace Delver.UI
{
    /// <summary>
    /// The stamina bar, bottom centre.
    ///
    /// Fades out when you are rested and full, so the HUD is empty while nothing is happening -
    /// the dungeon should be the thing you are looking at. It reappears the moment sprinting
    /// costs you something, and turns red while sprint is locked out.
    /// </summary>
    [DisallowMultipleComponent]
    public class StaminaHUD : MonoBehaviour
    {
        [SerializeField] private float width = 280f;
        [SerializeField] private float height = 8f;
        [SerializeField] private float bottomMargin = 64f;

        [Tooltip("How quickly the bar fades in and out.")]
        [SerializeField] private float fadeSpeed = 5f;

        private CanvasGroup _group;
        private RectTransform _fill;
        private Image _fillImage;
        private TextMeshProUGUI _lockLabel;

        private DelverCharacterController _character;
        private float _displayed = 1f;

        private static readonly Color Rested = new Color32(0x9C, 0xC7, 0xA1, 0xFF);
        private static readonly Color Spent = new Color32(0xE0, 0x8A, 0x46, 0xFF);
        private static readonly Color Locked = new Color32(0xDC, 0x7A, 0x74, 0xFF);

        private void Awake()
        {
            BuildUI();
        }

        private void BuildUI()
        {
            var canvas = UIKit.CreateOverlayCanvas("StaminaHUD", 300, transform);
            _group = canvas.gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;

            var root = UIKit.Rect("Bar", canvas.transform);
            UIKit.Anchor(root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, bottomMargin), new Vector2(width, height));

            var back = UIKit.Block("Track", root, new Color(0.06f, 0.07f, 0.065f, 0.85f));
            UIKit.Stretch(back.rectTransform, 0, 0, 0, 0);

            _fillImage = UIKit.Block("Fill", root, Rested);
            _fill = _fillImage.rectTransform;
            _fill.anchorMin = new Vector2(0f, 0f);
            _fill.anchorMax = new Vector2(0f, 1f);
            _fill.pivot = new Vector2(0f, 0.5f);
            _fill.anchoredPosition = Vector2.zero;
            _fill.sizeDelta = new Vector2(width, 0f);

            _lockLabel = UIKit.Text("Winded", root, "WINDED", 13f, Locked, FontStyles.Bold,
                TextAlignmentOptions.Center);
            _lockLabel.characterSpacing = 10f;
            UIKit.Anchor(_lockLabel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0f),
                new Vector2(0f, 6f), new Vector2(width, 18f));
            _lockLabel.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (_character == null || !_character.isActiveAndEnabled)
            {
                var local = DelverIdentity.Local;
                if (local != null) _character = local.GetComponentInChildren<DelverCharacterController>();
                if (_character == null) { Fade(0f); return; }
            }

            float value = Mathf.Clamp01(_character.StaminaNormalised);

            // Smooth so a burst of sprinting reads as drain rather than a jitter.
            _displayed = Mathf.MoveTowards(_displayed, value, Time.deltaTime * 2.5f);
            _fill.sizeDelta = new Vector2(width * _displayed, 0f);

            bool winded = value <= 0.02f;
            bool busy = value < 0.995f;

            _fillImage.color = winded ? Locked : (_character.IsSprinting ? Spent : Rested);
            _lockLabel.gameObject.SetActive(winded);

            // Hidden while rested and full: nothing to say, so say nothing.
            Fade(busy ? 1f : 0f);
        }

        private void Fade(float target)
        {
            if (_group == null) return;
            _group.alpha = Mathf.MoveTowards(_group.alpha, target, Time.deltaTime * fadeSpeed);
        }
    }
}
