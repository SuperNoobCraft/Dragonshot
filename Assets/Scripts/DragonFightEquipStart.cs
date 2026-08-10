using UnityEngine;
using Votanic.vXR.vCast;

/// <summary>
/// Pre-fight tutorial: pick up ground bow, choose a difficulty quiver (Easy / Normal / Hard),
/// optionally grab the scope, strap the quiver on, then auto-start the fight.
/// Quivers/scope appear after the bow. After a quiver is chosen, other quivers hide —
/// click the fight panel to reset and choose again.
/// </summary>
public class DragonFightEquipStart : MonoBehaviour
{
    private enum EquipPhase
    {
        NeedBow,
        NeedQuiverPickup,
        NeedQuiverBack,
        Complete
    }

    [System.Serializable]
    public class DifficultyQuiver
    {
        public FightDifficulty difficulty = FightDifficulty.Normal;
        [Tooltip("Ground prop for this difficulty. Hidden until the bow is equipped.")]
        public GameObject groundVisual;
        [Tooltip("Optional world pose for this quiver. Empty = use the visual's scene pose.")]
        public Transform groundAnchor;
        [Tooltip("TMP / mesh label under the quiver anchor. Shown with the quivers; does not spin.")]
        public GameObject label;
    }

    [Header("References")]
    [SerializeField] private DragonBoss dragon;
    [Tooltip("Playable bow (BowController) — usually under Frame. Disabled until picked up.")]
    [SerializeField] private BowController bow;
    [SerializeField] private LeftHandChild leftHandChild;
    [Tooltip("Ground prop only — hidden once the real bow is equipped.")]
    [SerializeField] private GameObject groundBowVisual;
    [Tooltip("Legacy single ground quiver (migrated to Normal if Difficulty Quivers is empty).")]
    [SerializeField] private GameObject groundQuiverVisual;
    [Tooltip("One entry per difficulty. Other quivers hide after one is picked (reset via fight panel).")]
    [SerializeField] private DifficultyQuiver[] difficultyQuivers;
    [Tooltip("Optional shared held mesh. Empty = the picked ground visual is parented to the hand.")]
    [SerializeField] private Transform heldQuiverVisual;
    [Tooltip("Legacy single reference: used as ground and/or held quiver if the fields above are empty.")]
    [SerializeField] private Transform groundQuiver;
    [SerializeField] private DragonFightUI fightUI;
    [Tooltip("World-space instruction signs hidden once the fight begins.")]
    [SerializeField] private GameObject[] instructionSigns;
    [Tooltip("Optional scope pickups unlocked after the bow is equipped.")]
    [SerializeField] private ScopePickup[] scopePickups;

    [Header("Ground Poses")]
    [Tooltip("Optional pickup position for the ground bow. Rotation always comes from Ground Bow Visual.")]
    [SerializeField] private Transform groundBowAnchor;
    [Tooltip("Legacy single quiver anchor (used when migrating one quiver).")]
    [SerializeField] private Transform groundQuiverAnchor;

    [Header("Pickup")]
    [SerializeField] private float pickupDistance = 0.45f;
    [SerializeField] private float mountDwellSeconds = 0.22f;
    [SerializeField] private string leftHandName = "Hand2";
    [Tooltip("Fallback name if vGear Head/Hand/Controller is missing.")]
    [SerializeField] private string rightHandName = "Hand1";
    [Tooltip("Desktop: also allow pickup when the view camera is near the prop.")]
    [SerializeField] private bool desktopProximityPickup = true;

    [Header("Quiver Attach")]
    [SerializeField] private Vector3 quiverHandLocalPosition = new Vector3(0.06f, -0.04f, 0.12f);
    [SerializeField] private Vector3 quiverHandLocalEuler = new Vector3(15f, 0f, -20f);
    [SerializeField] private Vector3 quiverBackLocalPosition = new Vector3(0.18f, -0.08f, -0.22f);
    [SerializeField] private Vector3 quiverBackLocalEuler = new Vector3(10f, -15f, 8f);
    [Tooltip("Desktop fallback when vGear right-hand controller is not in the scene.")]
    [SerializeField] private Vector3 desktopQuiverHandLocalPosition = new Vector3(0.22f, -0.18f, 0.38f);
    [SerializeField] private Vector3 desktopQuiverHandLocalEuler = new Vector3(20f, 0f, 0f);

    [Header("Desktop Test Keys")]
    [SerializeField] private KeyCode desktopEquipBowKey = KeyCode.Alpha1;
    [SerializeField] private KeyCode desktopPickupQuiverKey = KeyCode.Alpha2;
    [SerializeField] private KeyCode desktopMountQuiverKey = KeyCode.Alpha3;
    [SerializeField] private KeyCode desktopPickEasyKey = KeyCode.E;
    [SerializeField] private KeyCode desktopPickNormalKey = KeyCode.N;
    [SerializeField] private KeyCode desktopPickHardKey = KeyCode.H;

