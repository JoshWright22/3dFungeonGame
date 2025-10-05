using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class StairClimb : MonoBehaviour
{
    Rigidbody rigidBody;
    [SerializeField] GameObject stepRayUpper;
    [SerializeField] GameObject stepRayLower;
    [SerializeField] float stepHeight = 0.3f;
    [SerializeField] float stepSmooth = 2f;

    private void Awake()
    {
        rigidBody = GetComponent<Rigidbody>();

        stepRayUpper.transform.position = new Vector3(stepRayUpper.transform.position.x, stepRayUpper.transform.position.y + stepHeight, stepRayUpper.transform.position.z);
    }

    private void FixedUpdate()
    {
        stepClimb();
    }

    void stepClimb()
    {
        RaycastHit hitLower;
        if (Physics.Raycast(stepRayLower.transform.position, transform.TransformDirection(Vector3.forward), out hitLower, 1f))
        {
            Debug.DrawRay(transform.position, transform.TransformDirection(Vector3.forward) * hitLower.distance, Color.white);
            RaycastHit hitUpper;
            if (!Physics.Raycast(stepRayUpper.transform.position, transform.TransformDirection(Vector3.forward), out hitUpper, 1f))
            {
                rigidBody.position -= new Vector3(0f, -stepSmooth * Time.deltaTime, 0f);
                Debug.Log("test");
                Debug.DrawRay(transform.position, transform.TransformDirection(Vector3.forward) * hitUpper.distance, Color.green);
            }
        }
        else
        {
            Debug.DrawRay(transform.position, transform.TransformDirection(Vector3.forward) * 150f, Color.blue);
        }

        RaycastHit hitLower45;
        if (Physics.Raycast(stepRayLower.transform.position, transform.TransformDirection(1.5f, 0, 1), out hitLower45, 1f))
        {

            RaycastHit hitUpper45;
            if (!Physics.Raycast(stepRayUpper.transform.position, transform.TransformDirection(1.5f, 0, 1), out hitUpper45, 1f))
            {
                rigidBody.position -= new Vector3(0f, -stepSmooth * Time.deltaTime, 0f);
            }
        }

        RaycastHit hitLowerMinus45;
        if (Physics.Raycast(stepRayLower.transform.position, transform.TransformDirection(-1.5f, 0, 1), out hitLowerMinus45, 1f))
        {

            RaycastHit hitUpperMinus45;
            if (!Physics.Raycast(stepRayUpper.transform.position, transform.TransformDirection(-1.5f, 0, 1), out hitUpperMinus45, 1f))
            {
                rigidBody.position -= new Vector3(0f, -stepSmooth * Time.deltaTime, 0f);
            }
        }
    }
}