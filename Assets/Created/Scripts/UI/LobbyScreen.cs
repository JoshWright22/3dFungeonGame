using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using Delver.Game;

namespace Delver.UI
{
    /// <summary>
    /// The muster screen. Shows who is in the party, what they rolled, and whether they are
    /// ready; the host gets the button that starts the descent.
    ///
    /// Built at runtime like every other screen, and driven entirely off
    /// <see cref="DelverIdentity"/>, so it works the same on the host and on a late joiner.
    /// </summary>
    [DisallowMultipleComponent]
    public class LobbyScreen : MonoBehaviour
    {
        [SerializeField] private KeyCode readyKey = KeyCode.R;

        private Canvas _canvas;
        private CanvasGroup _group;
        private RectTransform _rowParent;
        private TextMeshProUGUI _statusText, _seedText, _joinText, _beginLabel, _readyHint;
        private Button _beginButton, _readyButton;
        private TextMeshProUGUI _readyLabel;

        private readonly List<Row> _rows = new List<Row>();
        private bool _visible = true;

        private class Row
        {
            public RectTransform Root;
            public Image Pip;
            public TextMeshProUGUI Name;
            public TextMeshProUGUI Stats;
            public TextMeshProUGUI State;
        }

        /// <summary>Raised when the host commits to starting the run.</summary>
        public static event System.Action DescentRequested;

        private void Awake()
        {
            BuildUI();
        }

        private void OnEnable() => DelverIdentity.RosterChanged += Refresh;
        private void OnDisable() => DelverIdentity.RosterChanged -= Refresh;

        private void Start() => Refresh();

        private void Update()
        {
            if (!_visible) return;

            if (Input.GetKeyDown(readyKey)) ToggleLocalReady();

            Refresh();
        }

        /// <summary>Shows or hides the whole screen, and takes the cursor with it.</summary>
        public void SetVisible(bool visible)
        {
            _visible = visible;
            if (_group == null) return;

            _group.alpha = visible ? 1f : 0f;
            _group.blocksRaycasts = visible;
            _group.interactable = visible;

            Cursor.lockState = visible ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = visible;
        }

        private void ToggleLocalReady()
        {
            var local = DelverIdentity.Local;
            if (local != null) local.ToggleReady();
        }

        // ------------------------------------------------------------------ construction

        private void BuildUI()
        {
            _canvas = UIKit.CreateOverlayCanvas("LobbyScreen", 400, transform);
            _group = _canvas.gameObject.AddComponent<CanvasGroup>();

            var scrim = UIKit.Block("Scrim", _canvas.transform, UIKit.Scrim);
            UIKit.Stretch(scrim.rectTransform, 0, 0, 0, 0);
            scrim.raycastTarget = true;

            var panel = UIKit.Rect("Panel", _canvas.transform);
            UIKit.Anchor(panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(980f, 700f));

            var bg = UIKit.Block("Bg", panel, UIKit.Panel);
            UIKit.Stretch(bg.rectTransform, 0, 0, 0, 0);
            bg.raycastTarget = true;
            UIKit.TopRule(panel, UIKit.Torch, 4f);

            // --- header ---
            UIKit.Label("Eyebrow", panel, "Muster", UIKit.Muted).rectTransform
                .SetInsetAndSizeFromParentEdge(RectTransform.Edge.Top, 40f, 22f);

            var title = UIKit.Text("Title", panel, "THE PARTY", 56f, UIKit.Ink, FontStyles.Bold);
            title.characterSpacing = 4f;
            title.rectTransform.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Top, 66f, 66f);
            InsetSides(title.rectTransform, 48f, 48f);

            _statusText = UIKit.Text("Status", panel, "", 20f, UIKit.InkSoft);
            _statusText.rectTransform.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Top, 132f, 28f);
            InsetSides(_statusText.rectTransform, 48f, 48f);

            // --- roster ---
            var list = UIKit.Rect("Roster", panel);
            list.anchorMin = new Vector2(0f, 1f);
            list.anchorMax = new Vector2(1f, 1f);
            list.pivot = new Vector2(0.5f, 1f);
            list.offsetMin = new Vector2(48f, 0f);
            list.offsetMax = new Vector2(-48f, 0f);
            list.sizeDelta = new Vector2(list.sizeDelta.x, 360f);
            list.anchoredPosition = new Vector2(0f, -180f);
            UIKit.VerticalList(list, 10f);
            _rowParent = list;