    [Header("Idle Float Spin")]
    [Tooltip("Slowly rotate pickup props while they are waiting to be picked up.")]
    [SerializeField] private bool idlePropSpin = true;
    [SerializeField] private float idleSpinDegreesPerSecond = 36f;
    [Tooltip("World-space spin axis. Default = world up (Y). Ignored if Use Anchor Up Axis is on.")]
    [SerializeField] private Vector3 idleSpinAxis = Vector3.up;
    [Tooltip("If on, spin around each prop's ground anchor local Y. If that looks like Z, leave this off.")]
    [SerializeField] private bool useAnchorUpAxis = false;

    private struct QuiverPoseCache
    {
        public Vector3 position;
        public Quaternion rotation;
        public Transform parent;
        public bool valid;
    }

    private struct LabelPoseCache
    {
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Transform parent;
        public bool valid;
    }

    private EquipPhase phase = EquipPhase.NeedBow;
    private EquipPhase lastInstructionPhase = (EquipPhase)(-1);
    private float dwell;
    private Vector3 bowGroundPosition;
    private Quaternion bowGroundRotation;
    private Transform bowGroundParent;
    private QuiverPoseCache[] quiverPoseCache = System.Array.Empty<QuiverPoseCache>();
    private LabelPoseCache[] labelPoseCache = System.Array.Empty<LabelPoseCache>();
    private float[] quiverSpinAngles = System.Array.Empty<float>();
    private float bowSpinAngle;
    private bool groundPosesCached;
    private bool startedFight;
    private bool quiverHeld;
    private int selectedQuiverIndex = -1;
    private FightDifficulty selectedDifficulty = FightDifficulty.Normal;

    public bool IsBowEquipped => phase != EquipPhase.NeedBow;
    public bool IsQuiverMounted => phase == EquipPhase.Complete;
    public FightDifficulty SelectedDifficulty => selectedDifficulty;

    /// <summary>
    /// True while the equip tutorial is running and the scope should wait for quiver choice.
    /// </summary>
    public bool DefersScopeUntilQuiverChosen =>
        isActiveAndEnabled && phase != EquipPhase.Complete;

    public void Assign(
        DragonBoss boss,
        BowController bowController,
        LeftHandChild handChild,
        GameObject groundBowProp,
        GameObject groundQuiverProp,
        Transform heldQuiver,
        Transform legacyQuiver,
        Transform bowAnchor,
        Transform quiverAnchor,
        DragonFightUI ui,
        GameObject[] signs)
    {
        dragon = boss;
        bow = bowController;
        leftHandChild = handChild;
        groundBowVisual = groundBowProp;
        groundQuiverVisual = groundQuiverProp;
        heldQuiverVisual = heldQuiver;
        groundQuiver = legacyQuiver;
        groundBowAnchor = bowAnchor;
        groundQuiverAnchor = quiverAnchor;
        fightUI = ui;
        instructionSigns = signs;
        EnsureDifficultyQuiversMigrated();
    }

    private void Awake()
    {
        ResolveReferences();
        EnsureDifficultyQuiversMigrated();
        CacheGroundPosesIfNeeded();
    }

    private void Start()
    {
        ResetForWaiting();
    }

    private void Update()
    {
        if (dragon == null || bow == null)
        {
            ResolveReferences();
        }

        UpdateIdlePropSpin();

        if (dragon == null || bow == null || phase == EquipPhase.Complete)
        {
            return;
        }

        if (PlayEnvironment.IsDesktopInput)
        {
            UpdateDesktopShortcuts();
        }

        UpdatePickupDwell();

        RefreshInstructionsIfNeeded();
    }

    private void UpdateIdlePropSpin()
    {
        if (!idlePropSpin || phase == EquipPhase.Complete)
        {
            return;
        }

        float delta = idleSpinDegreesPerSecond * Time.deltaTime;

        if (phase == EquipPhase.NeedBow
            && groundBowVisual != null
            && groundBowVisual.activeSelf)
        {
            bowSpinAngle += delta;
            Vector3 axis = ResolveSpinAxis(groundBowAnchor);
            // World-axis spin: AngleAxis * rest (keeps upright props turning like a turntable).
            groundBowVisual.transform.rotation =
                Quaternion.AngleAxis(bowSpinAngle, axis) * bowGroundRotation;
            return;
        }

        if (phase != EquipPhase.NeedQuiverPickup && phase != EquipPhase.NeedQuiverBack)
        {
            return;
        }

        EnsureSpinAngleBuffer();
        for (int i = 0; i < DifficultyQuiverCount; i++)
        {
            if (quiverHeld && i == selectedQuiverIndex && !UsesSharedHeldVisual())
            {
                continue;
            }

            GameObject prop = GetQuiverGroundObject(i);
            QuiverPoseCache pose = GetQuiverPose(i);
            if (prop == null || !prop.activeSelf || !pose.valid)
            {
                continue;
            }

            Transform anchor = difficultyQuivers[i] != null
                ? difficultyQuivers[i].groundAnchor
                : null;
            Vector3 axis = ResolveSpinAxis(anchor);

            quiverSpinAngles[i] += delta;
            prop.transform.rotation =
                Quaternion.AngleAxis(quiverSpinAngles[i], axis) * pose.rotation;
        }
    }

