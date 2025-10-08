using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class AttackArea : MonoBehaviour
{
    private string[] pogoTargetTags = { "Ground" };

    private PlayerMovement player;

    private void Awake()
    {
        player = GetComponentInParent<PlayerMovement>();

        var col = GetComponent<Collider2D>();
        if (col != null && !col.isTrigger)
        {
            col.isTrigger = true;
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (player.attackDownward)
        {
            foreach (var tag in pogoTargetTags)
            {
                if (other.CompareTag(tag))
                {
                    player.Pogo();
                    break;
                }
            }
        }
    }
}
