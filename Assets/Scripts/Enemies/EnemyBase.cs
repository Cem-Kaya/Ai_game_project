using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public abstract class EnemyBase : MonoBehaviour
{
    public enum EnemyState { Idle, Patrol, Chase, Attack, Hurt, Dead }

    [Header("Stats")]
    [SerializeField] protected int maxHealth = 3;
    [SerializeField] protected float moveSpeed = 2f;
    [SerializeField] protected int contactDamage = 1;

    [Header("Physics")]
    [SerializeField] protected LayerMask groundLayer;
    [SerializeField] protected float gravityScale = 3f;

    [Header("Components (auto if null)")]
    [SerializeField] protected Rigidbody2D rb;
    [SerializeField] protected Collider2D hitbox;
    [SerializeField] protected SpriteRenderer sr;
    [SerializeField] protected Animator anim;

    protected int health;
    protected bool facingRight = true;
    protected EnemyState state = EnemyState.Idle;

    protected virtual void Awake()
    {
        rb = rb ? rb : GetComponent<Rigidbody2D>();
        hitbox = hitbox ? hitbox : GetComponent<Collider2D>();
        sr = sr ? sr : GetComponentInChildren<SpriteRenderer>();
        anim = anim ? anim : GetComponentInChildren<Animator>();
    }

    protected virtual void OnEnable()
    {
        health = maxHealth;
        SetState(EnemyState.Idle);
        rb.gravityScale = gravityScale;
    }

    protected void SetState(EnemyState next)
    {
        state = next;
        //if (anim) anim.SetInteger("State", (int)state);
    }

    protected void Move(float xVel)
    {
        if (state == EnemyState.Dead) return;
        rb.linearVelocity = new Vector2(xVel, rb.linearVelocity.y);
        if ((xVel > 0 && !facingRight) || (xVel < 0 && facingRight)) Flip();
    }

    protected void Flip()
    {
        facingRight = !facingRight;
        if (sr) sr.flipX = !sr.flipX; // Unity 6-safe; avoids legacy scale flipping issues
    }

    public virtual void TakeDamage(int dmg)
    {
        if (state == EnemyState.Dead) return;
        health -= Mathf.Max(1, dmg);
        //if (anim) anim.SetTrigger("Hurt");
        if (health <= 0) Die();
        else SetState(EnemyState.Hurt);
    }

    protected virtual void Die()
    {
        SetState(EnemyState.Dead);
        //if (anim) anim.SetTrigger("Die");
        hitbox.enabled = false;
        rb.linearVelocity = Vector2.zero;
        rb.bodyType = RigidbodyType2D.Kinematic; // Unity 6 supported
        Destroy(gameObject, 2f); // or via animation event
    }
}
