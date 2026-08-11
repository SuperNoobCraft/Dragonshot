using UnityEngine;

/// <summary>
/// Tunable look / combat settings for <see cref="DragonFireball"/> (set on DragonBoss).
/// All visuals are opaque (CAVE-safe) — no transparent glow.
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
    [Tooltip("Overall fireball scale (meters).")]
    [Min(0.15f)] public float size;
    [Tooltip("Unused for head-on look (kept for older scenes).")]
    [Min(1f)] public float bodyStretch;
    [Tooltip("Spin rate of the outer flame petals (deg/sec). Visible head-on.")]
    public float coronaSpinSpeed;
    [Tooltip("How large the frontal corona is vs the core (1.2–2).")]
    [Min(1f)] public float coronaScale;
    public Color coreColor;
    public Color midColor;
    [Tooltip("Opaque magenta outline (CAVE-safe inverted hull).")]
    public Color outlineColor;
    [Min(0.01f)] public float outlineWidth;
    public Color emberColor;
    public Color sparkColor;
    [Tooltip("Opaque mesh sparks that spray outward in a ring (visible head-on).")]
    public bool enableEmbers;
    [Min(0f)] public float emberRate;
    [Min(0.02f)] public float emberLifetime;

    [Header("Explosion")]
    [Min(0.05f)] public float explodeDuration;
    [Min(0.2f)] public float explodeRadius;
    public Color explodeColor;
    [Min(0)] public int explodeSparkCount;
    [Min(0f)] public float explodeLightIntensity;
    [Min(0.5f)] public float explodeLightRange;

    public static DragonFireballSettings Default => new DragonFireballSettings
    {
        speed = 3.2f,
        lifetime = 14f,
        playerHitRadius = 0.5f,
        size = 0.55f,
        bodyStretch = 1.7f,
        coronaSpinSpeed = 220f,
        coronaScale = 1.55f,
        coreColor = new Color(0.85f, 0.55f, 1f, 1f),
        midColor = new Color(0.45f, 0.05f, 0.9f, 1f),
        outlineColor = new Color(1f, 0.15f, 0.9f, 1f),
        outlineWidth = 0.1f,
        emberColor = new Color(0.7f, 0.1f, 1f, 1f),
        sparkColor = new Color(1f, 0.45f, 0.95f, 1f),
        enableEmbers = true,
        emberRate = 36f,
        emberLifetime = 0.35f,
        explodeDuration = 0.55f,
        explodeRadius = 2.8f,
        explodeColor = new Color(1f, 0.4f, 1f, 1f),
        explodeSparkCount = 22,
        explodeLightIntensity = 5f,
        explodeLightRange = 6f
    };
}

/// <summary>
/// Standalone explosion: opaque expanding core + opaque mesh sparks (no transparency).
/// </summary>
public class DragonFireballBurst : MonoBehaviour
{
    private DragonFireballSettings settings;
    private float elapsed;
    private Material burstMaterial;
    private Material sparkMaterial;
    private Light flashLight;
    private float startDiameter;
    private ParticleSystem sparks;

    public static void Spawn(Vector3 worldPosition, DragonFireballSettings settings)
    {
        GameObject go = new GameObject("FireballBurst");
        go.transform.position = worldPosition;

        DragonFireballBurst burst = go.AddComponent<DragonFireballBurst>();
        burst.settings = settings;
        burst.startDiameter = Mathf.Max(0.15f, settings.size);
        burst.elapsed = 0f;
        burst.Build(worldPosition);
    }

