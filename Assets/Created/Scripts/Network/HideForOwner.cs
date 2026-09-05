using UnityEngine;
using Unity.Netcode;

public class HideForOwner : NetworkBehaviour
{
    public override void OnNetworkSpawn()
    {
        if(IsOwner) {
            this.GetComponent<Renderer>().enabled = false;
        }
    }
}
