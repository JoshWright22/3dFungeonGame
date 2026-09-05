using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Delver.Game;

namespace Delver.UI
{
    /// <summary>
    /// Hold Tab to see the party's character sheets: your own in full, everyone else's beside it.
    ///
    /// Pillar I of the design is that the dice are public, so this deliberately shows the whole
    /// party's numbers rather than only your own - knowing your cleric rolled a 6 for Strength
    /// is information the party is supposed to have.
    /// </summary>
    [DisallowMultipleComponent]
    public class CharacterSheetHUD : MonoBehaviour
    {
        [SerializeField] private KeyCode toggleKey = KeyCode.Tab;

        [Tooltip("Hold to view (Lethal Company style) rather than press to latch open.")]
        [SerializeField] private bool holdToView = true;

        private Canvas _canvas;
        private CanvasGroup _group;
        private RectTransform _selfPanel;
        private RectTransform _partyColumn;

        private TextMeshProUGUI _selfName, _selfHp, _selfHands;
        private readonly Dictionary<string, TextMeshProUGUI> _selfAbilities = new Dictionary<string, TextMeshProUGUI>();
        private readonly Dictionary<string, TextMeshProUGUI> _selfMods = new Dictionary<string, TextMeshProUGUI>();
        private readonly List<PartyRow> _rows = new List<PartyRow>();

        private bool _visible;
        private static readonly string[] AbilityOrder = { "STR", "DEX", "INT" };

        private class PartyRow
        {
            public RectTransform Root;
            public TextMeshProUGUI Name;
            public TextMeshProUGUI Line;
            public Image ReadyPip;
        }

        private void Awake()
        {
            BuildUI();
            SetVisible(false, instant: true);
        }

        private void OnEnable() => DelverIdentity.RosterChanged += Refresh;
        private void OnDisable() => DelverIdentity.RosterChanged -= Refresh;

        private void Update()
        {
            bool want = holdToView ? Input.GetKey(toggleKey) : _visible;

            if (!holdToView && Input.GetKeyDown(toggleKey))
                want = !_visible;

            if (want != _visible) SetVisible(want, instant: false);

            // Health and hands change during a run, so keep refreshing while the sheet is open.
            if (_visible) Refresh();
        }

        private void SetVisible(bool visible, bool instant)
        {
            _visible = visible;
            if (_group == null) return;

            _group.alpha = visible ? 1f : 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;

            if (visible) Refresh();
        }

        // ------------------------------------------------------------------ construction

        private void BuildUI()
        {
            _canvas = UIKit.CreateOverlayCanvas("CharacterSheetHUD", 500, transform);
            _group = _canvas.gameObject.AddComponent<CanvasGroup>();

            var scrim = UIKit.Block("Scrim", _canvas.transform, new Color(0.04f, 0.05f, 0.045f, 0.55f));
            UIKit.Stretch(scrim.rectTransform, 0, 0, 0, 0);

            // --- own sheet, left ---
            _selfPanel = UIKit.Rect("SelfSheet", _canvas.transform);
            UIKit.Anchor(_selfPanel, new Vector2(0.5f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-24f, 0f), new Vector2(560f, 620f));

            var selfBg = UIKit.Block("Bg", _selfPanel, UIKit.Panel);
            UIKit.Stretch(selfBg.rectTransform, 0, 0, 0, 0);
            UIKit.TopRule(_selfPanel, UIKit.Torch, 3f);

            var head = UIKit.Rect("Head", _selfPanel);
            UIKit.Anchor(head, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -34f), new Vector2(496f, 96f));

            UIKit.Label("Eyebrow", head, "Character sheet", UIKit.Muted).rectTransform
                .SetInsetAndSizeFromParentEdge(RectTransform.Edge.Top, 0f, 20f);

            _selfName = UIKit.Text("Name", head, "-", 44f, UIKit.Ink, FontStyles.Bold);
            _selfName.rectTransform.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Top, 26f, 56f);
            _selfName.characterSpacing = 2f;

