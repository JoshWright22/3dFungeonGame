using UnityEngine;
using Unity.Netcode;

public class PlayerPickupDetector : MonoBehaviour
{
    public KeyCode rhPickupKey = KeyCode.E;
    public KeyCode lhPickupKey = KeyCode.Q;
    public PlayerStats playerStats;
    public float pickupDistance = 5;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public void OnNetworkSpawn()
    {

    }

    void FixedUpdate()
    {
        RaycastHit hit;
        // Does the ray intersect any objects excluding the player layer
        if (Physics.Raycast(transform.position, transform.TransformDirection(Vector3.forward), out hit, pickupDistance))
        {
            if (hit.collider.gameObject.layer == LayerMask.NameToLayer("Fantasy Object"))
            {
                if (Input.GetKey(rhPickupKey) && playerStats.CanPickUp(hit.collider.gameObject.GetComponent<PickupObject>(), "rh"))
                {
                    playerStats.pickupObjectRight(hit.collider.gameObject.GetComponent<PickupObject>());
                }
                else if (Input.GetKey(lhPickupKey) && playerStats.CanPickUp(hit.collider.gameObject.GetComponent<PickupObject>(), "lh"))
                {
                    playerStats.pickupObjectLeft(hit.collider.gameObject.GetComponent<PickupObject>());
                }
                Debug.DrawRay(transform.position, transform.TransformDirection(Vector3.forward) * hit.distance, Color.green);

            }
            else
            {
                Debug.DrawRay(transform.position, transform.TransformDirection(Vector3.forward) * 1000, Color.red);

            }

        }


    }
}