    private Vector3 ResolveSpinAxis(Transform anchor)
    {
        if (useAnchorUpAxis && anchor != null)
        {
            Vector3 up = anchor.up;
            if (up.sqrMagnitude > 1e-6f)
            {
                return up.normalized;
            }
        }

        if (idleSpinAxis.sqrMagnitude > 1e-6f)
        {
            return idleSpinAxis.normalized;
        }

        return Vector3.up;
    }

    private void LateUpdate()
    {
        PlayEnvironment.SuppressWandRay();

        if (quiverHeld && phase == EquipPhase.NeedQuiverBack)
        {
            SyncHeldQuiverToHand();
        }

        // Keep assigned difficulty labels locked to their anchor pose (never spin).
        StabilizeDifficultyLabels();
    }

    public void ResetForWaiting()
    {
        ResolveReferences();
        EnsureDifficultyQuiversMigrated();
        // Keep the original scene poses — do not re-cache after idle spin.
        CacheGroundPosesIfNeeded();
        ResetIdleSpinAngles();
        startedFight = false;
        quiverHeld = false;
        selectedQuiverIndex = -1;
        selectedDifficulty = FightDifficulty.Normal;
        dwell = 0f;
        phase = EquipPhase.NeedBow;
        lastInstructionPhase = (EquipPhase)(-1);

        SetPlayableBowEquipped(false);
        PlaceGroundBowVisual();
        // Quivers stay hidden until the bow is equipped.
        HideAllGroundQuivers();
        HideDifficultyLabels();
        HideHeldQuiver();
        ResetScopePickups();
        if (bow != null)
        {
            bow.SetQuiverMountedOnBack(false);
        }

        SetInstructionSignsVisible(true);
        RefreshInstructionsIfNeeded();
    }

    private void UpdatePickupDwell()
    {
        switch (phase)
        {
            case EquipPhase.NeedBow:
                if (IsNearPickup(leftHandName, GetBowPickupPoint()))
                {
                    dwell += Time.deltaTime;
                    if (dwell >= mountDwellSeconds)
                    {
                        EquipBow();
                    }
                }
                else
                {
                    dwell = 0f;
                }

                break;

            case EquipPhase.NeedQuiverPickup:
                int nearIndex = FindNearestGroundQuiverIndex();
                if (nearIndex >= 0)
                {
                    dwell += Time.deltaTime;
                    if (dwell >= mountDwellSeconds)
                    {
                        PickupQuiver(nearIndex);
                    }
                }
                else
                {
                    dwell = 0f;
                }

                break;

            case EquipPhase.NeedQuiverBack:
                // Choice is locked — strap on, grab scope, or click the panel to reset.
                if (bow != null && bow.IsRightHandAtBackQuiverZone())
                {
                    dwell += Time.deltaTime;
                    if (dwell >= mountDwellSeconds)
                    {
                        MountQuiverOnBack();
                    }
                }
                else
                {
                    dwell = 0f;
                }

                break;
        }
    }

    private void UpdateDesktopShortcuts()
    {
        if (phase == EquipPhase.NeedBow && Input.GetKeyDown(desktopEquipBowKey))
        {
            EquipBow();
            return;
        }

        if (phase == EquipPhase.NeedQuiverPickup || phase == EquipPhase.NeedQuiverBack)
        {
            if (phase == EquipPhase.NeedQuiverPickup && Input.GetKeyDown(desktopPickEasyKey))
            {
                TryDesktopPickDifficulty(FightDifficulty.Easy);
            }
            else if (phase == EquipPhase.NeedQuiverPickup && Input.GetKeyDown(desktopPickNormalKey))
            {
                TryDesktopPickDifficulty(FightDifficulty.Normal);
            }
            else if (phase == EquipPhase.NeedQuiverPickup && Input.GetKeyDown(desktopPickHardKey))
            {
                TryDesktopPickDifficulty(FightDifficulty.Hard);
            }
            else if (phase == EquipPhase.NeedQuiverPickup && Input.GetKeyDown(desktopPickupQuiverKey))
            {
                int nearest = FindNearestGroundQuiverIndex(ignoreDistance: true);
                if (nearest < 0)
                {
                    nearest = IndexOfDifficulty(FightDifficulty.Normal);
                }

                if (nearest >= 0)
                {
                    PickupQuiver(nearest);
                }
            }
        }

        if (phase == EquipPhase.NeedQuiverBack && Input.GetKeyDown(desktopMountQuiverKey))
        {
            MountQuiverOnBack();
        }
    }

    private void TryDesktopPickDifficulty(FightDifficulty difficulty)
    {
        int index = IndexOfDifficulty(difficulty);
        if (index < 0 || phase != EquipPhase.NeedQuiverPickup)
        {
            return;
        }

        PickupQuiver(index);
    }

