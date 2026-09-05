using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Delver.Game
{
    /// <summary>
    /// Who a player is, as far as the rest of the party is concerned: a display name, an
    /// appearance seed, and whether they have declared themselves ready.
    ///
    /// Every instance registers itself in <see cref="All"/> on spawn, on every client. That
    /// registry is what the lobby and the character sheet enumerate - it works identically on
    /// the host and on a late-joining client, which <c>ConnectedClientsList</c> does not.
    /// </summary>
    [DisallowMultipleComponent]
    public class DelverIdentity : NetworkBehaviour
    {
        private static readonly List<DelverIdentity> _all = new List<DelverIdentity>();

        /// <summary>Every spawned delver, host included, in join order.</summary>
        public static IReadOnlyList<DelverIdentity> All => _all;

        /// <summary>Raised whenever the roster or any member's replicated state changes.</summary>
        public static event System.Action RosterChanged;

        private readonly NetworkVariable<FixedString32Bytes> _displayName =
            new NetworkVariable<FixedString32Bytes>(default,
                NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _appearanceSeed =
            new NetworkVariable<int>(0,
                NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // Owner-writable: readiness is the one piece of state a player asserts about themselves.
        private readonly NetworkVariable<bool> _isReady =
            new NetworkVariable<bool>(false,
                NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        public string DisplayName => _displayName.Value.ToString();
        public int AppearanceSeed => _appearanceSeed.Value;
        public bool IsReady => _isReady.Value;

        public PlayerStats Stats { get; private set; }

        /// <summary>True for the delver this client is actually playing.</summary>
        public bool IsLocalDelver => IsOwner;

        private void Awake()
        {
            Stats = GetComponent<PlayerStats>();
            if (Stats == null) Stats = GetComponentInChildren<PlayerStats>();
        }

        public override void OnNetworkSpawn()
        {
            if (!_all.Contains(this)) _all.Add(this);

            _displayName.OnValueChanged += OnAnyValueChanged;
            _appearanceSeed.OnValueChanged += OnAnySeedChanged;
            _isReady.OnValueChanged += OnAnyReadyChanged;

            if (IsServer)
            {
                // Appearance is one replicated integer, exactly like the dungeon seed. Clients
                // rebuild the character from it rather than receiving a part list.
                _appearanceSeed.Value = Random.Range(int.MinValue, int.MaxValue);
                _displayName.Value = FallbackName(OwnerClientId);
            }

            if (IsOwner)
                SubmitNameServerRpc(LocalPlayerName());

            RosterChanged?.Invoke();
        }

        public override void OnNetworkDespawn()
        {
            _displayName.OnValueChanged -= OnAnyValueChanged;
            _appearanceSeed.OnValueChanged -= OnAnySeedChanged;
            _isReady.OnValueChanged -= OnAnyReadyChanged;

            _all.Remove(this);
            RosterChanged?.Invoke();
        }

        private void OnAnyValueChanged(FixedString32Bytes _, FixedString32Bytes __) => RosterChanged?.Invoke();
        private void OnAnySeedChanged(int _, int __) => RosterChanged?.Invoke();
        private void OnAnyReadyChanged(bool _, bool __) => RosterChanged?.Invoke();

        /// <summary>Toggles this client's own ready flag. No-op for anyone else's delver.</summary>
        public void ToggleReady()
        {
            if (!IsOwner) return;
            _isReady.Value = !_isReady.Value;
        }

        public void SetReady(bool ready)
        {
            if (!IsOwner) return;
            _isReady.Value = ready;
        }

        /// <summary>Server-side: clears readiness for everyone, e.g. when a run ends.</summary>
        public static void ClearAllReady()
        {
            foreach (var d in _all)
                if (d != null && d.IsServer) d._isReady.Value = false;
        }

        public static DelverIdentity Local
        {
            get
            {
                for (int i = 0; i < _all.Count; i++)
                    if (_all[i] != null && _all[i].IsOwner) return _all[i];
                return null;
            }
        }

        public static bool EveryoneReady()
        {
            if (_all.Count == 0) return false;
            for (int i = 0; i < _all.Count; i++)
                if (_all[i] == null || !_all[i].IsReady) return false;
            return true;
        }

        [ServerRpc]
        private void SubmitNameServerRpc(string name, ServerRpcParams p = default)
        {
            // Never trust a client-supplied string wholesale - clamp it and strip control chars.
            _displayName.Value = Sanitize(name, p.Receive.SenderClientId);
        }

        private static FixedString32Bytes Sanitize(string raw, ulong clientId)
        {
            if (string.IsNullOrWhiteSpace(raw)) return FallbackName(clientId);

            var sb = new System.Text.StringBuilder(24);
            foreach (char c in raw.Trim())
            {
                if (char.IsControl(c)) continue;
                sb.Append(c);
                if (sb.Length >= 20) break;
            }

            return sb.Length == 0 ? FallbackName(clientId) : new FixedString32Bytes(sb.ToString());
        }

        private static FixedString32Bytes FallbackName(ulong clientId)
        {
            return new FixedString32Bytes("Delver " + clientId);
        }

        /// <summary>Steam persona name when Steam is up, something usable when it is not.</summary>
        private static string LocalPlayerName()
        {
#if !DISABLESTEAMWORKS
            try
            {
                if (Steamworks.SteamClient.IsValid)
                    return Steamworks.SteamClient.Name;
            }
            catch (System.Exception)
            {
                // Steam not initialised - editor autohost, LAN testing. Fall through.
            }
#endif
            return "Delver " + (NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0);
        }
    }
}
