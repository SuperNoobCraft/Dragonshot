using UnityEngine;
using Votanic.vXR.vCast;

public enum ArrowSupplyMode
{
    AlwaysReady,
    FromBarrel
}

/// <summary>
/// CAVE aim styles to A/B test.
/// BowOnly = Wii-style (bow aims, string only draws).
/// FreeShaft = classic VR (aim along rest → string hand).
/// SoftCoupled = bow primary, string offset gently steers.
/// </summary>
public enum BowAimMode
{
    BowOnly,
    FreeShaft,
    SoftCoupled
}

/// <summary>
/// Desktop PC: bow + held arrow follow vCast Head (mouse look) in world space each LateUpdate.
/// Hold RMB to draw, release to shoot along look forward.
/// Tracked XR: bow on left hand; pull distance sets power; aim mode is configurable.
/// </summary>
[DefaultExecutionOrder(1000)]
public class BowController : MonoBehaviour
{
    [Header("Arrow")]
    [SerializeField] private ArrowProjectile arrowPrefab;
    [SerializeField] private Transform arrowRest;
    [Tooltip("Bow model string control (moves the string curve). Follows right hand / arrow rear while drawing.")]
    [SerializeField] private Transform bowString;
    [Tooltip("If set (or found in scene), used when supply mode is From Barrel.")]
    [SerializeField] private ArrowQuiver quiver;
    [Tooltip("When Always Ready, keep a held arrow available automatically after each shot.")]
    [SerializeField] private bool autoSpawnHeldArrow = true;
    [Tooltip("Always Ready = arrow on hand without barrel. From Barrel = pick up from quiver.")]
    [SerializeField] private ArrowSupplyMode supplyMode = ArrowSupplyMode.FromBarrel;

    [Header("Shot")]
    [Tooltip("HP removed from the dragon per arrow hit.")]
    [SerializeField, Min(1)] private int damagePerShot = 1;
    [Tooltip("Weak / flop release (m/s). Real bows flop much softer than full draw.")]
    [SerializeField] private float minSpeed = 8f;
    [Tooltip("Full-draw launch speed (m/s). Olympic recurve ≈ 55–60 m/s; room-scale demos "
             + "read better around 22–30 so the arc is visible. 40+ feels laser-like with default gravity.")]
    [SerializeField] private float maxSpeed = 26f;
    [Tooltip("Shape of draw→speed. 1 = linear; >1 needs closer to full draw for top speed.")]
    [SerializeField, Range(1f, 2.5f)] private float drawSpeedExponent = 1.35f;
    [Tooltip("Tracked: release below this draw cancels instead of firing. Keep low so a tiny pull still flop-fires.")]
    [SerializeField, Range(0f, 1f)] private float minDrawToShoot = 0.02f;

    [Header("Desktop")]
    [SerializeField] private Vector3 holdOffset = new Vector3(0.25f, -0.2f, 0.55f);
    [SerializeField] private Vector3 holdEuler;
    [SerializeField] private float fullDrawTime = 0.75f;
    [SerializeField] private float pullDistance = 0.35f;
    [Tooltip("Local offset of the held arrow relative to the bow (before tip is aimed along look).")]
    [SerializeField] private Vector3 desktopArrowLocalPosition;
    [Tooltip("Local euler of held arrow under the bow. (0,-90,0) for tip-along-+X meshes.")]
    [SerializeField] private Vector3 desktopArrowLocalEuler = new Vector3(0f, -90f, 0f);
    [Tooltip("Also shoot with Space on desktop (backup if RMB is eaten by Votanic).")]
    [SerializeField] private bool desktopSpaceToShoot = true;

    [Header("Tracked XR")]
    [SerializeField] private LeftHandChild leftHandChild;
    [Tooltip("Primary right-hand name. Also tries Head/Hand path under vGear.")]
    [SerializeField] private string rightHandName = "Hand1";
    [Tooltip("Start draw when right hand is within this distance of the bow / left hand (meters). Generous on purpose.")]
    [SerializeField] private float nockStartDistance = 0.5f;
    [Tooltip("Hand separation at full draw (meters). Power scales from nock → this.")]
    [SerializeField] private float maxDrawDistance = 1.0f;
    [SerializeField, Range(0.01f, 0.5f)] private float axisDeadzone = 0.08f;
    [SerializeField] private int maxControllersToScan = 4;
    [SerializeField] private int maxAxesToScan = 16;
    [SerializeField] private bool logInputDetection = true;
    [Tooltip("Visual only: keep idle arrow parented to the right hand when found.")]
    [SerializeField] private bool idleArrowOnRightHand = true;
    [SerializeField] private Vector3 rightHandArrowLocalPosition;
    [Tooltip("Idle arrow on right hand. (0,-90,0) matches imported tip-along-+X meshes.")]
    [SerializeField] private Vector3 rightHandArrowLocalEuler = new Vector3(0f, -90f, 0f);
    [Tooltip("CAVE shot direction in bow-model local space. Recurve import aims along -Y.")]
    [SerializeField] private Vector3 trackedAimLocalAxis = new Vector3(0f, -1f, 0f);
    [Tooltip("Extra pitch (degrees) applied to CAVE aim. Negative = tip slightly down "
             + "(compensates grip so a 'level' hold does not loft).")]
    [SerializeField] private float trackedAimPitchOffsetDegrees = -1.5f;
    [Tooltip("CAVE aim style. Cycle at runtime via Aim Mode config label.")]
    [SerializeField] private BowAimMode aimMode = BowAimMode.SoftCoupled;
    [Tooltip("SoftCoupled only: 0 = pure bow aim, 1 = pure string-hand shaft aim.")]
    [SerializeField, Range(0f, 1f)] private float softCoupleBlend = 0.3f;
    [Tooltip("Hide Votanic wand laser while an arrow is in hand (uses DisplayWandRay / EnableWandRay).")]
    [SerializeField] private bool hideWandRayWhileHoldingArrow = true;

