using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum DragonHitColliderMode
{
    MeshOnVisualChildren,
    BoxOnRoot
}

/// <summary>
/// Dragon boss: shielded while crystals are alive. Shoot crystals, then the dragon.
/// Auto-fits a hit collider to the visual mesh and flies a smooth figure-8 loop while fighting.
/// </summary>
public class DragonBoss : MonoBehaviour
{
    [Header("Crystals")]
    [Tooltip("Optional. Empty = auto-find all EnderCrystal in the scene on Start.")]
    [SerializeField] private List<EnderCrystal> crystals = new List<EnderCrystal>();
    [Tooltip("Where crystal beams aim. Drag an empty child here, or use Create Shield Attach Point.")]
    [SerializeField] private Transform shieldAttach;
    [Tooltip("Extra offset from shieldAttach (local space). Use Y to lift beams into the body.")]
    [SerializeField] private Vector3 shieldAttachLocalOffset = Vector3.zero;

    [Header("Shield Visual")]
    [Tooltip("Rebuilds a solid crystal-colored outline that hugs child mesh renderers.")]
    [SerializeField] private bool createShieldIfMissing = true;
    [Tooltip("Assign so the outline shader is included in builds (CAVE). Auto-finds if empty.")]
    [SerializeField] private Material shieldOutlineMaterial;
    [Tooltip("Same magenta energy as the crystals / beams.")]
    [SerializeField] private Color shieldColor = new Color(1f, 0.35f, 1f, 1f);
    [SerializeField] private float outlineWidth = 0.08f;
    [SerializeField] private float shieldPulseSpeed = 2.2f;
    [Tooltip("Outline width pulse amount (world units).")]
    [SerializeField] private float shieldPulseAmount = 0.012f;

    [Header("Hit Feedback")]
    [Tooltip("Destroy the arrow when it hits the shield (blocked).")]
    [SerializeField] private bool consumeArrowOnShieldHit = true;
    [SerializeField] private bool logStateChanges = true;

    [Header("Fight")]
    [SerializeField] private float roundSeconds = 90f;
    [SerializeField, Min(1)] private int maxHp = 5;
    [SerializeField] private DragonFightUI fightUI;
    [Tooltip("If true, fight waits for the world-space Start button.")]
    [SerializeField] private bool requireStartButton = true;

    [Header("Model / Animation")]
    [Tooltip("Optional Animator on the Sketchfab mesh (fly / wing flap). Auto-finds in children if empty.")]
    [SerializeField] private Animator animator;
    [Tooltip("Wing flap speed during the fight.")]
    [SerializeField] private float aliveAnimSpeed = 1f;
    [Tooltip("Wing flap speed while waiting on Start.")]
    [SerializeField] private float idleAnimSpeed = 1f;
    [Tooltip("Mesh root scaled down on death. Auto-finds first skinned mesh child if empty.")]
    [SerializeField] private Transform visualScaleRoot;

    [Header("Damage")]
    [SerializeField] private Color damageFlashColor = new Color(1f, 0.12f, 0.08f, 1f);
    [SerializeField] private float damageFlashDuration = 0.28f;

    [Header("Death")]
    [Tooltip("Gravity scale while diving after death (1 = Physics.gravity).")]
    [SerializeField] private float deathGravityMultiplier = 1.5f;
    [Tooltip("How quickly the nose aligns with dive velocity (higher = snappier).")]
    [SerializeField] private float deathDiveAlignSpeed = 3.5f;
    [SerializeField] private float deathMaxFallSeconds = 8f;
    [SerializeField] private LayerMask deathGroundMask = ~0;
    [SerializeField] private float deathGroundProbeHeight = 120f;
    [Tooltip("Stop when the head is this far above the hit ground.")]
    [SerializeField] private float deathImpactClearance = 0.2f;
    [Tooltip("Fallback fall distance if no floor collider is found. Flight Bounds are NOT used — "
             + "death can fall below the flight box to the real ground.")]
    [SerializeField] private float fallDropDistance = 12f;
    [Tooltip("Optional floor marker. If set, death crashes at this height instead of raycasting.")]
    [SerializeField] private Transform deathGroundAnchor;
    [SerializeField] private float deathImpactArmSeconds = 0.25f;
    [SerializeField] private float deathMaxSpeed = 22f;
    [SerializeField] private float fadeOutDuration = 0.65f;

    [Header("Hit Collider")]
    [Tooltip("Mesh colliders on the visual mesh children (recommended). Box is a simple fallback.")]
    [SerializeField] private DragonHitColliderMode hitColliderMode = DragonHitColliderMode.MeshOnVisualChildren;
    [SerializeField] private bool autoSetupHitCollider = true;
    [SerializeField] private float hitColliderPadding = 1.08f;
    [SerializeField] private bool meshColliderConvex = true;
    [SerializeField] private BoxCollider hitCollider;

    [Header("Flight Path")]
    [Tooltip("World center of the figure-8. If empty, uses Flight Bounds center or this object.")]
    [SerializeField] private Transform flightCenterAnchor;
    [Tooltip("Optional box the dragon flies inside. Create via context menu, then scale it — "
             + "the figure-8 fills this box when Constrain is on.")]
    [SerializeField] private Transform flightBounds;
    [Tooltip("When on, path size comes from Flight Bounds (fills the box). Path Width/Depth are ignored.")]
    [SerializeField] private bool constrainToFlightBounds = true;
    [Tooltip("Use this fraction of the bounds (1 = edge-to-edge).")]
    [SerializeField, Range(0.5f, 1f)] private float flightBoundsPadding = 0.92f;
    [Tooltip("Figure-8 half-width (X). Only used when Flight Bounds is empty / constrain is off.")]
    [SerializeField] private float pathWidth = 4f;
    [Tooltip("Figure-8 half-depth (Z). Only used when Flight Bounds is empty / constrain is off.")]
    [SerializeField] private float pathDepth = 3f;
    [SerializeField] private float pathSpeed = 0.28f;
    [Tooltip("Rotate the figure-8 horizontally (degrees). Ignored when Flight Bounds is used (follows box yaw).")]
    [SerializeField] private float pathYawDegrees = 0f;
    [Tooltip("Empty at the snout/head. Path tracks this point. Create via context menu.")]
    [SerializeField] private Transform flightLead;
    [Tooltip("Empty at the tail tip. Used with Flight Lead to size the body and aim rotation.")]
    [SerializeField] private Transform flightTail;
    [Tooltip("Fallback nose offset if Flight Lead is empty (local-forward meters).")]
    [SerializeField, Min(0.1f)] private float flightLeadDistance = 2f;
    [Tooltip("Fallback tail offset if Flight Tail is empty (local-back meters).")]
    [SerializeField, Min(0.1f)] private float flightTailDistance = 2f;
    [Tooltip("If on, path lobes grow with body length (still capped by Flight Bounds if set).")]
    [SerializeField] private bool autoFitPathToBody = false;
    [SerializeField, Min(1.2f)] private float pathBodyClearance = 2.2f;
    [Tooltip("Extra twist around the body axis after lead→tail aiming (degrees).")]
    [SerializeField] private float modelTwistDegrees = 0f;
    [SerializeField] private float heightAmplitude = 1.2f;
    [SerializeField] private float heightFrequency = 0.55f;
    [SerializeField] private float pitchAmplitude = 8f;
    [SerializeField] private float pitchFrequency = 0.22f;
    [SerializeField] private float pitchWobbleFrequency = 0.17f;
    [Tooltip("Figure-8 while waiting on Start (slower).")]
    [SerializeField] private bool idleFlightWhileWaiting = true;
    [SerializeField] private float idlePathSpeedMultiplier = 0.45f;

    [Header("Fireballs")]
    [SerializeField] private bool enableFireballs = true;
    [Tooltip("Mouth / spawn point. Defaults to Flight Lead, then this transform.")]
    [SerializeField] private Transform fireballSpawn;
    [Tooltip("Seconds between shots while the fight is active.")]
    [SerializeField, Min(1f)] private float fireballInterval = 5.5f;
    [Tooltip("Delay after Start before the first fireball.")]
    [SerializeField, Min(0f)] private float fireballFirstDelay = 2.5f;
    [Tooltip("Chance to fire when a shot is due (0–1).")]
    [SerializeField, Range(0f, 1f)] private float fireballChance = 0.75f;
    [Tooltip("Only spawn when the mouth is in the center this fraction of Flight Bounds X "
             + "(0.6 = middle 60%). Yellow gizmo when selected.")]
    [SerializeField, Range(0.1f, 1f)] private float fireballSpawnCenterXFraction = 0.6f;
    [Tooltip("Size, colors, outline, explosion — all editable here.")]
    [SerializeField] private DragonFireballSettings fireballSettings = DragonFireballSettings.Default;

    private enum FightPhase
    {
        Waiting,
        Playing,
        Ended
    }

    private readonly List<EnderCrystal> liveCrystals = new List<EnderCrystal>(8);
    private readonly List<GameObject> shieldOutlineObjects = new List<GameObject>(8);
    private readonly List<DragonFireball> liveFireballs = new List<DragonFireball>(8);
    private bool shieldUp;
    private bool dead;
    private Material shieldMaterial;
    private float baseOutlineWidth;
    private FightPhase phase = FightPhase.Waiting;
    private float timeRemaining;
    private int currentHp;
    private Collider[] dragonHitColliders;
    private Vector3 flightCenter;
    private float flightPhase;
    private Vector3 lastFlightPosition;
    private Vector3 lastRootPosition;
    private Vector3 flightVelocity;
    private bool hasRootFlightSample;
    private Vector3 flightLeadLocal;
    private Vector3 flightTailLocal;
    private float flightBodyLength;
    private Vector3 spawnPosition;
    private Quaternion spawnRotation;
    private bool isDying;
    private float damageFlashEndTime;
    private Coroutine deathRoutine;
    private Vector3 visualScaleRootBaseScale;
    private float nextFireballTime;

    private readonly List<BodyMaterialSlot> bodyMaterialSlots = new List<BodyMaterialSlot>(16);

    private struct BodyMaterialSlot
    {
        public Renderer Renderer;
        public Material Material;
        public Color BaseColor;
        public bool UsesBaseColorProp;
    }

