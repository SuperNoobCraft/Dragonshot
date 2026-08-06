using UnityEngine;

/// <summary>
/// Tunable look / combat settings for <see cref="DragonFireball"/> (set on DragonBoss).
/// Outline is opaque (CAVE-safe) — no transparent glow.
/// </summary>
[System.Serializable]
public struct DragonFireballSettings
{
    [Header("Motion")]
    [Min(0.5f)] public float speed;
    [Min(1f)] public float lifetime;
    [Tooltip("Hit radius around the player's eyes / aim point.")]
    [Min(0.15f)] public float playerHitRadius;

    [Header("Size / Look")]
    [Tooltip("Solid purple core diameter (meters).")]
    [Min(0.15f)] public float size;
    public Color coreColor;
    [Tooltip("Opaque magenta outline around the core (CAVE-safe).")]
    public Color outlineColor;
    [Tooltip("Outline shell thickness in local mesh units (~0.05–0.2).")]
    [Min(0.01f)] public float outlineWidth;
    public Color trailColor;
    [Tooltip("Trail length in seconds.")]
    [Min(0.05f)] public float trailTime;
    [Min(0.01f)] public float trailStartWidth;
    [Min(0.001f)] public float trailEndWidth;

    [Header("Explosion")]
    [Tooltip("How long the burst lasts (seconds).")]
    [Min(0.05f)] public float explodeDuration;
    [Tooltip("World-space radius the burst grows to.")]
    [Min(0.2f)] public float explodeRadius;
    public Color explodeColor;
    [Min(0f)] public float explodeLightIntensity;
    [Min(0.5f)] public float explodeLightRange;

    public static DragonFireballSettings Default => new DragonFireballSettings
    {
        speed = 3.2f,
        lifetime = 14f,
        playerHitRadius = 0.5f,
        size = 0.55f,
        coreColor = new Color(0.45f, 0.05f, 0.9f, 1f),
        outlineColor = new Color(1f, 0.2f, 0.95f, 1f),
        outlineWidth = 0.12f,
        trailColor = new Color(1f, 0.35f, 1f, 1f),
        trailTime = 0.7f,
        trailStartWidth = 0.35f,
        trailEndWidth = 0.04f,
        explodeDuration = 0.55f,
        explodeRadius = 2.8f,
        explodeColor = new Color(1f, 0.45f, 1f, 1f),
        explodeLightIntensity = 5f,
        explodeLightRange = 6f
    };
}

/// <summary>
/// Standalone explosion VFX — survives fireball destroy (e.g. player defeat clears fireballs).
/// </summary>
public class DragonFireballBurst : MonoBehaviour
{
    private DragonFireballSettings settings;
    private float elapsed;
    private Material burstMaterial;
    private Light flashLight;
    private float startDiameter;

    public static void Spawn(Vector3 worldPosition, DragonFireballSettings settings)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "FireballBurst";
        go.transform.position = worldPosition;
        go.transform.rotation = Quaternion.identity;

        float start = Mathf.Max(0.15f, settings.size);
        go.transform.localScale = Vector3.one * start;

        Collider col = go.GetComponent<Collider>();
        if (col != null)
        {
            Destroy(col);
        }

        DragonFireballBurst burst = go.AddComponent<DragonFireballBurst>();
        burst.settings = settings;
        burst.startDiameter = start;
        burst.elapsed = 0f;