    private void EquipBow()
    {
        SetPlayableBowEquipped(true);

        if (groundBowVisual != null)
        {
            groundBowVisual.SetActive(false);
        }

        dwell = 0f;
        phase = EquipPhase.NeedQuiverPickup;
        lastInstructionPhase = (EquipPhase)(-1);

        ShowAllGroundQuivers();
        ShowDifficultyLabels();
        FightAudio.PlayEquipBow(transform.position);
        RefreshInstructionsIfNeeded();
    }

    private void PickupQuiver(int index)
    {
        if (!IsValidQuiverIndex(index) || phase != EquipPhase.NeedQuiverPickup)
        {
            return;
        }

        selectedQuiverIndex = index;
        selectedDifficulty = difficultyQuivers[index].difficulty;
        quiverHeld = true;

        AttachQuiverToHand(index);
        HideUnselectedQuiversAndLabels(index);
        ShowScopePickups();
        FightAudio.PlayEquipQuiverPickup(GetHeldQuiverWorldPosition());

        dwell = 0f;
        phase = EquipPhase.NeedQuiverBack;
        lastInstructionPhase = (EquipPhase)(-1);
        RefreshInstructionsIfNeeded();
    }

    private void AttachQuiverToHand(int index)
    {
        Transform mobile = ResolveHeldVisualForIndex(index);
        if (mobile == null)
        {
            Debug.LogWarning("DragonFightEquipStart: no quiver visual for index " + index + ".", this);
            return;
        }

        // Hide dedicated held mesh when using the ground prop as the held object.
        if (heldQuiverVisual != null && mobile != heldQuiverVisual)
        {
            heldQuiverVisual.gameObject.SetActive(false);
        }

        mobile.gameObject.SetActive(true);
        Transform attach = ResolveQuiverHandAttach();
        if (attach != null)
        {
            mobile.SetParent(attach, false);
            ApplyQuiverHandPose(mobile, PlayEnvironment.IsDesktopInput);
        }
        else
        {
            Debug.LogWarning(
                "DragonFightEquipStart: quiver picked up but no hand attach target was found.",
                this);
        }
    }

    private void RestoreQuiverToGround(int index)
    {
        if (!IsValidQuiverIndex(index))
        {
            return;
        }

        GameObject groundProp = GetQuiverGroundObject(index);
        if (groundProp == null)
        {
            return;
        }

        // If a shared held mesh was used, just turn the ground prop back on.
        Transform held = heldQuiverVisual;
        if (held != null && UsesSharedHeldVisual())
        {
            held.gameObject.SetActive(false);
            PlaceQuiverOnGround(index);
            return;
        }

        PlaceQuiverOnGround(index);
    }

    private void MountQuiverOnBack()
    {
        ExpireScopePickupsIfNeeded();
        HideHeldQuiver();
        HideAllGroundQuivers();
        HideDifficultyLabels();

        quiverHeld = false;
        if (IsValidQuiverIndex(selectedQuiverIndex))
        {
            selectedDifficulty = difficultyQuivers[selectedQuiverIndex].difficulty;
        }

        if (dragon != null)
        {
            dragon.SetDifficulty(selectedDifficulty);
        }

        bow.SetQuiverMountedOnBack(true);
        FightAudio.PlayEquipQuiverWear(
            PlayEnvironment.ResolveHeadTransform() != null
                ? PlayEnvironment.ResolveHeadTransform().position
                : transform.position);
        dwell = 0f;
        phase = EquipPhase.Complete;
        SetInstructionSignsVisible(false);
        TryStartFight();
    }

    private void SyncHeldQuiverToHand()
    {
        Transform mobile = GetActiveHeldQuiverTransform();
        if (mobile == null)
        {
            return;
        }

        Transform attach = ResolveQuiverHandAttach();
        if (attach == null || mobile.parent == attach)
        {
            return;
        }

        mobile.SetParent(attach, false);
        ApplyQuiverHandPose(mobile, PlayEnvironment.IsDesktopInput);
    }

    private void ApplyQuiverHandPose(Transform mobile, bool desktop)
    {
        if (desktop)
        {
            mobile.localPosition = desktopQuiverHandLocalPosition;
            mobile.localRotation = Quaternion.Euler(desktopQuiverHandLocalEuler);
        }
        else
        {
            mobile.localPosition = quiverHandLocalPosition;
            mobile.localRotation = Quaternion.Euler(quiverHandLocalEuler);
        }
    }

    private Transform ResolveQuiverHandAttach()
    {
        if (PlayEnvironment.IsDesktopInput)
        {
            return ResolveDesktopQuiverHandAttach();
        }

        Transform hand = PlayEnvironment.ResolveRightHandTransform();
        if (hand != null && IsActiveAttachTarget(hand))
        {
            return hand;
        }

        hand = FindHand(rightHandName);
        if (hand != null && IsActiveAttachTarget(hand))
        {
            return hand;
        }

        if (bow != null)
        {
            Transform bowHand = bow.RightHandTransform;
            if (bowHand != null && IsActiveAttachTarget(bowHand))
            {
                return bowHand;
            }
        }

        return PlayEnvironment.ResolveDesktopBowParent();
    }

