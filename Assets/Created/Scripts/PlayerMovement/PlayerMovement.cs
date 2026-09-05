using UnityEngine;
using Unity.Netcode;
using System.Collections;


public class PlayerMovement : NetworkBehaviour
{
    public override void OnNetworkSpawn()
    {
        if (!IsOwner) enabled = false;
    }
    [Header("Movement")]
    public float walkSpeed;
    public float sprintSpeed;
    public float moveSpeed;

    public float groundDrag;

    public float jumpForce;
    public float jumpCooldown;
    public float jumpDelay;
    public float airMultiplier;
    bool readyToJump;

    [Header("Keybinds")]
    public KeyCode jumpKey = KeyCode.Space;
    public KeyCode sprintKey = KeyCode.LeftShift;

    [Header("Ground Check")]
    public float playerHeight;
    public LayerMask whatIsGround;
    public bool grounded;
    public bool m_IsOnGround;

    public Transform orientation;

    float horizontalInput;
    float verticalInput;

    Vector3 moveDirection;

    public Rigidbody rb;
    private bool overshooting;

    public Animator _animator;
    public Animator _firstPersonAnimator;
    public float sensX;
    public float sensY;
    public float xRotation;
    public float yRotation;
    public GameObject cameraTarget;

    private void Start()
    {
        rb.freezeRotation = true;

        readyToJump = true;

        Cursor.lockState = CursorLockMode.Locked;

        Cursor.visible = false;
    }

    private void Update()
    {
        // ground check
        if (m_IsOnGround)
        {
            _animator.SetBool("Grounded", true);
            _firstPersonAnimator.SetBool("Grounded", true); ;
        }
        else
        {
            _animator.SetBool("Grounded", false);
            _firstPersonAnimator.SetBool("Grounded", false);
        }
        Vector3 flatVel = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        _animator.SetFloat("Speed", flatVel.magnitude);
        _firstPersonAnimator.SetFloat("Speed", flatVel.magnitude);
        _animator.SetFloat("MotionSpeed", 1);
        _firstPersonAnimator.SetFloat("MotionSpeed", 1);
        Rotate();
        if (_animator.GetBool("canMove"))
        {
            MyInput();
        }
        SpeedControl();
    }

    private void Rotate()
    {
        float mouseX = Input.GetAxisRaw("Mouse X") * Time.deltaTime * sensX;
        float mouseY = Input.GetAxisRaw("Mouse Y") * Time.deltaTime * sensY;

        yRotation += mouseX;
        xRotation -= mouseY;

        xRotation = Mathf.Clamp(xRotation, -75f, 75f);
        orientation.rotation = UnityEngine.Quaternion.Euler(0, yRotation, 0);
        cameraTarget.transform.rotation = UnityEngine.Quaternion.Euler(xRotation, yRotation, 0);
    }

    private void FixedUpdate()
    {
        MovePlayer();
    }

    private void MyInput()
    {
        //Set move speed and speed cap
        horizontalInput = Input.GetAxisRaw("Horizontal");
        verticalInput = Input.GetAxisRaw("Vertical");
        Vector3 tempMoveDirection = orientation.forward * verticalInput + orientation.right * horizontalInput;
        if (Input.GetKey(sprintKey) && m_IsOnGround && tempMoveDirection.normalized == orientation.forward)
        {
            moveSpeed = sprintSpeed;
        }
        else
        {
            moveSpeed = walkSpeed;
        }
        horizontalInput = Input.GetAxisRaw("Horizontal");
        verticalInput = Input.GetAxisRaw("Vertical");
        // when to jump
        if (Input.GetKey(jumpKey) && readyToJump && m_IsOnGround)
        {
            Jump();

            readyToJump = false;
        }

    }

    private void MovePlayer()
    {
        // calculate movement direction
        moveDirection = orientation.forward * verticalInput + orientation.right * horizontalInput;

        // on ground
        if (m_IsOnGround)
        {
            rb.AddForce(moveDirection.normalized * moveSpeed * 10f, ForceMode.Force);
        }


        // in air
        else if (!m_IsOnGround)
        {
            rb.AddForce(moveDirection.normalized * moveSpeed * 10f * airMultiplier, ForceMode.Force);
            if (rb.linearVelocity.y < 1)
            {
                rb.AddForce(new Vector3(0, moveSpeed * -3, 0));
            }
        }
    }

    private void SpeedControl()
    {
        Vector3 flatVel = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);

        // limit velocity if needed
        if (flatVel.magnitude > moveSpeed)
        {
            Vector3 limitedVel = flatVel.normalized * moveSpeed;
            rb.linearVelocity = new Vector3(limitedVel.x, rb.linearVelocity.y, limitedVel.z);
        }
    }

    private void Jump()
    {
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        _animator.SetBool("Jump", true);
        _firstPersonAnimator.SetBool("Jump", true);
        StartCoroutine(JumpWithDelay());
    }

    IEnumerator JumpWithDelay()
    {
        // Optional: play wind-up animation or sound here
        yield return new WaitForSeconds(jumpDelay);
        if (m_IsOnGround)
        {
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
            rb.AddForce(transform.up * jumpForce, ForceMode.Impulse);
        }
        _animator.SetBool("Jump", false);
        _firstPersonAnimator.SetBool("Jump", false);
    }

    IEnumerator JumpReset()
    {
        // Optional: play wind-up animation or sound here
        yield return new WaitForSeconds(jumpCooldown);
        readyToJump = true;
        overshooting = false;

    }

    void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.layer == LayerMask.NameToLayer("Ground"))
        {
            if (!readyToJump && !overshooting && m_IsOnGround == false)
            {
                overshooting = true;
                StartCoroutine(JumpReset());
            }
            m_IsOnGround = true;
            rb.linearDamping = groundDrag;
        }
    }

    void OnCollisionStay(Collision collision)
    {
        if (collision.gameObject.layer == LayerMask.NameToLayer("Ground"))
        {
            rb.linearDamping = groundDrag;
            m_IsOnGround = true;
        }
    }

    void OnCollisionExit(Collision collision)
    {
        if (collision.gameObject.layer == LayerMask.NameToLayer("Ground"))
        {

            m_IsOnGround = false;
            rb.linearDamping = groundDrag / 2;
        }
    }

}