        Renderer renderer = go.GetComponent<Renderer>();
        burst.burstMaterial = DragonFireball.CreateOpaqueUnlit(settings.explodeColor, "FireballBurstMat");
        if (renderer != null)
        {
            renderer.sharedMaterial = burst.burstMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        GameObject lightGo = new GameObject("FlashLight");
        lightGo.transform.SetParent(go.transform, false);
        burst.flashLight = lightGo.AddComponent<Light>();
        burst.flashLight.type = LightType.Point;
        burst.flashLight.color = settings.explodeColor;
        burst.flashLight.intensity = settings.explodeLightIntensity;
        burst.flashLight.range = settings.explodeLightRange;
        burst.flashLight.shadows = LightShadows.None;
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        float duration = Mathf.Max(0.05f, settings.explodeDuration);
        float t = Mathf.Clamp01(elapsed / duration);
        float pop = 1f - (1f - t) * (1f - t);

        float end = Mathf.Max(startDiameter, settings.explodeRadius * 2f);
        transform.localScale = Vector3.one * Mathf.Lerp(startDiameter, end, pop);

        if (burstMaterial != null)
        {
            Color c = Color.Lerp(settings.explodeColor, Color.white, pop * 0.45f);
            c.a = 1f;
            DragonFireball.ApplyColor(burstMaterial, c);
        }

        if (flashLight != null)
        {
            flashLight.intensity = Mathf.Lerp(settings.explodeLightIntensity, 0f, t);
            flashLight.range = Mathf.Lerp(settings.explodeLightRange, settings.explodeLightRange * 0.35f, t);
        }

        if (t >= 1f)
        {
            Destroy(gameObject);
        }
    }

    private void OnDestroy()
    {
        if (burstMaterial != null)
        {
            Destroy(burstMaterial);
        }
    }
}

/// <summary>
/// Slow purple fireball from the dragon. Destroyed by arrows; hits the player → defeat.
/// Solid purple core + opaque magenta outline (no transparency).
/// </summary>
[RequireComponent(typeof(SphereCollider))]
public class DragonFireball : MonoBehaviour
{
    private DragonBoss owner;
    private DragonFireballSettings settings;
    private Vector3 velocity;
    private float killTime;
    private bool resolved;
    private bool destroyedByArrow;
    private TrailRenderer trail;
    private Transform outlineRoot;
    private Renderer coreRenderer;
    private Material coreMaterial;
    private Material outlineMaterial;
    private Material trailMaterial;

    [Tooltip("If an in-flight arrow is this close, treat the fireball as shot down "
             + "instead of killing the player (fairness when arrow and body arrive together).")]
    private const float ArrowSaveRadiusPad = 0.75f;

    public static DragonFireball Spawn(
        Vector3 worldPosition,
        Vector3 direction,
        DragonBoss boss,
        DragonFireballSettings settings)
    {
        float diameter = Mathf.Max(0.15f, settings.size);

        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "DragonFireball";
        go.transform.position = worldPosition;
        go.transform.localScale = Vector3.one * diameter;

        SphereCollider sphere = go.GetComponent<SphereCollider>();
        if (sphere == null)
        {
            sphere = go.AddComponent<SphereCollider>();
        }

        sphere.isTrigger = true;
        sphere.radius = 0.5f;

        Rigidbody body = go.GetComponent<Rigidbody>();
        if (body == null)
        {
            body = go.AddComponent<Rigidbody>();
        }

        body.isKinematic = true;
        body.useGravity = false;
        body.detectCollisions = true;

        DragonFireball fireball = go.AddComponent<DragonFireball>();
        fireball.settings = settings;
        fireball.Launch(direction.normalized, boss);
        return fireball;
    }

    public void Launch(Vector3 direction, DragonBoss boss)
    {
        owner = boss;
        if (direction.sqrMagnitude < 1e-6f)
        {
            direction = Vector3.forward;
        }

        float speed = Mathf.Max(0.5f, settings.speed);
        velocity = direction.normalized * speed;
        killTime = Time.time + Mathf.Max(1f, settings.lifetime);
        resolved = false;
        destroyedByArrow = false;
        transform.localScale = Vector3.one * Mathf.Max(0.15f, settings.size);
        BuildVisuals();
        IgnoreDragonColliders();
    }

    private void Update()
    {
        if (resolved)
        {
            return;
        }

        transform.position += velocity * Time.deltaTime;

        if (Time.time >= killTime)
        {
            BeginExplode(hitPlayer: false);
            return;
        }

        // Prefer arrow save over player kill when both are about to happen.
        if (TryConsumeNearbyArrow())
        {
            BeginExplode(hitPlayer: false);
            return;
        }

        float hitR = Mathf.Max(0.15f, settings.playerHitRadius);
        Vector3 playerPos = PlayEnvironment.ResolvePlayerAimPosition();
        if ((transform.position - playerPos).sqrMagnitude <= hitR * hitR)
        {
            // Last-chance arrow check inside the kill radius.
            if (TryConsumeNearbyArrow())
            {
                BeginExplode(hitPlayer: false);
                return;
            }

            BeginExplode(hitPlayer: true);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        HandleCollider(other);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision != null)
        {
            HandleCollider(collision.collider);
        }
    }