    private Transform ResolveDesktopQuiverHandAttach()
    {
        if (bow != null && bow.gameObject.activeInHierarchy)
        {
            return bow.transform;
        }

        Camera cam = PlayEnvironment.ResolveViewCamera();
        if (cam != null)
        {
            return cam.transform;
        }

        return PlayEnvironment.ResolveDesktopBowParent();
    }

    private static bool IsActiveAttachTarget(Transform attach)
    {
        return attach != null && attach.gameObject.activeInHierarchy;
    }

    private void TryStartFight()
    {
        if (startedFight || dragon == null || !dragon.IsWaitingForStart)
        {
            return;
        }

        startedFight = true;
        SetInstructionSignsVisible(false);
        dragon.StartFight();
    }

    private void SetPlayableBowEquipped(bool equipped)
    {
        if (bow == null)
        {
            return;
        }

        if (leftHandChild == null)
        {
            leftHandChild = bow.GetComponent<LeftHandChild>();
        }

        // Order matters: SetBowGrounded notifies ScopePickup when leaving the ground.
        bow.SetBowGrounded(!equipped);
        bow.gameObject.SetActive(equipped);

        if (equipped)
        {
            if (leftHandChild != null)
            {
                leftHandChild.enabled = true;
                leftHandChild.FollowBoundHand();
            }
        }
        else if (leftHandChild != null)
        {
            leftHandChild.enabled = false;
        }
    }

    private void PlaceGroundBowVisual()
    {
        if (groundBowVisual == null)
        {
            return;
        }

        groundBowVisual.transform.SetPositionAndRotation(bowGroundPosition, bowGroundRotation);
        groundBowVisual.transform.SetParent(bowGroundParent, true);
        groundBowVisual.SetActive(true);
    }

    private void ShowAllGroundQuivers()
    {
        for (int i = 0; i < DifficultyQuiverCount; i++)
        {
            PlaceQuiverOnGround(i);
        }
    }

    private void HideAllGroundQuivers()
    {
        for (int i = 0; i < DifficultyQuiverCount; i++)
        {
            GameObject prop = GetQuiverGroundObject(i);
            if (prop != null)
            {
                QuiverPoseCache pose = GetQuiverPose(i);
                if (pose.valid)
                {
                    prop.transform.SetParent(pose.parent, true);
                    prop.transform.SetPositionAndRotation(pose.position, pose.rotation);
                }

                prop.SetActive(false);
            }
        }
    }

    private void PlaceQuiverOnGround(int index)
    {
        GameObject prop = GetQuiverGroundObject(index);
        if (prop == null)
        {
            return;
        }

        QuiverPoseCache pose = GetQuiverPose(index);
        if (pose.valid)
        {
            prop.transform.SetParent(pose.parent, true);
            prop.transform.SetPositionAndRotation(pose.position, pose.rotation);
        }
        else
        {
            DifficultyQuiver option = difficultyQuivers[index];
            if (option.groundAnchor != null)
            {
                prop.transform.SetPositionAndRotation(
                    option.groundAnchor.position,
                    option.groundAnchor.rotation);
            }
        }

        EnsureSpinAngleBuffer();
        if (index >= 0 && index < quiverSpinAngles.Length)
        {
            quiverSpinAngles[index] = 0f;
        }

        prop.SetActive(true);
    }

    private void HideUnselectedQuiversAndLabels(int keepIndex)
    {
        for (int i = 0; i < DifficultyQuiverCount; i++)
        {
            SetDifficultyLabelVisible(i, false);

            if (i == keepIndex)
            {
                continue;
            }

            GameObject prop = GetQuiverGroundObject(i);
            if (prop == null)
            {
                continue;
            }

            QuiverPoseCache pose = GetQuiverPose(i);
            if (pose.valid)
            {
                prop.transform.SetParent(pose.parent, true);
                prop.transform.SetPositionAndRotation(pose.position, pose.rotation);
            }

            prop.SetActive(false);
        }
    }

    private void ShowDifficultyLabels()
    {
        CacheLabelPosesIfNeeded();
        for (int i = 0; i < DifficultyQuiverCount; i++)
        {
            SetDifficultyLabelVisible(i, true);
        }
    }

    private void HideDifficultyLabels()
    {
        for (int i = 0; i < DifficultyQuiverCount; i++)
        {
            SetDifficultyLabelVisible(i, false);
        }
    }

    private void SetDifficultyLabelVisible(int index, bool visible)
    {
        if (!IsValidQuiverIndex(index))
        {
            return;
        }

        GameObject label = difficultyQuivers[index].label;
        if (label == null)
        {
            return;
        }

        if (visible)
        {
            RestoreLabelPose(index);
        }

        label.SetActive(visible);
    }

    private void StabilizeDifficultyLabels()
    {
        // Only while choosing — labels are hidden once a quiver is held.
        if (phase != EquipPhase.NeedQuiverPickup)
        {
            return;
        }

        for (int i = 0; i < DifficultyQuiverCount; i++)
        {
            GameObject label = difficultyQuivers[i] != null ? difficultyQuivers[i].label : null;
            if (label == null || !label.activeSelf)
            {
                continue;
            }

            RestoreLabelPose(i);
        }
    }

