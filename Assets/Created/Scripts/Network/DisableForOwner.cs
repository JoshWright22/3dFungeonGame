using UnityEngine;
using Unity.Netcode;

public class DisableForOwner : NetworkBehaviour
{
    public override void OnNetworkSpawn()
    {
        if(IsOwner) {
            gameObject.SetActive(false);
        }
    }
}