    private enum State { Idle, Drawing }

    private State state;
    private float draw;
    private bool desktop;
    private ArrowProjectile arrow;
    private Transform rightHand;
    private float nextHandSearch;
    private Collider[] bowColliders;
    private bool wasInputHeld;
    private float nextNockDebugTime;
    private Vector3 bowStringRestLocalPos;
    private bool hasBowStringRest;
    private bool wandRayForcedHidden;
    private bool loggedDesktopHead;
    private bool rmbWasHeld;

    /// <summary>
    /// Places an editable world-space toggle panel near the bow.
    /// </summary>
    [ContextMenu("Create Arrow Supply Config Label")]
    public void CreateArrowSupplyConfigLabel()
    {
        ArrowSupplyConfigLabel existing = FindObjectOfType<ArrowSupplyConfigLabel>();
        if (existing != null)
        {
#if UNITY_EDITOR
            UnityEditor.Selection.activeGameObject = existing.gameObject;
#endif
            Debug.Log("ArrowSupplyConfigLabel already exists — selected it.", existing);
            return;
        }

        GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Quad);
        panel.name = "ArrowSupplyConfigLabel";
        panel.transform.position = transform.position + transform.right * 0.6f + Vector3.up * 0.3f;
        panel.transform.rotation = Quaternion.LookRotation(
            (panel.transform.position - transform.position).normalized);
        panel.transform.localScale = new Vector3(0.9f, 0.55f, 1f);

        Renderer renderer = panel.GetComponent<Renderer>();
        if (renderer != null && renderer.sharedMaterial != null)
        {
            Material mat = new Material(renderer.sharedMaterial);
            if (mat.HasProperty("_Color"))
            {
                mat.color = new Color(0.08f, 0.12f, 0.18f, 0.9f);
            }
            else if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", new Color(0.08f, 0.12f, 0.18f, 0.9f));
            }

            renderer.sharedMaterial = mat;
        }

        GameObject textGo = new GameObject("Label");
        textGo.transform.SetParent(panel.transform, false);
        textGo.transform.localPosition = new Vector3(0f, 0f, -0.01f);
        TextMesh textMesh = textGo.AddComponent<TextMesh>();
        textMesh.anchor = TextAnchor.MiddleCenter;
        textMesh.alignment = TextAlignment.Center;
        textMesh.characterSize = 0.06f;
        textMesh.fontSize = 48;
        textMesh.color = Color.white;

        ArrowSupplyConfigLabel config = panel.AddComponent<ArrowSupplyConfigLabel>();
        config.AssignBowAndLabel(this, textMesh);

#if UNITY_EDITOR
        UnityEditor.Selection.activeGameObject = panel;
        UnityEditor.EditorUtility.SetDirty(panel);
#endif
        Debug.Log("Created ArrowSupplyConfigLabel. Move it in the Scene view.", panel);
    }

    /// <summary>
    /// Places a world-space panel to cycle CAVE aim modes (Bow Only / Free Shaft / Soft Coupled).
    /// </summary>
    [ContextMenu("Create Aim Mode Config Label")]
    public void CreateAimModeConfigLabel()
    {
        AimModeConfigLabel existing = FindObjectOfType<AimModeConfigLabel>();
        if (existing != null)
        {
#if UNITY_EDITOR
            UnityEditor.Selection.activeGameObject = existing.gameObject;
#endif
            Debug.Log("AimModeConfigLabel already exists — selected it.", existing);
            return;
        }

        GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Quad);
        panel.name = "AimModeConfigLabel";
        panel.transform.position = transform.position + transform.right * -0.6f + Vector3.up * 0.3f;
        Vector3 toPanel = panel.transform.position - transform.position;
        if (toPanel.sqrMagnitude > 1e-6f)
        {
            panel.transform.rotation = Quaternion.LookRotation(toPanel.normalized);
        }

        panel.transform.localScale = new Vector3(0.95f, 0.55f, 1f);

        Renderer renderer = panel.GetComponent<Renderer>();
        if (renderer != null && renderer.sharedMaterial != null)
        {
            Material mat = new Material(renderer.sharedMaterial);
            if (mat.HasProperty("_Color"))
            {
                mat.color = new Color(0.12f, 0.08f, 0.18f, 0.9f);
            }
            else if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", new Color(0.12f, 0.08f, 0.18f, 0.9f));
            }

            renderer.sharedMaterial = mat;
        }

        GameObject textGo = new GameObject("Label");
        textGo.transform.SetParent(panel.transform, false);
        textGo.transform.localPosition = new Vector3(0f, 0f, -0.01f);
        TextMesh textMesh = textGo.AddComponent<TextMesh>();
        textMesh.anchor = TextAnchor.MiddleCenter;
        textMesh.alignment = TextAlignment.Center;
        textMesh.characterSize = 0.055f;
        textMesh.fontSize = 48;
        textMesh.color = Color.white;

        AimModeConfigLabel config = panel.AddComponent<AimModeConfigLabel>();
        config.AssignBowAndLabel(this, textMesh);

