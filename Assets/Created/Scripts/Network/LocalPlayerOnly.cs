using Unity.Netcode;
using UnityEngine;

namespace Delver.Netcode
{
    /// <summary>
    /// Switches off everything on this GameObject that only the local player should be running.
    ///
    /// The camera rig is a child of the player prefab, so without this every client ends up with
    /// one Camera and one AudioListener per delver in the lobby - four cameras fighting over the
    /// screen, and Unity warning about multiple audio listeners.
    /// </summary>
    [DisallowMultipleComponent]
    public class LocalPlayerOnly : NetworkBehaviour
    {
        [Tooltip("Disable the Camera component on this object for remote players.")]
        [SerializeField] private bool disableCamera = true;

        [Tooltip("Disable the AudioListener on this object for remote players. Unity only supports one.")]
        [SerializeField] private bool disableAudioListener = true;

        [Tooltip("Additional behaviours to switch off for remote players.")]
        [SerializeField] private Behaviour[] alsoDisable;

        [Tooltip("Whole GameObjects to switch off for remote players.")]
        [SerializeField] private GameObject[] alsoDeactivate;

        public override void OnNetworkSpawn()
        {
            // Note this switches things ON for the owner as well as off for everyone else: the
            // prefab ships with the AudioListener disabled, so somebody has to turn the local
            // player's back on or the game has no audio at all.
            bool local = IsOwner;

            if (disableCamera)
            {
                var cam = GetComponent<Camera>();
                if (cam != null)
                {
                    cam.enabled = local;

                    // Player cameras spawn after DungeonAtmosphere has already swept the scene,
                    // so they'd otherwise keep clearing to a bright default background.
                    if (local) Delver.Game.DungeonAtmosphere.ApplyToCamera(cam);
                }
            }

            if (disableAudioListener)
            {
                var listener = GetComponent<AudioListener>();
                if (listener != null) listener.enabled = local;
            }

            if (alsoDisable != null)
                foreach (var b in alsoDisable)
                    if (b != null) b.enabled = local;

            if (alsoDeactivate != null)
                foreach (var go in alsoDeactivate)
                    if (go != null) go.SetActive(local);
        }
    }
}