    public bool IsShielded => phase == FightPhase.Playing && shieldUp && !dead && !isDying;
    public bool IsDead => dead;
    public bool IsFightActive => phase == FightPhase.Playing && !dead && !isDying;
    public bool ShouldShowCrystalShieldVisual =>
        (phase == FightPhase.Waiting || phase == FightPhase.Playing)
        && !dead && !isDying
        && liveCrystals.Count > 0;
    public int CurrentHp => currentHp;
    public int MaxHp => maxHp;
    public Color CrystalEnergyColor => shieldColor;
    public float TimeRemaining => timeRemaining;
    public Vector3 ShieldAttachPoint
    {
        get
        {
            Transform attach = shieldAttach != null ? shieldAttach : transform;
            return attach.TransformPoint(shieldAttachLocalOffset);
        }
    }

    public void RegisterCrystal(EnderCrystal crystal)
    {
        if (crystal == null)
        {
            return;
        }

        if (!crystals.Contains(crystal))
        {
            crystals.Add(crystal);
        }

        if (crystal.IsAlive && !liveCrystals.Contains(crystal))
        {
            liveCrystals.Add(crystal);
        }

        crystal.Bind(this);
        RefreshShieldState();
    }

    public void NotifyCrystalDestroyed(EnderCrystal crystal)
    {
        if (crystal != null)
        {
            liveCrystals.Remove(crystal);
        }

        RefreshShieldState();
    }

    private void Awake()
    {
        ResolveShieldAttach();
        EnsureShieldOutlineMaterial();

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        ResolveVisualScaleRoot();
        CacheFlightMarkers();
        EnsureFireballSettings();
        spawnPosition = transform.position;
        spawnRotation = transform.rotation;
        currentHp = maxHp;

        CacheDragonHitColliders();
        EnsureShieldVisual();
        ResolveFlightCenter();
        lastFlightPosition = transform.position;

        if (fightUI == null)
        {
            fightUI = FindObjectOfType<DragonFightUI>();
        }

        if (fightUI != null)
        {
            fightUI.Bind(this);
        }
    }

    private void Start()
    {
        if (autoSetupHitCollider)
        {
            SetupHitColliders();
        }

        if (createShieldIfMissing)
        {
            RebuildMeshShieldOutline();
        }

        CollectCrystals();
        CacheBodyVisualMaterials();

        if (requireStartButton)
        {
            EnterWaiting();
        }
        else
        {
            StartFight();
        }
    }

    private void Update()
    {
        UpdateDamageFlash();
        UpdateFlightMotion();
        UpdateFireballs();

        if (phase == FightPhase.Playing && !dead && !isDying)
        {
            timeRemaining -= Time.deltaTime;
            if (fightUI != null)
            {
                fightUI.ShowTimer(timeRemaining, currentHp, maxHp);
            }

            if (timeRemaining <= 0f)
            {
                EndFightTimeout();
            }
        }

        if (dead || !ShouldShowCrystalShieldVisual || shieldMaterial == null)
        {
            return;
        }

        float pulse = 1f + Mathf.Sin(Time.time * shieldPulseSpeed) * shieldPulseAmount;
        if (shieldMaterial.HasProperty("_OutlineWidth"))
        {
            shieldMaterial.SetFloat("_OutlineWidth", baseOutlineWidth * pulse);
        }

        if (shieldMaterial.HasProperty("_Color"))
        {
            float brightness = 0.88f + 0.12f * (0.5f + 0.5f * Mathf.Sin(Time.time * shieldPulseSpeed));
            Color c = shieldColor * brightness;
            c.a = shieldColor.a;
            shieldMaterial.SetColor("_Color", c);
        }
    }

    /// <summary>Begin a timed fight from the world-space Start panel.</summary>
    public void StartFight()
    {
        ResetFightState(beginPlaying: true);
    }

    /// <summary>Restore crystals, dragon, and shield; return to Start.</summary>
    public void ResetFight()
    {
        ResetFightState(beginPlaying: false);
    }

    private void ResetFightState(bool beginPlaying)
    {
        if (deathRoutine != null)
        {
            StopCoroutine(deathRoutine);
            deathRoutine = null;
        }

        isDying = false;
        dead = false;
        timeRemaining = roundSeconds;
        currentHp = maxHp;
        damageFlashEndTime = 0f;
        CacheFlightMarkers();
        ClearFireballs();
        nextFireballTime = Time.time + fireballFirstDelay;

        transform.position = spawnPosition;
        transform.rotation = spawnRotation;
        RestoreBodyVisuals();
        hasRootFlightSample = false;
        flightVelocity = Vector3.zero;
        lastRootPosition = spawnPosition;

        SetAnimatorRunning(false);
        SetDragonCollidersEnabled(true);

        if (autoSetupHitCollider)
        {
            SetupHitColliders();
        }

        // Revive every known crystal (shot crystals stay in the list now).
        for (int i = 0; i < crystals.Count; i++)
        {
            if (crystals[i] != null)
            {
                crystals[i].Revive();
            }
        }

        CollectCrystals();

        if (createShieldIfMissing && shieldOutlineObjects.Count == 0)
        {
            RebuildMeshShieldOutline();
        }

        if (beginPlaying)
        {
            phase = FightPhase.Playing;
            SetAnimatorRunning(true, aliveAnimSpeed);
            RefreshShieldState();
            if (fightUI != null)
            {
                fightUI.ShowTimer(timeRemaining, currentHp, maxHp);
            }

            if (logStateChanges)
            {
                Debug.Log("DragonBoss: fight started (" + roundSeconds + "s).", this);
            }
        }
        else
        {
            EnterWaiting();
        }
    }

    private void EnterWaiting()
    {
        phase = FightPhase.Waiting;
        dead = false;
        isDying = false;
        timeRemaining = roundSeconds;
        currentHp = maxHp;
        SetAnimatorRunning(true, idleAnimSpeed);
        RefreshShieldState();
        RestoreBodyVisuals();

        if (fightUI != null)
        {
            fightUI.ShowStart();
        }
    }

    private void EndFightTimeout()
    {
        if (phase != FightPhase.Playing)
        {
            return;
        }

        phase = FightPhase.Ended;
        timeRemaining = 0f;
        SetAnimatorRunning(false);
        SetShieldVisible(false);
        shieldUp = false;
        ClearFireballs();
        FightAudio.SetDragonFlying(false);

        if (fightUI != null)
        {
            fightUI.ShowTimeout();
        }

        if (logStateChanges)
        {
            Debug.Log("DragonBoss: time up.", this);
        }
    }

    /// <summary>Player was hit by a fireball — fight lost.</summary>
    public void NotifyPlayerHitByFireball(DragonFireball fireball)
    {
        EndFightDefeat();
    }

    public void UnregisterFireball(DragonFireball fireball)
    {
        if (fireball != null)
        {
            liveFireballs.Remove(fireball);
        }
    }

    private void EndFightDefeat()
    {
        if (phase != FightPhase.Playing)
        {
            return;
        }

        phase = FightPhase.Ended;
        SetAnimatorRunning(false);
        SetShieldVisible(false);
        shieldUp = false;
        ClearFireballs();
        FightAudio.SetDragonFlying(false);

        if (fightUI != null)
        {
            fightUI.ShowDefeat();
        }

        if (logStateChanges)
        {
            Debug.Log("DragonBoss: defeat — player hit by fireball.", this);
        }
    }

    private void UpdateFireballs()
    {
        if (!enableFireballs || !IsFightActive)
        {
            return;
        }

        if (Time.time < nextFireballTime)
        {
            return;
        }

        // Wait in the spawn band (center X of bounds) instead of wasting a long cooldown.
        if (!IsFireballSpawnPositionAllowed(ResolveFireballSpawnPosition()))
        {
            return;
        }

        nextFireballTime = Time.time + fireballInterval;

        if (Random.value > fireballChance)
        {
            return;
        }

        TrySpawnFireball();
    }

    private bool IsFireballSpawnPositionAllowed(Vector3 worldPosition)
    {
        if (!constrainToFlightBounds
            || !TryGetFlightBounds(out Vector3 center, out Quaternion rotation, out Vector3 halfExtents))
        {
            return true;
        }

        Vector3 local = Quaternion.Inverse(rotation) * (worldPosition - center);
        float allowedHalfX = halfExtents.x * Mathf.Clamp(fireballSpawnCenterXFraction, 0.1f, 1f);
        return Mathf.Abs(local.x) <= allowedHalfX + 0.001f;
    }

    private void EnsureFireballSettings()
    {
        if (fireballSettings.size >= 0.15f && fireballSettings.speed >= 0.5f)
        {
            return;
        }

        fireballSettings = DragonFireballSettings.Default;
    }

    private void TrySpawnFireball()
    {
        EnsureFireballSettings();
        Vector3 spawnPos = ResolveFireballSpawnPosition();
        Vector3 target = PlayEnvironment.ResolvePlayerAimPosition();
        Vector3 dir = target - spawnPos;
        if (dir.sqrMagnitude < 1e-4f)
        {
            dir = -transform.forward;
        }

        dir.Normalize();

        DragonFireball fireball = DragonFireball.Spawn(
            spawnPos,
            dir,
            this,
            fireballSettings);

        FightAudio.PlayFireballShoot(spawnPos);

        if (fireball != null)
        {
            liveFireballs.Add(fireball);
        }

        if (logStateChanges)
        {
            Debug.Log("DragonBoss: fireball launched toward player.", this);
        }
    }

    private Vector3 ResolveFireballSpawnPosition()
    {
        if (fireballSpawn != null)
        {
            return fireballSpawn.position;
        }

        if (flightLead != null)
        {
            return flightLead.position;
        }

        return transform.TransformPoint(flightLeadLocal);
    }

    private void ClearFireballs()
    {
        for (int i = liveFireballs.Count - 1; i >= 0; i--)
        {
            if (liveFireballs[i] != null)
            {
                Destroy(liveFireballs[i].gameObject);
            }
        }

        liveFireballs.Clear();

#if UNITY_2023_1_OR_NEWER
        DragonFireball[] leftover = FindObjectsByType<DragonFireball>(FindObjectsSortMode.None);
#else
        DragonFireball[] leftover = FindObjectsOfType<DragonFireball>();
#endif
        for (int i = 0; i < leftover.Length; i++)
        {
            if (leftover[i] != null)
            {
                Destroy(leftover[i].gameObject);
            }
        }
    }

