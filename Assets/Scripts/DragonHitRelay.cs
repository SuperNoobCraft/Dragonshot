using UnityEngine;

/// <summary>
/// Forwards arrow hits from child mesh colliders to <see cref="DragonBoss"/> on the parent.
/// Auto-added when mesh hit colliders are built on the dragon visual.
/// </summary>
public class DragonHitRelay : MonoBehaviour
{
    private DragonBoss boss;

    public void Bind(DragonBoss dragon)
    {
        boss = dragon;
    }

    private void Awake()
    {
        if (boss == null)
        {
            boss = GetComponentInParent<DragonBoss>();
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (boss == null)
        {
            boss = GetComponentInParent<DragonBoss>();
        }

        if (boss == null || collision == null)
        {
            return;
        }

        boss.HandleArrowFromCollider(collision.collider);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (boss == null)
        {
            boss = GetComponentInParent<DragonBoss>();
        }

        if (boss == null)
        {
            return;
        }

        boss.HandleArrowFromCollider(other);
    }
}
