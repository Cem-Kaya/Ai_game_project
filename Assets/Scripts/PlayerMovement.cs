using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    [Header("Move")]
    public float speed = 7f;

    [Header("Jump (press)")]
    public float jumpForce = 10f;

    [Header("Jump (hold for higher)")]
    public float maxHoldTime = 0.3f;

    [Header("Gravity tuning (feel)")]
    public float baseGravityScale = 1f;
    public float fallScale = 3.0f;
    public float lowJumpScale = 6.0f;

    private Rigidbody2D rb;
    private float moveInput;

    private bool isGrounded;
    private bool pressJump;     // pressed this frame
    private bool holdJump;      // currently held
    private bool canHoldJump;   // we're in the "hold to rise more" window
    private float holdTimer;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = baseGravityScale;
    }

    void Update()
    {
        GetInput();
        HandleJumpPress();
        TuneGravityForFeel();
    }

    private void FixedUpdate()
    {
        MovePlayer();
        HandleJumpHold();
    }

    void GetInput()
    {
        moveInput = Input.GetAxisRaw("Horizontal");
        pressJump = Input.GetButtonDown("Jump");
        holdJump = Input.GetButton("Jump");
    }

    void MovePlayer()
    {
        rb.linearVelocity = new Vector2(moveInput * speed, rb.linearVelocity.y);
    }

    void HandleJumpPress()
    {
        if (pressJump && isGrounded)
        {
            // Initial launch
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
            isGrounded = false;

            // Enable the hold window
            canHoldJump = true;
            holdTimer   = 0f;
        }
    }

    void HandleJumpHold()
    {
        // While rising, button held, and still within the hold window -> add gentle upward force
        if (canHoldJump && holdJump && rb.linearVelocity.y > 0f && holdTimer < maxHoldTime)
        {
            holdTimer += Time.fixedDeltaTime;
        }
        else
        {
            canHoldJump = false; // stop extending once released or timer elapsed
        }
    }

    void TuneGravityForFeel()
    {
        // Variable jump height + snappier fall
        if (rb.linearVelocity.y < 0f)
        {
            rb.gravityScale = fallScale;
        }
        else if (rb.linearVelocity.y > 0f && (!holdJump || !canHoldJump))
        {
            rb.gravityScale = lowJumpScale;
        }
        else
        {
            rb.gravityScale = baseGravityScale;
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Ground"))
            isGrounded = true;
    }
}
