using KinematicCharacterController;
using UnityEngine;

public class Fallbox : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {

    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.gameObject.GetComponent<KinematicCharacterMotor>() != null)
        {
            other.gameObject.GetComponent<KinematicCharacterMotor>().SetPosition(new Vector3(0, 0, 0));
        }
    }
}
