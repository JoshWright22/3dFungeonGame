using System.Collections;
using Unity.Netcode;
using UnityEngine;
using Delver.UI;

namespace Delver.Game
{
    /// <summary>
    /// The one component the scene needs. Spawns the UI, and - when testing - starts hosting on
    /// its own so pressing Play drops you straight into a lobby instead of a menu.
    ///
    /// Put this on an empty GameObject in the scene alongside the NetworkManager.
    /// </summary>
    [DisallowMultipleComponent]
    public class GameBootstrap : MonoBehaviour
    {
        public enum AutoHostMode
        {
            Never,
            EditorOnly,
            EditorAndBuilds,
        }

        [Header("Testing")]
        [Tooltip("Start hosting automatically instead of waiting for the menu. EditorOnly keeps shipped builds on the normal flow.")]
        [SerializeField] private AutoHostMode autoHost = AutoHostMode.EditorOnly;

        [Tooltip("Frames to wait before auto-hosting, so the transport and Steam have settled.")]
        [SerializeField] private int autoHostDelayFrames = 2;

        [Tooltip("Skip the lobby entirely and generate a dungeon immediately. Solo iteration on level design.")]
        [SerializeField] private bool autoBeginDescent = false;

        [Header("UI")]
        [SerializeField] private bool spawnLobbyScreen = true;
        [SerializeField] private bool spawnCharacterSheet = true;
        [SerializeField] private bool spawnStaminaBar = true;

        [Header("Steam")]
        [Tooltip("Create a Steam lobby when auto-hosting. Off keeps editor testing entirely local.")]
        [SerializeField] private bool autoHostCreatesSteamLobby = false;

        [SerializeField] private uint maxPartySize = 4;

        [Tooltip("The hand-authored Host/Connect menu. Hidden when auto-hosting, since nobody is going to click it.")]
        [SerializeField] private GameObject legacyMenuCanvas;

        private LobbyScreen _lobby;

        private void Start()
        {
            BuildUI();
            StartCoroutine(AutoHostRoutine());
        }

        private void OnEnable() => DungeonRunner.PhaseChanged += OnPhaseChanged;
        private void OnDisable() => DungeonRunner.PhaseChanged -= OnPhaseChanged;

        private void BuildUI()
        {
            if (spawnLobbyScreen)
            {
                var go = new GameObject("~LobbyScreen");
                go.transform.SetParent(transform, false);
                _lobby = go.AddComponent<LobbyScreen>();
            }

            if (spawnCharacterSheet)
            {
                var go = new GameObject("~CharacterSheet");
                go.transform.SetParent(transform, false);
                go.AddComponent<CharacterSheetHUD>();
            }

            if (spawnStaminaBar)
            {
                var go = new GameObject("~StaminaBar");
                go.transform.SetParent(transform, false);
                go.AddComponent<StaminaHUD>();
            }
        }

        private void OnPhaseChanged(DungeonRunner.RunPhase phase)
        {
            // The lobby goes away the moment the descent starts, and takes the cursor with it.
            if (_lobby != null)
                _lobby.SetVisible(phase == DungeonRunner.RunPhase.Lobby);
        }

        private IEnumerator AutoHostRoutine()
        {
            if (!ShouldAutoHost()) yield break;

            if (legacyMenuCanvas != null) legacyMenuCanvas.SetActive(false);

            for (int i = 0; i < autoHostDelayFrames; i++) yield return null;

            // Both NetworkManagers in the scene start inactive; SteamClientConnector enables one
            // of them in its Start. Wait for whichever it picked rather than assuming a singleton.
            float managerDeadline = Time.time + 5f;
            while (NetworkManager.Singleton == null && Time.time < managerDeadline) yield return null;

            var nm = NetworkManager.Singleton;
            if (nm == null)
            {
                Debug.LogError("[GameBootstrap] No active NetworkManager after 5s - cannot auto-host. " +
                               "Check that ConnectionManager's SteamClientConnector enabled one.", this);
                yield break;
            }

            if (nm.IsListening)
            {
                Debug.Log("[GameBootstrap] Already connected; skipping auto-host.", this);
                yield break;
            }

            if (autoHostCreatesSteamLobby && GameNetworkManager.Instance != null)
            {
                Debug.Log("[GameBootstrap] Auto-hosting with a Steam lobby.", this);
                GameNetworkManager.Instance.StartHost(maxPartySize);
            }
            else
            {
                Debug.Log("[GameBootstrap] Auto-hosting locally (no Steam lobby).", this);
                if (!nm.StartHost())
                {
                    Debug.LogError("[GameBootstrap] StartHost failed.", this);
                    yield break;
                }
            }

            if (!autoBeginDescent) yield break;

            // Wait for the local delver to actually spawn before declaring it ready.
            float deadline = Time.time + 10f;
            while (DelverIdentity.Local == null && Time.time < deadline) yield return null;

            var local = DelverIdentity.Local;
            if (local != null) local.SetReady(true);

            yield return null;

            if (DungeonRunner.Instance != null) DungeonRunner.Instance.BeginDescent();
        }

        private bool ShouldAutoHost()
        {
            switch (autoHost)
            {
                case AutoHostMode.Never: return false;
                case AutoHostMode.EditorAndBuilds: return true;
                case AutoHostMode.EditorOnly:
#if UNITY_EDITOR
                    return true;
#else
                    return false;
#endif
                default: return false;
            }
        }
    }
}
