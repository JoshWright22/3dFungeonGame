using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using Steamworks;
using TMPro;

public class SteamClientConnector : MonoBehaviour
{
    [SerializeField] private Button hostButton;
    [SerializeField] private Button clientButton;
    [SerializeField] private TMP_InputField clientSteamField;
    [SerializeField] private GameNetworkManager manager;
    [SerializeField] private Camera tempCamera;
    [SerializeField] private bool steamConnect;
    [SerializeField] private GameObject steamNetwork;
    [SerializeField] private GameObject lanNetwork;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        hostButton.onClick.AddListener(HostButtonOnClick);
        clientButton.onClick.AddListener(ClientButtonOnClick);
        if  (steamConnect)
        {
            steamNetwork.SetActive(true);
            lanNetwork.SetActive(false);
        }else
        {
            steamNetwork.SetActive(false);
            lanNetwork.SetActive(true);            
        }
    }

    private void HostButtonOnClick()
    {
        Destroy(tempCamera);
        if (steamConnect)
        {
            manager.StartHost(4);
            Debug.Log("Created lobby from: " + SteamClient.SteamId);
        }else{
            NetworkManager.Singleton.StartHost();
        }
        HideUI();
    }
    private void ClientButtonOnClick()
    {
        Destroy(tempCamera);
        if (steamConnect)
        {
            var playersteamid = SteamClient.SteamId;
            manager.StartClient(playersteamid);
        }else
        {
            NetworkManager.Singleton.StartClient();
        }
        HideUI();
    }

    private void HideUI()
    {
        hostButton.gameObject.SetActive(false);
        clientButton.gameObject.SetActive(false);
        clientSteamField.gameObject.SetActive(false);
    }
}
