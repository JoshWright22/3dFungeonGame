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
        if (tempCamera != null) Destroy(tempCamera.gameObject);
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
        if (steamConnect)
        {
            // The field holds the *host's* SteamID. Using SteamClient.SteamId here made every
            // client try to join itself, so joining a friend never worked.
            string raw = clientSteamField != null ? clientSteamField.text.Trim() : string.Empty;
            if (!ulong.TryParse(raw, out ulong hostSteamId) || hostSteamId == 0)
            {
                Debug.LogError($"SteamClientConnector: '{raw}' is not a valid SteamID64.", this);
                return;
            }

            if (tempCamera != null) Destroy(tempCamera.gameObject);
            manager.StartClient(hostSteamId);
        }else
        {
            if (tempCamera != null) Destroy(tempCamera.gameObject);
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
