using UnityEngine;
using Votanic.vXR.vCast;

/// <summary>
/// Pre-fight tutorial: pick up ground bow with left hand, ground quiver with right hand,
/// reach behind the back to mount the quiver, then auto-start the dragon fight.
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

    [Header("References")]
    [SerializeField] private DragonBoss dragon;
    [Tooltip("Playable bow (BowController) — usually under Frame. Disabled until picked up.")]
    [SerializeField] private BowController bow;
    [SerializeField] private LeftHandChild leftHandChild;
    [Tooltip("Ground prop only — hidden once the real bow is equipped.")]
    [SerializeField] private GameObject groundBowVisual;
    [Tooltip("Ground quiver prop — hidden when the held quiver is picked up.")]
    [SerializeField] private GameObject groundQuiverVisual;
    [Tooltip("Quiver mesh that follows the right hand, then mounts on the back.")]
    [SerializeField] private Transform heldQuiverVisual;
    [Tooltip("Legacy single reference: used as ground and/or held quiver if the fields above are empty.")]
    [SerializeField] private Transform groundQuiver;
    [SerializeField] private DragonFightUI fightUI;
    [Tooltip("World-space instruction signs hidden once the fight begins.")]
    [SerializeField] private GameObject[] instructionSigns;

    [Header("Ground Poses")]
    [Tooltip("Optional pickup position for the ground bow. Rotation always comes from Ground Bow Visual.")]
    [SerializeField] private Transform groundBowAnchor;
    [Tooltip("Optional pickup position for the ground quiver. Rotation always comes from Ground Quiver Visual.")]
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

    [Header("Idle Float Spin")]
    [Tooltip("Slowly rotate pickup props while they are waiting to be picked up.")]
    [SerializeField] private bool idlePropSpin = true;
    [SerializeField] private float idleSpinDegreesPerSecond = 36f;
    [Tooltip("World-space spin axis (default = vertical).")]
    [SerializeField] private Vector3 idleSpinAxis = Vector3.up;

    private EquipPhase phase = EquipPhase.NeedBow;
    private EquipPhase lastInstructionPhase = (EquipPhase)(-1);
    private float dwell;
    private Vector3 bowGroundPosition;
    private Quaternion bowGroundRotation;
    private Transform bowGroundParent;
    private Vector3 quiverGroundPosition;
    private Quaternion quiverGroundRotation;
    private Transform quiverGroundParent;
    private bool startedFight;
    private bool quiverHeld;

    public bool IsBowEquipped => phase != EquipPhase.NeedBow;
    public bool IsQuiverMounted => phase == EquipPhase.Complete;

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
        ResolveLegacyQuiverRefs();
    }

    private void Awake()
    {
        ResolveReferences();
        ResolveLegacyQuiverRefs();
        CacheGroundPoses();
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

        Vector3 axis = idleSpinAxis.sqrMagnitude > 0.0001f ? idleSpinAxis.normalized : Vector3.up;
        Quaternion spin = Quaternion.AngleAxis(idleSpinDegreesPerSecond * Time.deltaTime, axis);

        if (phase == EquipPhase.NeedBow
            && groundBowVisual != null
            && groundBowVisual.activeSelf)
        {
            groundBowVisual.transform.rotation = spin * groundBowVisual.transform.rotation;
        }

        if (!quiverHeld && phase != EquipPhase.NeedQuiverBack)
        {
            GameObject quiverProp = GetGroundQuiverObject();
            if (quiverProp != null && quiverProp.activeSelf)
            {
                quiverProp.transform.rotation = spin * quiverProp.transform.rotation;
            }
        }
    }

    private void LateUpdate()
    {
        PlayEnvironment.SuppressWandRay();

        if (quiverHeld && phase == EquipPhase.NeedQuiverBack)
        {
            SyncHeldQuiverToHand();
        }
    }

    public void ResetForWaiting()
    {
        ResolveReferences();
        ResolveLegacyQuiverRefs();
        CacheGroundPoses();
        startedFight = false;
        quiverHeld = false;
        dwell = 0f;
        phase = EquipPhase.NeedBow;
        lastInstructionPhase = (EquipPhase)(-1);

        SetPlayableBowEquipped(false);
        PlaceGroundBowVisual();
        PlaceGroundQuiverVisual();
        HideHeldQuiver();
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
                if (IsNearRightHandPickup(GetQuiverPickupPoint()))
                {
                    dwell += Time.deltaTime;
                    if (dwell >= mountDwellSeconds)
                    {
                        PickupQuiverInRightHand();
                    }
                }
                else
                {
                    dwell = 0f;
                }

                break;

            case EquipPhase.NeedQuiverBack:
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
        }
        else if (phase == EquipPhase.NeedQuiverPickup && Input.GetKeyDown(desktopPickupQuiverKey))
        {
            PickupQuiverInRightHand();
        }
        else if (phase == EquipPhase.NeedQuiverBack && Input.GetKeyDown(desktopMountQuiverKey))
        {
            MountQuiverOnBack();
        }
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
        RefreshInstructionsIfNeeded();
    }

    private void PickupQuiverInRightHand()
    {
        Transform mobile = GetHeldQuiverTransform();
        if (mobile == null)
        {
            Debug.LogWarning("DragonFightEquipStart: no held quiver visual assigned.", this);
            return;
        }

        SetGroundQuiverVisualVisible(false);
        mobile.gameObject.SetActive(true);
        quiverHeld = true;

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

        dwell = 0f;
        phase = EquipPhase.NeedQuiverBack;
        lastInstructionPhase = (EquipPhase)(-1);
        RefreshInstructionsIfNeeded();
    }

    private void MountQuiverOnBack()
    {
        Transform mobile = GetHeldQuiverTransform();
        if (mobile == null)
        {
            return;
        }

        Transform back = PlayEnvironment.ResolveHeadTransform();
        if (back == null)
        {
            back = ResolveQuiverHandAttach();
        }

        if (back != null)
        {
            mobile.SetParent(back, false);
            mobile.localPosition = quiverBackLocalPosition;
            mobile.localRotation = Quaternion.Euler(quiverBackLocalEuler);
        }

        quiverHeld = false;
        bow.SetQuiverMountedOnBack(true);
        dwell = 0f;
        phase = EquipPhase.Complete;
        SetInstructionSignsVisible(false);
        TryStartFight();
    }

    private void SyncHeldQuiverToHand()
    {
        Transform mobile = GetHeldQuiverTransform();
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

        bow.gameObject.SetActive(equipped);
        bow.SetBowGrounded(!equipped);

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

    private void PlaceGroundQuiverVisual()
    {
        GameObject groundProp = GetGroundQuiverObject();
        if (groundProp == null)
        {
            return;
        }

        groundProp.transform.SetPositionAndRotation(quiverGroundPosition, quiverGroundRotation);
        groundProp.transform.SetParent(quiverGroundParent, true);
        groundProp.SetActive(true);
    }

    private void HideHeldQuiver()
    {
        Transform held = GetHeldQuiverTransform();
        if (held == null)
        {
            return;
        }

        if (GetGroundQuiverObject() != null && held.gameObject == GetGroundQuiverObject())
        {
            return;
        }

        held.gameObject.SetActive(false);
    }

    private void SetGroundQuiverVisualVisible(bool visible)
    {
        GameObject groundProp = GetGroundQuiverObject();
        if (groundProp == null)
        {
            return;
        }

        groundProp.SetActive(visible);
    }

    private GameObject GetGroundQuiverObject()
    {
        if (groundQuiverVisual != null)
        {
            return groundQuiverVisual;
        }

        if (groundQuiver != null && (heldQuiverVisual == null || heldQuiverVisual != groundQuiver))
        {
            return groundQuiver.gameObject;
        }

        return null;
    }

    private Transform GetHeldQuiverTransform()
    {
        if (heldQuiverVisual != null)
        {
            return heldQuiverVisual;
        }

        if (groundQuiver != null)
        {
            return groundQuiver;
        }

        if (groundQuiverVisual != null)
        {
            return groundQuiverVisual.transform;
        }

        return null;
    }

    private void ResolveLegacyQuiverRefs()
    {
        if (groundQuiverVisual == null && groundQuiver != null
            && (heldQuiverVisual == null || heldQuiverVisual != groundQuiver))
        {
            groundQuiverVisual = groundQuiver.gameObject;
        }
    }

    private void CacheGroundPoses()
    {
        if (groundBowVisual != null)
        {
            bowGroundPosition = groundBowAnchor != null
                ? groundBowAnchor.position
                : groundBowVisual.transform.position;
            bowGroundRotation = groundBowVisual.transform.rotation;
            bowGroundParent = groundBowAnchor != null
                ? groundBowAnchor.parent
                : groundBowVisual.transform.parent;
        }

        GameObject groundProp = GetGroundQuiverObject();
        if (groundProp != null)
        {
            quiverGroundPosition = groundQuiverAnchor != null
                ? groundQuiverAnchor.position
                : groundProp.transform.position;
            quiverGroundRotation = groundProp.transform.rotation;
            quiverGroundParent = groundQuiverAnchor != null
                ? groundQuiverAnchor.parent
                : groundProp.transform.parent;
        }
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

    private Vector3 GetQuiverPickupPoint()
    {
        GameObject groundProp = GetGroundQuiverObject();
        if (groundProp != null)
        {
            return groundProp.transform.position;
        }

        if (groundQuiverAnchor != null)
        {
            return groundQuiverAnchor.position;
        }

        return transform.position;
    }

    private bool IsNearRightHandPickup(Vector3 point)
    {
        Transform hand = PlayEnvironment.ResolveRightHandTransform();
        if (hand != null && Vector3.Distance(hand.position, point) <= pickupDistance)
        {
            return true;
        }

        if (IsHandNear(rightHandName, point, pickupDistance))
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