    private void CollectCrystals()
    {
        liveCrystals.Clear();

        if (crystals == null)
        {
            crystals = new List<EnderCrystal>();
        }

        crystals.RemoveAll(c => c == null);

        if (crystals.Count == 0)
        {
#if UNITY_2023_1_OR_NEWER
            EnderCrystal[] found = FindObjectsByType<EnderCrystal>(FindObjectsSortMode.None);
#else
            EnderCrystal[] found = FindObjectsOfType<EnderCrystal>();
#endif
            for (int i = 0; i < found.Length; i++)
            {
                if (found[i] != null && !crystals.Contains(found[i]))
                {
                    crystals.Add(found[i]);
                }
            }
        }

        for (int i = 0; i < crystals.Count; i++)
        {
            EnderCrystal crystal = crystals[i];
            if (crystal == null)
            {
                continue;
            }

            crystal.Bind(this);
            if (crystal.IsAlive && !liveCrystals.Contains(crystal))
            {
                liveCrystals.Add(crystal);
            }
        }

        if (logStateChanges)
        {
            Debug.Log("DragonBoss: " + liveCrystals.Count + " live crystal(s) / "
                      + crystals.Count + " total.", this);
        }
    }

    private void RefreshShieldState()
    {
        liveCrystals.RemoveAll(c => c == null || !c.IsAlive);

        bool shouldShieldFunctional = phase == FightPhase.Playing && !dead && !isDying && liveCrystals.Count > 0;
        bool shouldShieldVisual = ShouldShowCrystalShieldVisual;

        if (shouldShieldFunctional != shieldUp)
        {
            shieldUp = shouldShieldFunctional;
            if (logStateChanges && phase == FightPhase.Playing)
            {
                Debug.Log(
                    shieldUp
                        ? "DragonBoss: shield UP (" + liveCrystals.Count + " crystal(s))."
                        : "DragonBoss: shield DOWN — dragon is vulnerable!",
                    this);
            }
        }

        SetShieldVisible(shouldShieldVisual);
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
        HandleArrowFromCollider(other);
    }

    /// <summary>
    /// Called from child mesh colliders (<see cref="DragonHitRelay"/>) or arrow collision.
    /// </summary>
    public bool HandleArrowCollision(ArrowProjectile arrow)
    {
        if (!IsFightActive || arrow == null)
        {
            return false;
        }

        if (!arrow.IsInFlight && !arrow.HasStuck)
        {
            return false;
        }

        // ArrowProjectile and DragonHitRelay can both see the same impact.
        if (!arrow.TryHandleDragonHit())
        {
            return true;
        }

        if (IsShielded)
        {
            FightAudio.PlayShieldBounce(arrow.transform.position);
            if (consumeArrowOnShieldHit)
            {
                Destroy(arrow.gameObject);
            }

            return true;
        }

        Destroy(arrow.gameObject);
        TakeDamage(Mathf.Max(1, arrow.Damage));
        return true;
    }

    public void HandleArrowFromCollider(Collider other)
    {
        if (other == null)
        {
            return;
        }

        ArrowProjectile arrow = other.GetComponentInParent<ArrowProjectile>();
        if (arrow != null)
        {
            HandleArrowCollision(arrow);
        }
    }

    private void TakeDamage(int amount)
    {
        if (!IsFightActive || amount <= 0)
        {
            return;
        }

        currentHp = Mathf.Max(0, currentHp - amount);
        damageFlashEndTime = Time.time + damageFlashDuration;
        FightAudio.PlayDragonHurt(transform.position);

        if (fightUI != null)
        {
            fightUI.ShowTimer(timeRemaining, currentHp, maxHp);
        }

        if (logStateChanges)
        {
            Debug.Log("DragonBoss: hit — HP " + currentHp + "/" + maxHp, this);
        }

        if (currentHp <= 0)
        {
            BeginDeathSequence();
        }
    }

    private void BeginDeathSequence()
    {
        if (dead || isDying || phase != FightPhase.Playing)
        {
            return;
        }

        isDying = true;
        dead = true;
        shieldUp = false;
        phase = FightPhase.Ended;
        SetShieldVisible(false);
        SetAnimatorRunning(false);
        SetDragonCollidersEnabled(false);
        ClearFireballs();
        FightAudio.SetDragonFlying(false);
        FightAudio.PlayDragonDeath(transform.position);
        OpaqueBurstVfx.SpawnDragonDeath(transform, CrystalEnergyColor);

        if (deathRoutine != null)
        {
            StopCoroutine(deathRoutine);
        }

        deathRoutine = StartCoroutine(DeathFallAndFadeRoutine());
    }

    private IEnumerator DeathFallAndFadeRoutine()
    {
        Vector3 velocity = flightVelocity;
        if (velocity.sqrMagnitude < 0.25f)
        {
            velocity = GetBodyForwardWorld() * Mathf.Max(3f, EstimateFlightSpeed());
        }

        float maxSpeed = Mathf.Max(4f, deathMaxSpeed);
        if (velocity.sqrMagnitude > maxSpeed * maxSpeed)
        {
            velocity = velocity.normalized * maxSpeed;
        }

        float startNoseY = ResolveFlightLeadWorldPosition().y;
        float startY = Mathf.Min(startNoseY, transform.position.y);
        float groundY = ResolveDeathGroundY(transform.position, startY);

        float fallElapsed = 0f;
        bool crashed = false;

        while (fallElapsed < deathMaxFallSeconds && !crashed)
        {
            float dt = Time.deltaTime;
            fallElapsed += dt;

            velocity += Physics.gravity * deathGravityMultiplier * dt;
            if (velocity.sqrMagnitude > maxSpeed * maxSpeed)
            {
                velocity = velocity.normalized * maxSpeed;
            }

            transform.position += velocity * dt;

            // Tip nose along velocity without quaternion flips that can bury the mesh.
            Vector3 diveAxis = velocity.sqrMagnitude > 0.01f ? velocity.normalized : Vector3.down;
            Vector3 currentAxis = GetBodyForwardWorld();
            float align = 1f - Mathf.Exp(-deathDiveAlignSpeed * dt);
            Vector3 tippedAxis = Vector3.Slerp(currentAxis, diveAxis, align).normalized;
            if (tippedAxis.sqrMagnitude > 1e-6f)
            {
                transform.rotation = RotationFromBodyAxisForDive(tippedAxis);
            }

            float noseY = ResolveFlightLeadWorldPosition().y;
            bool armed = fallElapsed >= deathImpactArmSeconds
                         && velocity.y <= 0f
                         && noseY < startY - 0.75f;
            if (armed && noseY <= groundY + deathImpactClearance)
            {
                transform.position += Vector3.up * (groundY + deathImpactClearance - noseY);
                crashed = true;
            }

            yield return null;
        }

        // Impact pop when the head hits the ground.
        OpaqueBurstVfx.Settings impact = OpaqueBurstVfx.Settings.DragonDeathDefault;
        impact.startSize = 0.6f;
        impact.radius = 2.8f;
        impact.duration = 0.7f;
        impact.sparkCount = 32;
        impact.shardCount = 12;
        impact.coreColor = CrystalEnergyColor;
        impact.sparkColor = Color.Lerp(CrystalEnergyColor, Color.white, 0.3f);
        OpaqueBurstVfx.Spawn(ResolveFlightLeadWorldPosition(), impact);

        float fadeElapsed = 0f;
        while (fadeElapsed < fadeOutDuration)
        {
            fadeElapsed += Time.deltaTime;
            float t = Mathf.Clamp01(fadeElapsed / fadeOutDuration);
            float scale = 1f - t;
            if (visualScaleRoot != null)
            {
                visualScaleRoot.localScale = visualScaleRootBaseScale * scale;
            }

            SetBodyVisualAlpha(1f - t);
            yield return null;
        }

        SetBodyVisualsVisible(false);
        if (visualScaleRoot != null)
        {
            visualScaleRoot.localScale = Vector3.zero;
        }

        isDying = false;
        deathRoutine = null;

        if (logStateChanges)
        {
            Debug.Log("DragonBoss: defeated!", this);
        }

        if (fightUI != null)
        {
            fightUI.ShowVictory(timeRemaining);
        }
    }

    private void Die()
    {
        BeginDeathSequence();
    }

    private void ResolveFlightCenter()
    {
        if (constrainToFlightBounds
            && TryGetFlightBounds(out Vector3 boundsCenter, out _, out _))
        {
            flightCenter = boundsCenter;
            return;
        }

        if (flightCenterAnchor != null)
        {
            flightCenter = flightCenterAnchor.position;
        }
        else
        {
            flightCenter = transform.position;
        }
    }

    private void UpdateFlightMotion()
    {
        if (isDying)
        {
            FightAudio.SetDragonFlying(false);
            return;
        }

        bool flying = phase == FightPhase.Playing && !dead
                        || (phase == FightPhase.Waiting && idleFlightWhileWaiting);

        FightAudio.SetDragonFlying(flying);

        if (!flying)
        {
            return;
        }

        float speedMul = phase == FightPhase.Playing ? 1f : idlePathSpeedMultiplier;
        flightPhase += pathSpeed * speedMul * Time.deltaTime;

        ResolveFlightCenter();

        float width;
        float depth;
        float heightAmp;
        GetEffectivePathSize(out width, out depth, out heightAmp);

        // Head on path now; tail on path one body-length behind (arc length).
        Vector3 headPath = EvaluatePathPoint(flightPhase, width, depth, heightAmp);
        Vector3 tailPath = FindPathPointBehind(flightPhase, flightBodyLength, width, depth, heightAmp);

        Vector3 pathAxis = headPath - tailPath;
        if (pathAxis.sqrMagnitude < 1e-6f)
        {
            pathAxis = EvaluatePathTangent(flightPhase, width, depth, heightAmp);
        }

        if (pathAxis.sqrMagnitude < 1e-6f)
        {
            pathAxis = headPath - lastFlightPosition;
        }

        if (pathAxis.sqrMagnitude < 1e-6f)
        {
            pathAxis = Vector3.forward;
        }

        pathAxis.Normalize();

        Quaternion targetRot = RotationFromBodyAxis(pathAxis);

        // Light path pitch wobble around the body right axis (does not replace body aiming).
        if (Mathf.Abs(pitchAmplitude) > 0.01f)
        {
            float pitch = pitchAmplitude * Mathf.Sin(flightPhase * pitchFrequency)
                          * Mathf.Sin(flightPhase * pitchWobbleFrequency + 0.6f);
            targetRot = Quaternion.AngleAxis(pitch, targetRot * Vector3.right) * targetRot;
        }

        // Root so the lead marker sits on headPath (tail then lands near tailPath).
        Vector3 targetRootPos = headPath - targetRot * flightLeadLocal;

        transform.SetPositionAndRotation(targetRootPos, targetRot);
        lastFlightPosition = headPath;

        if (hasRootFlightSample && Time.deltaTime > 1e-5f)
        {
            flightVelocity = (transform.position - lastRootPosition) / Time.deltaTime;
        }

        lastRootPosition = transform.position;
        hasRootFlightSample = true;
    }