    private void HandleCollider(Collider other)
    {
        if (resolved || other == null)
        {
            return;
        }

        if (owner != null && other.transform.IsChildOf(owner.transform))
        {
            return;
        }

        ArrowProjectile arrow = other.GetComponentInParent<ArrowProjectile>();
        if (arrow != null && (arrow.IsInFlight || arrow.HasStuck))
        {
            destroyedByArrow = true;
            Destroy(arrow.gameObject);
            BeginExplode(hitPlayer: false);
            return;
        }

        if (IsPlayerCollider(other))
        {
            if (TryConsumeNearbyArrow())
            {
                BeginExplode(hitPlayer: false);
                return;
            }

            BeginExplode(hitPlayer: true);
        }
    }

    private bool TryConsumeNearbyArrow()
    {
        float saveRadius = Mathf.Max(0.4f, settings.size * 0.5f + ArrowSaveRadiusPad);
        ArrowProjectile arrow = ArrowManager.FindNearestInFlight(transform.position, saveRadius);
        if (arrow == null)
        {
            return false;
        }

        destroyedByArrow = true;
        Destroy(arrow.gameObject);
        return true;
    }

    private static bool IsPlayerCollider(Collider other)
    {
        if (other == null)
        {
            return false;
        }

        string n = other.transform.name;
        if (n.IndexOf("Vision", System.StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("Head", System.StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("Player", System.StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("Camera", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        return other.GetComponentInParent<Camera>() != null;
    }

    private void BeginExplode(bool hitPlayer)
    {
        if (resolved)
        {
            return;
        }

        resolved = true;
        velocity = Vector3.zero;

        if (trail != null)
        {
            trail.emitting = false;
        }

        Vector3 blastPos = transform.position;
        // Independent VFX — must not live on this object; defeat clears fireballs immediately.
        DragonFireballBurst.Spawn(blastPos, settings);
        FightAudio.PlayFireballExplode(blastPos);

        DragonBoss boss = owner;
        // Arrow shot-down can never defeat the player, even if the blast is in their face.
        bool defeat = hitPlayer && !destroyedByArrow && boss != null && boss.IsFightActive;

        if (owner != null)
        {
            owner.UnregisterFireball(this);
            owner = null;
        }

        if (defeat)
        {
            boss.NotifyPlayerHitByFireball(this);
        }

        // Remove the projectile; burst keeps playing on its own.
        Destroy(gameObject);
    }

    private void BuildVisuals()
    {
        coreRenderer = GetComponent<Renderer>();
        if (coreRenderer != null)
        {
            coreMaterial = CreateOpaqueUnlit(settings.coreColor, "DragonFireballCore");
            coreRenderer.sharedMaterial = coreMaterial;
            coreRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            coreRenderer.receiveShadows = false;
        }

        GameObject outlineGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        outlineGo.name = "MagentaOutline";
        outlineGo.transform.SetParent(transform, false);
        outlineGo.transform.localPosition = Vector3.zero;
        outlineGo.transform.localRotation = Quaternion.identity;
        outlineGo.transform.localScale = Vector3.one;
        outlineRoot = outlineGo.transform;

        Collider outlineCol = outlineGo.GetComponent<Collider>();
        if (outlineCol != null)
        {
            Destroy(outlineCol);
        }

        Renderer outlineRenderer = outlineGo.GetComponent<Renderer>();
        outlineMaterial = CreateOutlineMaterial(settings.outlineColor, settings.outlineWidth);
        if (outlineRenderer != null)
        {
            outlineRenderer.sharedMaterial = outlineMaterial;
            outlineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            outlineRenderer.receiveShadows = false;
        }

        if (outlineMaterial != null && !outlineMaterial.HasProperty("_OutlineWidth"))
        {
            float inflate = 1f + Mathf.Max(0.01f, settings.outlineWidth) * 3f;
            outlineGo.transform.localScale = Vector3.one * inflate;
        }

        float size = Mathf.Max(0.15f, settings.size);
        trail = gameObject.AddComponent<TrailRenderer>();
        trail.time = Mathf.Max(0.05f, settings.trailTime);
        trail.startWidth = settings.trailStartWidth > 0.01f ? settings.trailStartWidth : size * 0.7f;
        trail.endWidth = Mathf.Max(0.001f, settings.trailEndWidth);
        trail.minVertexDistance = 0.04f;
        trail.numCapVertices = 3;
        trail.autodestruct = false;
        trail.emitting = true;
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        Color trailCol = settings.trailColor;
        trailCol.a = 1f;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(trailCol, 0f),
                new GradientColorKey(settings.outlineColor, 0.5f),
                new GradientColorKey(settings.coreColor, 1f)
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 0.5f),
                new GradientAlphaKey(0f, 1f)
            });
        trail.colorGradient = gradient;

        Shader sprites = Shader.Find("Sprites/Default");
        if (sprites != null)
        {
            trailMaterial = new Material(sprites);
            trailMaterial.name = "DragonFireballTrail";
            trailMaterial.color = Color.white;
        }
        else
        {
            trailMaterial = CreateOpaqueUnlit(Color.white, "DragonFireballTrail");
        }

        trail.sharedMaterial = trailMaterial;
    }

    private void IgnoreDragonColliders()
    {
        if (owner == null)
        {
            return;
        }

        Collider self = GetComponent<Collider>();
        if (self == null)
        {
            return;
        }

        Collider[] cols = owner.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] != null)
            {
                Physics.IgnoreCollision(self, cols[i], true);
            }
        }
    }

    private static Material CreateOutlineMaterial(Color color, float outlineWidth)
    {
        Material fromResources = Resources.Load<Material>("DragonShieldOutline");
        if (fromResources != null)
        {
            Material mat = new Material(fromResources);
            mat.name = "DragonFireballOutline";
            ApplyColor(mat, color);
            if (mat.HasProperty("_OutlineWidth"))
            {
                mat.SetFloat("_OutlineWidth", Mathf.Max(0.01f, outlineWidth));
            }

            return mat;
        }

        Shader outlineShader = Shader.Find("VotanicBow/CrystalShieldGlow");
        if (outlineShader != null)
        {
            Material mat = new Material(outlineShader);
            mat.name = "DragonFireballOutline";
            ApplyColor(mat, color);
            if (mat.HasProperty("_OutlineWidth"))
            {
                mat.SetFloat("_OutlineWidth", Mathf.Max(0.01f, outlineWidth));
            }

            return mat;
        }

        return CreateOpaqueUnlit(color, "DragonFireballOutlineFallback");
    }

    public static Material CreateOpaqueUnlit(Color color, string name)
    {
        color.a = 1f;
        string[] shaders =
        {
            "Unlit/Color",
            "Universal Render Pipeline/Unlit",
            "Sprites/Default"
        };

        for (int i = 0; i < shaders.Length; i++)
        {
            Shader shader = Shader.Find(shaders[i]);
            if (shader == null)
            {
                continue;
            }

            Material mat = new Material(shader);
            mat.name = name;
            ApplyColor(mat, color);
            return mat;
        }

        return new Material(Shader.Find("Hidden/InternalErrorShader"));
    }

    public static void ApplyColor(Material mat, Color color)
    {
        if (mat == null)
        {
            return;
        }

        if (mat.HasProperty("_Color"))
        {
            mat.color = color;
        }

        if (mat.HasProperty("_BaseColor"))
        {
            mat.SetColor("_BaseColor", color);
        }
    }

    private void OnDestroy()
    {
        if (coreMaterial != null)
        {
            Destroy(coreMaterial);
        }

        if (outlineMaterial != null)
        {
            Destroy(outlineMaterial);
        }

        if (trailMaterial != null)
        {
            Destroy(trailMaterial);
        }

        if (owner != null)
        {
            owner.UnregisterFireball(this);
        }
    }
}
