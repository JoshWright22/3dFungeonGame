using Unity.Netcode;
using UnityEngine;

namespace Delver.Appearance
{
    /// <summary>
    /// Drives <see cref="CharacterAppearance"/> from the delver's replicated appearance seed, so
    /// every client draws the same character.
    /// </summary>
    [DisallowMultipleComponent]
    public class NetworkedAppearance : NetworkBehaviour
    {
        [Tooltip("Every rig to build from the delver's seed. Left empty, all CharacterAppearance components under this object are used - which is both the third-person body and the first-person arms.")]
        [SerializeField] private CharacterAppearance[] appearances;

        private int _builtSeed;
        private bool _built;

        private void Awake()
        {
            if (appearances == null || appearances.Length == 0)
                appearances = GetComponentsInChildren<CharacterAppearance>(true);
        }

        public override void OnNetworkSpawn()
        {
            TryBuild();
        }

        private void Update()
        {
            // The seed arrives with the NetworkVariable snapshot, which may land a frame or two
            // after spawn on a joining client.
            if (!_built) TryBuild();
        }

        private void TryBuild()
        {
            if (appearances == null || appearances.Length == 0) return;

            var identity = GetComponent<Delver.Game.DelverIdentity>();
            if (identity == null) identity = GetComponentInParent<Delver.Game.DelverIdentity>();
            if (identity == null) return;

            int seed = identity.AppearanceSeed;
            if (seed == 0) return;

            if (_built && seed == _builtSeed) return;

            // Same seed into every rig, so the arms you see and the body the party sees agree.
            foreach (var a in appearances)
                if (a != null) a.Build(seed);

            _builtSeed = seed;
            _built = true;
        }
    }
}