    private Vector3 ResolveFlightLeadWorldPosition()
    {
        if (flightLead != null)
        {
            return flightLead.position;
        }

        return transform.TransformPoint(flightLeadLocal);
    }

    private Vector3 GetBodyForwardWorld()
    {
        Vector3 localAxis = flightLeadLocal - flightTailLocal;
        if (localAxis.sqrMagnitude < 1e-6f)
        {
            localAxis = Vector3.forward;
        }

        return transform.TransformDirection(localAxis.normalized);
    }

    private float EstimateFlightSpeed()
    {
        GetEffectivePathSize(out float width, out float depth, out _);
        float pathScale = Mathf.Max(width, depth, 1f);
        return pathSpeed * pathScale * 2.5f;
    }

    private float ResolveDeathGroundY(Vector3 fromPosition, float startY)
    {
        // Flight Bounds are only the flight envelope — death falls past them to the real floor.
        if (deathGroundAnchor != null)
        {
            return deathGroundAnchor.position.y;
        }

        float fallback = startY - Mathf.Max(2f, fallDropDistance);
        float probeDistance = Mathf.Max(fallDropDistance + 20f, deathGroundProbeHeight);
        Vector3 origin = fromPosition + Vector3.up * 2f;

        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            Vector3.down,
            probeDistance,
            deathGroundMask,
            QueryTriggerInteraction.Ignore);

        float lowestY = float.PositiveInfinity;
        bool found = false;
        for (int i = 0; i < hits.Length; i++)
        {
            float y = hits[i].point.y;
            // Skip hits near/above the body (own leftover colliders, nearby props).
            if (y >= startY - 0.5f)
            {
                continue;
            }

            if (y < lowestY)
            {
                lowestY = y;
                found = true;
            }
        }