            // Ability row - three big blocks, the way a sheet actually reads.
            var abilities = UIKit.Rect("Abilities", _selfPanel);
            UIKit.Anchor(abilities, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -150f), new Vector2(496f, 150f));

            float cardW = (496f - 24f) / 3f;
            for (int i = 0; i < AbilityOrder.Length; i++)
            {
                string key = AbilityOrder[i];

                var card = UIKit.Rect("Ability_" + key, abilities);
                UIKit.Anchor(card, new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(i * (cardW + 12f), 0f), new Vector2(cardW, 150f));

                var cardBg = UIKit.Block("Bg", card, UIKit.PanelSunk);
                UIKit.Stretch(cardBg.rectTransform, 0, 0, 0, 0);

                var lbl = UIKit.Label("Key", card, key, UIKit.Muted);
                lbl.alignment = TextAlignmentOptions.Top;
                lbl.rectTransform.anchorMin = new Vector2(0f, 1f);
                lbl.rectTransform.anchorMax = new Vector2(1f, 1f);
                lbl.rectTransform.pivot = new Vector2(0.5f, 1f);
                lbl.rectTransform.sizeDelta = new Vector2(0f, 22f);
                lbl.rectTransform.anchoredPosition = new Vector2(0f, -14f);

                var score = UIKit.Text("Score", card, "-", 58f, UIKit.Ink, FontStyles.Bold, TextAlignmentOptions.Center);
                score.rectTransform.anchorMin = new Vector2(0f, 1f);
                score.rectTransform.anchorMax = new Vector2(1f, 1f);
                score.rectTransform.pivot = new Vector2(0.5f, 1f);
                score.rectTransform.sizeDelta = new Vector2(0f, 66f);
                score.rectTransform.anchoredPosition = new Vector2(0f, -38f);
                _selfAbilities[key] = score;

                var mod = UIKit.Text("Mod", card, "-", 22f, UIKit.Torch, FontStyles.Bold, TextAlignmentOptions.Center);
                mod.rectTransform.anchorMin = new Vector2(0f, 1f);
                mod.rectTransform.anchorMax = new Vector2(1f, 1f);
                mod.rectTransform.pivot = new Vector2(0.5f, 1f);
                mod.rectTransform.sizeDelta = new Vector2(0f, 28f);
                mod.rectTransform.anchoredPosition = new Vector2(0f, -104f);
                _selfMods[key] = mod;
            }

            // Vitals
            var vitals = UIKit.Rect("Vitals", _selfPanel);
            UIKit.Anchor(vitals, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -320f), new Vector2(496f, 130f));

            UIKit.Label("HpLabel", vitals, "Hit points", UIKit.Muted).rectTransform
                .SetInsetAndSizeFromParentEdge(RectTransform.Edge.Top, 0f, 20f);

            _selfHp = UIKit.Text("Hp", vitals, "-", 34f, UIKit.Ink, FontStyles.Bold);
            _selfHp.rectTransform.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Top, 24f, 44f);

            UIKit.Label("HandsLabel", vitals, "Hands", UIKit.Muted).rectTransform
                .SetInsetAndSizeFromParentEdge(RectTransform.Edge.Top, 74f, 20f);

            _selfHands = UIKit.Text("Hands", vitals, "Empty / Empty", 22f, UIKit.InkSoft);
            _selfHands.rectTransform.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Top, 98f, 30f);

            var foot = UIKit.Text("Foot", _selfPanel,
                "Rolled at spawn. 3d6 per ability, HP = best physical + 1d4. No rerolls.",
                16f, UIKit.Muted, FontStyles.Italic);
            foot.rectTransform.anchorMin = new Vector2(0f, 0f);
            foot.rectTransform.anchorMax = new Vector2(1f, 0f);
            foot.rectTransform.pivot = new Vector2(0.5f, 0f);
            foot.rectTransform.offsetMin = new Vector2(32f, 24f);
            foot.rectTransform.offsetMax = new Vector2(-32f, 60f);

            // --- party column, right ---
            _partyColumn = UIKit.Rect("Party", _canvas.transform);
            UIKit.Anchor(_partyColumn, new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(24f, 0f), new Vector2(460f, 620f));

            var partyBg = UIKit.Block("Bg", _partyColumn, UIKit.Panel);
            UIKit.Stretch(partyBg.rectTransform, 0, 0, 0, 0);
            UIKit.TopRule(_partyColumn, UIKit.Arcane, 3f);

            var partyLabel = UIKit.Label("PartyLabel", _partyColumn, "The party", UIKit.Muted);
            partyLabel.rectTransform.anchorMin = new Vector2(0f, 1f);
            partyLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
            partyLabel.rectTransform.pivot = new Vector2(0.5f, 1f);
            partyLabel.rectTransform.offsetMin = new Vector2(28f, 0f);
            partyLabel.rectTransform.offsetMax = new Vector2(-28f, 0f);
            partyLabel.rectTransform.sizeDelta = new Vector2(partyLabel.rectTransform.sizeDelta.x, 22f);
            partyLabel.rectTransform.anchoredPosition = new Vector2(0f, -34f);

            var list = UIKit.Rect("List", _partyColumn);
            list.anchorMin = new Vector2(0f, 0f);
            list.anchorMax = new Vector2(1f, 1f);
            list.pivot = new Vector2(0.5f, 1f);
            list.offsetMin = new Vector2(28f, 24f);
            list.offsetMax = new Vector2(-28f, -70f);
            UIKit.VerticalList(list, 10f);

            _partyColumn.gameObject.name = "Party";
            _partyRowParent = list;
        }

        private RectTransform _partyRowParent;

        // ------------------------------------------------------------------ refresh

        private void Refresh()
        {
            if (_canvas == null) return;

            var local = DelverIdentity.Local;
            var stats = local != null ? local.Stats : null;

            if (local != null) _selfName.text = local.DisplayName;

            if (stats != null && stats.IsSpawned)
            {
                SetAbility("STR", stats.maxStr);
                SetAbility("DEX", stats.maxDex);
                SetAbility("INT", stats.maxInt);

                _selfHp.text = stats.currentHP + " / " + stats.maxHp;
                _selfHp.color = stats.maxHp > 0 && stats.currentHP <= stats.maxHp * 0.34f
                    ? new Color32(0xDC, 0x7A, 0x74, 0xFF)
                    : UIKit.Ink;

                _selfHands.text = DescribeHands(stats);
            }
            else
            {
                SetAbility("STR", -1);
                SetAbility("DEX", -1);
                SetAbility("INT", -1);
                _selfHp.text = "-";
            }

            RefreshParty(local);
        }

        private void SetAbility(string key, int score)
        {
            if (!_selfAbilities.TryGetValue(key, out var scoreText)) return;

            if (score < 0)
            {
                scoreText.text = "-";
                _selfMods[key].text = "";
                return;
            }

            scoreText.text = score.ToString();
            _selfMods[key].text = UIKit.SignedModifier(score);

            // A genuinely bad roll should look bad on the sheet.
            scoreText.color = score <= 7 ? new Color32(0xDC, 0x7A, 0x74, 0xFF)
                            : score >= 16 ? UIKit.Torch
                            : UIKit.Ink;
        }

        private static string DescribeHands(PlayerStats stats)
        {
            string right = stats.currentRightHand != null && !string.IsNullOrEmpty(stats.currentRightHand.nameText)
                ? stats.currentRightHand.nameText : "Empty";
            string left = stats.currentLeftHand != null && !string.IsNullOrEmpty(stats.currentLeftHand.nameText)
                ? stats.currentLeftHand.nameText : "Empty";
            return right + "  /  " + left;
        }

        private void RefreshParty(DelverIdentity local)
        {
            var roster = DelverIdentity.All;

            // Grow the row pool to match the party. Rows are never destroyed, only hidden -
            // a delver disconnecting mid-run should not cost a layout rebuild.
            while (_rows.Count < roster.Count)
                _rows.Add(CreatePartyRow());

            int shown = 0;
            for (int i = 0; i < roster.Count; i++)
            {
                var d = roster[i];
                if (d == null) continue;

                var row = _rows[shown];
                row.Root.gameObject.SetActive(true);

                bool isSelf = d == local;
                row.Name.text = d.DisplayName + (isSelf ? "  (you)" : "");
                row.Name.color = isSelf ? UIKit.Torch : UIKit.Ink;

                var s = d.Stats;
                if (s != null && s.IsSpawned)
                {
                    row.Line.text = string.Format(
                        "STR {0,2}   DEX {1,2}   INT {2,2}      HP {3}/{4}",
                        s.maxStr, s.maxDex, s.maxInt, s.currentHP, s.maxHp);
                }
                else
                {
                    row.Line.text = "rolling...";
                }

                row.ReadyPip.color = d.IsReady ? UIKit.Ready : UIKit.Rule;
                shown++;
            }

            for (int i = shown; i < _rows.Count; i++)
                _rows[i].Root.gameObject.SetActive(false);
        }

        private PartyRow CreatePartyRow()
        {
            var root = UIKit.Rect("PartyRow", _partyRowParent);
            UIKit.FixedHeight(root.gameObject, 74f);

            var bg = UIKit.Block("Bg", root, UIKit.PanelSunk);
            UIKit.Stretch(bg.rectTransform, 0, 0, 0, 0);

            var pip = UIKit.Block("ReadyPip", root, UIKit.Rule);
            pip.rectTransform.anchorMin = new Vector2(0f, 0f);
            pip.rectTransform.anchorMax = new Vector2(0f, 1f);
            pip.rectTransform.pivot = new Vector2(0f, 0.5f);
            pip.rectTransform.sizeDelta = new Vector2(4f, 0f);
            pip.rectTransform.anchoredPosition = Vector2.zero;

            var name = UIKit.Text("Name", root, "-", 24f, UIKit.Ink, FontStyles.Bold);
            name.rectTransform.anchorMin = new Vector2(0f, 1f);
            name.rectTransform.anchorMax = new Vector2(1f, 1f);
            name.rectTransform.pivot = new Vector2(0.5f, 1f);
            name.rectTransform.offsetMin = new Vector2(18f, 0f);
            name.rectTransform.offsetMax = new Vector2(-14f, 0f);
            name.rectTransform.sizeDelta = new Vector2(name.rectTransform.sizeDelta.x, 30f);
            name.rectTransform.anchoredPosition = new Vector2(0f, -12f);

            var line = UIKit.Text("Line", root, "-", 18f, UIKit.InkSoft);
            line.rectTransform.anchorMin = new Vector2(0f, 1f);
            line.rectTransform.anchorMax = new Vector2(1f, 1f);
            line.rectTransform.pivot = new Vector2(0.5f, 1f);
            line.rectTransform.offsetMin = new Vector2(18f, 0f);
            line.rectTransform.offsetMax = new Vector2(-14f, 0f);
            line.rectTransform.sizeDelta = new Vector2(line.rectTransform.sizeDelta.x, 26f);
            line.rectTransform.anchoredPosition = new Vector2(0f, -42f);

            return new PartyRow { Root = root, Name = name, Line = line, ReadyPip = pip };
        }
    }
}