    private void RestoreLabelPose(int index)
    {
        if (labelPoseCache == null || index < 0 || index >= labelPoseCache.Length)
        {
            return;
        }

        LabelPoseCache pose = labelPoseCache[index];
        if (!pose.valid || !IsValidQuiverIndex(index))
        {
            return;
        }

        GameObject label = difficultyQuivers[index].label;
        if (label == null)
        {
            return;
        }

        Transform t = label.transform;
        if (pose.parent != null && t.parent != pose.parent)
        {
            t.SetParent(pose.parent, false);
        }

        t.localPosition = pose.localPosition;
        t.localRotation = pose.localRotation;
    }

    private void CacheLabelPosesIfNeeded()
    {
        if (labelPoseCache != null
            && labelPoseCache.Length == DifficultyQuiverCount)
        {
            bool anyValid = false;
            for (int i = 0; i < labelPoseCache.Length; i++)
            {
                if (labelPoseCache[i].valid)
                {
                    anyValid = true;
                    break;
                }
            }

            if (anyValid)
            {
                return;
            }
        }

        CacheLabelPoses();
    }

    private void EnsureSpinAngleBuffer()
    {
        int count = DifficultyQuiverCount;
        if (quiverSpinAngles != null && quiverSpinAngles.Length == count)
        {
            return;
        }

        quiverSpinAngles = new float[count];
    }

    private void ResetIdleSpinAngles()
    {
        bowSpinAngle = 0f;
        EnsureSpinAngleBuffer();
        for (int i = 0; i < quiverSpinAngles.Length; i++)
        {
            quiverSpinAngles[i] = 0f;
        }
    }

    private void HideHeldQuiver()
    {
        if (UsesSharedHeldVisual() && heldQuiverVisual != null)
        {
            heldQuiverVisual.gameObject.SetActive(false);
        }

        if (IsValidQuiverIndex(selectedQuiverIndex))
        {
            GameObject prop = GetQuiverGroundObject(selectedQuiverIndex);
            if (prop != null && (!UsesSharedHeldVisual() || prop.transform == heldQuiverVisual))
            {
                prop.SetActive(false);
            }
        }
        else if (heldQuiverVisual != null)
        {
            heldQuiverVisual.gameObject.SetActive(false);
        }
    }

    private Transform ResolveHeldVisualForIndex(int index)
    {
        if (UsesSharedHeldVisual())
        {
            // Leave the ground prop where it is (hidden) and show the shared held mesh.
            GameObject ground = GetQuiverGroundObject(index);
            if (ground != null)
            {
                ground.SetActive(false);
            }

            return heldQuiverVisual;
        }

        GameObject prop = GetQuiverGroundObject(index);
        return prop != null ? prop.transform : null;
    }

    private Transform GetActiveHeldQuiverTransform()
    {
        if (UsesSharedHeldVisual())
        {
            return heldQuiverVisual;
        }

        if (!IsValidQuiverIndex(selectedQuiverIndex))
        {
            return null;
        }

        GameObject prop = GetQuiverGroundObject(selectedQuiverIndex);
        return prop != null ? prop.transform : null;
    }

    private Vector3 GetHeldQuiverWorldPosition()
    {
        Transform held = GetActiveHeldQuiverTransform();
        return held != null ? held.position : transform.position;
    }

    private bool UsesSharedHeldVisual()
    {
        // With multiple difficulty quivers, carry the chosen ground mesh so each looks distinct.
        return heldQuiverVisual != null && DifficultyQuiverCount <= 1;
    }

    private int DifficultyQuiverCount => difficultyQuivers != null ? difficultyQuivers.Length : 0;

    private bool IsValidQuiverIndex(int index)
    {
        return index >= 0
               && difficultyQuivers != null
               && index < difficultyQuivers.Length
               && difficultyQuivers[index] != null
               && difficultyQuivers[index].groundVisual != null;
    }

    private GameObject GetQuiverGroundObject(int index)
    {
        if (!IsValidQuiverIndex(index))
        {
            return null;
        }

        return difficultyQuivers[index].groundVisual;
    }

    private QuiverPoseCache GetQuiverPose(int index)
    {
        if (quiverPoseCache == null || index < 0 || index >= quiverPoseCache.Length)
        {
            return default;
        }

        return quiverPoseCache[index];
    }

    private int IndexOfDifficulty(FightDifficulty difficulty)
    {
        for (int i = 0; i < DifficultyQuiverCount; i++)
        {
            if (difficultyQuivers[i] != null
                && difficultyQuivers[i].groundVisual != null
                && difficultyQuivers[i].difficulty == difficulty)
            {
                return i;
            }
        }

        return -1;
    }

