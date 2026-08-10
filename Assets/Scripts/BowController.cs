using UnityEngine;
using Votanic.vXR.vCast;

public enum ArrowSupplyMode
{
    Infinite,
    BackQuiver
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
    [Tooltip("Infinite = arrow auto-spawns on hand after each shot. Back Quiver = reach behind your back for each arrow.")]
    [SerializeField] private ArrowSupplyMode supplyMode = ArrowSupplyMode.BackQuiver;
    [Tooltip("Infinite mode only: keep a held arrow available automatically after each shot.")]
    [SerializeField] private bool autoSpawnHeldArrow = true;

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
    [Tooltip("Primary right-hand name fallback if vGear Head/Hand/Controller is missing.")]
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
    [Tooltip("Keep the Votanic wand laser off at all times in tracked XR.")]
    [SerializeField] private bool suppressWandRay = true;

    [Header("Back Quiver")]
    [Tooltip("Used when Arrow Supply Mode is Back Quiver.")]
    [SerializeField] private float backQuiverReachDistance = 0.68f;
    [Tooltip("Hand must be at least this far behind the head (head-local −Z, meters).")]
    [SerializeField] private float backQuiverMinBehind = 0.14f;
    [Tooltip("Head-local Y upper limit: hand must be at or below −this value. Negative allows slightly above the head "
             + "(arrows sticking out of a back quiver).")]
    [SerializeField] private float backQuiverMinBelow = -0.25f;
    [Tooltip("Head-local Y lower limit: hand must stay above −this value (waist floor — blocks behind-the-butt reaches).")]
    [SerializeField] private float backQuiverMaxBelow = 0.5f;
    [Tooltip("Head→hand must point clearly behind the look direction (0–1). Higher = harder to trigger by accident.")]
    [SerializeField, Range(0.2f, 0.85f)] private float backQuiverBehindDot = 0.42f;
    [Tooltip("Max sideways offset from head center (head-local |X|, meters).")]
    [SerializeField] private float backQuiverMaxLateral = 0.42f;
    [Tooltip("Right hand must be at least this far from the bow while reaching (avoids nock-zone false grabs).")]
    [SerializeField] private float backQuiverMinBowClearance = 0.38f;
    [Tooltip("Hold the reach pose briefly before an arrow appears.")]
    [SerializeField] private float backQuiverDwellSeconds = 0.14f;
    [Tooltip("Desktop fallback while testing back quiver (simulates a reach pickup).")]
    [SerializeField] private KeyCode desktopBackQuiverKey = KeyCode.B;

    [Header("Scope Trajectory")]
    [Tooltip("If true, the green dotted aim arc only shows after picking up a ScopePickup.")]
    [SerializeField] private bool trajectoryRequiresScope = true;
    [SerializeField] private bool scopeEquipped = false;
    [SerializeField] private Color trajectoryColor = new Color(0.2f, 1f, 0.35f, 0.95f);
    [Tooltip("Trajectory color when the arc would hit a crystal, fireball, or unshielded dragon.")]
    [SerializeField] private Color trajectoryTargetColor = new Color(1f, 0.2f, 0.15f, 0.95f);
    [SerializeField, Min(4)] private int trajectoryPoints = 48;
    [SerializeField, Min(0.01f)] private float trajectoryTimeStep = 0.05f;
    [SerializeField, Min(0.5f)] private float trajectoryMaxSeconds = 3.5f;
    [SerializeField] private float trajectoryWidth = 0.025f;
    [SerializeField] private float trajectoryDashWorldSize = 0.18f;
    [SerializeField] private LayerMask trajectoryHitMask = ~0;
    [Tooltip("Desktop: toggle scope without a pickup (for testing).")]
    [SerializeField] private KeyCode desktopToggleScopeKey = KeyCode.T;

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
    private bool loggedDesktopHead;
    private bool rmbWasHeld;
    private float backQuiverDwell;
    private bool quiverMountedOnBack;
    private bool bowGrounded;
    private int backQuiverArrowPickups;
    private LineRenderer trajectoryLine;
    private Material trajectoryMaterial;
    private readonly Vector3[] trajectoryBuffer = new Vector3[128];
    private readonly RaycastHit[] trajectoryHits = new RaycastHit[24];
    private DragonBoss cachedTrajectoryDragon;

