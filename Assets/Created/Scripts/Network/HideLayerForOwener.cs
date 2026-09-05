using UnityEngine;
using UnityEngine.Rendering;
using Unity.Netcode;

/// <summary>
/// Hides a character's third-person body from the player wearing it, while leaving it fully
/// visible to everyone else.
///
/// The owner still casts a shadow, so you can see your own silhouette on the floor by torchlight
/// without the camera sitting inside your own head.
/// </summary>
public class HideLayerForOwener : NetworkBehaviour
{
    [Tooltip("ON for the third-person body: the owner sees only its shadow. OFF for a first-person viewmodel, which the owner is meant to see.")]
    public bool hideAllButShadows;

    public override void OnNetworkSpawn()
    {
        Apply(IsOwner);
    }

    private void Apply(bool isOwner)
    {
        // Owner-only: hiding the body for everyone would leave the party invisible to each other,
        // which is what happened when this ran regardless of ownership.
        ShadowCastingMode mode = (hideAllButShadows && isOwner)
            ? ShadowCastingMode.ShadowsOnly
            : ShadowCastingMode.On;

        foreach (var renderer in GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true))
            renderer.shadowCastingMode = mode;

        foreach (var renderer in GetComponentsInChildren<MeshRenderer>(includeInactive: true))
            renderer.shadowCastingMode = mode;
    }
}
