using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Raycasts forward from this transform and equips whatever <see cref="PickupObject"/> it hits.
/// Deliberately a plain MonoBehaviour: this component lives on the camera rig, which is not
/// guaranteed to sit under a NetworkObject. Ownership is resolved at runtime instead.
/// </summary>
public class PlayerPickupDetector : MonoBehaviour
{
    public KeyCode rhPickupKey = KeyCode.E;
    public KeyCode lhPickupKey = KeyCode.Q;
    public PlayerStats playerStats;
    public float pickupDistance = 5f;

    private int pickupLayerMask;
    private NetworkObject owningNetworkObject;

    private void Awake()
    {
        int layer = LayerMask.NameToLayer("Fantasy Object");
        if (layer < 0)
            Debug.LogError("PlayerPickupDetector: layer 'Fantasy Object' is missing from the Tag Manager.", this);
        else
            pickupLayerMask = 1 << layer;

        if (playerStats == null)
            playerStats = GetComponentInParent<PlayerStats>();

        owningNetworkObject = GetComponentInParent<NetworkObject>();
    }

    /// <summary>True when this rig belongs to the local player (or the game is running un-networked).</summary>
    private bool IsLocalPlayer =>
        owningNetworkObject == null || !owningNetworkObject.IsSpawned || owningNetworkObject.IsOwner;

    private void Update()
    {
        // Without this gate, every client drives the pickup raycast of every player in the lobby.
        if (!IsLocalPlayer || playerStats == null || pickupLayerMask == 0) return;

        bool wantsRight = Input.GetKeyDown(rhPickupKey);
        bool wantsLeft = Input.GetKeyDown(lhPickupKey);
        if (!wantsRight && !wantsLeft) return;

        Vector3 forward = transform.TransformDirection(Vector3.forward);
        if (!Physics.Raycast(transform.position, forward, out RaycastHit hit, pickupDistance, pickupLayerMask))
            return;

        PickupObject pickup = hit.collider.GetComponentInParent<PickupObject>();
        if (pickup == null) return;

        if (wantsRight && playerStats.CanPickUp(pickup, "rh"))
            playerStats.pickupObjectRight(pickup);
        else if (wantsLeft && playerStats.CanPickUp(pickup, "lh"))
            playerStats.pickupObjectLeft(pickup);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawRay(transform.position, transform.TransformDirection(Vector3.forward) * pickupDistance);
    }
}