            // --- footer: seed + join info on the left, buttons on the right ---
            var footer = UIKit.Rect("Footer", panel);
            footer.anchorMin = new Vector2(0f, 0f);
            footer.anchorMax = new Vector2(1f, 0f);
            footer.pivot = new Vector2(0.5f, 0f);
            footer.offsetMin = new Vector2(48f, 36f);
            footer.offsetMax = new Vector2(-48f, 0f);
            footer.sizeDelta = new Vector2(footer.sizeDelta.x, 132f);

            // Left column is width-capped so a long "share your SteamID" line can never run
            // underneath the buttons on the right.
            var info = UIKit.Rect("Info", footer);
            UIKit.Anchor(info, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(400f, 132f));

            UIKit.Label("SeedLabel", info, "Dungeon seed", UIKit.Muted).rectTransform
                .SetInsetAndSizeFromParentEdge(RectTransform.Edge.Top, 0f, 20f);

            _seedText = UIKit.Text("Seed", info, "-", 26f, UIKit.Arcane, FontStyles.Bold);
            _seedText.rectTransform.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Top, 24f, 34f);

            _joinText = UIKit.Text("Join", info, "", 16f, UIKit.Muted);
            _joinText.rectTransform.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Top, 62f, 52f);
            _joinText.textWrappingMode = TextWrappingModes.Normal;

            _readyButton = UIKit.TextButton("ReadyButton", footer, "Ready (R)", UIKit.Arcane, out _readyLabel);
            UIKit.Anchor(_readyButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-286f, 0f), new Vector2(260f, 62f));
            _readyButton.onClick.AddListener(ToggleLocalReady);

            _beginButton = UIKit.TextButton("BeginButton", footer, "Begin descent", UIKit.Torch, out _beginLabel);
            UIKit.Anchor(_beginButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-6f, 0f), new Vector2(268f, 62f));
            _beginButton.onClick.AddListener(OnBeginClicked);

            _readyHint = UIKit.Text("Hint", footer, "", 15f, UIKit.Muted, FontStyles.Italic,
                TextAlignmentOptions.TopRight);
            UIKit.Anchor(_readyHint.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-6f, -70f), new Vector2(530f, 44f));
            _readyHint.textWrappingMode = TextWrappingModes.Normal;

            SetVisible(true);
        }

        private static void InsetSides(RectTransform rt, float left, float right)
        {
            rt.anchorMin = new Vector2(0f, rt.anchorMin.y);
            rt.anchorMax = new Vector2(1f, rt.anchorMax.y);
            rt.offsetMin = new Vector2(left, rt.offsetMin.y);
            rt.offsetMax = new Vector2(-right, rt.offsetMax.y);
        }

        private void OnBeginClicked()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer) return;

            DescentRequested?.Invoke();
        }

        // ------------------------------------------------------------------ refresh

        private void Refresh()
        {
            if (_canvas == null) return;

            var nm = NetworkManager.Singleton;
            bool connected = nm != null && nm.IsListening;
            bool isHost = connected && nm.IsServer;

            int count = 0;
            var roster = DelverIdentity.All;
            for (int i = 0; i < roster.Count; i++) if (roster[i] != null) count++;

            _statusText.text = !connected
                ? "Not connected."
                : count == 1
                    ? "1 delver mustered. Waiting for the rest of the party."
                    : count + " delvers mustered.";

            var runner = DungeonRunner.Instance;
            _seedText.text = runner != null && runner.Seed != 0 ? runner.Seed.ToString() : "not yet drawn";

            _joinText.text = BuildJoinLine(isHost);

            RefreshRows(roster);

            // Ready button reflects the local delver's own state.
            var local = DelverIdentity.Local;
            bool localReady = local != null && local.IsReady;
            _readyLabel.text = localReady ? "READY" : "READY (R)";
            _readyLabel.color = localReady ? UIKit.Ready : UIKit.Ink;
            _readyButton.interactable = local != null;

            // Only the host can start, and only once everybody has said they are ready.
            bool everyone = DelverIdentity.EveryoneReady();
            _beginButton.gameObject.SetActive(isHost);
            _beginButton.interactable = isHost && everyone && count > 0;
            _beginLabel.color = _beginButton.interactable ? UIKit.Ink : UIKit.Muted;

            _readyHint.text = !isHost
                ? "The host starts the descent."
                : everyone ? "" : "Everyone must be ready before the descent can begin.";
        }

        private static string BuildJoinLine(bool isHost)
        {
#if !DISABLESTEAMWORKS
            try
            {
                if (Steamworks.SteamClient.IsValid)
                {
                    return isHost
                        ? "Invite through Steam, or share your SteamID: " + Steamworks.SteamClient.SteamId.Value
                        : "Connected through Steam.";
                }
            }
            catch (System.Exception)
            {
                // Steam is not running. Local/LAN testing - fall through.
            }
#endif
            return isHost ? "Hosting locally. Steam is not running." : "";
        }

        private void RefreshRows(IReadOnlyList<DelverIdentity> roster)
        {
            while (_rows.Count < roster.Count) _rows.Add(CreateRow());

            int shown = 0;
            for (int i = 0; i < roster.Count; i++)
            {
                var d = roster[i];
                if (d == null) continue;

                var row = _rows[shown];
                row.Root.gameObject.SetActive(true);

                bool isSelf = d.IsLocalDelver;
                row.Name.text = d.DisplayName + (isSelf ? "  (you)" : "");
                row.Name.color = isSelf ? UIKit.Torch : UIKit.Ink;

                var s = d.Stats;
                row.Stats.text = s != null && s.IsSpawned
                    ? string.Format("STR {0,2}  {1}      DEX {2,2}  {3}      INT {4,2}  {5}      HP {6}",
                        s.maxStr, UIKit.SignedModifier(s.maxStr),
                        s.maxDex, UIKit.SignedModifier(s.maxDex),
                        s.maxInt, UIKit.SignedModifier(s.maxInt),
                        s.maxHp)
                    : "rolling...";

                row.State.text = d.IsReady ? "READY" : "WAITING";
                row.State.color = d.IsReady ? UIKit.Ready : UIKit.Muted;
                row.Pip.color = d.IsReady ? UIKit.Ready : UIKit.Rule;

                shown++;
            }

            for (int i = shown; i < _rows.Count; i++)
                _rows[i].Root.gameObject.SetActive(false);
        }

        private Row CreateRow()
        {
            var root = UIKit.Rect("LobbyRow", _rowParent);
            UIKit.FixedHeight(root.gameObject, 78f);

            var bg = UIKit.Block("Bg", root, UIKit.PanelSunk);
            UIKit.Stretch(bg.rectTransform, 0, 0, 0, 0);

            var pip = UIKit.Block("Pip", root, UIKit.Rule);
            pip.rectTransform.anchorMin = new Vector2(0f, 0f);
            pip.rectTransform.anchorMax = new Vector2(0f, 1f);
            pip.rectTransform.pivot = new Vector2(0f, 0.5f);
            pip.rectTransform.sizeDelta = new Vector2(5f, 0f);
            pip.rectTransform.anchoredPosition = Vector2.zero;

            var name = UIKit.Text("Name", root, "-", 26f, UIKit.Ink, FontStyles.Bold);
            UIKit.Anchor(name.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(24f, -12f), new Vector2(420f, 32f));

            var stats = UIKit.Text("Stats", root, "-", 18f, UIKit.InkSoft);
            UIKit.Anchor(stats.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(24f, -44f), new Vector2(640f, 26f));

            var state = UIKit.Text("State", root, "WAITING", 16f, UIKit.Muted, FontStyles.Bold,
                TextAlignmentOptions.MidlineRight);
            state.characterSpacing = 10f;
            UIKit.Anchor(state.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-22f, 0f), new Vector2(180f, 30f));

            return new Row { Root = root, Pip = pip, Name = name, Stats = stats, State = state };
        }
    }
}
