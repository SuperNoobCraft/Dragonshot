using UnityEngine;
using Votanic.vXR.vCast;

/// <summary>
/// Pre-fight tutorial: choose a ground bow (with or without scope), pick a difficulty quiver,
/// strap it on, then auto-start the fight. After a quiver is chosen, other quivers hide —
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
    public class GroundBowChoice
    {
        [Tooltip("If true, this bow starts with the scope / trajectory preview equipped.")]
        public bool withScope;
        [Tooltip("Ground prop for this bow choice.")]
        public GameObject groundVisual;
        [Tooltip("Optional world pose. Empty = use the visual's scene pose.")]
        public Transform groundAnchor;
        [Tooltip("TMP / mesh label above the bow. Shown while choosing; does not spin.")]
        public GameObject label;
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
    [Tooltip("Two (or more) ground bows: typically one with scope, one without.")]
    [SerializeField] private GroundBowChoice[] groundBows;
    [Tooltip("Legacy single ground bow — migrated into Ground Bows if that array is empty.")]
    [SerializeField, HideInInspector] private GameObject groundBowVisual;
    [Tooltip("Legacy single ground quiver (migrated to Normal if Difficulty Quivers is empty).")]
    [SerializeField] private GameObject groundQuiverVisual;
    [Tooltip("One entry per difficulty. Other quivers hide after one is picked (reset via fight panel).")]
    [SerializeField] private DifficultyQuiver[] difficultyQuivers;
    [Tooltip("Optional shared held mesh. Empty = the picked ground visual is parented to the hand.")]
    [SerializeField] private Transform heldQuiverVisual;
    [Tooltip("Legacy single reference: used as ground and/or held quiver if the fields above are empty.")]
    [SerializeField] private Transform groundQuiver;
    [SerializeField] private DragonFightUI fightUI;
    [Tooltip("Secret crystal target practice (panel click before bow pick).")]
    [SerializeField] private CrystalTargetPractice targetPractice;
    [Tooltip("World-space instruction signs that rise for equip and sink when the fight begins.")]
    [SerializeField] private GameObject[] instructionSigns;
    [Tooltip("How far below rest height signs bury (meters).")]
    [SerializeField, Min(0.25f)] private float instructionSignBuryDepth = 3.5f;
    [SerializeField, Min(0.1f)] private float instructionSignRiseSpeed = 3.5f;
    [SerializeField, Min(0.1f)] private float instructionSignRetreatSpeed = 4.5f;
    [Tooltip("Same style as instruction signs. Starts buried; rises after victory (dragon exploded); sinks on reset. Not shown on defeat.")]
    [SerializeField] private GameObject[] creditsSigns;
    [Tooltip("Optional. Used only to sync the equipped scope mesh on the playable bow.")]
    [SerializeField] private ScopePickup[] scopePickups;

    [Header("Ground Poses")]
    [Tooltip("Legacy single bow anchor — migrated into Ground Bows.")]
    [SerializeField, HideInInspector] private Transform groundBowAnchor;
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
    [Tooltip("Desktop: pick the no-scope ground bow.")]
    [SerializeField] private KeyCode desktopPickBowNoScopeKey = KeyCode.B;
    [Tooltip("Desktop: pick the scoped ground bow.")]
    [SerializeField] private KeyCode desktopPickBowWithScopeKey = KeyCode.G;
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

    private enum SignMotion
    {
        Raised,
        Rising,
        Buried,
        Retreating
    }

    private struct InstructionSignSlot
    {
        public Transform transform;
        public Vector3 restPosition;
        public float buriedY;
        public SignMotion motion;
        public bool cached;
    }

    private EquipPhase phase = EquipPhase.NeedBow;
    private EquipPhase lastInstructionPhase = (EquipPhase)(-1);
    private float dwell;
    private QuiverPoseCache[] bowPoseCache = System.Array.Empty<QuiverPoseCache>();
    private LabelPoseCache[] bowLabelPoseCache = System.Array.Empty<LabelPoseCache>();
    private QuiverPoseCache[] quiverPoseCache = System.Array.Empty<QuiverPoseCache>();
    private LabelPoseCache[] labelPoseCache = System.Array.Empty<LabelPoseCache>();
    private InstructionSignSlot[] instructionSignSlots = System.Array.Empty<InstructionSignSlot>();
    private InstructionSignSlot[] creditsSignSlots = System.Array.Empty<InstructionSignSlot>();
    private float[] quiverSpinAngles = System.Array.Empty<float>();
    private float[] bowSpinAngles = System.Array.Empty<float>();
    private bool groundPosesCached;
    private bool startedFight;
    private bool quiverHeld;
    private bool targetPracticeActive;
    private int selectedQuiverIndex = -1;
    private int selectedBowIndex = -1;
    private FightDifficulty selectedDifficulty = FightDifficulty.Normal;

    public bool IsBowEquipped => phase != EquipPhase.NeedBow;
    public bool IsQuiverMounted => phase == EquipPhase.Complete;
    public bool IsTargetPracticeActive => targetPracticeActive;
    public FightDifficulty SelectedDifficulty => selectedDifficulty;

    /// <summary>
    /// Equip tutorial chooses scope with the bow; ground scope pickups stay hidden.
    /// </summary>
    public bool DefersScopeUntilQuiverChosen => isActiveAndEnabled;

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
        EnsureGroundBowsMigrated();
    }

    private void Awake()
    {
        ResolveReferences();
        EnsureDifficultyQuiversMigrated();
        EnsureGroundBowsMigrated();
        CacheGroundPosesIfNeeded();
        CacheInstructionSignPoses();
        CacheCreditsSignPoses();
        SnapCreditsSignsBuried();
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
        UpdateInstructionSigns();
        UpdateCreditsSigns();

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

        if (phase == EquipPhase.NeedBow)
        {
            EnsureBowSpinAngleBuffer();
            for (int i = 0; i < GroundBowCount; i++)
            {
                GameObject prop = GetBowGroundObject(i);
                QuiverPoseCache pose = GetBowPose(i);
                if (prop == null || !prop.activeSelf || !pose.valid)
                {
                    continue;
                }

                Transform anchor = groundBows[i] != null ? groundBows[i].groundAnchor : null;
                Vector3 axis = ResolveSpinAxis(anchor);
                bowSpinAngles[i] += delta;
                prop.transform.rotation =
                    Quaternion.AngleAxis(bowSpinAngles[i], axis) * pose.rotation;
            }

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

        // Keep assigned labels locked to their anchor pose (never spin).
        StabilizeBowLabels();
        StabilizeDifficultyLabels();
    }

    public void ResetForWaiting()
    {
        if (targetPractice != null && targetPractice.IsActive)
        {
            targetPractice.ForceStopWithoutEquipReset();
        }

        targetPracticeActive = false;
        ResolveReferences();
        EnsureDifficultyQuiversMigrated();
        EnsureGroundBowsMigrated();
        // Keep the original scene poses — do not re-cache after idle spin.
        CacheGroundPosesIfNeeded();
        ResetIdleSpinAngles();
        startedFight = false;
        quiverHeld = false;
        selectedQuiverIndex = -1;
        selectedBowIndex = -1;
        selectedDifficulty = FightDifficulty.Normal;
        dwell = 0f;
        phase = EquipPhase.NeedBow;
        lastInstructionPhase = (EquipPhase)(-1);

        SetPlayableBowEquipped(false);
        ApplyScopeFromBowChoice(false);
        ShowAllGroundBows();
        ShowBowLabels();
        // Quivers stay hidden until a bow is equipped.
        HideAllGroundQuivers();
        HideDifficultyLabels();
        HideHeldQuiver();
        ResetScopePickups();
        if (bow != null)
        {
            bow.SetQuiverMountedOnBack(false);
        }

        SetInstructionSignsVisible(true);
        SetCreditsSignsVisible(false);
        RefreshInstructionsIfNeeded();
    }

    /// <summary>Secret target practice: no dragon, no scope bow; Normal quiver only after bow.</summary>
    public void EnterTargetPracticeMode()
    {
        targetPracticeActive = true;
        phase = EquipPhase.NeedBow;
        lastInstructionPhase = (EquipPhase)(-1);
        dwell = 0f;
        selectedBowIndex = -1;
        selectedQuiverIndex = -1;
        quiverHeld = false;
        startedFight = false;

        SetPlayableBowEquipped(false);
        ApplyScopeFromBowChoice(false);
        ShowNoScopeGroundBowsOnly();
        HideAllGroundQuivers();
        HideDifficultyLabels();
        HideHeldQuiver();
        ResetScopePickups();
        if (bow != null)
        {
            bow.SetQuiverMountedOnBack(false);
        }

        SetInstructionSignsVisible(false);
        SetCreditsSignsVisible(false);
    }

    /// <summary>Rise credits signs after victory (dragon exploded).</summary>
    public void ShowCreditsSigns()
    {
        SetCreditsSignsVisible(true);
    }

    /// <summary>Sink credits signs (reset / defeat / fight start).</summary>
    public void HideCreditsSigns()
    {
        SetCreditsSignsVisible(false);
    }

    private void UpdatePickupDwell()
    {
        switch (phase)
        {
            case EquipPhase.NeedBow:
                int nearBow = FindNearestGroundBowIndex();
                if (nearBow >= 0)
                {
                    dwell += Time.deltaTime;
                    if (dwell >= mountDwellSeconds)
                    {
                        EquipBow(nearBow);
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
                // Choice is locked — strap on, or click the panel to reset.
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
        if (phase == EquipPhase.NeedBow)
        {
            if (Input.GetKeyDown(desktopPickBowNoScopeKey))
            {
                TryDesktopPickBow(withScope: false);
                return;
            }

            if (Input.GetKeyDown(desktopPickBowWithScopeKey))
            {
                TryDesktopPickBow(withScope: true);
                return;
            }

            if (Input.GetKeyDown(desktopEquipBowKey))
            {
                int nearest = FindNearestGroundBowIndex(ignoreDistance: true);
                if (nearest >= 0)
                {
                    EquipBow(nearest);
                }

                return;
            }
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

    private void TryDesktopPickBow(bool withScope)
    {
        if (targetPracticeActive && withScope)
        {
            return;
        }

        if (phase != EquipPhase.NeedBow)
        {
            return;
        }

        int index = IndexOfBow(withScope);
        if (index < 0)
        {
            index = FindNearestGroundBowIndex(ignoreDistance: true);
        }

        if (index >= 0)
        {
            EquipBow(index);
        }
    }

    private void TryDesktopPickDifficulty(FightDifficulty difficulty)
    {
        if (targetPracticeActive && difficulty != FightDifficulty.Normal)
        {
            return;
        }

        int index = IndexOfDifficulty(difficulty);
        if (index < 0 || phase != EquipPhase.NeedQuiverPickup)
        {
            return;
        }

        PickupQuiver(index);
    }

    private void EquipBow(int index)
    {
        if (!IsValidBowIndex(index) || phase != EquipPhase.NeedBow)
        {
            return;
        }

        if (targetPracticeActive)
        {
            if (groundBows[index].withScope)
            {
                return;
            }
        }

        selectedBowIndex = index;
        bool withScope = !targetPracticeActive
            && groundBows[index] != null
            && groundBows[index].withScope;

        SetPlayableBowEquipped(true);
        ApplyScopeFromBowChoice(withScope);
        HideAllGroundBows();
        HideBowLabels();

        dwell = 0f;
        phase = EquipPhase.NeedQuiverPickup;
        lastInstructionPhase = (EquipPhase)(-1);

        if (targetPracticeActive)
        {
            ShowNormalGroundQuiverOnly();
        }
        else
        {
            ShowAllGroundQuivers();
            ShowDifficultyLabels();
        }

        FightAudio.PlayEquipBow(transform.position);
        if (withScope)
        {
            FightAudio.PlayEquipScope(transform.position);
        }

        RefreshInstructionsIfNeeded();
    }

    private void PickupQuiver(int index)
    {
        if (!IsValidQuiverIndex(index) || phase != EquipPhase.NeedQuiverPickup)
        {
            return;
        }

        if (targetPracticeActive)
        {
            int normalIndex = IndexOfDifficulty(FightDifficulty.Normal);
            if (normalIndex >= 0 && index != normalIndex)
            {
                return;
            }
        }

        selectedQuiverIndex = index;
        selectedDifficulty = difficultyQuivers[index].difficulty;
        quiverHeld = true;

        AttachQuiverToHand(index);
        HideUnselectedQuiversAndLabels(index);
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

        if (dragon != null && !targetPracticeActive)
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
        if (startedFight)
        {
            return;
        }

        if (targetPracticeActive)
        {
            if (targetPractice == null)
            {
                return;
            }

            startedFight = true;
            SetInstructionSignsVisible(false);
            targetPractice.OnQuiverMounted();
            return;
        }

        if (dragon == null || !dragon.IsWaitingForStart)
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
        ShowAllGroundBows();
    }

    private void ShowAllGroundBows()
    {
        for (int i = 0; i < GroundBowCount; i++)
        {
            PlaceBowOnGround(i);
        }
    }

    private void ShowNoScopeGroundBowsOnly()
    {
        for (int i = 0; i < GroundBowCount; i++)
        {
            if (!IsValidBowIndex(i))
            {
                continue;
            }

            bool show = !groundBows[i].withScope;
            GameObject prop = groundBows[i].groundVisual;
            if (prop != null)
            {
                prop.SetActive(show);
            }

            if (groundBows[i].label != null)
            {
                groundBows[i].label.SetActive(show);
            }

            if (show)
            {
                PlaceBowOnGround(i);
            }
        }
    }

    private void HideAllGroundBows()
    {
        for (int i = 0; i < GroundBowCount; i++)
        {
            GameObject prop = GetBowGroundObject(i);
            if (prop == null)
            {
                continue;
            }

            QuiverPoseCache pose = GetBowPose(i);
            if (pose.valid)
            {
                prop.transform.SetParent(pose.parent, true);
                prop.transform.SetPositionAndRotation(pose.position, pose.rotation);
            }

            prop.SetActive(false);
        }
    }

    private void PlaceBowOnGround(int index)
    {
        GameObject prop = GetBowGroundObject(index);
        if (prop == null)
        {
            return;
        }

        QuiverPoseCache pose = GetBowPose(index);
        if (pose.valid)
        {
            prop.transform.SetParent(pose.parent, true);
            prop.transform.SetPositionAndRotation(pose.position, pose.rotation);
        }

        prop.SetActive(true);
    }

    private void ShowBowLabels()
    {
        CacheBowLabelPosesIfNeeded();
        for (int i = 0; i < GroundBowCount; i++)
        {
            SetBowLabelVisible(i, true);
        }
    }

    private void HideBowLabels()
    {
        for (int i = 0; i < GroundBowCount; i++)
        {
            SetBowLabelVisible(i, false);
        }
    }

    private void SetBowLabelVisible(int index, bool visible)
    {
        if (!IsValidBowIndex(index))
        {
            return;
        }

        GameObject label = groundBows[index].label;
        if (label == null)
        {
            return;
        }

        if (visible)
        {
            RestoreBowLabelPose(index);
        }

        label.SetActive(visible);
    }

    private void StabilizeBowLabels()
    {
        if (phase != EquipPhase.NeedBow)
        {
            return;
        }

        for (int i = 0; i < GroundBowCount; i++)
        {
            GameObject label = groundBows[i] != null ? groundBows[i].label : null;
            if (label == null || !label.activeSelf)
            {
                continue;
            }

            RestoreBowLabelPose(i);
        }
    }

    private void RestoreBowLabelPose(int index)
    {
        if (bowLabelPoseCache == null || index < 0 || index >= bowLabelPoseCache.Length)
        {
            return;
        }

        LabelPoseCache pose = bowLabelPoseCache[index];
        if (!pose.valid || !IsValidBowIndex(index) || groundBows[index].label == null)
        {
            return;
        }

        Transform t = groundBows[index].label.transform;
        if (pose.parent != null && t.parent != pose.parent)
        {
            t.SetParent(pose.parent, false);
        }

        t.localPosition = pose.localPosition;
        t.localRotation = pose.localRotation;
    }

    private void ShowAllGroundQuivers()
    {
        for (int i = 0; i < DifficultyQuiverCount; i++)
        {
            PlaceQuiverOnGround(i);
        }
    }

    private void ShowNormalGroundQuiverOnly()
    {
        int normalIndex = IndexOfDifficulty(FightDifficulty.Normal);
        CacheLabelPosesIfNeeded();

        for (int i = 0; i < DifficultyQuiverCount; i++)
        {
            GameObject prop = GetQuiverGroundObject(i);
            bool show = i == normalIndex && IsValidQuiverIndex(i);
            if (prop != null)
            {
                if (show)
                {
                    PlaceQuiverOnGround(i);
                }
                else
                {
                    prop.SetActive(false);
                }
            }

            SetDifficultyLabelVisible(i, show);
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
        if (quiverSpinAngles == null || quiverSpinAngles.Length != count)
        {
            quiverSpinAngles = new float[count];
        }

        EnsureBowSpinAngleBuffer();
    }

    private void EnsureBowSpinAngleBuffer()
    {
        int count = GroundBowCount;
        if (bowSpinAngles != null && bowSpinAngles.Length == count)
        {
            return;
        }

        bowSpinAngles = new float[count];
    }

    private void ResetIdleSpinAngles()
    {
        EnsureSpinAngleBuffer();
        for (int i = 0; i < quiverSpinAngles.Length; i++)
        {
            quiverSpinAngles[i] = 0f;
        }

        for (int i = 0; i < bowSpinAngles.Length; i++)
        {
            bowSpinAngles[i] = 0f;
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
        if (groundPosesCached
            && quiverPoseCache != null
            && quiverPoseCache.Length == DifficultyQuiverCount
            && bowPoseCache != null
            && bowPoseCache.Length == GroundBowCount)
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

            for (int i = 0; allValid && i < bowPoseCache.Length; i++)
            {
                if (!bowPoseCache[i].valid && IsValidBowIndex(i))
                {
                    allValid = false;
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
        CacheBowPoses();

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
        CacheBowLabelPoses();
        groundPosesCached = true;
        EnsureSpinAngleBuffer();
    }

    private void CacheBowPoses()
    {
        int count = GroundBowCount;
        QuiverPoseCache[] next = new QuiverPoseCache[count];
        for (int i = 0; i < count; i++)
        {
            if (bowPoseCache != null
                && i < bowPoseCache.Length
                && bowPoseCache[i].valid)
            {
                next[i] = bowPoseCache[i];
                continue;
            }

            if (!IsValidBowIndex(i))
            {
                continue;
            }

            GroundBowChoice option = groundBows[i];
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

        bowPoseCache = next;
    }

    private void CacheBowLabelPosesIfNeeded()
    {
        if (bowLabelPoseCache != null && bowLabelPoseCache.Length == GroundBowCount)
        {
            bool anyValid = false;
            for (int i = 0; i < bowLabelPoseCache.Length; i++)
            {
                if (bowLabelPoseCache[i].valid)
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

        CacheBowLabelPoses();
    }

    private void CacheBowLabelPoses()
    {
        int count = GroundBowCount;
        LabelPoseCache[] next = new LabelPoseCache[count];
        for (int i = 0; i < count; i++)
        {
            if (bowLabelPoseCache != null
                && i < bowLabelPoseCache.Length
                && bowLabelPoseCache[i].valid)
            {
                next[i] = bowLabelPoseCache[i];
                continue;
            }

            if (!IsValidBowIndex(i) || groundBows[i].label == null)
            {
                continue;
            }

            Transform t = groundBows[i].label.transform;
            next[i] = new LabelPoseCache
            {
                localPosition = t.localPosition,
                localRotation = t.localRotation,
                parent = t.parent,
                valid = true
            };
        }

        bowLabelPoseCache = next;
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

    private int GroundBowCount => groundBows != null ? groundBows.Length : 0;

    private bool IsValidBowIndex(int index)
    {
        return index >= 0
               && groundBows != null
               && index < groundBows.Length
               && groundBows[index] != null
               && groundBows[index].groundVisual != null;
    }

    private GameObject GetBowGroundObject(int index)
    {
        return IsValidBowIndex(index) ? groundBows[index].groundVisual : null;
    }

    private QuiverPoseCache GetBowPose(int index)
    {
        if (bowPoseCache != null && index >= 0 && index < bowPoseCache.Length)
        {
            return bowPoseCache[index];
        }

        return default;
    }

    private int IndexOfBow(bool withScope)
    {
        for (int i = 0; i < GroundBowCount; i++)
        {
            if (IsValidBowIndex(i) && groundBows[i].withScope == withScope)
            {
                return i;
            }
        }

        return -1;
    }

    private int FindNearestGroundBowIndex(bool ignoreDistance = false)
    {
        Transform hand = FindHand(leftHandName);
        Camera cam = null;
        if (PlayEnvironment.IsDesktopInput && desktopProximityPickup)
        {
            cam = PlayEnvironment.ResolveViewCamera();
        }

        int best = -1;
        float bestDist = float.MaxValue;

        for (int i = 0; i < GroundBowCount; i++)
        {
            if (targetPracticeActive && IsValidBowIndex(i) && groundBows[i].withScope)
            {
                continue;
            }

            GameObject prop = GetBowGroundObject(i);
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

            if (IsHandNear(leftHandName, point, pickupDistance * 4f))
            {
                Transform named = FindHand(leftHandName);
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

    private void EnsureGroundBowsMigrated()
    {
        if (groundBows != null && groundBows.Length > 0)
        {
            bool any = false;
            for (int i = 0; i < groundBows.Length; i++)
            {
                if (groundBows[i] != null && groundBows[i].groundVisual != null)
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

        if (groundBowVisual == null)
        {
            return;
        }

        groundBows = new[]
        {
            new GroundBowChoice
            {
                withScope = false,
                groundVisual = groundBowVisual,
                groundAnchor = groundBowAnchor,
                label = null
            }
        };
    }

    private void ApplyScopeFromBowChoice(bool withScope)
    {
        if (bow != null)
        {
            bow.SetScopeEquipped(withScope);
        }

        ResolveScopePickups();
        if (scopePickups == null)
        {
            return;
        }

        for (int i = 0; i < scopePickups.Length; i++)
        {
            if (scopePickups[i] != null)
            {
                scopePickups[i].ApplyEquippedFromBowChoice(withScope);
            }
        }
    }

    private Vector3 GetBowPickupPoint()
    {
        int nearest = FindNearestGroundBowIndex(ignoreDistance: true);
        if (nearest >= 0)
        {
            return GetBowGroundObject(nearest).transform.position;
        }

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

        // Waiting-for-bow copy is owned by CrystalTargetPractice.
        if (targetPracticeActive && phase == EquipPhase.NeedBow)
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
        CacheInstructionSignPoses();
        if (visible)
        {
            BeginRaiseInstructionSigns();
        }
        else
        {
            BeginSinkInstructionSigns();
        }
    }

    private void CacheInstructionSignPoses()
    {
        int count = instructionSigns != null ? instructionSigns.Length : 0;
        if (instructionSignSlots == null || instructionSignSlots.Length != count)
        {
            instructionSignSlots = new InstructionSignSlot[count];
        }

        for (int i = 0; i < count; i++)
        {
            GameObject sign = instructionSigns[i];
            if (sign == null)
            {
                instructionSignSlots[i] = default;
                continue;
            }

            InstructionSignSlot slot = instructionSignSlots[i];
            if (slot.cached && slot.transform == sign.transform)
            {
                // Keep original rest pose (do not re-cache mid-bury).
                slot.buriedY = slot.restPosition.y - Mathf.Abs(instructionSignBuryDepth);
                instructionSignSlots[i] = slot;
                continue;
            }

            Transform t = sign.transform;
            Vector3 rest = t.position;
            instructionSignSlots[i] = new InstructionSignSlot
            {
                transform = t,
                restPosition = rest,
                buriedY = rest.y - Mathf.Abs(instructionSignBuryDepth),
                motion = SignMotion.Raised,
                cached = true
            };
        }
    }

    private void BeginRaiseInstructionSigns()
    {
        CacheInstructionSignPoses();
        for (int i = 0; i < instructionSignSlots.Length; i++)
        {
            InstructionSignSlot slot = instructionSignSlots[i];
            if (!slot.cached || slot.transform == null)
            {
                continue;
            }

            if (!slot.transform.gameObject.activeSelf)
            {
                slot.transform.gameObject.SetActive(true);
            }

            // Already up — snap to rest.
            if (Mathf.Abs(slot.transform.position.y - slot.restPosition.y) <= 0.02f
                && slot.motion != SignMotion.Retreating)
            {
                Vector3 p = slot.restPosition;
                slot.transform.position = p;
                slot.motion = SignMotion.Raised;
            }
            else
            {
                slot.motion = SignMotion.Rising;
            }

            instructionSignSlots[i] = slot;
        }
    }

    private void BeginSinkInstructionSigns()
    {
        CacheInstructionSignPoses();
        for (int i = 0; i < instructionSignSlots.Length; i++)
        {
            InstructionSignSlot slot = instructionSignSlots[i];
            if (!slot.cached || slot.transform == null)
            {
                continue;
            }

            if (!slot.transform.gameObject.activeSelf)
            {
                slot.transform.gameObject.SetActive(true);
            }

            slot.buriedY = slot.restPosition.y - Mathf.Abs(instructionSignBuryDepth);
            if (Mathf.Abs(slot.transform.position.y - slot.buriedY) <= 0.02f)
            {
                Vector3 p = slot.transform.position;
                p.y = slot.buriedY;
                slot.transform.position = p;
                slot.motion = SignMotion.Buried;
            }
            else
            {
                slot.motion = SignMotion.Retreating;
            }

            instructionSignSlots[i] = slot;
        }
    }

    private void UpdateInstructionSigns()
    {
        if (instructionSignSlots == null || instructionSignSlots.Length == 0)
        {
            return;
        }

        float dt = Time.deltaTime;
        for (int i = 0; i < instructionSignSlots.Length; i++)
        {
            InstructionSignSlot slot = instructionSignSlots[i];
            if (!slot.cached || slot.transform == null)
            {
                continue;
            }

            if (slot.motion == SignMotion.Rising)
            {
                Vector3 p = slot.transform.position;
                float nextY = Mathf.MoveTowards(p.y, slot.restPosition.y, instructionSignRiseSpeed * dt);
                p.x = slot.restPosition.x;
                p.z = slot.restPosition.z;
                p.y = nextY;
                slot.transform.position = p;
                if (Mathf.Abs(nextY - slot.restPosition.y) <= 0.01f)
                {
                    p.y = slot.restPosition.y;
                    slot.transform.position = p;
                    slot.motion = SignMotion.Raised;
                }

                instructionSignSlots[i] = slot;
            }
            else if (slot.motion == SignMotion.Retreating)
            {
                Vector3 p = slot.transform.position;
                float buriedY = slot.restPosition.y - Mathf.Abs(instructionSignBuryDepth);
                float nextY = Mathf.MoveTowards(p.y, buriedY, instructionSignRetreatSpeed * dt);
                p.x = slot.restPosition.x;
                p.z = slot.restPosition.z;
                p.y = nextY;
                slot.transform.position = p;
                if (Mathf.Abs(nextY - buriedY) <= 0.01f)
                {
                    p.y = buriedY;
                    slot.transform.position = p;
                    slot.motion = SignMotion.Buried;
                }

                instructionSignSlots[i] = slot;
            }
        }
    }

    private void SetCreditsSignsVisible(bool visible)
    {
        CacheCreditsSignPoses();
        if (visible)
        {
            BeginRaiseCreditsSigns();
        }
        else
        {
            BeginSinkCreditsSigns();
        }
    }

    private void SnapCreditsSignsBuried()
    {
        CacheCreditsSignPoses();
        for (int i = 0; i < creditsSignSlots.Length; i++)
        {
            InstructionSignSlot slot = creditsSignSlots[i];
            if (!slot.cached || slot.transform == null)
            {
                continue;
            }

            if (!slot.transform.gameObject.activeSelf)
            {
                slot.transform.gameObject.SetActive(true);
            }

            Vector3 p = slot.restPosition;
            p.y = slot.restPosition.y - Mathf.Abs(instructionSignBuryDepth);
            slot.transform.position = p;
            slot.buriedY = p.y;
            slot.motion = SignMotion.Buried;
            creditsSignSlots[i] = slot;
        }
    }

    private void CacheCreditsSignPoses()
    {
        int count = creditsSigns != null ? creditsSigns.Length : 0;
        if (creditsSignSlots == null || creditsSignSlots.Length != count)
        {
            creditsSignSlots = new InstructionSignSlot[count];
        }

        for (int i = 0; i < count; i++)
        {
            GameObject sign = creditsSigns[i];
            if (sign == null)
            {
                creditsSignSlots[i] = default;
                continue;
            }

            InstructionSignSlot slot = creditsSignSlots[i];
            if (slot.cached && slot.transform == sign.transform)
            {
                slot.buriedY = slot.restPosition.y - Mathf.Abs(instructionSignBuryDepth);
                creditsSignSlots[i] = slot;
                continue;
            }

            Transform t = sign.transform;
            Vector3 rest = t.position;
            creditsSignSlots[i] = new InstructionSignSlot
            {
                transform = t,
                restPosition = rest,
                buriedY = rest.y - Mathf.Abs(instructionSignBuryDepth),
                motion = SignMotion.Raised,
                cached = true
            };
        }
    }

    private void BeginRaiseCreditsSigns()
    {
        CacheCreditsSignPoses();
        for (int i = 0; i < creditsSignSlots.Length; i++)
        {
            InstructionSignSlot slot = creditsSignSlots[i];
            if (!slot.cached || slot.transform == null)
            {
                continue;
            }

            if (!slot.transform.gameObject.activeSelf)
            {
                slot.transform.gameObject.SetActive(true);
            }

            if (Mathf.Abs(slot.transform.position.y - slot.restPosition.y) <= 0.02f
                && slot.motion != SignMotion.Retreating)
            {
                slot.transform.position = slot.restPosition;
                slot.motion = SignMotion.Raised;
            }
            else
            {
                slot.motion = SignMotion.Rising;
            }

            creditsSignSlots[i] = slot;
        }
    }

    private void BeginSinkCreditsSigns()
    {
        CacheCreditsSignPoses();
        for (int i = 0; i < creditsSignSlots.Length; i++)
        {
            InstructionSignSlot slot = creditsSignSlots[i];
            if (!slot.cached || slot.transform == null)
            {
                continue;
            }

            if (!slot.transform.gameObject.activeSelf)
            {
                slot.transform.gameObject.SetActive(true);
            }

            slot.buriedY = slot.restPosition.y - Mathf.Abs(instructionSignBuryDepth);
            if (Mathf.Abs(slot.transform.position.y - slot.buriedY) <= 0.02f)
            {
                Vector3 p = slot.transform.position;
                p.y = slot.buriedY;
                slot.transform.position = p;
                slot.motion = SignMotion.Buried;
            }
            else
            {
                slot.motion = SignMotion.Retreating;
            }

            creditsSignSlots[i] = slot;
        }
    }

    private void UpdateCreditsSigns()
    {
        if (creditsSignSlots == null || creditsSignSlots.Length == 0)
        {
            return;
        }

        float dt = Time.deltaTime;
        for (int i = 0; i < creditsSignSlots.Length; i++)
        {
            InstructionSignSlot slot = creditsSignSlots[i];
            if (!slot.cached || slot.transform == null)
            {
                continue;
            }

            if (slot.motion == SignMotion.Rising)
            {
                Vector3 p = slot.transform.position;
                float nextY = Mathf.MoveTowards(p.y, slot.restPosition.y, instructionSignRiseSpeed * dt);
                p.x = slot.restPosition.x;
                p.z = slot.restPosition.z;
                p.y = nextY;
                slot.transform.position = p;
                if (Mathf.Abs(nextY - slot.restPosition.y) <= 0.01f)
                {
                    p.y = slot.restPosition.y;
                    slot.transform.position = p;
                    slot.motion = SignMotion.Raised;
                }

                creditsSignSlots[i] = slot;
            }
            else if (slot.motion == SignMotion.Retreating)
            {
                Vector3 p = slot.transform.position;
                float buriedY = slot.restPosition.y - Mathf.Abs(instructionSignBuryDepth);
                float nextY = Mathf.MoveTowards(p.y, buriedY, instructionSignRetreatSpeed * dt);
                p.x = slot.restPosition.x;
                p.z = slot.restPosition.z;
                p.y = nextY;
                slot.transform.position = p;
                if (Mathf.Abs(nextY - buriedY) <= 0.01f)
                {
                    p.y = buriedY;
                    slot.transform.position = p;
                    slot.motion = SignMotion.Buried;
                }

                creditsSignSlots[i] = slot;
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

        if (targetPractice == null)
        {
            targetPractice = FindObjectOfType<CrystalTargetPractice>();
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

#if UNITY_EDITOR
    /// <summary>
    /// Upgrades a single legacy ground bow into No Scope + With Scope choices with labels.
    /// </summary>
    [ContextMenu("Ensure Dual Ground Bows With Labels")]
    public void EnsureDualGroundBowsWithLabels()
    {
        EnsureGroundBowsMigrated();
        EnsureDifficultyQuiversMigrated();

        GroundBowChoice source = null;
        if (groundBows != null)
        {
            for (int i = 0; i < groundBows.Length; i++)
            {
                if (groundBows[i] != null && groundBows[i].groundVisual != null)
                {
                    source = groundBows[i];
                    break;
                }
            }
        }

        if (source == null && groundBowVisual != null)
        {
            source = new GroundBowChoice
            {
                withScope = false,
                groundVisual = groundBowVisual,
                groundAnchor = groundBowAnchor,
                label = null
            };
        }

        if (source == null || source.groundVisual == null)
        {
            Debug.LogWarning(
                "DragonFightEquipStart: no ground bow visual to duplicate.",
                this);
            return;
        }

        bool hasNoScope = IndexOfBow(false) >= 0;
        bool hasWithScope = IndexOfBow(true) >= 0;

        if (hasNoScope && hasWithScope)
        {
            EnsureBowLabelsExist();
            EnsureDifficultyLabelsExist();
            groundPosesCached = false;
            bowLabelPoseCache = System.Array.Empty<LabelPoseCache>();
            CacheGroundPoses();
            UnityEditor.EditorUtility.SetDirty(this);
            Debug.Log("DragonFightEquipStart: dual bows present — ensured labels.", this);
            return;
        }

        Transform parent = transform;
        Vector3 basePos = source.groundAnchor != null
            ? source.groundAnchor.position
            : source.groundVisual.transform.position;
        Quaternion baseRot = source.groundVisual.transform.rotation;
        Vector3 right = source.groundAnchor != null
            ? source.groundAnchor.right
            : source.groundVisual.transform.right;

        GroundBowChoice noScope = hasNoScope
            ? groundBows[IndexOfBow(false)]
            : BuildBowChoice(
                source,
                withScope: false,
                "GroundBow_NoScope",
                basePos - right * 0.55f,
                baseRot,
                parent);

        GroundBowChoice withScopeChoice = hasWithScope
            ? groundBows[IndexOfBow(true)]
            : BuildBowChoice(
                source,
                withScope: true,
                "GroundBow_WithScope",
                basePos + right * 0.55f,
                baseRot,
                parent);

        if (withScopeChoice.groundVisual != null
            && withScopeChoice.groundVisual.transform.Find("GroundScopeVisual") == null)
        {
            AttachScopeIndicatorEditor(withScopeChoice.groundVisual.transform);
        }

        // Hide / leave the old single center bow if it was neither of the two slots.
        if (source.groundVisual != null
            && source.groundVisual != noScope.groundVisual
            && source.groundVisual != withScopeChoice.groundVisual)
        {
            source.groundVisual.SetActive(false);
            if (source.label != null)
            {
                source.label.SetActive(false);
            }
        }

        groundBows = new[] { noScope, withScopeChoice };
        groundBowVisual = noScope.groundVisual;
        groundBowAnchor = noScope.groundAnchor;

        EnsureBowLabelsExist();
        EnsureDifficultyLabelsExist();

        groundPosesCached = false;
        bowLabelPoseCache = System.Array.Empty<LabelPoseCache>();
        CacheGroundPoses();
        UnityEditor.EditorUtility.SetDirty(this);
        Debug.Log(
            "DragonFightEquipStart: ensured No Scope / With Scope ground bows with labels.",
            this);
    }

    private GroundBowChoice BuildBowChoice(
        GroundBowChoice template,
        bool withScope,
        string name,
        Vector3 position,
        Quaternion rotation,
        Transform parent)
    {
        GameObject anchorGo = new GameObject(name + "Anchor");
        anchorGo.transform.SetParent(parent, true);
        anchorGo.transform.SetPositionAndRotation(position, rotation);

        GameObject visual;
        if (!withScope
            && template.groundVisual != null
            && IndexOfBow(false) < 0
            && IndexOfBow(true) < 0)
        {
            // Reuse the existing single bow as the no-scope choice.
            visual = template.groundVisual;
            visual.name = name;
            visual.transform.SetPositionAndRotation(position, rotation);
        }
        else
        {
            visual = UnityEngine.Object.Instantiate(template.groundVisual);
            visual.name = name;
            visual.transform.SetParent(parent, true);
            visual.transform.SetPositionAndRotation(position, rotation);
            visual.SetActive(true);
        }

        return new GroundBowChoice
        {
            withScope = withScope,
            groundVisual = visual,
            groundAnchor = anchorGo.transform,
            label = null
        };
    }

    private void EnsureBowLabelsExist()
    {
        if (groundBows == null)
        {
            return;
        }

        for (int i = 0; i < groundBows.Length; i++)
        {
            if (groundBows[i] == null || groundBows[i].groundVisual == null)
            {
                continue;
            }

            if (groundBows[i].label != null)
            {
                TextMesh existing = groundBows[i].label.GetComponent<TextMesh>();
                if (existing != null)
                {
                    existing.text = groundBows[i].withScope ? "With Scope" : "No Scope";
                }

                continue;
            }

            Vector3 pos = groundBows[i].groundAnchor != null
                ? groundBows[i].groundAnchor.position
                : groundBows[i].groundVisual.transform.position;
            groundBows[i].label = CreateWorldTextLabelEditor(
                groundBows[i].withScope ? "With Scope" : "No Scope",
                pos + Vector3.up * 0.55f,
                transform,
                groundBows[i].withScope ? "GroundBow_WithScopeLabel" : "GroundBow_NoScopeLabel");
        }
    }

    private void EnsureDifficultyLabelsExist()
    {
        if (difficultyQuivers == null)
        {
            return;
        }

        for (int i = 0; i < difficultyQuivers.Length; i++)
        {
            if (difficultyQuivers[i] == null || difficultyQuivers[i].groundVisual == null)
            {
                continue;
            }

            if (difficultyQuivers[i].label != null)
            {
                continue;
            }

            Vector3 pos = difficultyQuivers[i].groundAnchor != null
                ? difficultyQuivers[i].groundAnchor.position
                : difficultyQuivers[i].groundVisual.transform.position;
            string text = difficultyQuivers[i].difficulty.ToString();
            difficultyQuivers[i].label = CreateWorldTextLabelEditor(
                text,
                pos + Vector3.up * 0.55f,
                transform,
                "GroundQuiver_" + text + "Label");
        }
    }

    private static GameObject CreateWorldTextLabelEditor(
        string text,
        Vector3 worldPosition,
        Transform parent,
        string objectName)
    {
        GameObject labelGo = new GameObject(objectName);
        labelGo.transform.SetParent(parent, true);
        labelGo.transform.position = worldPosition;
        labelGo.transform.rotation = Quaternion.identity;

        TextMesh textMesh = labelGo.AddComponent<TextMesh>();
        textMesh.text = text;
        textMesh.anchor = TextAnchor.MiddleCenter;
        textMesh.alignment = TextAlignment.Center;
        textMesh.characterSize = 0.06f;
        textMesh.fontSize = 48;
        textMesh.color = Color.white;
        return labelGo;
    }

    private static void AttachScopeIndicatorEditor(Transform bowRoot)
    {
        if (bowRoot == null || bowRoot.Find("GroundScopeVisual") != null)
        {
            return;
        }

        ScopePickup scope = FindObjectOfType<ScopePickup>();
        GameObject equipped = null;
        if (scope != null)
        {
            UnityEditor.SerializedObject so = new UnityEditor.SerializedObject(scope);
            UnityEditor.SerializedProperty prop = so.FindProperty("equippedVisual");
            if (prop != null)
            {
                equipped = prop.objectReferenceValue as GameObject;
            }
        }

        if (equipped != null)
        {
            GameObject clone = UnityEngine.Object.Instantiate(equipped, bowRoot);
            clone.name = "GroundScopeVisual";
            clone.SetActive(true);
            return;
        }

        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        marker.name = "GroundScopeVisual";
        marker.transform.SetParent(bowRoot, false);
        marker.transform.localPosition = new Vector3(0f, 0.12f, 0.05f);
        marker.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        marker.transform.localScale = new Vector3(0.04f, 0.12f, 0.04f);
        DestroyImmediate(marker.GetComponent<Collider>());
        MeshRenderer mr = marker.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            mr.sharedMaterial = new Material(Shader.Find("Standard"))
            {
                color = new Color(0.25f, 0.85f, 0.35f, 1f)
            };
        }
    }
#endif
}