    private void Build(Vector3 worldPosition)
    {
        GameObject core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        core.name = "BurstCore";
        core.transform.SetParent(transform, false);
        core.transform.localScale = Vector3.one * startDiameter;
        Destroy(core.GetComponent<Collider>());

        Renderer coreRenderer = core.GetComponent<Renderer>();
        burstMaterial = DragonFireball.CreateOpaqueUnlit(settings.explodeColor, "FireballBurstMat");
        if (coreRenderer != null)
        {
            coreRenderer.sharedMaterial = burstMaterial;
            coreRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            coreRenderer.receiveShadows = false;
        }

        GameObject lightGo = new GameObject("FlashLight");
        lightGo.transform.SetParent(transform, false);
        flashLight = lightGo.AddComponent<Light>();
        flashLight.type = LightType.Point;
        flashLight.color = settings.explodeColor;
        flashLight.intensity = settings.explodeLightIntensity;
        flashLight.range = settings.explodeLightRange;
        flashLight.shadows = LightShadows.None;

        if (settings.explodeSparkCount > 0)
        {
            sparks = DragonFireball.CreateOpaqueMeshParticleSystem(
                transform,
                "BurstSparks",
                settings.sparkColor,
                out sparkMaterial);

            var main = sparks.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = Mathf.Max(0.05f, settings.explodeDuration);
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(
                settings.explodeRadius * 1.5f,
                settings.explodeRadius * 4f);
            main.startSize = new ParticleSystem.MinMaxCurve(
                startDiameter * 0.12f,
                startDiameter * 0.28f);
            main.startColor = OpaqueColor(settings.sparkColor);
            main.gravityModifier = 0.15f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = Mathf.Max(8, settings.explodeSparkCount);

            var emission = sparks.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0f, (short)Mathf.Clamp(settings.explodeSparkCount, 1, 64))
            });

            var shape = sparks.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = startDiameter * 0.2f;

            ConfigureOpaqueParticleLifetime(sparks, settings.explodeColor, settings.sparkColor);
            sparks.Play(true);
        }
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        float duration = Mathf.Max(0.05f, settings.explodeDuration);
        float t = Mathf.Clamp01(elapsed / duration);
        float pop = 1f - (1f - t) * (1f - t);

        float end = Mathf.Max(startDiameter, settings.explodeRadius * 2f);
        Transform core = transform.Find("BurstCore");
        if (core != null)
        {
            core.localScale = Vector3.one * Mathf.Lerp(startDiameter, end, pop);
        }

        if (burstMaterial != null)
        {
            Color c = Color.Lerp(settings.explodeColor, settings.sparkColor, pop * 0.55f);
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

        if (sparkMaterial != null)
        {
            Destroy(sparkMaterial);
        }
    }

    private static Color OpaqueColor(Color c)
    {
        c.a = 1f;
        return c;
    }

    private static void ConfigureOpaqueParticleLifetime(
        ParticleSystem ps, Color start, Color end)
    {
        var colorOver = ps.colorOverLifetime;
        colorOver.enabled = true;
        Gradient g = new Gradient();
        Color a = start;
        a.a = 1f;
        Color b = end;
        b.a = 1f;
        g.SetKeys(
            new[] { new GradientColorKey(a, 0f), new GradientColorKey(b, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        colorOver.color = g;

        var sizeOver = ps.sizeOverLifetime;
        sizeOver.enabled = true;
        sizeOver.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
    }
}

/// <summary>
/// Slow purple fireball. Opaque teardrop body + opaque mesh embers (CAVE-safe).
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
    private Transform visualRoot;
    private Transform coreRoot;
    private Transform coronaSpinRoot;
    private ParticleSystem emberParticles;
    private Material coreMaterial;
    private Material midMaterial;
    private Material coronaMaterial;
    private Material outlineMaterial;
    private Material emberMaterial;
    private float spinAngle;
    private readonly System.Collections.Generic.List<Material> ownedMaterials =
        new System.Collections.Generic.List<Material>(8);

    private const float ArrowSaveRadiusPad = 0.75f;

    /// <summary>
    /// Visual motion scale vs default travel speed (3.2). Slow fireballs spin/pulse less frantically.
    /// </summary>
    private float AnimMotionScale
    {
        get
        {
            float reference = Mathf.Max(0.5f, DragonFireballSettings.Default.speed);
            return Mathf.Clamp(settings.speed / reference, 0.25f, 2.5f);
        }
    }

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
        // Collider scale stays spherical for fair hits; visuals live under VisualRoot.
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

        // Hide the collider mesh — visuals are custom.
        Renderer rootRenderer = go.GetComponent<Renderer>();
        if (rootRenderer != null)
        {
            rootRenderer.enabled = false;
        }

        DragonFireball fireball = go.AddComponent<DragonFireball>();
        fireball.settings = settings;
        fireball.Launch(direction.normalized, boss);
        return fireball;
    }

    public void Launch(Vector3 direction, DragonBoss boss)
    {
        owner = boss;
        NormalizeSettings();
        if (direction.sqrMagnitude < 1e-6f)
        {
            direction = Vector3.forward;
        }

        float speed = Mathf.Max(0.5f, settings.speed);
        velocity = direction.normalized * speed;
        killTime = Time.time + Mathf.Max(1f, settings.lifetime);
        resolved = false;
        destroyedByArrow = false;
        BuildVisuals();
        FaceVelocity();
        IgnoreDragonColliders();
    }

    private void NormalizeSettings()
    {
        DragonFireballSettings d = DragonFireballSettings.Default;
        if (settings.size < 0.15f)
        {
            settings.size = d.size;
        }

        if (settings.bodyStretch < 1f)
        {
            settings.bodyStretch = d.bodyStretch;
        }

        if (Mathf.Abs(settings.coronaSpinSpeed) < 1f)
        {
            settings.coronaSpinSpeed = d.coronaSpinSpeed;
        }

        if (settings.coronaScale < 1f)
        {
            settings.coronaScale = d.coronaScale;
        }

        if (settings.emberRate <= 0.1f && settings.enableEmbers)
        {
            settings.emberRate = d.emberRate;
        }

        // Older serialized settings: enable embers by default when field was missing (false).
        if (!settings.enableEmbers && settings.emberLifetime <= 0.01f)
        {
            settings.enableEmbers = true;
            settings.emberRate = d.emberRate;
            settings.emberLifetime = d.emberLifetime;
            settings.emberColor = d.emberColor;
            settings.sparkColor = d.sparkColor;
            settings.midColor = d.midColor;
            settings.coreColor = d.coreColor;
        }

        if (settings.emberLifetime < 0.05f)
        {
            settings.emberLifetime = d.emberLifetime;
        }

        if (settings.explodeSparkCount <= 0)
        {
            settings.explodeSparkCount = d.explodeSparkCount;
        }
    }

    private void Update()
    {
        if (resolved)
        {
            return;
        }

        transform.position += velocity * Time.deltaTime;
        FaceVelocity();
        PulseCore();

        if (Time.time >= killTime)
        {
            BeginExplode(hitPlayer: false);
            return;
        }

        if (TryConsumeNearbyArrow())
        {
            BeginExplode(hitPlayer: false);
            return;
        }

        float hitR = Mathf.Max(0.15f, settings.playerHitRadius);
        Vector3 playerPos = PlayEnvironment.ResolvePlayerAimPosition();
        if ((transform.position - playerPos).sqrMagnitude <= hitR * hitR)
        {
            if (TryConsumeNearbyArrow())
            {
                BeginExplode(hitPlayer: false);
                return;
            }

            BeginExplode(hitPlayer: true);
        }
    }

    private void FaceVelocity()
    {
        if (visualRoot == null || velocity.sqrMagnitude < 1e-6f)
        {
            return;
        }

        visualRoot.rotation = Quaternion.LookRotation(velocity.normalized, Vector3.up);
    }

    private void PulseCore()
    {
        float anim = AnimMotionScale;
        if (coreRoot != null)
        {
            float pulse = 1f + 0.12f * Mathf.Sin(Time.time * 16f * anim);
            float breathe = 1f + 0.06f * Mathf.Sin(Time.time * 7f * anim + 1.3f);
            coreRoot.localScale = Vector3.one * (pulse * breathe);
        }

        if (coronaSpinRoot != null)
        {
            spinAngle += settings.coronaSpinSpeed * anim * Time.deltaTime;
            coronaSpinRoot.localRotation = Quaternion.Euler(0f, 0f, spinAngle);
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

        if (emberParticles != null)
        {
            var emission = emberParticles.emission;
            emission.enabled = false;
            emberParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        Vector3 blastPos = transform.position;
        DragonFireballBurst.Spawn(blastPos, settings);
        FightAudio.PlayFireballExplode(blastPos);

        DragonBoss boss = owner;
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

        Destroy(gameObject);
    }

    private void BuildVisuals()
    {
        // Built for HEAD-ON viewing: player looks down the flight axis, so trails
        // behind the ball are invisible. Everything lives in the frontal plane.
        GameObject visual = new GameObject("VisualRoot");
        visual.transform.SetParent(transform, false);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;
        visual.transform.localScale = Vector3.one;
        visualRoot = visual.transform;

        float corona = Mathf.Max(1.1f, settings.coronaScale);

        // Deep back plate — flattened opaque disc (reads as a halo from the front).
        CreateSolidSphere(
            visual.transform,
            "HaloDisc",
            new Vector3(0f, 0f, -0.05f),
            new Vector3(corona * 1.05f, corona * 1.05f, 0.12f),
            settings.emberColor,
            out coronaMaterial);

        // Main body.
        CreateSolidSphere(
            visual.transform,
            "Body",
            Vector3.zero,
            0.78f,
            settings.midColor,
            out midMaterial);

        Transform body = visual.transform.Find("Body");
        if (body != null)
        {
            GameObject outlineGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            outlineGo.name = "MagentaOutline";
            outlineGo.transform.SetParent(body, false);
            outlineGo.transform.localPosition = Vector3.zero;
            outlineGo.transform.localScale = Vector3.one;
            Destroy(outlineGo.GetComponent<Collider>());

            Renderer outlineRenderer = outlineGo.GetComponent<Renderer>();
            outlineMaterial = CreateOutlineMaterial(settings.outlineColor, settings.outlineWidth);
            TrackMaterial(outlineMaterial);
            if (outlineRenderer != null)
            {
                outlineRenderer.sharedMaterial = outlineMaterial;
                outlineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                outlineRenderer.receiveShadows = false;
            }

            if (outlineMaterial != null && !outlineMaterial.HasProperty("_OutlineWidth"))
            {
                outlineGo.transform.localScale = Vector3.one * (1f + settings.outlineWidth * 3f);
            }
        }

        // Hot core in front of body (always facing the player).
        coreRoot = CreateSolidSphere(
            visual.transform,
            "HotCore",
            new Vector3(0f, 0f, 0.12f),
            0.48f,
            settings.coreColor,
            out coreMaterial).transform;

        // Spinning petal crown — silhouette churns when viewed head-on.
        GameObject spinner = new GameObject("CoronaSpin");
        spinner.transform.SetParent(visual.transform, false);
        spinner.transform.localPosition = Vector3.zero;
        coronaSpinRoot = spinner.transform;

        int petals = 6;
        float petalRadius = 0.52f * corona;
        for (int i = 0; i < petals; i++)
        {
            float ang = i * Mathf.PI * 2f / petals;
            Vector3 pos = new Vector3(Mathf.Cos(ang) * petalRadius, Mathf.Sin(ang) * petalRadius, 0.02f);
            Color petalColor = (i % 2 == 0) ? settings.sparkColor : settings.emberColor;
            CreateSolidSphere(
                coronaSpinRoot,
                "Petal_" + i,
                pos,
                new Vector3(0.28f, 0.22f, 0.18f),
                petalColor,
                out _);
        }

        // Inner star points (offset spin group for denser motion).
        for (int i = 0; i < 3; i++)
        {
            float ang = i * Mathf.PI * 2f / 3f + 0.4f;
            Vector3 pos = new Vector3(
                Mathf.Cos(ang) * petalRadius * 0.62f,
                Mathf.Sin(ang) * petalRadius * 0.62f,
                0.08f);
            CreateSolidSphere(
                coronaSpinRoot,
                "Spark_" + i,
                pos,
                0.2f,
                settings.coreColor,
                out _);
        }

        if (settings.enableEmbers && settings.emberRate > 0.1f)
        {
            emberParticles = CreateOpaqueMeshParticleSystem(
                visual.transform,
                "RadialSparks",
                settings.sparkColor,
                out emberMaterial);
            TrackMaterial(emberMaterial);
            ConfigureHeadOnSparks(emberParticles, corona);
            emberParticles.Play(true);
        }
    }

    /// <summary>
    /// Sparks spray in the plane facing the player (around the silhouette), not behind it.
    /// </summary>
    private void ConfigureHeadOnSparks(ParticleSystem ps, float corona)
    {
        float size = Mathf.Max(0.15f, settings.size);
        float anim = AnimMotionScale;
        var main = ps.main;
        main.loop = true;
        main.playOnAwake = true;
        // Longer-lived, slower sparks when the ball itself is slow.
        main.startLifetime = Mathf.Max(0.05f, settings.emberLifetime / Mathf.Max(0.25f, anim));
        main.startSpeed = new ParticleSystem.MinMaxCurve(
            size * 0.55f * anim,
            size * 1.4f * anim);
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.07f, size * 0.16f);
        main.startColor = Opaque(settings.sparkColor);
        // World space: sparks bloom in place as the ball flies through, so you see a
        // growing cloud around the approach path — not stuck behind the ball.
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 80;
        main.gravityModifier = 0f;
        main.simulationSpeed = anim;

        var emission = ps.emission;
        emission.rateOverTime = settings.emberRate * anim;

        // Sphere shell = sparks bloom outward around the silhouette (readable head-on).
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.38f * corona;
        shape.radiusThickness = 0f; // surface only → radial outward
        shape.arc = 360f;
        shape.rotation = Vector3.zero;
        shape.position = Vector3.zero;
        shape.scale = Vector3.one;

        var colorOver = ps.colorOverLifetime;
        colorOver.enabled = true;
        Gradient g = new Gradient();
        Color a = Opaque(settings.coreColor);
        Color b = Opaque(settings.sparkColor);
        Color c = Opaque(settings.emberColor);
        g.SetKeys(
            new[]
            {
                new GradientColorKey(a, 0f),
                new GradientColorKey(b, 0.4f),
                new GradientColorKey(c, 1f)
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 1f)
            });
        colorOver.color = g;

        var sizeOver = ps.sizeOverLifetime;
        sizeOver.enabled = true;
        sizeOver.size = new ParticleSystem.MinMaxCurve(
            1f,
            AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.55f;
        noise.frequency = 0.9f;
        noise.scrollSpeed = 0.7f;
        noise.separateAxes = false;
    }

    private GameObject CreateSolidSphere(
        Transform parent,
        string name,
        Vector3 localPos,
        float uniformScale,
        Color color,
        out Material material)
    {
        return CreateSolidSphere(parent, name, localPos, Vector3.one * uniformScale, color, out material);
    }

    private GameObject CreateSolidSphere(
        Transform parent,
        string name,
        Vector3 localPos,
        Vector3 localScale,
        Color color,
        out Material material)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = localScale;
        Destroy(go.GetComponent<Collider>());

        material = CreateOpaqueUnlit(color, name + "Mat");
        TrackMaterial(material);
        Renderer renderer = go.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        return go;
    }

    private void TrackMaterial(Material mat)
    {
        if (mat != null && !ownedMaterials.Contains(mat))
        {
            ownedMaterials.Add(mat);
        }
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
            ApplyColor(mat, Opaque(color));
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
            ApplyColor(mat, Opaque(color));
            if (mat.HasProperty("_OutlineWidth"))
            {
                mat.SetFloat("_OutlineWidth", Mathf.Max(0.01f, outlineWidth));
            }

            return mat;
        }

        return CreateOpaqueUnlit(color, "DragonFireballOutlineFallback");
    }

    public static ParticleSystem CreateOpaqueMeshParticleSystem(
        Transform parent,
        string name,
        Color color,
        out Material material)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        material = CreateOpaqueUnlit(color, name + "Mat");
        ParticleSystemRenderer psRenderer = go.GetComponent<ParticleSystemRenderer>();
        psRenderer.renderMode = ParticleSystemRenderMode.Mesh;
        psRenderer.mesh = GetUnitSphereMesh();
        psRenderer.sharedMaterial = material;
        psRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        psRenderer.receiveShadows = false;
        // Mesh particles — fully opaque shading.
        psRenderer.enableGPUInstancing = true;

        return ps;
    }

    private static Mesh cachedSphereMesh;

    private static Mesh GetUnitSphereMesh()
    {
        if (cachedSphereMesh != null)
        {
            return cachedSphereMesh;
        }

        GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        MeshFilter filter = temp.GetComponent<MeshFilter>();
        cachedSphereMesh = filter != null ? filter.sharedMesh : null;
        Destroy(temp);
        return cachedSphereMesh;
    }

    public static Material CreateOpaqueUnlit(Color color, string name)
    {
        color = Opaque(color);
        string[] shaders =
        {
            "Unlit/Color",
            "Universal Render Pipeline/Unlit"
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

        color = Opaque(color);
        if (mat.HasProperty("_Color"))
        {
            mat.color = color;
        }

        if (mat.HasProperty("_BaseColor"))
        {
            mat.SetColor("_BaseColor", color);
        }
    }

    private static Color Opaque(Color c)
    {
        c.a = 1f;
        return c;
    }

    private void OnDestroy()
    {
        for (int i = 0; i < ownedMaterials.Count; i++)
        {
            if (ownedMaterials[i] != null)
            {
                Destroy(ownedMaterials[i]);
            }
        }

        ownedMaterials.Clear();

        if (owner != null)
        {
            owner.UnregisterFireball(this);
        }
    }
}
