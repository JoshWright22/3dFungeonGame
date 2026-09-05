using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Delver.Game
{
    /// <summary>
    /// Owns the run: draws the dungeon seed, gets every client to build the same dungeon from
    /// it, and only then releases the party into the level.
    ///
    /// Geometry is never replicated. The host picks one integer, every client runs the same
    /// deterministic generator, and clients report back when they are done - which is both far
    /// cheaper than spawning several thousand networked tiles and the reason
    /// <see cref="Generator3D.Generate"/> takes an explicit seed.
    /// </summary>
    [DisallowMultipleComponent]
    public class DungeonRunner : NetworkBehaviour
    {
        public static DungeonRunner Instance { get; private set; }

        public enum RunPhase
        {
            Lobby,
            Generating,
            Delving,
        }

        [Header("Generation")]
        [SerializeField] private Generator3D generator;

        [Tooltip("Fixed seed for repeatable testing. Leave 0 to draw a fresh seed each run.")]
        [SerializeField] private int debugSeed = 0;

        [Header("Spawning")]
        [Tooltip("Party spawns spread around the entry room by this radius, in world units.")]
        [SerializeField] private float spawnScatterRadius = 3f;

        private readonly NetworkVariable<int> _seed = new NetworkVariable<int>(0);
        private readonly NetworkVariable<RunPhase> _phase = new NetworkVariable<RunPhase>(RunPhase.Lobby);

        private int _clientsGenerated;
        private int _builtSeed;

        public int Seed => _seed.Value;
        public RunPhase Phase => _phase.Value;
        public bool IsDelving => _phase.Value == RunPhase.Delving;

        /// <summary>Raised on every client when the phase changes.</summary>
        public static event System.Action<RunPhase> PhaseChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;

            if (generator == null) generator = FindAnyObjectByType<Generator3D>();
        }

        public override void OnNetworkSpawn()
        {
            _seed.OnValueChanged += OnSeedChanged;
            _phase.OnValueChanged += OnPhaseChanged;

            LobbyScreenHook(true);

            // A client joining after the host already started still needs to build the dungeon.
            if (_seed.Value != 0) BuildLocally(_seed.Value);
        }

        public override void OnNetworkDespawn()
        {
            _seed.OnValueChanged -= OnSeedChanged;
            _phase.OnValueChanged -= OnPhaseChanged;
            LobbyScreenHook(false);
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        private void LobbyScreenHook(bool subscribe)
        {
            if (subscribe) UI.LobbyScreen.DescentRequested += BeginDescent;
            else UI.LobbyScreen.DescentRequested -= BeginDescent;
        }

        // ------------------------------------------------------------------ host side

        /// <summary>Host only. Draws a seed and moves the run into generation.</summary>
        public void BeginDescent()
        {
            if (!IsServer) return;
            if (_phase.Value != RunPhase.Lobby) return;

            int seed = debugSeed != 0 ? debugSeed : Random.Range(int.MinValue, int.MaxValue);

            _clientsGenerated = 0;
            _phase.Value = RunPhase.Generating;
            _seed.Value = seed;   // every client's OnSeedChanged fires from here

            Debug.Log($"[DungeonRunner] Descent beginning on seed {seed}.", this);

            // The host builds too, and reports like anybody else.
            BuildLocally(seed);
        }

        [ServerRpc(RequireOwnership = false)]
        private void ReportGeneratedServerRpc(ServerRpcParams p = default)
        {
            if (_phase.Value != RunPhase.Generating) return;

            _clientsGenerated++;

            int expected = NetworkManager.Singleton != null
                ? NetworkManager.Singleton.ConnectedClientsIds.Count
                : 1;

            Debug.Log($"[DungeonRunner] {_clientsGenerated}/{expected} clients generated.", this);

            if (_clientsGenerated >= expected)
                StartCoroutine(ReleaseParty());
        }

        private IEnumerator ReleaseParty()
        {
            // One frame so the last client's colliders are registered before anyone is teleported
            // onto them.
            yield return null;

            PlaceParty();
            _phase.Value = RunPhase.Delving;

            Debug.Log("[DungeonRunner] Party released.", this);
        }

        /// <summary>Server-side: scatters the party around the generator's entry point.</summary>
        private void PlaceParty()
        {
            if (!IsServer) return;

            Vector3 origin = generator != null ? generator.PartySpawnPoint : Vector3.zero;

            int i = 0;
            var roster = DelverIdentity.All;
            for (int r = 0; r < roster.Count; r++)
            {
                var d = roster[r];
                if (d == null) continue;

                float angle = (i / Mathf.Max(1f, roster.Count)) * Mathf.PI * 2f;
                Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * spawnScatterRadius;

                TeleportClientRpc(origin + offset, new ClientRpcParams
                {
                    Send = new ClientRpcSendParams { TargetClientIds = new[] { d.OwnerClientId } }
                });

                i++;
            }
        }

        [ClientRpc]
        private void TeleportClientRpc(Vector3 position, ClientRpcParams p = default)
        {
            var local = DelverIdentity.Local;
            if (local == null) return;

            // KCC owns the transform, so going through the motor is the only safe way to move it.
            var motor = local.GetComponentInChildren<KinematicCharacterController.KinematicCharacterMotor>();
            if (motor != null) motor.SetPosition(position);
            else local.transform.position = position;
        }

        // ------------------------------------------------------------------ every client

        private void OnSeedChanged(int previous, int current)
        {
            if (current == 0) return;
            BuildLocally(current);
        }

        private void OnPhaseChanged(RunPhase previous, RunPhase current)
        {
            PhaseChanged?.Invoke(current);
        }

        private void BuildLocally(int seed)
        {
            if (_builtSeed == seed) return;

            if (generator == null) generator = FindAnyObjectByType<Generator3D>();
            if (generator == null)
            {
                Debug.LogError("[DungeonRunner] No Generator3D in the scene - cannot build the dungeon.", this);
                return;
            }

            generator.Generate(seed);
            _builtSeed = seed;

            ReportGeneratedServerRpc();
        }
    }
}
