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
    [SerializeField] private float fallDuration = 1.4f;
    [SerializeField] private float fallDropDistance = 3.5f;
    [SerializeField] private float fallPitchDegrees = 75f;
    [SerializeField] private float fadeOutDuration = 0.65f;

    [Header("Hit Collider")]
    [Tooltip("Mesh colliders on the visual mesh children (recommended). Box is a simple fallback.")]
    [SerializeField] private DragonHitColliderMode hitColliderMode = DragonHitColliderMode.MeshOnVisualChildren;
    [SerializeField] private bool autoSetupHitCollider = true;
    [SerializeField] private float hitColliderPadding = 1.08f;
    [SerializeField] private bool meshColliderConvex = true;
    [SerializeField] private BoxCollider hitCollider;

    [Header("Flight Path")]
    [Tooltip("World center of the figure-8. If empty, uses this object's position at Awake.")]
    [SerializeField] private Transform flightCenterAnchor;
    [SerializeField] private float pathWidth = 5f;
    [SerializeField] private float pathDepth = 3.5f;
    [SerializeField] private float pathSpeed = 0.35f;
    [Tooltip("Rotate the figure-8 horizontally (degrees).")]
    [SerializeField] private float pathYawDegrees = 0f;
    [Tooltip("Extra yaw so flight direction matches model forward. 180 for models facing -Z.")]
    [SerializeField] private float modelYawOffsetDegrees = 180f;
    [SerializeField] private float heightAmplitude = 1.2f;
    [SerializeField] private float heightFrequency = 0.55f;
    [SerializeField] private float pitchAmplitude = 12f;
    [SerializeField] private float pitchFrequency = 0.22f;
    [SerializeField] private float pitchWobbleFrequency = 0.17f;
    [Tooltip("Figure-8 while waiting on Start (slower).")]
    [SerializeField] private bool idleFlightWhileWaiting = true;
    [SerializeField] private float idlePathSpeedMultiplier = 0.45f;

    private enum FightPhase
    {
        Waiting,
        Playing,
        Ended
    }

    private readonly List<EnderCrystal> liveCrystals = new List<EnderCrystal>(8);
    private readonly List<GameObject> shieldOutlineObjects = new List<GameObject>(8);
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
    private Vector3 spawnPosition;
    private Quaternion spawnRotation;
    private bool isDying;
    private float damageFlashEndTime;
    private Coroutine deathRoutine;
    private Vector3 visualScaleRootBaseScale;

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

        if (dead || !shieldUp || shieldMaterial == null || phase != FightPhase.Playing)
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

        transform.position = spawnPosition;
        transform.rotation = spawnRotation;
        RestoreBodyVisuals();

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
        SetShieldVisible(false);
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

        if (fightUI != null)
        {
            fightUI.ShowTimeout();
        }

        if (logStateChanges)
        {
            Debug.Log("DragonBoss: time up.", this);
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

        bool shouldShield = phase == FightPhase.Playing && !dead && !isDying && liveCrystals.Count > 0;
        if (shouldShield != shieldUp)
        {
            shieldUp = shouldShield;
            if (logStateChanges)
            {
                Debug.Log(
                    shieldUp
                        ? "DragonBoss: shield UP (" + liveCrystals.Count + " crystal(s))."
                        : "DragonBoss: shield DOWN — dragon is vulnerable!",
                    this);
            }
        }

        SetShieldVisible(shieldUp);
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

        if (deathRoutine != null)
        {
            StopCoroutine(deathRoutine);
        }

        deathRoutine = StartCoroutine(DeathFallAndFadeRoutine());
    }

    private IEnumerator DeathFallAndFadeRoutine()
    {
        Vector3 startPos = transform.position;
        Quaternion startRot = transform.rotation;
        Vector3 endPos = startPos + Vector3.down * fallDropDistance;
        Quaternion tippedRot = startRot * Quaternion.Euler(fallPitchDegrees, 0f, 0f);

        float fallElapsed = 0f;
        while (fallElapsed < fallDuration)
        {
            fallElapsed += Time.deltaTime;
            float t = Mathf.Clamp01(fallElapsed / fallDuration);
            float eased = t * t;
            transform.position = Vector3.Lerp(startPos, endPos, eased);
            transform.rotation = Quaternion.Slerp(startRot, tippedRot, eased);
            yield return null;
        }

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
            return;
        }

        bool flying = phase == FightPhase.Playing && !dead
                        || (phase == FightPhase.Waiting && idleFlightWhileWaiting);

        if (!flying)
        {
            return;
        }

        float speedMul = phase == FightPhase.Playing ? 1f : idlePathSpeedMultiplier;
        flightPhase += pathSpeed * speedMul * Time.deltaTime;

        float t = flightPhase;
        float omegaT = t;

        // Horizontal figure-8 in local XZ, then rotate to arena orientation.
        Vector3 localOffset = new Vector3(
            pathWidth * Mathf.Sin(omegaT),
            heightAmplitude * Mathf.Sin(omegaT * heightFrequency)
            + heightAmplitude * 0.35f * Mathf.Sin(omegaT * heightFrequency * 2.3f + 1.1f),
            pathDepth * Mathf.Sin(2f * omegaT));

        Quaternion pathRotation = Quaternion.Euler(0f, pathYawDegrees, 0f);
        Vector3 offset = pathRotation * localOffset;

        if (flightCenterAnchor != null)
        {
            flightCenter = flightCenterAnchor.position;
        }

        Vector3 targetPos = flightCenter + offset;
        transform.position = Vector3.Lerp(transform.position, targetPos, 1f - Mathf.Exp(-8f * Time.deltaTime));

        Vector3 localTangent = new Vector3(
            pathWidth * Mathf.Cos(omegaT),
            heightAmplitude * heightFrequency * Mathf.Cos(omegaT * heightFrequency)
            + heightAmplitude * 0.35f * heightFrequency * 2.3f
              * Mathf.Cos(omegaT * heightFrequency * 2.3f + 1.1f),
            pathDepth * 2f * Mathf.Cos(2f * omegaT));

        Vector3 tangent = pathRotation * localTangent;

        if (tangent.sqrMagnitude < 1e-6f)
        {
            tangent = transform.position - lastFlightPosition;
        }

        if (tangent.sqrMagnitude < 1e-6f)
        {
            tangent = -Vector3.forward;
        }

        tangent.Normalize();

        float pitch = pitchAmplitude * Mathf.Sin(omegaT * pitchFrequency)
                      * Mathf.Sin(omegaT * pitchWobbleFrequency + 0.6f);
        Quaternion face = Quaternion.LookRotation(tangent, Vector3.up)
                          * Quaternion.Euler(0f, modelYawOffsetDegrees, 0f);
        Quaternion pitchRot = Quaternion.AngleAxis(pitch, face * Vector3.right);
        Quaternion targetRot = pitchRot * face;
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRot,
            1f - Mathf.Exp(-6f * Time.deltaTime));

        lastFlightPosition = transform.position;
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

        SetShieldVisible(shieldUp && !dead);
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
#if UNITY_EDITOR
            UnityEditor.Selection.activeGameObject = existing.gameObject;
#endif
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

#if UNITY_EDITOR
        UnityEditor.Selection.activeGameObject = anchor;
        UnityEditor.EditorUtility.SetDirty(this);
#endif
        Debug.Log(
            "DragonBoss: ShieldAttach placed at visual center. Move it in Scene view "
            + "so crystal beams aim inside the dragon.",
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
