using UnityEngine;

/// <summary>
/// Shootable crystal that feeds a beam into <see cref="DragonBoss"/>.
/// While any crystal is alive, the dragon keeps its shield.
/// </summary>
[RequireComponent(typeof(SphereCollider))]
public class EnderCrystal : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private DragonBoss dragon;
    [Tooltip("Where the beam starts. Defaults to this transform.")]
    [SerializeField] private Transform beamOrigin;
    [Tooltip("Optional visual root destroyed with the crystal (glow mesh, etc.).")]
    [SerializeField] private GameObject crystalVisual;

    [Header("Beam")]
    [SerializeField] private bool createBeamIfMissing = true;
    [SerializeField] private Color beamColor = new Color(1f, 0.35f, 1f, 0.95f);
    [SerializeField] private float beamWidth = 0.08f;
    [SerializeField] private float beamScrollSpeed = 1.5f;

    private LineRenderer beam;
    private bool destroyed;
    private Material beamMaterial;

    public bool IsAlive => !destroyed;

    public void Bind(DragonBoss owner)
    {
        dragon = owner;
        if (dragon != null)
        {
            beamColor = dragon.CrystalEnergyColor;
            // Keep beams a bit more solid than the soft shield fill.
            beamColor.a = Mathf.Max(0.75f, beamColor.a);
        }

        EnsureBeam();
        ApplyBeamColor(beamColor);
    }

    private void Awake()
    {
        if (beamOrigin == null)
        {
            beamOrigin = transform;
        }

        if (dragon == null)
        {
            dragon = FindObjectOfType<DragonBoss>();
        }

        EnsureBeam();
    }

    private void Start()
    {
        if (dragon != null)
        {
            dragon.RegisterCrystal(this);
        }
        else
        {
            Debug.LogWarning("EnderCrystal: no DragonBoss found — crystal will still be shootable.", this);
        }
    }

    private void LateUpdate()
    {
        if (destroyed || beam == null)
        {
            return;
        }

        bool showBeam = dragon == null || dragon.IsFightActive;
        if (beam.enabled != showBeam)
        {
            beam.enabled = showBeam;
        }

        if (!showBeam)
        {
            return;
        }

        Vector3 start = beamOrigin != null ? beamOrigin.position : transform.position;
        Vector3 end = dragon != null ? dragon.ShieldAttachPoint : start + Vector3.up * 2f;

        beam.positionCount = 2;
        beam.SetPosition(0, start);
        beam.SetPosition(1, end);

        if (beamMaterial != null && beamMaterial.HasProperty("_MainTex"))
        {
            Vector2 offset = beamMaterial.mainTextureOffset;
            offset.x -= beamScrollSpeed * Time.deltaTime;
            beamMaterial.mainTextureOffset = offset;
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
        if (destroyed || other == null)
        {
            return;
        }

        if (dragon != null && !dragon.IsFightActive)
        {
            return;
        }

        ArrowProjectile arrow = other.GetComponentInParent<ArrowProjectile>();
        if (arrow == null || (!arrow.IsInFlight && !arrow.HasStuck))
        {
            return;
        }

        destroyed = true;
        Destroy(arrow.gameObject);
        FightAudio.PlayCrystalExplode(transform.position);

        if (beam != null)
        {
            beam.enabled = false;
        }

        if (dragon != null)
        {
            dragon.NotifyCrystalDestroyed(this);
        }

        SetCrystalActiveVisual(false);
        // Stay in the scene so the fight can reset / revive crystals.
    }

    /// <summary>
    /// Restores a crystal after it was shot (used by fight reset).
    /// </summary>
    public void Revive()
    {
        destroyed = false;
        SetCrystalActiveVisual(true);

        if (beam != null)
        {
            beam.enabled = true;
        }

        EnsureBeam();
        ApplyBeamColor(beamColor);

        if (dragon != null)
        {
            dragon.RegisterCrystal(this);
        }
    }

    private void SetCrystalActiveVisual(bool active)
    {
        Collider[] cols = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] != null)
            {
                cols[i].enabled = active;
            }
        }

        if (crystalVisual != null)
        {
            crystalVisual.SetActive(active);
        }

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null)
            {
                continue;
            }

            if (beam != null && renderers[i] == beam)
            {
                renderers[i].enabled = active;
                continue;
            }

            renderers[i].enabled = active;
        }
    }

    private void OnDestroy()
    {
        if (beamMaterial != null)
        {
            Destroy(beamMaterial);
            beamMaterial = null;
        }

        if (!destroyed && dragon != null)
        {
            dragon.NotifyCrystalDestroyed(this);
        }
    }

    private void EnsureBeam()
    {
        beam = GetComponent<LineRenderer>();
        if (beam == null && createBeamIfMissing)
        {
            beam = gameObject.AddComponent<LineRenderer>();
        }

        if (beam == null)
        {
            return;
        }

        beam.positionCount = 2;
        beam.startWidth = beamWidth;
        beam.endWidth = beamWidth * 0.55f;
        beam.numCapVertices = 4;
        beam.numCornerVertices = 2;
        beam.useWorldSpace = true;
        beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        beam.receiveShadows = false;
        beam.alignment = LineAlignment.View;

        if (beam.sharedMaterial == null)
        {
            beamMaterial = CreateBeamMaterial(beamColor);
            beam.sharedMaterial = beamMaterial;
        }
        else
        {
            beamMaterial = beam.material;
        }

        ApplyBeamColor(beamColor);
        beam.enabled = true;
    }

    private void ApplyBeamColor(Color color)
    {
        if (beam == null)
        {
            return;
        }

        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(color, 0f),
                new GradientColorKey(color, 1f)
            },
            new[]
            {
                new GradientAlphaKey(color.a, 0f),
                new GradientAlphaKey(color.a * 0.65f, 1f)
            });
        beam.colorGradient = gradient;

        if (beamMaterial != null)
        {
            if (beamMaterial.HasProperty("_Color"))
            {
                beamMaterial.color = color;
            }

            if (beamMaterial.HasProperty("_BaseColor"))
            {
                beamMaterial.SetColor("_BaseColor", color);
            }
        }
    }

    private static Material CreateBeamMaterial(Color color)
    {
        string[] shaderNames =
        {
            "Sprites/Default",
            "Unlit/Color",
            "Universal Render Pipeline/Unlit",
            "Particles/Standard Unlit"
        };

        for (int i = 0; i < shaderNames.Length; i++)
        {
            Shader shader = Shader.Find(shaderNames[i]);
            if (shader == null)
            {
                continue;
            }

            Material mat = new Material(shader);
            mat.name = "CrystalBeam";
            if (mat.HasProperty("_Color"))
            {
                mat.color = color;
            }

            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", color);
            }

            return mat;
        }

        return new Material(Shader.Find("Hidden/InternalErrorShader"));
    }

#if UNITY_EDITOR
    [ContextMenu("Create Placeholder Crystal Mesh")]
    private void CreatePlaceholderMesh()
    {
        if (transform.Find("CrystalVisual") != null)
        {
            return;
        }

        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        visual.name = "CrystalVisual";
        visual.transform.SetParent(transform, false);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localScale = Vector3.one * 0.55f;

        Collider childCol = visual.GetComponent<Collider>();
        if (childCol != null)
        {
            DestroyImmediate(childCol);
        }

        Renderer renderer = visual.GetComponent<Renderer>();
        if (renderer != null)
        {
            Material mat = new Material(
                Shader.Find("Standard") != null
                    ? Shader.Find("Standard")
                    : Shader.Find("Universal Render Pipeline/Lit"));
            Color c = new Color(1f, 0.4f, 1f, 1f);
            if (mat.HasProperty("_Color"))
            {
                mat.color = c;
            }

            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", c);
            }

            if (mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", c * 1.5f);
            }

            renderer.sharedMaterial = mat;
        }

        crystalVisual = visual;
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}
