using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class CameraMove : NetworkBehaviour
{
    [SerializeField] private AudioListener listener;
    public override void OnNetworkSpawn()
    {
        if (!IsOwner) this.listener.enabled = false;
    }
    public Transform cameraPosition;
    // Start is called before the first frame update
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {
        this.transform.position = cameraPosition.position;
    }
    
    
}