    private int FindNearestGroundQuiverIndex(bool excludeHeld = false, bool ignoreDistance = false)
    {
        Transform hand = PlayEnvironment.ResolveRightHandTransform();
        if (hand == null)
        {
            hand = FindHand(rightHandName);
        }

        Camera cam = null;
        if (PlayEnvironment.IsDesktopInput && desktopProximityPickup)
        {
            cam = PlayEnvironment.ResolveViewCamera();
        }

        int best = -1;
        float bestDist = float.MaxValue;

        for (int i = 0; i < DifficultyQuiverCount; i++)
        {
            if (excludeHeld && i == selectedQuiverIndex)
            {
                continue;
            }

            GameObject prop = GetQuiverGroundObject(i);
            if (prop == null || !prop.activeInHierarchy)
            {
                continue;
            }

            Vector3 point = prop.transform.position;
            float dist = float.MaxValue;

            if (hand != null)
            {
                dist = Mathf.Min(dist, Vector3.Distance(hand.position, point));
            }

            if (IsHandNear(rightHandName, point, pickupDistance * 4f))
            {
                Transform named = FindHand(rightHandName);
                if (named != null)
                {
                    dist = Mathf.Min(dist, Vector3.Distance(named.position, point));
                }
            }

            if (cam != null)
            {
                dist = Mathf.Min(dist, Vector3.Distance(cam.transform.position, point));
            }

            if (ignoreDistance)
            {
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = i;
                }

                continue;
            }

            float limit = cam != null && hand == null ? pickupDistance * 1.35f : pickupDistance;
            if (dist <= limit && dist < bestDist)
            {
                bestDist = dist;
                best = i;
            }
        }

