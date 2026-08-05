using UnityEngine;

/// <summary>
/// Shootable target for <see cref="TargetPracticeGame"/>. Destroyed on arrow hit.
/// Preview (next) targets use a different solid color and ignore collisions until promoted.
/// </summary>
public class ArcheryTarget : MonoBehaviour
{
    private TargetPracticeGame game;
    private bool claimed;
    private bool isStarter;
    private bool isPreview;
    private Collider[] colliders;
    private Renderer[] renderers;
    private Material[] instanceMaterials;
    private bool[] usesBaseColorProp;
    private bool visualsCached;

    public bool IsStarter => isStarter;
    public bool IsPreview => isPreview;

    public void Bind(TargetPracticeGame owner, bool starter = false, bool preview = false)
    {
        game = owner;
        claimed = false;
        isStarter = starter;
        CacheVisualsAndColliders();
        SetPreview(preview);
    }

    /// <summary>
    /// Ghost next-target: different color, non-colliding so players can pre-aim.
    /// </summary>
    public void SetPreview(bool preview)
    {
        isPreview = preview;
        claimed = false;
        CacheVisualsAndColliders();

        if (colliders != null)
        {
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                {
                    colliders[i].enabled = !preview;
                }
            }
        }

        ApplyStateColor(preview);
    }

    private void CacheVisualsAndColliders()
    {
        if (colliders == null)
        {
            colliders = GetComponentsInChildren<Collider>(true);
        }

        if (visualsCached)
        {
            return;
        }

        renderers = GetComponentsInChildren<Renderer>(true);
        int matCount = 0;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
            {
                matCount += renderers[i].materials.Length;
            }
        }

        instanceMaterials = new Material[matCount];
        usesBaseColorProp = new bool[matCount];

        int index = 0;
        for (int r = 0; r < renderers.Length; r++)
        {
            Renderer renderer = renderers[r];
            if (renderer == null)
            {
                continue;
            }

            Material[] mats = renderer.materials;
            for (int m = 0; m < mats.Length; m++)
            {
                Material mat = mats[m];
                instanceMaterials[index] = mat;
                usesBaseColorProp[index] = mat != null && mat.HasProperty("_BaseColor");
                index++;
            }

            renderer.materials = mats;
        }

        visualsCached = true;
    }

    private void ApplyStateColor(bool preview)
    {
        if (instanceMaterials == null)
        {
            return;
        }

        Color color;
        if (game != null)
        {
            color = preview ? game.PreviewTargetColor : game.ActiveTargetColor;
        }
        else
        {
            color = preview
                ? new Color(0.2f, 0.75f, 1f, 1f)
                : new Color(0.85f, 0.15f, 0.15f, 1f);
        }

        color.a = 1f;

        for (int i = 0; i < instanceMaterials.Length; i++)
        {
            Material mat = instanceMaterials[i];
            if (mat == null)
            {
                continue;
            }

            if (usesBaseColorProp[i] && mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", color);
            }
            else if (mat.HasProperty("_Color"))
            {
                mat.color = color;
            }
        }
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
        if (claimed || isPreview || other == null)
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
