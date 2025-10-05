using Unity.Netcode;
using UnityEngine;

public class CameraRotate : NetworkBehaviour
{
    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            this.GetComponent<Camera>().enabled = false;
            enabled = false;
        }
    }
    public float sensX = 1;
    public float sensY = 1;

    public Transform orientation;

    public bool animatingAnticipation;
    public bool animatingOvershoot;
    float xRotation;
    float yRotation;
    private bool firstDone = false;
    private float currentTime;

    // Start is called before the first frame update

    private void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    // Update is called once per frame
    void Update()
    {
        float mouseX = Input.GetAxisRaw("Mouse X") * Time.deltaTime * sensX;
        float mouseY = Input.GetAxisRaw("Mouse Y") * Time.deltaTime * sensY;

        yRotation += mouseX;
        xRotation -= mouseY;

        xRotation = Mathf.Clamp(xRotation, -90f, 90f);

        transform.rotation = UnityEngine.Quaternion.Euler(xRotation, yRotation, 0);
        orientation.rotation = UnityEngine.Quaternion.Euler(0, yRotation, 0);

        if (this.animatingAnticipation)
        {
            this.gameObject.transform.localPosition = Vector3.Lerp(this.gameObject.transform.localPosition, new Vector3(0, -1, 0), Time.deltaTime);
        }
        else if (this.animatingOvershoot)
        {
            if (firstDone)
            {
                currentTime += Time.deltaTime;
                this.gameObject.transform.localPosition = Vector3.Lerp(this.gameObject.transform.localPosition, Vector3.zero, currentTime / 0.25f);
            }
            else
            {
                this.gameObject.transform.localPosition = Vector3.Lerp(this.gameObject.transform.localPosition, new Vector3(0, -1.5f, 0), Time.deltaTime);
            }
        }
        else
        {
            this.transform.localPosition = Vector3.zero;
        }

    }

    public void JumpAnticipation()
    {
        this.animatingAnticipation = true;
    }

    public void JumpResetCameraPos()
    {
        this.transform.localPosition = Vector3.zero;
        this.animatingAnticipation = false;
    }

    public void JumpOvershoot()
    {
        currentTime = 0f;
        this.animatingOvershoot = true;
        firstDone = false;
        Invoke("FirstDone", 0.1f);

    }

    void FirstDone()
    {
        firstDone = true;
    }

}
