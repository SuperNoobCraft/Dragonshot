using UnityEngine;

/// <summary>
/// Shootable target for <see cref="TargetPracticeGame"/>. Destroyed on arrow hit.
/// </summary>
public class ArcheryTarget : MonoBehaviour
{
    private TargetPracticeGame game;
    private bool claimed;
    private bool isStarter;

    public bool IsStarter => isStarter;

    public void Bind(TargetPracticeGame owner, bool starter = false)
    {
        game = owner;
        claimed = false;
        isStarter = starter;
    }

    private void OnCollisionEnter(Collision collision)
    {
        TryHit(collision.collider);
    }

    private void OnTriggerEnter(Collider other)
    {
        TryHit(other);
    }

    private void TryHit(Collider other)
    {
        if (claimed || other == null)
        {
            return;
        }

        ArrowProjectile arrow = other.GetComponentInParent<ArrowProjectile>();
        if (arrow == null || (!arrow.IsInFlight && !arrow.HasStuck))
        {
            return;
        }

        claimed = true;

        if (arrow != null)
        {
            Destroy(arrow.gameObject);
        }

        if (game != null)
        {
            game.NotifyTargetHit(this);
        }
        else
        {
            Destroy(gameObject);
        }
    }
}