        return found ? lowestY : fallback;
    }

    /// <summary>
    /// Same as body-axis aim, but stable when diving nearly straight down.
    /// </summary>
    private Quaternion RotationFromBodyAxisForDive(Vector3 worldAxis)
    {
        worldAxis.Normalize();
        Vector3 upHint = Vector3.up;
        if (Mathf.Abs(Vector3.Dot(worldAxis, Vector3.up)) > 0.92f)
        {
            upHint = Vector3.ProjectOnPlane(transform.up, worldAxis);
            if (upHint.sqrMagnitude < 1e-4f)
            {
                upHint = Vector3.ProjectOnPlane(transform.right, worldAxis);
            }

            if (upHint.sqrMagnitude < 1e-4f)
            {
                upHint = Vector3.forward;
            }
            else
            {
                upHint.Normalize();
            }
        }

        Vector3 localAxis = flightLeadLocal - flightTailLocal;
        if (localAxis.sqrMagnitude < 1e-6f)
        {
            localAxis = Vector3.forward;
        }
        else
        {
            localAxis.Normalize();
        }

        Quaternion worldFace = Quaternion.LookRotation(worldAxis, upHint);
        Quaternion localFace = Quaternion.LookRotation(localAxis, Vector3.up);
        Quaternion rot = worldFace * Quaternion.Inverse(localFace);

        if (Mathf.Abs(modelTwistDegrees) > 0.01f)
        {
            rot = Quaternion.AngleAxis(modelTwistDegrees, worldAxis) * rot;
        }

        return rot;
    }

    private void CacheFlightMarkers()
    {
        // Prefer scene markers; fall back to distances along a default nose axis.
        Vector3 defaultForward = Vector3.forward;

        if (flightLead != null)
        {
            flightLeadLocal = transform.InverseTransformPoint(flightLead.position);
        }
        else
        {
            flightLeadLocal = defaultForward * flightLeadDistance;
        }

        if (flightTail != null)
        {
            flightTailLocal = transform.InverseTransformPoint(flightTail.position);
        }
        else
        {
            flightTailLocal = -defaultForward * flightTailDistance;
        }

        flightBodyLength = Vector3.Distance(flightLeadLocal, flightTailLocal);
        if (flightBodyLength < 0.2f)
        {
            flightBodyLength = Mathf.Max(0.2f, flightLeadDistance + flightTailDistance);
            flightLeadLocal = defaultForward * (flightBodyLength * 0.5f);
            flightTailLocal = -defaultForward * (flightBodyLength * 0.5f);
        }

        if (logStateChanges)
        {
            Debug.Log(
                "DragonBoss: flight markers — body length " + flightBodyLength.ToString("0.00")
                + "m (lead " + flightLeadLocal + ", tail " + flightTailLocal + ").",
                this);
        }
    }

    private void GetEffectivePathSize(out float width, out float depth, out float heightAmp)
    {
        // Bounds mode: fill the box (this was staying centered because Path Width/Depth
        // were treated as a max and never grew up to the box size).
        if (constrainToFlightBounds
            && TryGetFlightBounds(out _, out _, out Vector3 halfExtents))
        {
            float pad = Mathf.Clamp(flightBoundsPadding, 0.5f, 1f);
            width = Mathf.Max(0.1f, halfExtents.x * pad);
            depth = Mathf.Max(0.1f, halfExtents.z * pad);
            // Path Y peaks at ~1.35× heightAmp — scale so peaks reach the box top/bottom.
            heightAmp = Mathf.Max(0f, halfExtents.y * pad / 1.35f);
            return;
        }

        width = Mathf.Max(0.1f, pathWidth);
        depth = Mathf.Max(0.1f, pathDepth);
        heightAmp = Mathf.Max(0f, heightAmplitude);

        if (autoFitPathToBody)
        {
            float minLobe = flightBodyLength * pathBodyClearance;
            width = Mathf.Max(width, minLobe);
            depth = Mathf.Max(depth, minLobe * 0.75f);
        }
    }

    private bool TryGetFlightBounds(out Vector3 center, out Quaternion rotation, out Vector3 halfExtents)
    {
        center = flightCenter;
        rotation = Quaternion.Euler(0f, pathYawDegrees, 0f);
        halfExtents = Vector3.zero;

        if (flightBounds == null)
        {
            return false;
        }

        BoxCollider box = flightBounds.GetComponent<BoxCollider>();
        if (box != null)
        {
            center = flightBounds.TransformPoint(box.center);
            rotation = flightBounds.rotation;
            Vector3 lossy = flightBounds.lossyScale;
            halfExtents = new Vector3(
                Mathf.Abs(box.size.x * lossy.x) * 0.5f,
                Mathf.Abs(box.size.y * lossy.y) * 0.5f,
                Mathf.Abs(box.size.z * lossy.z) * 0.5f);
            return halfExtents.x > 0.01f && halfExtents.z > 0.01f;
        }

        center = flightBounds.position;
        rotation = flightBounds.rotation;
        Vector3 scale = flightBounds.lossyScale;
        halfExtents = new Vector3(
            Mathf.Abs(scale.x) * 0.5f,
            Mathf.Abs(scale.y) * 0.5f,
            Mathf.Abs(scale.z) * 0.5f);
        return halfExtents.x > 0.01f && halfExtents.z > 0.01f;
    }

    /// <summary>
    /// Rotation that aligns the model lead←tail axis with <paramref name="worldAxis"/> (tail→head).
    /// </summary>
    private Quaternion RotationFromBodyAxis(Vector3 worldAxis)
    {
        Vector3 localAxis = flightLeadLocal - flightTailLocal;
        if (localAxis.sqrMagnitude < 1e-6f)
        {
            localAxis = Vector3.forward;
        }
        else
        {
            localAxis.Normalize();
        }

        worldAxis.Normalize();

        // Map local body axis → world path axis, keeping belly roughly toward -up via LookRotation.
        Quaternion worldFace = Quaternion.LookRotation(worldAxis, Vector3.up);
        Quaternion localFace = Quaternion.LookRotation(localAxis, Vector3.up);
        Quaternion rot = worldFace * Quaternion.Inverse(localFace);

        if (Mathf.Abs(modelTwistDegrees) > 0.01f)
        {
            rot = Quaternion.AngleAxis(modelTwistDegrees, worldAxis) * rot;
        }

        return rot;
    }

    private Vector3 EvaluatePathPoint(float phase)
    {
        GetEffectivePathSize(out float width, out float depth, out float heightAmp);
        return EvaluatePathPoint(phase, width, depth, heightAmp);
    }

    private Vector3 EvaluatePathPoint(float phase, float width, float depth, float heightAmp)
    {
        Vector3 localOffset = new Vector3(
            width * Mathf.Sin(phase),
            heightAmp * Mathf.Sin(phase * heightFrequency)
            + heightAmp * 0.35f * Mathf.Sin(phase * heightFrequency * 2.3f + 1.1f),
            depth * Mathf.Sin(2f * phase));

        if (constrainToFlightBounds
            && TryGetFlightBounds(out Vector3 center, out Quaternion rotation, out _))
        {
            return center + rotation * localOffset;
        }

        return flightCenter + Quaternion.Euler(0f, pathYawDegrees, 0f) * localOffset;
    }

    private Vector3 EvaluatePathTangent(float phase, float width, float depth, float heightAmp)
    {
        Vector3 localTangent = new Vector3(
            width * Mathf.Cos(phase),
            heightAmp * heightFrequency * Mathf.Cos(phase * heightFrequency)
            + heightAmp * 0.35f * heightFrequency * 2.3f
              * Mathf.Cos(phase * heightFrequency * 2.3f + 1.1f),
            depth * 2f * Mathf.Cos(2f * phase));

        if (constrainToFlightBounds && TryGetFlightBounds(out _, out Quaternion rotation, out _))
        {
            return rotation * localTangent;
        }

        return Quaternion.Euler(0f, pathYawDegrees, 0f) * localTangent;
    }

    /// <summary>
    /// Walk backward along the figure-8 until arc length ≈ body length.
    /// </summary>
    private Vector3 FindPathPointBehind(
        float headPhase, float arcLength, float width, float depth, float heightAmp)
    {
        if (arcLength <= 0.01f)
        {
            return EvaluatePathPoint(headPhase, width, depth, heightAmp);
        }

        float phase = headPhase;
        float traveled = 0f;
        Vector3 prev = EvaluatePathPoint(phase, width, depth, heightAmp);
        const float step = 0.025f;
        const int maxSteps = 800;

        for (int i = 0; i < maxSteps; i++)
        {
            phase -= step;
            Vector3 point = EvaluatePathPoint(phase, width, depth, heightAmp);
            float seg = Vector3.Distance(prev, point);
            if (seg < 1e-6f)
            {
                prev = point;
                continue;
            }

            if (traveled + seg >= arcLength)
            {
                float t = (arcLength - traveled) / seg;
                return Vector3.Lerp(prev, point, t);
            }

            traveled += seg;
            prev = point;
        }

        return prev;
    }

    private void ResolveVisualScaleRoot()
    {
        if (visualScaleRoot != null)
        {
            visualScaleRootBaseScale = visualScaleRoot.localScale;
            return;
        }

        SkinnedMeshRenderer skinned = GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (skinned != null)
        {
            visualScaleRoot = skinned.transform;
            visualScaleRootBaseScale = visualScaleRoot.localScale;
            return;
        }

        MeshRenderer mesh = GetComponentInChildren<MeshRenderer>(true);
        if (mesh != null && IsVisualMeshRenderer(mesh))
        {
            visualScaleRoot = mesh.transform;
            visualScaleRootBaseScale = visualScaleRoot.localScale;
        }
        else
        {
            visualScaleRoot = transform;
            visualScaleRootBaseScale = Vector3.one;
        }
    }

    private void CacheBodyVisualMaterials()
    {
        bodyMaterialSlots.Clear();

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (!IsVisualMeshRenderer(renderer))
            {
                continue;
            }

            Material[] mats = renderer.materials;
            for (int m = 0; m < mats.Length; m++)
            {
                Material mat = mats[m];
                if (mat == null)
                {
                    continue;
                }

                BodyMaterialSlot slot = new BodyMaterialSlot
                {
                    Renderer = renderer,
                    Material = mat,
                    UsesBaseColorProp = mat.HasProperty("_BaseColor")
                };

                if (slot.UsesBaseColorProp)
                {
                    slot.BaseColor = mat.GetColor("_BaseColor");
                }
                else if (mat.HasProperty("_Color"))
                {
                    slot.BaseColor = mat.color;
                }
                else
                {
                    slot.BaseColor = Color.white;
                }

                bodyMaterialSlots.Add(slot);
            }

            renderer.materials = mats;
        }
    }

    private void UpdateDamageFlash()
    {
        if (bodyMaterialSlots.Count == 0)
        {
            return;
        }

        bool flashing = Time.time < damageFlashEndTime;
        float flashT = 0f;
        if (flashing)
        {
            float remaining = damageFlashEndTime - Time.time;
            flashT = Mathf.Clamp01(remaining / damageFlashDuration);
            flashT = Mathf.Sin(flashT * Mathf.PI);
        }

        for (int i = 0; i < bodyMaterialSlots.Count; i++)
        {
            BodyMaterialSlot slot = bodyMaterialSlots[i];
            if (slot.Material == null)
            {
                continue;
            }

            Color c = slot.BaseColor;
            if (flashing)
            {
                c = Color.Lerp(slot.BaseColor, damageFlashColor, flashT);
            }

            ApplyMaterialColor(slot.Material, c, slot.UsesBaseColorProp);
        }
    }

    private void RestoreBodyVisuals()
    {
        SetBodyVisualsVisible(true);

        if (visualScaleRoot != null)
        {
            visualScaleRoot.localScale = visualScaleRootBaseScale;
        }

        for (int i = 0; i < bodyMaterialSlots.Count; i++)
        {
            BodyMaterialSlot slot = bodyMaterialSlots[i];
            if (slot.Material == null)
            {
                continue;
            }

            ApplyMaterialColor(slot.Material, slot.BaseColor, slot.UsesBaseColorProp);
        }
    }

    private void SetBodyVisualsVisible(bool visible)
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (!IsVisualMeshRenderer(renderer, includeWhenDisabled: true))
            {
                continue;
            }

            renderer.enabled = visible;
        }
    }

    private void SetBodyVisualAlpha(float alpha)
    {
        alpha = Mathf.Clamp01(alpha);

        for (int i = 0; i < bodyMaterialSlots.Count; i++)
        {
            BodyMaterialSlot slot = bodyMaterialSlots[i];
            if (slot.Material == null)
            {
                continue;
            }

            Color c = slot.BaseColor;
            c.a = slot.BaseColor.a * alpha;
            ApplyMaterialColor(slot.Material, c, slot.UsesBaseColorProp);
        }
    }

    private static void ApplyMaterialColor(Material mat, Color color, bool useBaseColorProp)
    {
        if (mat == null)
        {
            return;
        }

        if (useBaseColorProp && mat.HasProperty("_BaseColor"))
        {
            mat.SetColor("_BaseColor", color);
        }
        else if (mat.HasProperty("_Color"))
        {
            mat.color = color;
        }
    }

    private void SetupHitColliders()
    {
        if (hitColliderMode == DragonHitColliderMode.MeshOnVisualChildren)
        {
            SetupMeshHitColliders();
        }
        else
        {
            FitBoxHitCollider();
        }
    }

    private void SetupMeshHitColliders()
    {
        DisableRootPrimitiveHitColliders();

        int created = 0;
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (!IsVisualMeshRenderer(renderer, includeWhenDisabled: true))
            {
                continue;
            }

            if (!TryGetMeshFromRenderer(renderer, out Mesh mesh))
            {
                continue;
            }

            MeshCollider meshCollider = renderer.GetComponent<MeshCollider>();
            if (meshCollider == null)
            {
                meshCollider = renderer.gameObject.AddComponent<MeshCollider>();
            }

            meshCollider.sharedMesh = mesh;
            meshCollider.convex = meshColliderConvex;
            meshCollider.isTrigger = false;
            meshCollider.enabled = true;

            DragonHitRelay relay = renderer.GetComponent<DragonHitRelay>();
            if (relay == null)
            {
                relay = renderer.gameObject.AddComponent<DragonHitRelay>();
            }

            relay.Bind(this);
            created++;
        }

        // Respect manually placed mesh colliders on visual children too.
        MeshCollider[] existing = GetComponentsInChildren<MeshCollider>(true);
        for (int i = 0; i < existing.Length; i++)
        {
            MeshCollider meshCollider = existing[i];
            if (meshCollider == null)
            {
                continue;
            }

            if (meshCollider.GetComponentInParent<EnderCrystal>() != null
                || meshCollider.GetComponentInParent<DragonFightUI>() != null)
            {
                continue;
            }

            if (meshCollider.gameObject.name.EndsWith("_ShieldOutline"))
            {
                continue;
            }

            if (meshCollider.sharedMesh == null
                && meshCollider.TryGetComponent<Renderer>(out Renderer renderer)
                && TryGetMeshFromRenderer(renderer, out Mesh mesh))
            {
                meshCollider.sharedMesh = mesh;
            }

            meshCollider.convex = meshColliderConvex;
            meshCollider.isTrigger = false;
            meshCollider.enabled = true;

            DragonHitRelay relay = meshCollider.GetComponent<DragonHitRelay>();
            if (relay == null)
            {
                relay = meshCollider.gameObject.AddComponent<DragonHitRelay>();
            }

            relay.Bind(this);
            created++;
        }

        CacheDragonHitColliders();

        if (logStateChanges)
        {
            Debug.Log("DragonBoss: mesh hit collider(s) ready — " + dragonHitColliders.Length + " collider(s).", this);
        }

        if (created == 0 && logStateChanges)
        {
            Debug.LogWarning(
                "DragonBoss: no mesh colliders set up. Enable the dragon prefab child "
                + "or add MeshCollider manually, then use Setup Hit Colliders.",
                this);
        }
    }

    private void DisableRootPrimitiveHitColliders()
    {
        BoxCollider rootBox = GetComponent<BoxCollider>();
        if (rootBox != null)
        {
            rootBox.enabled = false;
        }

        CapsuleCollider rootCapsule = GetComponent<CapsuleCollider>();
        if (rootCapsule != null)
        {
            rootCapsule.enabled = false;
        }
    }

    private static bool TryGetMeshFromRenderer(Renderer renderer, out Mesh mesh)
    {
        mesh = null;
        if (renderer == null)
        {
            return false;
        }

        if (renderer is SkinnedMeshRenderer skinned)
        {
            mesh = skinned.sharedMesh;
            return mesh != null;
        }

        MeshFilter filter = renderer.GetComponent<MeshFilter>();
        if (filter != null && filter.sharedMesh != null)
        {
            mesh = filter.sharedMesh;
            return true;
        }

        return false;
    }

    private void FitBoxHitCollider()
    {
        if (!TryGetVisualBounds(out Bounds worldBounds))
        {
            worldBounds = new Bounds(transform.position, new Vector3(3f, 2f, 4f));
        }

        if (hitCollider == null)
        {
            hitCollider = GetComponent<BoxCollider>();
            if (hitCollider == null)
            {
                hitCollider = gameObject.AddComponent<BoxCollider>();
            }
        }

        CapsuleCollider legacyCapsule = GetComponent<CapsuleCollider>();
        if (legacyCapsule != null && legacyCapsule != hitCollider)
        {
            if (Application.isPlaying)
            {
                Destroy(legacyCapsule);
            }
            else
            {
                DestroyImmediate(legacyCapsule);
            }
        }

        // Turn off mesh colliders when using box fallback.
        MeshCollider[] meshColliders = GetComponentsInChildren<MeshCollider>(true);
        for (int i = 0; i < meshColliders.Length; i++)
        {
            if (meshColliders[i] != null
                && meshColliders[i].GetComponentInParent<EnderCrystal>() == null
                && meshColliders[i].GetComponentInParent<DragonFightUI>() == null)
            {
                meshColliders[i].enabled = false;
            }
        }

        Vector3 localCenter = transform.InverseTransformPoint(worldBounds.center);
        Vector3 worldSize = worldBounds.size * hitColliderPadding;
        Vector3 lossy = transform.lossyScale;
        Vector3 localSize = new Vector3(
            worldSize.x / Mathf.Max(0.001f, Mathf.Abs(lossy.x)),
            worldSize.y / Mathf.Max(0.001f, Mathf.Abs(lossy.y)),
            worldSize.z / Mathf.Max(0.001f, Mathf.Abs(lossy.z)));

        hitCollider.center = localCenter;
        hitCollider.size = localSize;
        hitCollider.isTrigger = false;
        hitCollider.enabled = true;

        CacheDragonHitColliders();

        if (logStateChanges)
        {
            Debug.Log(
                "DragonBoss: hit box fitted — center " + localCenter + " size " + localSize,
                this);
        }
    }

    private void FitHitColliderToMesh()
    {
        SetupHitColliders();
    }

    private bool TryGetVisualBounds(out Bounds bounds, bool includeWhenDisabled = false)
    {
        bounds = default;
        bool any = false;

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (!IsVisualMeshRenderer(renderer, includeWhenDisabled))
            {
                continue;
            }

            if (!any)
            {
                bounds = renderer.bounds;
                any = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return any;
    }

    private bool IsVisualMeshRenderer(Renderer renderer, bool includeWhenDisabled = false)
    {
        if (renderer == null)
        {
            return false;
        }

        if (!includeWhenDisabled
            && (!renderer.enabled || !renderer.gameObject.activeInHierarchy))
        {
            return false;
        }

        if (renderer is LineRenderer)
        {
            return false;
        }

        if (IsShieldOutlineRenderer(renderer))
        {
            return false;
        }

        if (RendererUsesShieldShader(renderer))
        {
            return false;
        }

        string n = renderer.gameObject.name;
        if (n == "DragonBody" || n == "ShieldAura" || n.EndsWith("_ShieldOutline"))
        {
            return false;
        }

        if (renderer.GetComponentInParent<EnderCrystal>() != null)
        {
            return false;
        }

        if (renderer.GetComponentInParent<DragonFightUI>() != null)
        {
            return false;
        }

        return renderer is MeshRenderer || renderer is SkinnedMeshRenderer;
    }

    private void CacheDragonHitColliders()
    {
        List<Collider> list = new List<Collider>();

        if (hitColliderMode == DragonHitColliderMode.BoxOnRoot && hitCollider != null && hitCollider.enabled)
        {
            list.Add(hitCollider);
        }

        Collider[] cols = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] == null)
            {
                continue;
            }

            if (cols[i].GetComponentInParent<EnderCrystal>() != null)
            {
                continue;
            }

            if (cols[i].GetComponentInParent<DragonFightUI>() != null)
            {
                continue;
            }

            if (cols[i] is CharacterController)
            {
                continue;
            }

            if (hitColliderMode == DragonHitColliderMode.MeshOnVisualChildren
                && cols[i] is BoxCollider
                && cols[i].transform == transform)
            {
                continue;
            }

            if (!list.Contains(cols[i]))
            {
                list.Add(cols[i]);
            }
        }

        dragonHitColliders = list.ToArray();
    }

    private void SetDragonCollidersEnabled(bool enabled)
    {
        if (dragonHitColliders == null || dragonHitColliders.Length == 0)
        {
            CacheDragonHitColliders();
        }

        for (int i = 0; i < dragonHitColliders.Length; i++)
        {
            if (dragonHitColliders[i] != null)
            {
                dragonHitColliders[i].enabled = enabled;
            }
        }
    }

    private void SetAnimatorRunning(bool running, float speed = 1f)
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        if (animator == null)
        {
            return;
        }

        animator.enabled = running;
        if (running)
        {
            animator.speed = speed;
        }
    }

    private void EnsureShieldVisual()
    {
        if (!createShieldIfMissing)
        {
            return;
        }

        // Drop the old placeholder sphere if present; mesh outline is built in Start
        // once the Sketchfab child is in the hierarchy.
        Transform legacy = transform.Find("ShieldAura");
        if (legacy != null && legacy.GetComponent<MeshFilter>() != null
            && legacy.GetComponent<SkinnedMeshRenderer>() == null
            && shieldOutlineObjects.Count == 0)
        {
            if (Application.isPlaying)
            {
                Destroy(legacy.gameObject);
            }
            else
            {
                DestroyImmediate(legacy.gameObject);
            }
        }
    }

    private void EnsureShieldOutlineMaterial()
    {
        if (shieldOutlineMaterial != null)
        {
            return;
        }

        shieldOutlineMaterial = Resources.Load<Material>("DragonShieldOutline");
    }

    private void ResolveShieldAttach()
    {
        if (shieldAttach != null && shieldAttach != transform)
        {
            return;
        }

        Transform existing = transform.Find("ShieldAttach");
        if (existing != null)
        {
            shieldAttach = existing;
        }
        else if (shieldAttach == null)
        {
            shieldAttach = transform;
        }
    }

    private bool TryCreateShieldMaterial(out Material material)
    {
        material = null;

        if (shieldOutlineMaterial != null)
        {
            material = new Material(shieldOutlineMaterial);
            material.name = "DragonCrystalShield";
            ApplyShieldMaterialSettings(material);
            return true;
        }

        Shader shader = Shader.Find("VotanicBow/CrystalShieldGlow");
        if (shader != null)
        {
            material = new Material(shader);
            material.name = "DragonCrystalShield";
            ApplyShieldMaterialSettings(material);
            return true;
        }

        string[] fallbacks =
        {
            "Unlit/Color",
            "Universal Render Pipeline/Unlit",
            "Sprites/Default"
        };

        for (int i = 0; i < fallbacks.Length; i++)
        {
            shader = Shader.Find(fallbacks[i]);
            if (shader == null)
            {
                continue;
            }

            material = new Material(shader);
            material.name = "DragonCrystalShield";
            ApplyShieldMaterialSettings(material);
            Debug.LogWarning(
                "DragonBoss: using fallback shader '" + fallbacks[i]
                + "' for shield outline. Assign Assets/Materials/DragonShieldOutline.mat "
                + "on DragonBoss for CAVE builds.",
                this);
            return true;
        }

        Debug.LogError(
            "DragonBoss: no shield outline shader found. Assign "
            + "Assets/Materials/DragonShieldOutline.mat on DragonBoss.",
            this);
        return false;
    }

    /// <summary>
    /// Builds a solid crystal-colored outline that hugs active child meshes
    /// (Sketchfab dragon, etc.). Right-click component → Rebuild Mesh Shield Outline.
    /// </summary>
    [ContextMenu("Rebuild Mesh Shield Outline")]
    public void RebuildMeshShieldOutline()
    {
        ClearShieldOutlines();

        if (!TryCreateShieldMaterial(out shieldMaterial))
        {
            return;
        }

        baseOutlineWidth = outlineWidth;

        int created = 0;

        SkinnedMeshRenderer[] skinned = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < skinned.Length; i++)
        {
            if (!IsValidShieldSource(skinned[i]))
            {
                continue;
            }

            if (CreateSkinnedOutline(skinned[i], shieldMaterial))
            {
                created++;
            }
        }

        MeshRenderer[] meshes = GetComponentsInChildren<MeshRenderer>(true);
        for (int i = 0; i < meshes.Length; i++)
        {
            if (!IsValidShieldSource(meshes[i]))
            {
                continue;
            }

            if (CreateMeshOutline(meshes[i], shieldMaterial))
            {
                created++;
            }
        }

        if (created == 0)
        {
            Debug.LogWarning(
                "DragonBoss: no active child meshes found for shield outline. "
                + "Enable your dragon prefab child, then use Rebuild Mesh Shield Outline.",
                this);
        }
        else if (logStateChanges)
        {
            Debug.Log("DragonBoss: built crystal shield outline on " + created + " mesh(es).", this);
        }

        SetShieldVisible(ShouldShowCrystalShieldVisual);
    }

    private void ClearShieldOutlines()
    {
        // Always scrub hierarchy for orphans from older rebuilds (common cause of
        // leftover aqua/cyan glow after the magenta shield is toggled off).
        DestroyAllShieldOutlineObjectsInHierarchy();

        shieldOutlineObjects.Clear();

        Transform legacy = transform.Find("ShieldAura");
        if (legacy != null)
        {
            if (Application.isPlaying)
            {
                Destroy(legacy.gameObject);
            }
            else
            {
                DestroyImmediate(legacy.gameObject);
            }
        }

        if (shieldMaterial != null)
        {
            if (Application.isPlaying)
            {
                Destroy(shieldMaterial);
            }
            else
            {
                DestroyImmediate(shieldMaterial);
            }

            shieldMaterial = null;
        }
    }

    private void DestroyAllShieldOutlineObjectsInHierarchy()
    {
        // Collect first — destroying while iterating children is unsafe.
        List<GameObject> toDestroy = new List<GameObject>();
        Transform[] all = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] == null)
            {
                continue;
            }

            string n = all[i].name;
            if (n.EndsWith("_ShieldOutline") || n == "ShieldAura")
            {
                toDestroy.Add(all[i].gameObject);
            }
        }

        for (int i = 0; i < toDestroy.Count; i++)
        {
            if (toDestroy[i] == null)
            {
                continue;
            }

            if (Application.isPlaying)
            {
                Destroy(toDestroy[i]);
            }
            else
            {
                DestroyImmediate(toDestroy[i]);
            }
        }
    }

    private bool IsValidShieldSource(Renderer renderer)
    {
        if (renderer == null || !renderer.enabled)
        {
            return false;
        }

        // Skip disabled placeholder body and anything already part of an outline.
        if (!renderer.gameObject.activeInHierarchy)
        {
            return false;
        }

        if (IsShieldOutlineRenderer(renderer))
        {
            return false;
        }

        string n = renderer.gameObject.name;
        if (n == "DragonBody" || n == "ShieldAura" || n.EndsWith("_ShieldOutline"))
        {
            return false;
        }

        // Ignore crystal visuals if a crystal was parented under the boss by mistake.
        if (renderer.GetComponentInParent<EnderCrystal>() != null)
        {
            return false;
        }

        return true;
    }

    private bool IsShieldOutlineRenderer(Renderer renderer)
    {
        if (renderer == null)
        {
            return false;
        }

        for (int i = 0; i < shieldOutlineObjects.Count; i++)
        {
            if (shieldOutlineObjects[i] != null
                && renderer.transform.IsChildOf(shieldOutlineObjects[i].transform))
            {
                return true;
            }

            if (shieldOutlineObjects[i] == renderer.gameObject)
            {
                return true;
            }
        }

        return renderer.gameObject.name.EndsWith("_ShieldOutline");
    }

    private bool CreateSkinnedOutline(SkinnedMeshRenderer source, Material outlineMat)
    {
        if (source.sharedMesh == null)
        {
            return false;
        }

        GameObject go = new GameObject(source.gameObject.name + "_ShieldOutline");
        go.transform.SetParent(source.transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        SkinnedMeshRenderer outline = go.AddComponent<SkinnedMeshRenderer>();
        outline.sharedMesh = source.sharedMesh;
        outline.bones = source.bones;
        outline.rootBone = source.rootBone;
        outline.localBounds = source.localBounds;
        outline.updateWhenOffscreen = true;
        outline.quality = source.quality;
        outline.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        outline.receiveShadows = false;
        outline.sharedMaterial = outlineMat;

        if (!MaterialSupportsOutlineExtrusion(outlineMat))
        {
            float inflate = 1f + outlineWidth * 4f;
            go.transform.localScale = Vector3.one * inflate;
        }

        shieldOutlineObjects.Add(go);
        return true;
    }

    private bool CreateMeshOutline(MeshRenderer source, Material outlineMat)
    {
        MeshFilter filter = source.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null)
        {
            return false;
        }

        GameObject go = new GameObject(source.gameObject.name + "_ShieldOutline");
        go.transform.SetParent(source.transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        MeshFilter outlineFilter = go.AddComponent<MeshFilter>();
        outlineFilter.sharedMesh = filter.sharedMesh;

        MeshRenderer outline = go.AddComponent<MeshRenderer>();
        outline.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        outline.receiveShadows = false;
        outline.sharedMaterial = outlineMat;

        if (!MaterialSupportsOutlineExtrusion(outlineMat))
        {
            float inflate = 1f + outlineWidth * 4f;
            go.transform.localScale = Vector3.one * inflate;
        }

        shieldOutlineObjects.Add(go);
        return true;
    }

    private static bool MaterialSupportsOutlineExtrusion(Material mat)
    {
        return mat != null && mat.HasProperty("_OutlineWidth");
    }

    private void ApplyShieldMaterialSettings(Material mat)
    {
        if (mat == null)
        {
            return;
        }

        if (mat.HasProperty("_Color"))
        {
            mat.SetColor("_Color", shieldColor);
        }

        if (mat.HasProperty("_OutlineWidth"))
        {
            mat.SetFloat("_OutlineWidth", outlineWidth);
        }
    }

    private void SetShieldVisible(bool visible)
    {
        // Re-scan so orphan outlines from earlier sessions can't keep glowing.
        SyncOutlineListFromHierarchy();

        for (int i = 0; i < shieldOutlineObjects.Count; i++)
        {
            GameObject go = shieldOutlineObjects[i];
            if (go == null)
            {
                continue;
            }

            go.SetActive(visible);

            Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                if (renderers[r] != null)
                {
                    renderers[r].enabled = visible;
                }
            }
        }

        // Final sweep: any shield-shader renderer under the boss must match visibility.
        Renderer[] all = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < all.Length; i++)
        {
            Renderer renderer = all[i];
            if (renderer == null || !RendererUsesShieldShader(renderer))
            {
                continue;
            }

            renderer.enabled = visible;
            if (!visible)
            {
                renderer.gameObject.SetActive(false);
            }
            else if (renderer.gameObject.name.EndsWith("_ShieldOutline"))
            {
                renderer.gameObject.SetActive(true);
            }
        }

    }

    private void SyncOutlineListFromHierarchy()
    {
        shieldOutlineObjects.RemoveAll(go => go == null);

        Transform[] all = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] == null || !all[i].name.EndsWith("_ShieldOutline"))
            {
                continue;
            }

            if (!shieldOutlineObjects.Contains(all[i].gameObject))
            {
                shieldOutlineObjects.Add(all[i].gameObject);
            }
        }
    }

    private static bool RendererUsesShieldShader(Renderer renderer)
    {
        if (renderer == null)
        {
            return false;
        }

        Material[] mats = renderer.sharedMaterials;
        for (int i = 0; i < mats.Length; i++)
        {
            if (mats[i] != null && mats[i].shader != null
                && mats[i].shader.name == "VotanicBow/CrystalShieldGlow")
            {
                return true;
            }
        }

        return false;
    }

    private static void SetMaterialColor(Material mat, Color color)
    {
        if (mat == null)
        {
            return;
        }

        if (mat.HasProperty("_BaseColor"))
        {
            mat.SetColor("_BaseColor", color);
        }

        if (mat.HasProperty("_Color"))
        {
            mat.color = color;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.35f, 1f, 0.95f);
        Gizmos.DrawWireSphere(ShieldAttachPoint, 0.22f);

        Vector3 leadLocal = flightLead != null
            ? transform.InverseTransformPoint(flightLead.position)
            : Vector3.forward * flightLeadDistance;
        Vector3 tailLocal = flightTail != null
            ? transform.InverseTransformPoint(flightTail.position)
            : Vector3.back * flightTailDistance;

        Vector3 leadWorld = transform.TransformPoint(leadLocal);
        Vector3 tailWorld = transform.TransformPoint(tailLocal);

        Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.95f);
        Gizmos.DrawWireSphere(leadWorld, 0.18f);
        Gizmos.color = new Color(1f, 0.55f, 0.15f, 0.95f);
        Gizmos.DrawWireSphere(tailWorld, 0.18f);
        Gizmos.color = Color.white;
        Gizmos.DrawLine(tailWorld, leadWorld);

        if (TryGetFlightBounds(out Vector3 center, out Quaternion rotation, out Vector3 halfExtents))
        {
            Matrix4x4 old = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(center, rotation, Vector3.one);
            Gizmos.color = new Color(0.3f, 1f, 0.45f, 0.9f);
            Gizmos.DrawWireCube(Vector3.zero, halfExtents * 2f);
            Gizmos.color = new Color(0.3f, 1f, 0.45f, 0.12f);
            Gizmos.DrawCube(Vector3.zero, halfExtents * 2f);

            // Fireball spawn band: center fraction of bounds X (full Y/Z of the box).
            float frac = Mathf.Clamp(fireballSpawnCenterXFraction, 0.1f, 1f);
            Vector3 spawnSize = new Vector3(halfExtents.x * 2f * frac, halfExtents.y * 2f, halfExtents.z * 2f);
            Gizmos.color = new Color(1f, 0.92f, 0.15f, 0.95f);
            Gizmos.DrawWireCube(Vector3.zero, spawnSize);
            Gizmos.color = new Color(1f, 0.92f, 0.15f, 0.1f);
            Gizmos.DrawCube(Vector3.zero, spawnSize);

            Gizmos.matrix = old;
        }
    }

