using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

public class ClientConnector : MonoBehaviour
{
    [SerializeField] private Button host;
    [SerializeField] private Button client;
    [SerializeField] private Camera tempCamera;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        host.onClick.AddListener(HostButtonOnClick);
        client.onClick.AddListener(ClientButtonOnClick);
    }

    private void HostButtonOnClick()
    {
        Destroy(tempCamera);
        NetworkManager.Singleton.StartHost();
    }
    private void ClientButtonOnClick()
    {
        Destroy(tempCamera);
        NetworkManager.Singleton.StartClient();
    }
}