#if UNITY_EDITOR
        UnityEditor.Selection.activeGameObject = panel;
        UnityEditor.EditorUtility.SetDirty(panel);
#endif
        Debug.Log("Created AimModeConfigLabel. Move it in the Scene view.", panel);
    }

    public bool HasArrowInHand => arrow != null;

    public ArrowSupplyMode SupplyMode => supplyMode;

    public bool IsAlwaysReadyMode => supplyMode == ArrowSupplyMode.AlwaysReady;

    /// <summary>
    /// Toggle or set whether arrows auto-appear on the hand, or must be taken from the barrel.
    /// </summary>
    public void SetSupplyMode(ArrowSupplyMode mode)
    {
        if (supplyMode == mode)
        {
            return;
        }

        supplyMode = mode;
        ApplySupplyMode();
    }

    public ArrowSupplyMode ToggleSupplyMode()
    {
        SetSupplyMode(
            supplyMode == ArrowSupplyMode.AlwaysReady
                ? ArrowSupplyMode.FromBarrel
                : ArrowSupplyMode.AlwaysReady);
        return supplyMode;
    }

    public BowAimMode AimMode => aimMode;

    public float SoftCoupleBlend
    {
        get => softCoupleBlend;
        set => softCoupleBlend = Mathf.Clamp01(value);
    }

    public void SetAimMode(BowAimMode mode)
    {
        if (aimMode == mode)
        {
            return;
        }

        aimMode = mode;
        if (logInputDetection)
        {
            Debug.Log("BowController: aim mode = " + AimModeLabel(aimMode), this);
        }
    }

    public BowAimMode CycleAimMode()
    {
        int next = ((int)aimMode + 1) % 3;
        SetAimMode((BowAimMode)next);
        return aimMode;
    }

    public static string AimModeLabel(BowAimMode mode)
    {
        switch (mode)
        {
            case BowAimMode.BowOnly:
                return "Bow Only (Wii)";
            case BowAimMode.FreeShaft:
                return "Free Shaft (VR)";
            case BowAimMode.SoftCoupled:
                return "Soft Coupled";
            default:
                return mode.ToString();
        }
    }

    private void ApplySupplyMode()
    {
        if (ShouldAutoSpawnHeldArrow && arrow == null)
        {
            SpawnArrow();
        }

        if (logInputDetection)
        {
            Debug.Log(
                supplyMode == ArrowSupplyMode.AlwaysReady
                    ? "BowController: arrow supply = Always Ready (auto on hand)."
                    : "BowController: arrow supply = From Barrel (pick up from quiver).",
                this);
        }
    }

    public Transform RightHandTransform
    {
        get
        {
            FindRightHand();
            return rightHand;
        }
    }

    public bool IsRightHandNearBow
    {
        get
        {
            FindRightHand();
            if (rightHand == null)
            {
                return false;
            }

            return GetHandsDistance() <= nockStartDistance;
        }
    }

    private bool UsesQuiver => supplyMode == ArrowSupplyMode.FromBarrel && quiver != null;

    private bool ShouldAutoSpawnHeldArrow =>
        supplyMode == ArrowSupplyMode.AlwaysReady && autoSpawnHeldArrow;

    private void Awake()
    {
        if (arrowRest == null)
        {
            arrowRest = transform;
        }

        if (leftHandChild == null)
        {
            leftHandChild = GetComponent<LeftHandChild>();
        }

        if (quiver == null)
        {
#if UNITY_2023_1_OR_NEWER
            quiver = FindFirstObjectByType<ArrowQuiver>();
#else
            quiver = FindObjectOfType<ArrowQuiver>();
#endif
        }

        bowColliders = GetComponentsInChildren<Collider>(true);
        CacheBowStringRest();

        if (ShouldAutoSpawnHeldArrow)
        {
            SpawnArrow();
        }
    }

    private void CacheBowStringRest()
    {
        if (bowString == null)
        {
            hasBowStringRest = false;
            return;
        }

        bowStringRestLocalPos = bowString.localPosition;
        hasBowStringRest = true;
    }

    private void OnEnable()
    {
        PlayEnvironment.EnvironmentChanged += RefreshMode;
        RefreshMode();
    }

    private void OnDisable()
    {
        PlayEnvironment.EnvironmentChanged -= RefreshMode;
        CancelDraw();
        SetWandRayVisible(true);
        wandRayForcedHidden = false;
    }

    private void Update()
    {
        if (!IsSceneInstance)
        {
            return;
        }

        desktop = PlayEnvironment.IsDesktopInput;

        if (desktop)
        {
            DesktopInput();
        }
        else
        {
            TrackedInput();
        }
    }

    private void LateUpdate()
    {
        if (!IsSceneInstance)
        {
            return;
        }

        desktop = PlayEnvironment.IsDesktopInput;

        if (!desktop)
        {
            if (leftHandChild != null && leftHandChild.isActiveAndEnabled)
            {
                leftHandChild.FollowBoundHand();
            }

            FindRightHand();
            UpdateWandRayVisibility();

            if (state == State.Idle)
            {
                UpdateIdleArrowVisual();
                ResetBowString();
            }
            else if (state == State.Drawing)
            {
                TrackedDrawVisual();
            }

            return;
        }

        // Desktop: drive pose in LateUpdate AFTER Votanic applies VirtualTracker0 to Head.
        ApplyDesktopPose();
        UpdateWandRayVisibility();
    }

    private void RefreshMode()
    {
        CancelDraw();
        desktop = PlayEnvironment.IsDesktopInput;
        loggedDesktopHead = false;

        if (leftHandChild != null)
        {
            leftHandChild.enabled = !desktop;
        }

        if (desktop)
        {
            ApplyDesktopPose();
        }
        else if (leftHandChild != null)
        {
            leftHandChild.FollowBoundHand();
        }

        if (ShouldAutoSpawnHeldArrow)
        {
            SpawnArrow();
        }

        if (!desktop && logInputDetection)
        {
            Debug.Log(
                UsesQuiver
                    ? "BowController: CAVE mode — pick arrows from the quiver, then nock/pull/release."
                    : "BowController: CAVE mode — always-ready arrow on hand; nock/pull/release.",
                this);
        }
    }

    // -------------------------------------------------------------------------
    // Desktop
    // -------------------------------------------------------------------------

    private void DesktopInput()
    {
        // RMB and optional Space (Votanic sometimes eats Mouse1).
        bool held = Input.GetMouseButton(1)
                    || Input.GetKey(KeyCode.Mouse1)
                    || (desktopSpaceToShoot && Input.GetKey(KeyCode.Space));

        if (held && !rmbWasHeld)
        {
            if (state == State.Idle)
            {
                BeginDraw();
            }
        }

        if (held && state == State.Drawing)
        {
            draw = Mathf.Clamp01(draw + Time.deltaTime / Mathf.Max(0.01f, fullDrawTime));
        }

        if (!held && rmbWasHeld && state == State.Drawing)
        {
            Release();
        }

        rmbWasHeld = held;
    }

    /// <summary>
    /// Parent bow under the live view camera (what the player actually sees).
    /// Arrow is a child of the bow so both follow look + locomotion with the hierarchy.
    /// </summary>
    private bool IsSceneInstance =>
        gameObject.scene.IsValid() && gameObject.scene.isLoaded;

    private void ApplyDesktopPose()
    {
        if (!IsSceneInstance)
        {
            return;
        }

        Transform anchor = ResolveDesktopAnchor();
        if (anchor == null)
        {
            if (logInputDetection && Time.frameCount % 120 == 0)
            {
                Debug.LogWarning("BowController: desktop view anchor not found yet.", this);
            }

            return;
        }

        if (!anchor.gameObject.scene.IsValid())
        {
            if (logInputDetection && Time.frameCount % 120 == 0)
            {
                Debug.LogWarning("BowController: view anchor is a Prefab asset, not a scene camera.", this);
            }

            return;
        }

        if (transform.parent != anchor)
        {
            transform.SetParent(anchor, false);
            if (logInputDetection)
            {
                Debug.Log("BowController: desktop bow parented to '" + GetPath(anchor) + "'.", this);
            }
        }

        transform.localPosition = holdOffset;
        transform.localRotation = Quaternion.Euler(holdEuler);

        if (!loggedDesktopHead && logInputDetection)
        {
            loggedDesktopHead = true;
            Debug.Log("BowController: desktop following '" + GetPath(anchor) + "'.", this);
        }

        if (arrow != null)
        {
            PlaceDesktopArrow(anchor);
        }
    }

    private Transform ResolveDesktopAnchor()
    {
        Camera cam = PlayEnvironment.ResolveViewCamera();
        if (cam != null)
        {
            return cam.transform;
        }

        return PlayEnvironment.ResolveDesktopBowParent();
    }

    private void PlaceDesktopArrow(Transform anchor)
    {
        if (arrow == null || !IsSceneInstance)
        {
            return;
        }

        if (!arrow.gameObject.scene.IsValid())
        {
            Debug.LogError("BowController: held arrow is a Prefab asset — equip must Instantiate.", this);
            arrow = null;
            return;
        }

        arrow.PrepareHeld();

        if (arrow.transform.parent != transform)
        {
            arrow.transform.SetParent(transform, false);
        }

        Vector3 localPos = desktopArrowLocalPosition;
        if (state == State.Drawing)
        {
            localPos += Vector3.back * (pullDistance * Mathf.Clamp01(draw));
        }

        arrow.transform.localPosition = localPos;

        Vector3 look = anchor.forward.sqrMagnitude > 1e-6f ? anchor.forward.normalized : transform.forward;
        arrow.transform.rotation = arrow.RotationForDirection(look);

        if (arrow.Tip == null || arrow.Rear == null)
        {
            arrow.transform.localRotation = Quaternion.Euler(desktopArrowLocalEuler);
        }
    }

    private Vector3 DesktopShotDirection()
    {
        if (arrow != null)
        {
            Vector3 tip = arrow.TipWorldDirection;
            if (tip.sqrMagnitude > 1e-6f)
            {
                return tip.normalized;
            }
        }

        Transform anchor = ResolveDesktopAnchor();
        if (anchor != null && anchor.forward.sqrMagnitude > 1e-6f)
        {
            return anchor.forward.normalized;
        }

        return transform.forward;
    }

    // -------------------------------------------------------------------------
    // Wand ray (official Votanic API — SetActive alone is overwritten every frame)
    // -------------------------------------------------------------------------

    private void UpdateWandRayVisibility()
    {
        if (!hideWandRayWhileHoldingArrow)
        {
            return;
        }

        bool wantHidden = HasArrowInHand;
        if (wantHidden)
        {
            SetWandRayVisible(false);
            wandRayForcedHidden = true;
        }
        else if (wandRayForcedHidden)
        {
            SetWandRayVisible(true);
            wandRayForcedHidden = false;
        }
    }

    private void SetWandRayVisible(bool visible)
    {
        // 1) Official API — Votanic reapplies wand display every frame, so we must call this
        //    from LateUpdate (order 1000) every frame while the arrow is held.
        try
        {
            if (vCast.controller != null)
            {
                vCast.controller.DisplayWandRay(visible);
                vCast.controller.EnableWandRay(visible);
            }
        }
        catch (System.Exception exception)
        {
            if (logInputDetection && Time.frameCount % 180 == 0)
            {
                Debug.LogWarning("BowController: DisplayWandRay failed: " + exception.Message, this);
            }
        }

        // 2) Hierarchy backup: Head/Hand/Controller/Wand/{Beam,Point,Ring}
        Transform wand = ResolveWandTransform();
        if (wand == null)
        {
            return;
        }

        for (int i = 0; i < wand.childCount; i++)
        {
            Transform child = wand.GetChild(i);
            if (!IsWandVisualName(child.name))
            {
                continue;
            }

            if (child.gameObject.activeSelf != visible)
            {
                child.gameObject.SetActive(visible);
            }
        }
    }

    private static bool IsWandVisualName(string name)
    {
        return string.Equals(name, "Beam", System.StringComparison.OrdinalIgnoreCase)
               || string.Equals(name, "Point", System.StringComparison.OrdinalIgnoreCase)
               || string.Equals(name, "Ring", System.StringComparison.OrdinalIgnoreCase)
               || string.Equals(name, "Cursor", System.StringComparison.OrdinalIgnoreCase);
    }

    private Transform ResolveWandTransform()
    {
        Transform head = PlayEnvironment.ResolveDesktopBowParent();
        if (head != null)
        {
            Transform byPath = FindChildPathIgnoreCase(head, "Hand", "Controller", "Wand");
            if (byPath != null)
            {
                return byPath;
            }
        }

        Transform vGear = PlayEnvironment.ResolveVGearTransform();
        if (vGear != null)
        {
            Transform byPath = FindChildPathIgnoreCase(vGear, "Frame", "User", "Head", "Hand", "Controller", "Wand");
            if (byPath != null)
            {
                return byPath;
            }
        }

        return FindByName("Wand");
    }

    // -------------------------------------------------------------------------
    // Draw / release
    // -------------------------------------------------------------------------

    private void BeginDraw()
    {
        if (arrow == null)
        {
            if (ShouldAutoSpawnHeldArrow)
            {
                SpawnArrow();
            }
        }

        if (arrow == null)
        {
            if (logInputDetection)
            {
                Debug.Log("Bow: RMB draw ignored — no arrow in hand (pick from quiver).", this);
            }

            return;
        }

        state = State.Drawing;
        draw = 0f;

        if (desktop)
        {
            ApplyDesktopPose();
        }
        else
        {
            arrow.transform.SetParent(null, true);
        }
    }

    private void Release()
    {
        if (state != State.Drawing)
        {
            return;
        }

        float power = draw;
        Vector3 dir = desktop ? DesktopShotDirection() : ShotDirectionTracked();
        state = State.Idle;
        draw = 0f;
        rmbWasHeld = Input.GetMouseButton(1) || Input.GetKey(KeyCode.Mouse1)
                     || (desktopSpaceToShoot && Input.GetKey(KeyCode.Space));

        if (arrow == null)
        {
            return;
        }

        // Desktop: always fire once drawn (even a tap). CAVE keeps the flop-cancel threshold.
        if (!desktop && power < minDrawToShoot)
        {
            if (logInputDetection)
            {
                Debug.Log($"Bow: release ignored (draw={power:0.00}).", this);
            }

            ResetBowString();
            RestoreHeldArrowPose();
            return;
        }

        // Tiny desktop tap still gets a minimum launch so it is visibly "a shot".
        if (desktop)
        {
            power = Mathf.Max(power, 0.15f);
        }

        float speed = SpeedFromDraw(power);
        ArrowProjectile shot = arrow;
        arrow = null;

        // Clear wand hide before flight so restore path is clean after shot.
        if (wandRayForcedHidden)
        {
            SetWandRayVisible(true);
            wandRayForcedHidden = false;
        }

        if (logInputDetection)
        {
            Debug.Log($"Bow: shot draw={power:0.00} speed={speed:0.0} dir={dir}.", this);
        }

        Vector3 shotPos = shot.transform.position;
        shot.Fire(dir, speed, damagePerShot, bowColliders);
        FightAudio.PlayArrowShot(shotPos);
        ResetBowString();

        if (ShouldAutoSpawnHeldArrow)
        {
            SpawnArrow();
        }

        RestoreHeldArrowPose();
    }

    private void CancelDraw()
    {
        state = State.Idle;
        draw = 0f;
        ResetBowString();
        if (arrow == null)
        {
            return;
        }

        RestoreHeldArrowPose();
    }

    private Vector3 ShotDirectionTracked()
    {
        return ResolveTrackedAimDirection();
    }

    /// <summary>
    /// CAVE aim from bow model local axis (default −Y), plus optional pitch trim.
    /// </summary>
    private Vector3 BowAimDirection()
    {
        Vector3 local = trackedAimLocalAxis.sqrMagnitude > 1e-8f
            ? trackedAimLocalAxis.normalized
            : Vector3.down;

        Vector3 world = transform.TransformDirection(local);
        if (world.sqrMagnitude < 1e-6f)
        {
            return Vector3.forward;
        }

        world.Normalize();

        if (Mathf.Abs(trackedAimPitchOffsetDegrees) > 0.01f)
        {
            Vector3 yawAxis = Vector3.Cross(world, Vector3.up);
            if (yawAxis.sqrMagnitude < 1e-6f)
            {
                yawAxis = transform.right;
            }

            Vector3 pitchAxis = Vector3.Cross(yawAxis.normalized, world);
            if (pitchAxis.sqrMagnitude > 1e-6f)
            {
                world = Quaternion.AngleAxis(trackedAimPitchOffsetDegrees, pitchAxis.normalized) * world;
            }
        }

        return world.normalized;
    }

    /// <summary>
    /// String-hand shaft aim: tip toward bow rest from the drawing hand.
    /// </summary>
    private bool TryGetShaftAimDirection(out Vector3 aim)
    {
        aim = Vector3.forward;
        if (rightHand == null)
        {
            return false;
        }

        Vector3 d = GetArrowRestPosition() - rightHand.position;
        if (d.sqrMagnitude < 1e-6f)
        {
            return false;
        }

        aim = d.normalized;
        return true;
    }

    /// <summary>
    /// Final CAVE aim based on <see cref="aimMode"/>.
    /// </summary>
    private Vector3 ResolveTrackedAimDirection()
    {
        Vector3 bowAim = BowAimDirection();

        if (aimMode == BowAimMode.BowOnly)
        {
            return bowAim;
        }

        if (!TryGetShaftAimDirection(out Vector3 shaftAim))
        {
            return bowAim;
        }

        if (aimMode == BowAimMode.FreeShaft)
        {
            return shaftAim;
        }

        // SoftCoupled: mostly bow, string offset steers a little.
        return Vector3.Slerp(bowAim, shaftAim, softCoupleBlend).normalized;
    }

    private float SpeedFromDraw(float draw01)
    {
        float t = Mathf.Clamp01(draw01);
        if (drawSpeedExponent > 1.001f)
        {
            t = Mathf.Pow(t, drawSpeedExponent);
        }

        return Mathf.Lerp(minSpeed, maxSpeed, t);
    }

    private Vector3 GetArrowRestPosition()
    {
        if (arrowRest != null)
        {
            return arrowRest.position;
        }

        return transform.position;
    }

    // -------------------------------------------------------------------------
    // Tracked XR
    // -------------------------------------------------------------------------

    private void TrackedInput()
    {
        FindRightHand();

        bool holding = IsAnyAxisHeld(out string source);
        float handDist = GetHandsDistance();

        if (holding != wasInputHeld)
        {
            wasInputHeld = holding;
            if (logInputDetection)
            {
                Debug.Log(holding ? $"Bow CAVE: axis held ({source})" : "Bow CAVE: axis released.", this);
            }
        }

        if (state == State.Idle)
        {
            bool handsClose = rightHand != null && handDist <= nockStartDistance;
            if (holding && logInputDetection && Time.unscaledTime >= nextNockDebugTime)
            {
                nextNockDebugTime = Time.unscaledTime + 0.5f;
                Debug.Log(
                    $"Bow CAVE: waiting to nock — handsClose={handsClose} "
                    + $"dist={handDist:0.00} need<={nockStartDistance:0.00} "
                    + $"rightHand={(rightHand != null ? rightHand.name : "null")}",
                    this);
            }

            if (holding && handsClose)
            {
                BeginDraw();
            }
        }
        else if (state == State.Drawing)
        {
            if (!holding)
            {
                Release();
                return;
            }

            float fullPull = maxDrawDistance;
            if (arrow != null)
            {
                fullPull = Mathf.Max(maxDrawDistance, arrow.ShaftLength);
            }

            float span = Mathf.Max(0.01f, fullPull - nockStartDistance);
            draw = Mathf.Clamp01((handDist - nockStartDistance) / span);
        }
    }

    private float GetHandsDistance()
    {
        if (rightHand == null)
        {
            return float.MaxValue;
        }

        float best = float.MaxValue;
        best = MinDist(best, rightHand.position, transform.position);
        best = MinDist(best, rightHand.position, GetArrowRestPosition());

        if (leftHandChild != null && leftHandChild.BoundHand != null)
        {
            best = MinDist(best, rightHand.position, leftHandChild.BoundHand.position);
        }

        return best;
    }

    private static float MinDist(float current, Vector3 a, Vector3 b)
    {
        return Mathf.Min(current, Vector3.Distance(a, b));
    }

    private void TrackedDrawVisual()
    {
        if (arrow == null || rightHand == null)
        {
            return;
        }

        Vector3 aim = ResolveTrackedAimDirection();
        Vector3 restPos = GetArrowRestPosition();

        float fullPull = maxDrawDistance;
        if (arrow != null)
        {
            fullPull = Mathf.Max(maxDrawDistance, arrow.ShaftLength);
        }

        float pull = fullPull * Mathf.Clamp01(draw);
        Vector3 rearPos;

        if (aimMode == BowAimMode.FreeShaft)
        {
            // Nock follows the string hand; tip points along rest ← hand.
            rearPos = rightHand.position;
            if (TryGetShaftAimDirection(out Vector3 shaftAim))
            {
                aim = shaftAim;
            }
        }
        else
        {
            // BowOnly / SoftCoupled: keep shaft on the resolved aim axis; power from hand distance.
            rearPos = restPos - aim * pull;
        }

        arrow.PlaceRearAt(rearPos, aim);
        UpdateBowString(arrow.Rear != null ? arrow.Rear.position : rearPos);
    }

    private void UpdateBowString(Vector3 worldNockPosition)
    {
        if (bowString == null)
        {
            return;
        }

        bowString.position = worldNockPosition;
    }

    private void ResetBowString()
    {
        if (bowString == null || !hasBowStringRest)
        {
            return;
        }

        bowString.localPosition = bowStringRestLocalPos;
    }

    private void UpdateIdleArrowVisual()
    {
        if (desktop || arrow == null || !idleArrowOnRightHand || rightHand == null)
        {
            return;
        }

        Vector3 holdPos = rightHand.TransformPoint(rightHandArrowLocalPosition);
        arrow.PlaceCenterAt(holdPos, rightHand.forward);
    }

    private void RestoreHeldArrowPose()
    {
        if (arrow == null)
        {
            return;
        }

        if (desktop)
        {
            ApplyDesktopPose();
        }
        else
        {
            UpdateIdleArrowVisual();
        }
    }

    /// <summary>
    /// Equip an arrow into the drawing hand (from a quiver). Returns false if already holding one.
    /// </summary>
    public bool EquipArrow(ArrowProjectile newArrow)
    {
        if (newArrow == null || arrow != null)
        {
            return false;
        }

        if (!IsSceneInstance)
        {
            Debug.LogError(
                "BowController.EquipArrow called on a Prefab asset. Assign the scene Recurve_Bow / BowController on ArrowQuiver.",
                this);
            return false;
        }

        if (!newArrow.gameObject.scene.IsValid())
        {
            Debug.LogError("BowController.EquipArrow: arrow is not a scene instance.", this);
            return false;
        }

        bool isDesktop = PlayEnvironment.IsDesktopInput;
        desktop = isDesktop;
        arrow = newArrow;

        if (isDesktop)
        {
            ApplyDesktopPose();
            if (logInputDetection)
            {
                Transform anchor = ResolveDesktopAnchor();
                Debug.Log(
                    "Bow: desktop equipped arrow. anchor="
                    + (anchor != null ? GetPath(anchor) : "null"),
                    this);
            }
        }
        else
        {
            FindRightHand();
            if (idleArrowOnRightHand && rightHand != null)
            {
                UpdateIdleArrowVisual();
            }
            else if (arrowRest != null)
            {
                arrow.Nock(arrowRest);
            }

            if (logInputDetection)
            {
                Debug.Log("Bow: tracked equipped arrow.", this);
            }
        }

        UpdateWandRayVisibility();
        return true;
    }

    private void SpawnArrow()
    {
        if (UsesQuiver || arrow != null || arrowPrefab == null)
        {
            return;
        }

        arrow = Instantiate(arrowPrefab);
        if (desktop)
        {
            ApplyDesktopPose();
        }
        else
        {
            Transform parent = arrowRest != null ? arrowRest : transform;
            arrow.Nock(parent);
        }
    }

    private void FindRightHand()
    {
        if (rightHand != null)
        {
            return;
        }

        if (Time.unscaledTime < nextHandSearch)
        {
            return;
        }

        nextHandSearch = Time.unscaledTime + 0.25f;
        rightHand = ResolveRightHandTransform();
        if (rightHand != null && logInputDetection)
        {
            Debug.Log("BowController: right hand bound to '" + GetPath(rightHand) + "'.", this);
        }
    }

    private Transform ResolveRightHandTransform()
    {
        // Prefer explicit right-hand entity. Do NOT fall back to generic "Hand"
        // (that is often the wand host and breaks quiver / nock distance checks).
        Transform hand1 = FindByName(rightHandName)
                          ?? FindByName("Hand1")
                          ?? FindByName("hand1");
        if (hand1 != null)
        {
            return hand1;
        }

        Transform vGear = PlayEnvironment.ResolveVGearTransform();
        if (vGear != null)
        {
            Transform byPath = FindChildPathIgnoreCase(vGear, "Frame", "User", "Hand1");
            if (byPath != null)
            {
                return byPath;
            }
        }

        try
        {
            if (vCast.hand != null)
            {
                return vCast.hand.transform;
            }
        }
        catch
        {
        }

        return null;
    }

    private bool IsAnyAxisHeld(out string source)
    {
        source = null;
        int controllers = Mathf.Max(1, maxControllersToScan);

        for (int c = 0; c < controllers; c++)
        {
            int axisCount = maxAxesToScan;
            try
            {
                int reported = vCast.Ctrl.NumberOfAxis(c);
                if (reported > 0)
                {
                    axisCount = Mathf.Max(reported, maxAxesToScan);
                }
            }
            catch
            {
            }

            for (int a = 0; a < axisCount; a++)
            {
                float value = 0f;
                try
                {
                    value = vCast.Ctrl.AxisValue(a, c);
                }
                catch
                {
                    continue;
                }

                if (Mathf.Abs(value) > axisDeadzone)
                {
                    source = $"ctrl={c} axis={a} value={value:0.000}";
                    return true;
                }
            }
        }

        return false;
    }

    private static Transform FindByName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