        return best;
    }

    private void EnsureDifficultyQuiversMigrated()
    {
        if (difficultyQuivers != null && difficultyQuivers.Length > 0)
        {
            bool any = false;
            for (int i = 0; i < difficultyQuivers.Length; i++)
            {
                if (difficultyQuivers[i] != null && difficultyQuivers[i].groundVisual != null)
                {
                    any = true;
                    break;
                }
            }

            if (any)
            {
                return;
            }
        }

        GameObject legacy = groundQuiverVisual;
        if (legacy == null && groundQuiver != null
            && (heldQuiverVisual == null || heldQuiverVisual != groundQuiver))
        {
            legacy = groundQuiver.gameObject;
            groundQuiverVisual = legacy;
        }

        if (legacy == null)
        {
            return;
        }

        difficultyQuivers = new[]
        {
            new DifficultyQuiver
            {
                difficulty = FightDifficulty.Normal,
                groundVisual = legacy,
                groundAnchor = groundQuiverAnchor
            }
        };
    }

    private void ResolveScopePickups()
    {
#if UNITY_2023_1_OR_NEWER
        ScopePickup[] found = FindObjectsByType<ScopePickup>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
        ScopePickup[] found = Resources.FindObjectsOfTypeAll<ScopePickup>();
#endif
        System.Collections.Generic.List<ScopePickup> list = new System.Collections.Generic.List<ScopePickup>(4);
        if (scopePickups != null)
        {
            for (int i = 0; i < scopePickups.Length; i++)
            {
                if (scopePickups[i] != null && !list.Contains(scopePickups[i]))
                {
                    list.Add(scopePickups[i]);
                }
            }
        }

        if (found != null)
        {
            for (int i = 0; i < found.Length; i++)
            {
                ScopePickup scope = found[i];
                if (scope == null || !scope.gameObject.scene.IsValid() || !scope.gameObject.scene.isLoaded)
                {
                    continue;
                }

                if (!list.Contains(scope))
                {
                    list.Add(scope);
                }
            }
        }

        scopePickups = list.ToArray();
    }

    private void ResetScopePickups()
    {
        ResolveScopePickups();
        if (scopePickups == null)
        {
            return;
        }

        for (int i = 0; i < scopePickups.Length; i++)
        {
            if (scopePickups[i] != null)
            {
                scopePickups[i].ResetPickup();
            }
        }
    }

    private void ShowScopePickups()
    {
        ResolveScopePickups();
        if (scopePickups == null)
        {
            return;
        }

        for (int i = 0; i < scopePickups.Length; i++)
        {
            if (scopePickups[i] != null)
            {
                scopePickups[i].ShowAfterBowEquipped();
            }
        }
    }

    private void ExpireScopePickupsIfNeeded()
    {
        ResolveScopePickups();
        if (scopePickups == null)
        {
            return;
        }

        for (int i = 0; i < scopePickups.Length; i++)
        {
            if (scopePickups[i] != null)
            {
                scopePickups[i].ExpireIfNotPickedUp();
            }
        }
    }

    private void CacheGroundPosesIfNeeded()
    {
        if (groundPosesCached && quiverPoseCache != null && quiverPoseCache.Length == DifficultyQuiverCount)
        {
            bool allValid = true;
            for (int i = 0; i < quiverPoseCache.Length; i++)
            {
                if (!quiverPoseCache[i].valid && IsValidQuiverIndex(i))
                {
                    allValid = false;
                    break;
                }
            }

            if (allValid)
            {
                return;
            }
        }

        CacheGroundPoses();
    }

    private void CacheGroundPoses()
    {
        if (groundBowVisual != null && !groundPosesCached)
        {
            bowGroundPosition = groundBowAnchor != null
                ? groundBowAnchor.position
                : groundBowVisual.transform.position;
            bowGroundRotation = groundBowVisual.transform.rotation;
            bowGroundParent = groundBowAnchor != null
                ? groundBowAnchor.parent
                : groundBowVisual.transform.parent;
        }

        int count = DifficultyQuiverCount;
        QuiverPoseCache[] next = new QuiverPoseCache[count];
        for (int i = 0; i < count; i++)
        {
            if (quiverPoseCache != null
                && i < quiverPoseCache.Length
                && quiverPoseCache[i].valid)
            {
                next[i] = quiverPoseCache[i];
                continue;
            }

            DifficultyQuiver option = difficultyQuivers[i];
            if (option == null || option.groundVisual == null)
            {
                continue;
            }

            Transform anchor = option.groundAnchor;
            next[i] = new QuiverPoseCache
            {
                position = anchor != null
                    ? anchor.position
                    : option.groundVisual.transform.position,
                rotation = option.groundVisual.transform.rotation,
                parent = anchor != null
                    ? anchor.parent
                    : option.groundVisual.transform.parent,
                valid = true
            };
        }

        quiverPoseCache = next;
        CacheLabelPoses();
        groundPosesCached = true;
        EnsureSpinAngleBuffer();
    }

    private void CacheLabelPoses()
    {
        int count = DifficultyQuiverCount;
        LabelPoseCache[] next = new LabelPoseCache[count];
        for (int i = 0; i < count; i++)
        {
            if (labelPoseCache != null
                && i < labelPoseCache.Length
                && labelPoseCache[i].valid)
            {
                next[i] = labelPoseCache[i];
                continue;
            }

            if (!IsValidQuiverIndex(i) || difficultyQuivers[i].label == null)
            {
                continue;
            }

            Transform t = difficultyQuivers[i].label.transform;
            next[i] = new LabelPoseCache
            {
                localPosition = t.localPosition,
                localRotation = t.localRotation,
                parent = t.parent,
                valid = true
            };
        }

        labelPoseCache = next;
    }

    private Vector3 GetBowPickupPoint()
    {
        if (groundBowVisual != null)
        {
            return groundBowVisual.transform.position;
        }

        if (groundBowAnchor != null)
        {
            return groundBowAnchor.position;
        }

        return transform.position;
    }

    private bool IsNearPickup(string handName, Vector3 point)
    {
        if (IsHandNear(handName, point, pickupDistance))
        {
            return true;
        }

        if (!PlayEnvironment.IsDesktopInput || !desktopProximityPickup)
        {
            return false;
        }

        Camera cam = PlayEnvironment.ResolveViewCamera();
        return cam != null && Vector3.Distance(cam.transform.position, point) <= pickupDistance * 1.35f;
    }

    private void RefreshInstructionsIfNeeded()
    {
        if (fightUI == null || phase == lastInstructionPhase)
        {
            return;
        }

        lastInstructionPhase = phase;

        int step = 1;
        switch (phase)
        {
            case EquipPhase.NeedQuiverPickup:
                step = 2;
                break;
            case EquipPhase.NeedQuiverBack:
                step = 3;
                break;
        }

        fightUI.ShowEquipStep(step);
    }

    private void SetInstructionSignsVisible(bool visible)
    {
        if (instructionSigns == null)
        {
            return;
        }

        for (int i = 0; i < instructionSigns.Length; i++)
        {
            if (instructionSigns[i] != null)
            {
                instructionSigns[i].SetActive(visible);
            }
        }
    }

    private void ResolveReferences()
    {
        if (dragon == null)
        {
            dragon = FindObjectOfType<DragonBoss>();
        }

        if (bow == null)
        {
#if UNITY_2023_1_OR_NEWER
            bow = FindFirstObjectByType<BowController>(FindObjectsInactive.Include);
#else
            bow = FindObjectOfType<BowController>(true);
#endif
        }

        if (leftHandChild == null && bow != null)
        {
            leftHandChild = bow.GetComponent<LeftHandChild>();
        }

        if (fightUI == null)
        {
            fightUI = FindObjectOfType<DragonFightUI>();
        }
    }

    private static bool IsHandNear(string handName, Vector3 point, float distance)
    {
        Transform hand = FindHand(handName);
        if (hand == null)
        {
            return false;
        }

        return Vector3.Distance(hand.position, point) <= distance;
    }

    private static Transform FindHand(string handName)
    {
        if (string.IsNullOrEmpty(handName))
        {
            return null;
        }

#if UNITY_2023_1_OR_NEWER
        Transform[] all = FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
        Transform[] all = FindObjectsOfType<Transform>();
#endif
        for (int i = 0; i < all.Length; i++)
        {
            Transform candidate = all[i];
            if (candidate != null
                && candidate.name == handName
                && candidate.gameObject.scene.IsValid()
                && candidate.gameObject.scene.isLoaded)
            {
                return candidate;
            }
        }

        return null;
    }
}