#if UNITY_EDITOR
    [ContextMenu("Setup Hit Colliders")]
    private void FitHitColliderMenu()
    {
        SetupHitColliders();
        UnityEditor.EditorUtility.SetDirty(this);
    }

    [ContextMenu("Create Shield Attach Point (Visual Center)")]
    public void CreateShieldAttachPoint()
    {
        Transform existing = transform.Find("ShieldAttach");
        if (existing != null)
        {
            shieldAttach = existing;
            UnityEditor.Selection.activeGameObject = existing.gameObject;
            return;
        }

        Vector3 center = transform.position;
        if (TryGetVisualBounds(out Bounds bounds, includeWhenDisabled: true))
        {
            center = bounds.center;
        }

        GameObject anchor = new GameObject("ShieldAttach");
        anchor.transform.SetParent(transform, true);
        anchor.transform.position = center;
        anchor.transform.localRotation = Quaternion.identity;
        shieldAttach = anchor.transform;
        shieldAttachLocalOffset = Vector3.zero;

        UnityEditor.Selection.activeGameObject = anchor;
        UnityEditor.EditorUtility.SetDirty(this);
        Debug.Log(
            "DragonBoss: ShieldAttach placed at visual center. Move it in Scene view "
            + "so crystal beams aim inside the dragon.",
            this);
    }

    [ContextMenu("Create Flight Markers (Head + Tail)")]
    public void CreateFlightMarkers()
    {
        Vector3 axis = transform.forward;
        Vector3 leadWorld = transform.position + axis * flightLeadDistance;
        Vector3 tailWorld = transform.position - axis * flightTailDistance;

        if (TryGetVisualBounds(out Bounds bounds, includeWhenDisabled: true))
        {
            Vector3 localFwd = transform.InverseTransformDirection(axis);
            float along = Mathf.Abs(localFwd.x) * bounds.extents.x
                          + Mathf.Abs(localFwd.y) * bounds.extents.y
                          + Mathf.Abs(localFwd.z) * bounds.extents.z;
            along = Mathf.Max(along, 0.5f);
            leadWorld = bounds.center + axis * along;
            tailWorld = bounds.center - axis * along;
        }

        flightLead = EnsureChildMarker("FlightLead", leadWorld);
        flightTail = EnsureChildMarker("FlightTail", tailWorld);
        CacheFlightMarkers();

        UnityEditor.Selection.activeGameObject = flightLead.gameObject;
        UnityEditor.EditorUtility.SetDirty(this);
        Debug.Log(
            "DragonBoss: FlightLead (cyan) + FlightTail (orange) created. "
            + "Move them to snout and tail tip — body length "
            + flightBodyLength.ToString("0.00") + "m drives path sizing and rotation.",
            this);
    }

    private Transform EnsureChildMarker(string markerName, Vector3 worldPosition)
    {
        Transform existing = transform.Find(markerName);
        if (existing != null)
        {
            existing.position = worldPosition;
            return existing;
        }

        GameObject go = new GameObject(markerName);
        go.transform.SetParent(transform, true);
        go.transform.position = worldPosition;
        go.transform.localRotation = Quaternion.identity;
        return go.transform;
    }

    [ContextMenu("Create Fight Audio")]
    public void CreateFightAudio()
    {
        FightAudio existing = GetComponent<FightAudio>();
        if (existing == null)
        {
            existing = FindObjectOfType<FightAudio>();
        }

        if (existing != null)
        {
            UnityEditor.Selection.activeGameObject = existing.gameObject;
            Debug.Log("FightAudio already exists — select it and assign AudioClips.", existing);
            return;
        }

        FightAudio audio = gameObject.AddComponent<FightAudio>();
        UnityEditor.Selection.activeGameObject = gameObject;
        UnityEditor.EditorUtility.SetDirty(this);
        Debug.Log(
            "FightAudio added. Assign clips: Arrow Shot, Shield Bounce, Dragon Hurt[] / Death, "
            + "Fireball Shoot/Explode, Crystal Explode, Dragon Flaps[]. Tune Flap Interval to match wing anim.",
            audio);
    }

    [ContextMenu("Create Flight Bounds")]
    public void CreateFlightBounds()
    {
        Transform existing = null;
        if (transform.parent != null)
        {
            existing = transform.parent.Find("FlightBounds");
        }

        if (existing == null)
        {
            GameObject found = GameObject.Find("FlightBounds");
            if (found != null)
            {
                existing = found.transform;
            }
        }

        if (existing != null)
        {
            flightBounds = existing;
            constrainToFlightBounds = true;
            UnityEditor.Selection.activeGameObject = existing.gameObject;
            UnityEditor.EditorUtility.SetDirty(this);
            return;
        }

        Vector3 center = flightCenterAnchor != null ? flightCenterAnchor.position : transform.position;
        GameObject go = new GameObject("FlightBounds");
        if (transform.parent != null)
        {
            go.transform.SetParent(transform.parent, true);
        }

        go.transform.position = center;
        go.transform.rotation = Quaternion.Euler(0f, pathYawDegrees, 0f);

        BoxCollider box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        // Default arena-sized box — scale in the Scene view to taste.
        box.size = new Vector3(
            Mathf.Max(6f, pathWidth * 2.2f),
            Mathf.Max(3f, heightAmplitude * 3f),
            Mathf.Max(5f, pathDepth * 2.2f));
        box.center = Vector3.zero;

        flightBounds = go.transform;
        constrainToFlightBounds = true;
        autoFitPathToBody = false;

        UnityEditor.Selection.activeGameObject = go;
        UnityEditor.EditorUtility.SetDirty(this);
        Debug.Log(
            "DragonBoss: FlightBounds created (green box). Scale the BoxCollider size "
            + "so the dragon stays inside that volume.",
            this);
    }

    [ContextMenu("Create Flight Center Anchor")]
    private void CreateFlightCenterAnchor()
    {
        Transform existing = transform.Find("FlightCenter");
        if (existing != null)
        {
            flightCenterAnchor = existing;
            UnityEditor.Selection.activeGameObject = existing.gameObject;
            return;
        }

        GameObject anchor = new GameObject("FlightCenter");
        anchor.transform.SetParent(transform.parent, true);
        anchor.transform.position = transform.position;
        anchor.transform.rotation = Quaternion.identity;
        flightCenterAnchor = anchor.transform;
        UnityEditor.Selection.activeGameObject = anchor;
        UnityEditor.EditorUtility.SetDirty(this);
    }

    [ContextMenu("Create Fight UI Panel")]
    public void CreateFightUiPanel()
    {
        DragonFightUI existing = FindObjectOfType<DragonFightUI>();
        if (existing != null)
        {
            fightUI = existing;
            existing.Bind(this);
            UnityEditor.Selection.activeGameObject = existing.gameObject;
            Debug.Log("DragonFightUI already exists — selected it.", existing);
            return;
        }

        GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Quad);
        panel.name = "DragonFightUI";
        panel.transform.position = transform.position + Vector3.left * 3f + Vector3.up * 1.6f;
        Vector3 toPanel = panel.transform.position - transform.position;
        if (toPanel.sqrMagnitude > 1e-6f)
        {
            panel.transform.rotation = Quaternion.LookRotation(toPanel.normalized);
        }

        panel.transform.localScale = new Vector3(1.1f, 0.7f, 1f);

        Renderer renderer = panel.GetComponent<Renderer>();
        if (renderer != null && renderer.sharedMaterial != null)
        {
            Material mat = new Material(renderer.sharedMaterial);
            Color panelColor = new Color(0.08f, 0.05f, 0.12f, 0.9f);
            if (mat.HasProperty("_Color"))
            {
                mat.color = panelColor;
            }
            else if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", panelColor);
            }

            renderer.sharedMaterial = mat;
        }

        GameObject textGo = new GameObject("Label");
        textGo.transform.SetParent(panel.transform, false);
        textGo.transform.localPosition = new Vector3(0f, 0f, -0.01f);
        TextMesh textMesh = textGo.AddComponent<TextMesh>();
        textMesh.anchor = TextAnchor.MiddleCenter;
        textMesh.alignment = TextAlignment.Center;
        textMesh.characterSize = 0.08f;
        textMesh.fontSize = 64;
        textMesh.color = Color.white;

        DragonFightUI ui = panel.AddComponent<DragonFightUI>();
        ui.Assign(this, textMesh);
        fightUI = ui;

        UnityEditor.Selection.activeGameObject = panel;
        UnityEditor.EditorUtility.SetDirty(this);
        Debug.Log("Created DragonFightUI. Move it in the Scene view.", panel);
    }

    /// <summary>
    /// Builds a simple stand-in dragon body + 4 pillar crystals around it for layout.
    /// </summary>
    [ContextMenu("Create Placeholder Arena (Dragon + 4 Crystals)")]
    public void CreatePlaceholderArena()
    {
        if (transform.Find("DragonBody") == null)
        {
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "DragonBody";
            body.transform.SetParent(transform, false);
            body.transform.localPosition = Vector3.zero;
            body.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            body.transform.localScale = new Vector3(1.2f, 2.5f, 1.2f);

            Collider bodyCol = body.GetComponent<Collider>();
            if (bodyCol != null)
            {
                DestroyImmediate(bodyCol);
            }

            Renderer renderer = body.GetComponent<Renderer>();
            if (renderer != null)
            {
                Material mat = CreateOpaqueMaterial(renderer.sharedMaterial);
                SetMaterialColor(mat, new Color(0.25f, 0.05f, 0.35f, 1f));
                renderer.sharedMaterial = mat;
            }
        }

        // Ensure root has a hittable collider (mesh on visual children by default).
        if (autoSetupHitCollider)
        {
            SetupHitColliders();
        }

        float radius = 6f;
        float height = 4f;
        for (int i = 0; i < 4; i++)
        {
            float angle = i * 90f * Mathf.Deg2Rad;
            Vector3 pillarPos = transform.position
                                + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);

            string pillarName = "CrystalPillar_" + (i + 1);
            Transform existingPillar = transform.parent != null
                ? transform.parent.Find(pillarName)
                : null;
            if (existingPillar == null)
            {
                // Search siblings / scene root by name near us.
                GameObject found = GameObject.Find(pillarName);
                if (found != null)
                {
                    existingPillar = found.transform;
                }
            }

            if (existingPillar != null)
            {
                continue;
            }

            GameObject pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pillar.name = pillarName;
            pillar.transform.position = pillarPos + Vector3.up * (height * 0.5f);
            pillar.transform.localScale = new Vector3(0.7f, height * 0.5f, 0.7f);

            Renderer pillarRenderer = pillar.GetComponent<Renderer>();
            if (pillarRenderer != null)
            {
                Material mat = CreateOpaqueMaterial(pillarRenderer.sharedMaterial);
                SetMaterialColor(mat, new Color(0.35f, 0.3f, 0.28f, 1f));
                pillarRenderer.sharedMaterial = mat;
            }

            GameObject crystalGo = new GameObject("EnderCrystal");
            crystalGo.transform.SetParent(pillar.transform, false);
            crystalGo.transform.localPosition = new Vector3(0f, 1.15f, 0f);
            crystalGo.transform.localScale = Vector3.one;

            SphereCollider crystalCol = crystalGo.AddComponent<SphereCollider>();
            crystalCol.radius = 0.4f;

            EnderCrystal crystal = crystalGo.AddComponent<EnderCrystal>();
            crystal.Bind(this);

            // Placeholder mesh via public-ish path: create child sphere.
            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            visual.name = "CrystalVisual";
            visual.transform.SetParent(crystalGo.transform, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localScale = Vector3.one * 0.7f;
            Collider visualCol = visual.GetComponent<Collider>();
            if (visualCol != null)
            {
                DestroyImmediate(visualCol);
            }

            Renderer crystalRenderer = visual.GetComponent<Renderer>();
            if (crystalRenderer != null)
            {
                Material mat = CreateOpaqueMaterial(crystalRenderer.sharedMaterial);
                Color c = new Color(1f, 0.4f, 1f, 1f);
                SetMaterialColor(mat, c);
                if (mat.HasProperty("_EmissionColor"))
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", c * 1.4f);
                }

                crystalRenderer.sharedMaterial = mat;
            }

            SerializedAssignCrystalVisual(crystal, visual);
            crystals.Add(crystal);
        }

        if (createShieldIfMissing)
        {
            RebuildMeshShieldOutline();
        }

        UnityEditor.EditorUtility.SetDirty(this);
        Debug.Log("DragonBoss: placeholder arena created (body + 4 pillars/crystals). Move/scale as needed.", this);
    }

    private static Material CreateOpaqueMaterial(Material fallback)
    {
        Shader shader = Shader.Find("Standard");
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Lit");
        }

        if (shader != null)
        {
            return new Material(shader);
        }

        if (fallback != null)
        {
            return new Material(fallback);
        }

        return new Material(Shader.Find("Hidden/InternalErrorShader"));
    }

    private static void SerializedAssignCrystalVisual(EnderCrystal crystal, GameObject visual)
    {
        var so = new UnityEditor.SerializedObject(crystal);
        so.FindProperty("crystalVisual").objectReferenceValue = visual;
        so.FindProperty("dragon").objectReferenceValue = null; // will auto-find / register
        so.ApplyModifiedPropertiesWithoutUndo();
    }
#endif
}