    public bool HasScopeEquipped => scopeEquipped;
    public bool IsDrawing => state == State.Drawing;

    public void SetScopeEquipped(bool equipped)
    {
        scopeEquipped = equipped;
        if (!equipped)
        {
            HideTrajectory();
        }

        if (logInputDetection)
        {
            Debug.Log(equipped ? "BowController: scope equipped — trajectory preview on."
                               : "BowController: scope removed.", this);
        }
    }

    [ContextMenu("Create Scope Pickup (Trajectory Preview)")]
    public void CreateScopePickup()
    {
        ScopePickup existing = FindObjectOfType<ScopePickup>();
        if (existing != null)
        {
#if UNITY_EDITOR
            UnityEditor.Selection.activeGameObject = existing.gameObject;
#endif
            Debug.Log("ScopePickup already exists — selected it.", existing);
            return;
        }

        GameObject root = new GameObject("ScopePickup");
        root.transform.position = transform.position + transform.right * -0.85f + Vector3.up * 1.1f;

        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        visual.name = "ScopeGroundVisual";
        visual.transform.SetParent(root.transform, false);
        visual.transform.localScale = new Vector3(0.12f, 0.22f, 0.12f);
        Collider col = visual.GetComponent<Collider>();
        if (col != null)
        {
            if (Application.isPlaying)
            {
                Destroy(col);
            }
            else
            {
                DestroyImmediate(col);
            }
        }

        Renderer renderer = visual.GetComponent<Renderer>();
        if (renderer != null)
        {
            Material mat = new Material(Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default"));
            mat.color = new Color(0.15f, 0.9f, 0.3f, 1f);
            renderer.sharedMaterial = mat;
        }

        ScopePickup pickup = root.AddComponent<ScopePickup>();
        pickup.Assign(this, visual);

#if UNITY_EDITOR
        UnityEditor.Selection.activeGameObject = root;
        UnityEditor.EditorUtility.SetDirty(root);
#endif
        Debug.Log("Created ScopePickup. Pick it up (desktop: G) to enable green trajectory while aiming.", root);
    }

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

    public bool IsInfiniteMode => supplyMode == ArrowSupplyMode.Infinite;

    public bool IsAlwaysReadyMode => IsInfiniteMode;

    public bool IsBackQuiverMode => supplyMode == ArrowSupplyMode.BackQuiver;

    public bool IsQuiverMountedOnBack => quiverMountedOnBack;

    public bool IsRightHandAtBackQuiverZone()
    {
        FindRightHand();
        return IsRightHandAtBackQuiver();
    }

    public void SetQuiverMountedOnBack(bool mounted)
    {
        if (quiverMountedOnBack == mounted)
        {
            return;
        }

        quiverMountedOnBack = mounted;
        if (mounted)
        {
            SetSupplyMode(ArrowSupplyMode.BackQuiver);
            backQuiverArrowPickups = 0;
        }
        else
        {
            ClearHeldArrow();
            backQuiverArrowPickups = 0;
        }

        UpdateWandRayVisibility();
    }

    public void SetBowGrounded(bool grounded)
    {
        bool wasGrounded = bowGrounded;
        bowGrounded = grounded;
        if (leftHandChild != null)
        {
            leftHandChild.enabled = !grounded && !PlayEnvironment.IsDesktopInput;
        }

        // Equip tutorial / any path that enables the playable bow should reveal the ground scope.
        if (wasGrounded && !grounded)
        {
            ScopePickup.NotifyBowEquipped();
        }
        else if (!wasGrounded && grounded)
        {
            ScopePickup.NotifyBowUnequipped();
        }
    }

    /// <summary>
    /// Toggle or set infinite auto-arrows vs back-quiver reach pickup.
    /// </summary>
    public void SetSupplyMode(ArrowSupplyMode mode)
    {
        if (supplyMode == mode)
        {
            return;
        }

        supplyMode = mode;
        if (IsBackQuiverMode)
        {
            ClearHeldArrow();
        }

        ApplySupplyMode();
    }

    public ArrowSupplyMode ToggleSupplyMode()
    {
        SetSupplyMode(
            supplyMode == ArrowSupplyMode.Infinite
                ? ArrowSupplyMode.BackQuiver
                : ArrowSupplyMode.Infinite);
        return supplyMode;
    }

    public static string SupplyModeLabel(ArrowSupplyMode mode)
    {
        switch (mode)
        {
            case ArrowSupplyMode.Infinite:
                return "Infinite";
            case ArrowSupplyMode.BackQuiver:
                return "Back Quiver";
            default:
                return mode.ToString();
        }
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
                "BowController: arrow supply = " + SupplyModeLabel(supplyMode) + ".",
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

    private bool ShouldAutoSpawnHeldArrow =>
        supplyMode == ArrowSupplyMode.Infinite && autoSpawnHeldArrow;

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

        bowColliders = GetComponentsInChildren<Collider>(true);
        CacheBowStringRest();
        SetScopeEquipped(false);

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
        HideTrajectory();
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

        UpdateBackQuiverPickup();

        if (desktop && Input.GetKeyDown(desktopToggleScopeKey))
        {
            SetScopeEquipped(!scopeEquipped);
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

            UpdateTrajectoryPreview();
            return;
        }

        // Desktop: drive pose in LateUpdate AFTER Votanic applies VirtualTracker0 to Head.
        ApplyDesktopPose();
        UpdateWandRayVisibility();
        UpdateTrajectoryPreview();
    }

    private void RefreshMode()
    {
        CancelDraw();
        desktop = PlayEnvironment.IsDesktopInput;
        loggedDesktopHead = false;

        if (leftHandChild != null)
        {
            leftHandChild.enabled = !desktop && !bowGrounded;
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
                IsBackQuiverMode
                    ? "BowController: CAVE mode — reach behind your back for arrows, then nock/pull/release."
                    : "BowController: CAVE mode — infinite arrows on hand; nock/pull/release.",
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
        if (!suppressWandRay)
        {
            return;
        }

        PlayEnvironment.SuppressWandRay();
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
                Debug.Log("Bow: RMB draw ignored — no arrow in hand (reach behind your back).", this);
            }

            return;
        }

        state = State.Drawing;
        draw = 0f;
        FightAudio.PlayBowDraw(transform.position);

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
        HideTrajectory();
        FightAudio.StopBowDraw();
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
        HideTrajectory();
        FightAudio.StopBowDraw();
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

    private bool ShouldShowTrajectory
    {
        get
        {
            if (state != State.Drawing || arrow == null)
            {
                return false;
            }

            if (trajectoryRequiresScope && !scopeEquipped)
            {
                return false;
            }

            float power = draw;
            if (desktop)
            {
                power = Mathf.Max(power, 0.15f);
            }
            else if (power < minDrawToShoot)
            {
                return false;
            }

            return true;
        }
    }

    private void UpdateTrajectoryPreview()
    {
        if (!ShouldShowTrajectory)
        {
            HideTrajectory();
            return;
        }

        EnsureTrajectoryLine();

        float power = draw;
        if (desktop)
        {
            power = Mathf.Max(power, 0.15f);
        }

        Vector3 origin = GetTrajectoryOrigin();
        Vector3 direction = desktop ? DesktopShotDirection() : ShotDirectionTracked();
        float speed = SpeedFromDraw(power);
        float gravityMul = 1.35f;
        float drag = 0.08f;
        if (arrow != null)
        {
            gravityMul = arrow.GravityMultiplier;
            drag = arrow.AirDrag;
        }
        else if (arrowPrefab != null)
        {
            gravityMul = arrowPrefab.GravityMultiplier;
            drag = arrowPrefab.AirDrag;
        }

        int count = SimulateTrajectory(
            origin,
            direction * speed,
            gravityMul,
            drag,
            trajectoryBuffer,
            out bool hitsValidTarget);

        if (count < 2)
        {
            HideTrajectory();
            return;
        }

        trajectoryLine.enabled = true;
        trajectoryLine.positionCount = count;
        for (int i = 0; i < count; i++)
        {
            trajectoryLine.SetPosition(i, trajectoryBuffer[i]);
        }

        ApplyTrajectoryColor(hitsValidTarget ? trajectoryTargetColor : trajectoryColor);

        float pathLength = 0f;
        for (int i = 1; i < count; i++)
        {
            pathLength += Vector3.Distance(trajectoryBuffer[i - 1], trajectoryBuffer[i]);
        }

        float tiles = Mathf.Max(1f, pathLength / Mathf.Max(0.01f, trajectoryDashWorldSize));
        if (trajectoryMaterial != null && trajectoryMaterial.HasProperty("_MainTex"))
        {
            trajectoryMaterial.mainTextureScale = new Vector2(tiles, 1f);
        }
    }

    private void ApplyTrajectoryColor(Color color)
    {
        if (trajectoryLine != null)
        {
            trajectoryLine.startColor = color;
            trajectoryLine.endColor = color;
        }

        if (trajectoryMaterial != null)
        {
            trajectoryMaterial.color = color;
            if (trajectoryMaterial.HasProperty("_Color"))
            {
                trajectoryMaterial.SetColor("_Color", color);
            }
        }
    }

    private Vector3 GetTrajectoryOrigin()
    {
        if (arrow != null)
        {
            if (arrow.Tip != null)
            {
                return arrow.Tip.position;
            }

            return arrow.transform.position;
        }

        return GetArrowRestPosition();
    }

    private int SimulateTrajectory(
        Vector3 origin,
        Vector3 velocity,
        float gravityMul,
        float drag,
        Vector3[] buffer,
        out bool hitsValidTarget)
    {
        hitsValidTarget = false;
        int maxPoints = Mathf.Min(buffer.Length, Mathf.Max(4, trajectoryPoints));
        float dt = Mathf.Max(0.01f, trajectoryTimeStep);
        float maxTime = Mathf.Max(dt, trajectoryMaxSeconds);

        Vector3 dir0 = velocity.sqrMagnitude > 1e-6f ? velocity.normalized : transform.forward;
        Vector3 pos = origin + dir0 * 0.08f;
        Vector3 vel = velocity;
        buffer[0] = pos;
        int count = 1;

        for (float t = dt; t <= maxTime && count < maxPoints; t += dt)
        {
            Vector3 nextVel = vel + Physics.gravity * gravityMul * dt;
            nextVel *= Mathf.Clamp01(1f - drag * dt);
            Vector3 nextPos = pos + nextVel * dt;

            // Color probe only — never clip the arc short.
            if (!hitsValidTarget)
            {
                Vector3 delta = nextPos - pos;
                float dist = delta.magnitude;
                if (dist > 1e-5f
                    && TryGetTrajectoryTargetHit(pos, delta / dist, dist))
                {
                    hitsValidTarget = true;
                }
            }

            pos = nextPos;
            vel = nextVel;
            buffer[count++] = pos;
        }

        return count;
    }

    /// <summary>
    /// True if this segment crosses a crystal, fireball, or unshielded dragon
    /// (ignores bow / hands / world blockers).
    /// </summary>
    private bool TryGetTrajectoryTargetHit(Vector3 origin, Vector3 direction, float distance)
    {
        // Aim color uses a cheap AABB probe — full triangle tests are for arrow damage only.
        DragonBoss dragon = cachedTrajectoryDragon;
        if (dragon == null)
        {
            dragon = DragonBoss.Resolve();
            cachedTrajectoryDragon = dragon;
        }

        if (dragon != null
            && dragon.TrajectorySegmentHitsBody(origin, direction, distance))
        {
            return true;
        }

        int hitCount = Physics.RaycastNonAlloc(
            origin,
            direction,
            trajectoryHits,
            distance,
            trajectoryHitMask,
            QueryTriggerInteraction.Collide);

        for (int i = 0; i < hitCount; i++)
        {
            Collider col = trajectoryHits[i].collider;
            if (col == null || ShouldIgnoreTrajectoryHit(col))
            {
                continue;
            }

            if (IsTrajectoryValidTarget(col))
            {
                return true;
            }
        }

        return false;
    }

    private bool ShouldIgnoreTrajectoryHit(Collider col)
    {
        if (col == null)
        {
            return true;
        }

        Transform t = col.transform;

        if (t == transform || t.IsChildOf(transform))
        {
            return true;
        }

        if (arrow != null && (t == arrow.transform || t.IsChildOf(arrow.transform)))
        {
            return true;
        }

        if (bowColliders != null)
        {
            for (int i = 0; i < bowColliders.Length; i++)
            {
                if (bowColliders[i] == col)
                {
                    return true;
                }
            }
        }

        if (leftHandChild != null && leftHandChild.BoundHand != null)
        {
            Transform hand = leftHandChild.BoundHand;
            if (t == hand || t.IsChildOf(hand))
            {
                return true;
            }
        }

        if (rightHand != null && (t == rightHand || t.IsChildOf(rightHand)))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Red-arc targets: live crystal, fireball, or unshielded dragon.
    /// Dragon body is probed via <see cref="TryGetTrajectoryTargetHit"/> mesh raycast;
    /// this path covers PhysX hits (crystals / fireballs / legacy colliders).
    /// </summary>
    private static bool IsTrajectoryValidTarget(Collider col)
    {
        if (col == null)
        {
            return false;
        }

        EnderCrystal crystal = col.GetComponentInParent<EnderCrystal>();
        if (crystal != null && crystal.IsAlive)
        {
            return true;
        }

        if (col.GetComponentInParent<DragonFireball>() != null)
        {
            return true;
        }

        // Ignore PhysX dragon hulls — trajectory uses AABB; arrows use RaycastBody.
        return false;
    }

    private void EnsureTrajectoryLine()
    {
        if (trajectoryLine != null)
        {
            return;
        }

        GameObject go = new GameObject("TrajectoryPreview");
        go.transform.SetParent(transform, false);
        trajectoryLine = go.AddComponent<LineRenderer>();
        trajectoryLine.useWorldSpace = true;
        trajectoryLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        trajectoryLine.receiveShadows = false;
        trajectoryLine.numCapVertices = 2;
        trajectoryLine.numCornerVertices = 2;
        trajectoryLine.widthMultiplier = 1f;
        trajectoryLine.startWidth = trajectoryWidth;
        trajectoryLine.endWidth = trajectoryWidth * 0.65f;
        trajectoryLine.alignment = LineAlignment.View;
        trajectoryLine.textureMode = LineTextureMode.Tile;
        trajectoryLine.material = CreateTrajectoryMaterial();
        trajectoryMaterial = trajectoryLine.material;
        trajectoryLine.enabled = false;
    }

    private Material CreateTrajectoryMaterial()
    {
        Shader shader = Shader.Find("Sprites/Default")
                        ?? Shader.Find("Unlit/Transparent")
                        ?? Shader.Find("Unlit/Color");
        Material mat = new Material(shader);
        mat.name = "ArrowTrajectoryDotted";
        mat.color = trajectoryColor;

        Texture2D dash = new Texture2D(8, 1, TextureFormat.RGBA32, false);
        dash.wrapMode = TextureWrapMode.Repeat;
        dash.filterMode = FilterMode.Point;
        for (int x = 0; x < 8; x++)
        {
            bool on = x < 3;
            dash.SetPixel(x, 0, on ? Color.white : new Color(1f, 1f, 1f, 0f));
        }

        dash.Apply();
        mat.mainTexture = dash;
        if (mat.HasProperty("_Color"))
        {
            mat.SetColor("_Color", trajectoryColor);
        }

        return mat;
    }

    private void HideTrajectory()
    {
        if (trajectoryLine != null)
        {
            trajectoryLine.enabled = false;
            trajectoryLine.positionCount = 0;
        }
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
        if (IsBackQuiverMode || arrow != null || arrowPrefab == null)
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

    private void ClearHeldArrow()
    {
        if (arrow == null)
        {
            return;
        }

        CancelDraw();
        Destroy(arrow.gameObject);
        arrow = null;
        UpdateWandRayVisibility();
    }

    private void UpdateBackQuiverPickup()
    {
        if (!IsBackQuiverMode || !quiverMountedOnBack || arrow != null || arrowPrefab == null || state != State.Idle)
        {
            backQuiverDwell = 0f;
            return;
        }

        desktop = PlayEnvironment.IsDesktopInput;

        if (desktop)
        {
            if (Input.GetKeyDown(desktopBackQuiverKey))
            {
                TryPickupFromBackQuiver();
            }

            return;
        }

        FindRightHand();
        if (IsRightHandAtBackQuiver())
        {
            backQuiverDwell += Time.deltaTime;
            if (backQuiverDwell >= backQuiverDwellSeconds)
            {
                TryPickupFromBackQuiver();
                backQuiverDwell = 0f;
            }
        }
        else
        {
            backQuiverDwell = 0f;
        }
    }

    private bool IsRightHandAtBackQuiver()
    {
        if (rightHand == null)
        {
            return false;
        }

        Transform head = PlayEnvironment.ResolveHeadTransform();
        if (head == null)
        {
            return false;
        }

        Vector3 handPos = rightHand.position;
        Vector3 toHand = handPos - head.position;
        float dist = toHand.magnitude;
        if (dist < 0.05f || dist > backQuiverReachDistance)
        {
            return false;
        }

        Vector3 local = head.InverseTransformPoint(handPos);
        if (local.z > -backQuiverMinBehind)
        {
            return false;
        }

        if (local.y > -backQuiverMinBelow)
        {
            return false;
        }

        if (local.y < -backQuiverMaxBelow)
        {
            return false;
        }

        if (Mathf.Abs(local.x) > backQuiverMaxLateral)
        {
            return false;
        }

        Vector3 flatForward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        if (flatForward.sqrMagnitude < 1e-6f)
        {
            flatForward = head.forward;
        }
        else
        {
            flatForward.Normalize();
        }

        Vector3 flatToHand = Vector3.ProjectOnPlane(toHand, Vector3.up);
        if (flatToHand.sqrMagnitude < 1e-6f)
        {
            return false;
        }

        flatToHand.Normalize();
        if (Vector3.Dot(flatForward, flatToHand) > -backQuiverBehindDot)
        {
            return false;
        }

        float bowClear = Vector3.Distance(handPos, GetArrowRestPosition());
        if (bowClear < backQuiverMinBowClearance)
        {
            return false;
        }

        if (leftHandChild != null && leftHandChild.BoundHand != null)
        {
            float leftClear = Vector3.Distance(handPos, leftHandChild.BoundHand.position);
            if (leftClear < backQuiverMinBowClearance)
            {
                return false;
            }
        }

        return true;
    }

    private void TryPickupFromBackQuiver()
    {
        if (!IsBackQuiverMode || !quiverMountedOnBack || arrow != null || arrowPrefab == null)
        {
            return;
        }

        ArrowProjectile instance = Instantiate(arrowPrefab);
        if (!EquipArrow(instance))
        {
            Destroy(instance.gameObject);
            return;
        }

        FightAudio.PlayEquipArrowFromQuiver(instance.transform.position);
        backQuiverArrowPickups++;

        if (logInputDetection)
        {
            Debug.Log("BowController: picked up arrow from back quiver ("
                      + backQuiverArrowPickups + ").", this);
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
        Transform resolved = PlayEnvironment.ResolveRightHandTransform();
        if (resolved != null)
        {
            return resolved;
        }

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
