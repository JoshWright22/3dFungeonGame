using UnityEngine;
using UnityEngine.Windows;
using UnityEngine.InputSystem;
using Input = UnityEngine.Input;
using UnityEngine.WSA;

public class PlayerStats : MonoBehaviour
{
    public float maxHp;
    public float currentHP;

    public float maxStr;
    public float currentStr;

    public float maxDex;
    public float currentDex;

    public float maxInt;
    public float currentInt;

    public ObjectModelSO currentRightHand;
    public ObjectModelSO currentLeftHand;

    public GameObject firstPersonRightHand;
    public GameObject thirdPersonRightHand;
    public GameObject firstPersonLeftHand;
    public GameObject thirdPersonLeftHand;

    public void RollStats()
    {
        maxStr = 0.0f + Random.Range(1, 6) + Random.Range(1, 6) + Random.Range(1, 6);
        maxDex = 0.0f + Random.Range(1, 6) + Random.Range(1, 6) + Random.Range(1, 6);
        maxInt = 0.0f + Random.Range(1, 6) + Random.Range(1, 6) + Random.Range(1, 6);
        maxHp = System.Math.Max(maxDex, maxStr) + Random.Range(1, 4);
    }

    public void ResetCurrentStats()
    {
        currentDex = maxDex;
        currentInt = maxInt;
        currentHP = maxHp;
        currentStr = maxStr;
    }

    public bool CanPickUp(PickupObject pickup, string hand)
    {
        return true;
    }

    public void pickupObjectLeft(PickupObject pickup)
    {
        GameObject newObj = Instantiate(pickup.fantasyObjectSO.prefab, firstPersonLeftHand.transform, false);
        newObj.transform.localPosition = pickup.fantasyObjectSO.fplhPos;
        newObj.transform.localRotation = pickup.fantasyObjectSO.fplhRot;
        newObj.transform.localScale = pickup.fantasyObjectSO.fplhSca;
        newObj.GetComponent<Rigidbody>().isKinematic = true;
        newObj.GetComponent<BoxCollider>().enabled = false;
        Destroy(pickup.gameObject);
    }

    public void pickupObjectRight(PickupObject pickup)
    {
        GameObject newObj = Instantiate(pickup.fantasyObjectSO.prefab, firstPersonRightHand.transform, false);
        newObj.transform.localPosition = pickup.fantasyObjectSO.fprhPos;
        newObj.transform.localRotation = pickup.fantasyObjectSO.fprhRot;
        newObj.transform.localScale = pickup.fantasyObjectSO.fprhSca;
        newObj.GetComponent<Rigidbody>().isKinematic = true;
        newObj.GetComponent<BoxCollider>().enabled = false;
        Destroy(pickup.gameObject);
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void OnNetworkSpawn()
    {
        RollStats();
        ResetCurrentStats();
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.P))
        {
            this.GetComponent<Rigidbody>().AddForce(Random.onUnitSphere * 30);
            Debug.Log("Launcher");
        }
    }
}
