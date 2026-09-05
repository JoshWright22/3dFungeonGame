using UnityEngine;
using Unity.Netcode;

public class PlayerStats : NetworkBehaviour
{
    // Server rolls these once on spawn; every client reads them.
    private readonly NetworkVariable<int> netMaxHp = new NetworkVariable<int>();
    private readonly NetworkVariable<int> netMaxStr = new NetworkVariable<int>();
    private readonly NetworkVariable<int> netMaxDex = new NetworkVariable<int>();
    private readonly NetworkVariable<int> netMaxInt = new NetworkVariable<int>();
    private readonly NetworkVariable<int> netCurrentHp = new NetworkVariable<int>();

    public int maxHp => netMaxHp.Value;
    public int currentHP => netCurrentHp.Value;

    public int maxStr => netMaxStr.Value;
    public int maxDex => netMaxDex.Value;
    public int maxInt => netMaxInt.Value;

    [Header("Debug")]
    [SerializeField] private bool enableLaunchKey = false;

    public ObjectModelSO currentRightHand;
    public ObjectModelSO currentLeftHand;

    public GameObject firstPersonRightHand;
    public GameObject thirdPersonRightHand;
    public GameObject firstPersonLeftHand;
    public GameObject thirdPersonLeftHand;

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            RollStats();
            ResetCurrentStats();
        }
    }

    /// <summary>3d6 for each ability, HP = best physical ability + 1d4.</summary>
    public void RollStats()
    {
        if (!IsServer) return;

        netMaxStr.Value = Roll(3, 6);
        netMaxDex.Value = Roll(3, 6);
        netMaxInt.Value = Roll(3, 6);
        netMaxHp.Value = Mathf.Max(netMaxDex.Value, netMaxStr.Value) + Roll(1, 4);
    }

    public void ResetCurrentStats()
    {
        if (!IsServer) return;

        netCurrentHp.Value = netMaxHp.Value;
    }

    public void ApplyDamage(int amount)
    {
        if (!IsServer) return;

        netCurrentHp.Value = Mathf.Max(0, netCurrentHp.Value - amount);
    }

    /// <summary>Sums <paramref name="count"/> dice with <paramref name="sides"/> faces. Range.Range's int overload
    /// is max-exclusive, so a d6 is Range(1, 7).</summary>
    private static int Roll(int count, int sides)
    {
        int total = 0;
        for (int i = 0; i < count; i++)
            total += Random.Range(1, sides + 1);
        return total;
    }

    public bool CanPickUp(PickupObject pickup, string hand)
    {
        return pickup != null && pickup.fantasyObjectSO != null && pickup.fantasyObjectSO.prefab != null;
    }

    public void pickupObjectLeft(PickupObject pickup)
    {
        AttachToHand(pickup, firstPersonLeftHand,
            pickup.fantasyObjectSO.fplhPos, pickup.fantasyObjectSO.fplhRot, pickup.fantasyObjectSO.fplhSca);
        currentLeftHand = pickup.fantasyObjectSO;
        Destroy(pickup.gameObject);
    }

    public void pickupObjectRight(PickupObject pickup)
    {
        AttachToHand(pickup, firstPersonRightHand,
            pickup.fantasyObjectSO.fprhPos, pickup.fantasyObjectSO.fprhRot, pickup.fantasyObjectSO.fprhSca);
        currentRightHand = pickup.fantasyObjectSO;
        Destroy(pickup.gameObject);
    }

    private static void AttachToHand(PickupObject pickup, GameObject hand, Vector3 pos, Quaternion rot, Vector3 scale)
    {
        if (hand == null)
        {
            Debug.LogError("PlayerStats: hand transform is not assigned, cannot equip pickup.", pickup);
            return;
        }

        GameObject newObj = Instantiate(pickup.fantasyObjectSO.prefab, hand.transform, false);
        newObj.transform.localPosition = pos;
        newObj.transform.localRotation = rot;
        newObj.transform.localScale = scale;

        // Held copies are visual only - kill the physics on whatever the source prefab happens to carry.
        if (newObj.TryGetComponent(out Rigidbody body))
            body.isKinematic = true;

        foreach (var col in newObj.GetComponentsInChildren<Collider>())
            col.enabled = false;
    }

    private void Update()
    {
        if (!enableLaunchKey || !IsOwner) return;

        if (Input.GetKeyDown(KeyCode.P) && TryGetComponent(out Rigidbody body))
        {
            body.AddForce(Random.onUnitSphere * 30f, ForceMode.Impulse);
            Debug.Log("Launcher");
        }
    }
}