#if UNITY_2023_1_OR_NEWER
        Transform[] all = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
        Transform[] all = Resources.FindObjectsOfTypeAll<Transform>();
#endif
        for (int i = 0; i < all.Length; i++)
        {
            Transform t = all[i];
            if (t != null && t.name == name && t.gameObject.scene.IsValid() && t.gameObject.scene.isLoaded)
            {
                return t;
            }
        }

        return null;
    }

    private static Transform FindChildPathIgnoreCase(Transform root, params string[] path)
    {
        Transform current = root;
        for (int i = 0; i < path.Length; i++)
        {
            if (current == null)
            {
                return null;
            }

            current = FindDirectChildIgnoreCase(current, path[i]);
        }

        return current;
    }

    private static Transform FindDirectChildIgnoreCase(Transform parent, string childName)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (string.Equals(child.name, childName, System.StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }
        }

        return null;
    }

    private static Transform FindChildRecursiveIgnoreCase(Transform root, string childName)
    {
        if (root == null)
        {
            return null;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (string.Equals(child.name, childName, System.StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }

            Transform nested = FindChildRecursiveIgnoreCase(child, childName);
            if (nested != null)
            {
                return nested;
            }
        }

        return null;
    }

    private static string GetPath(Transform t)
    {
        string path = t.name;
        Transform p = t.parent;
        while (p != null)
        {
            path = p.name + "/" + path;
            p = p.parent;
        }

        return path;
    }
}
