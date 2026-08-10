using UnityEngine;

/// <summary>
/// World pickup that unlocks the bow's green dotted shot-trajectory preview while aiming.
/// Ground scope appears only after the bow is equipped; pick up with the bow hand (Hand2 /
/// LeftHandChild). Scene-assigned equipped visuals keep their local pose; this script only
/// toggles active state.
/// </summary>
public class ScopePickup : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private BowController bow;
    [SerializeField] private LeftHandChild bowHand;
    [Tooltip("Floating / ground mesh. Hidden until the bow is picked up. Prefer a CHILD of this object — "
             + "never disable the GameObject that has ScopePickup.")]
    [SerializeField] private GameObject groundVisual;
    [Tooltip("Scope mesh already parented under the bow. Kept inactive until this pickup; pose is not overwritten.")]
    [SerializeField] private GameObject equippedVisual;

    [Header("Pickup")]
    [SerializeField] private float pickupDistance = 0.55f;
    [SerializeField] private float mountDwellSeconds = 0.18f;
    [Tooltip("Bow hand (holds the bow).")]
    [SerializeField] private string bowHandName = "Hand2";
    [SerializeField] private bool desktopProximityPickup = true;
    [SerializeField] private KeyCode desktopPickupKey = KeyCode.G;

    [Header("Idle Float Spin")]
    [SerializeField] private bool idlePropSpin = true;
    [SerializeField] private float idleSpinDegreesPerSecond = 48f;
    [SerializeField] private Vector3 idleSpinAxis = Vector3.up;

    private float dwell;
    private bool pickedUp;
    private bool groundAvailable;
    private Vector3 groundPosition;
    private Quaternion groundRotation;
    private bool hasCachedGroundPose;
    private Renderer[] groundRenderers;
    private Collider[] groundColliders;

    public bool IsPickedUp => pickedUp;
    public bool IsGroundAvailable => groundAvailable;

    public void Assign(BowController bowController, GameObject ground, GameObject equipped = null)
    {
        bow = bowController;
        groundVisual = ground;
        if (equipped != null)
        {
            equippedVisual = equipped;
        }
    }

    private void Awake()
    {
        ResolveRefs();
        CacheGroundPose();
        CacheGroundComponents();
        // Never start "already equipped" — hide bow-child scope until a real pickup.
        SetEquippedVisualActive(false);
        if (bow != null)
        {
            bow.SetScopeEquipped(false);
        }

        SetGroundVisible(false);
        groundAvailable = false;
    }

    private void Start()
    {
        ResolveRefs();
        SetEquippedVisualActive(false);
        if (!pickedUp && bow != null)
        {
            bow.SetScopeEquipped(false);
        }

        // Keep this component alive even if a mistaken setup pointed groundVisual at this GO.
        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }
    }

    private void Update()
    {
        if (pickedUp || !groundAvailable)
        {
            return;
        }

        if (idlePropSpin && IsGroundVisuallyActive())
        {
            Transform spinTarget = groundVisual != null ? groundVisual.transform : transform;
            Vector3 axis = idleSpinAxis.sqrMagnitude > 0.0001f ? idleSpinAxis.normalized : Vector3.up;
            spinTarget.rotation =
                Quaternion.AngleAxis(idleSpinDegreesPerSecond * Time.deltaTime, axis)
                * spinTarget.rotation;
        }

        if (PlayEnvironment.IsDesktopInput && Input.GetKeyDown(desktopPickupKey))
        {
            Pickup();
            return;
        }

        if (IsBowHandNearPickup())
        {
            dwell += Time.deltaTime;
            if (dwell >= mountDwellSeconds)
            {
                Pickup();
            }
        }
        else
        {
            dwell = 0f;
        }
    }

    /// <summary>Show the floating scope after the bow has been picked up.</summary>
    public void ShowAfterBowEquipped()
    {
        if (pickedUp)
        {
            return;
        }

        // Ensure this script can keep running.
        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }

        enabled = true;
        groundAvailable = true;
        dwell = 0f;
        RestoreGroundPose();
        SetGroundVisible(true);
        SetEquippedVisualActive(false);
        if (bow != null)
        {
            bow.SetScopeEquipped(false);
        }
    }

    /// <summary>Hide the ground scope if the player moved on without taking it (e.g. quiver equipped).</summary>
    public void ExpireIfNotPickedUp()
    {
        if (pickedUp)
        {
            return;
        }

        groundAvailable = false;
        dwell = 0f;
        SetGroundVisible(false);
    }

    /// <summary>Fight reset: clear equip, hide everything until the bow is picked up again.</summary>
    public void ResetPickup()
    {
        pickedUp = false;
        groundAvailable = false;
        dwell = 0f;

        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }

        enabled = true;

        if (bow != null)
        {
            bow.SetScopeEquipped(false);
        }

        SetEquippedVisualActive(false);
        RestoreGroundPose();
        SetGroundVisible(false);
    }

    private void Pickup()
    {
        if (pickedUp || !groundAvailable)
        {
            return;
        }

        ResolveRefs();
        if (bow == null)
        {
            Debug.LogWarning("ScopePickup: no BowController found.", this);
            return;
        }

        pickedUp = true;
        groundAvailable = false;
        dwell = 0f;
        bow.SetScopeEquipped(true);

        SetGroundVisible(false);
        // Respect scene pose on the bow child — only enable it.
        SetEquippedVisualActive(true);
        FightAudio.PlayEquipScope(transform.position);
    }

    private void ResolveRefs()
    {
        if (bow == null)
        {
#if UNITY_2023_1_OR_NEWER
            bow = FindFirstObjectByType<BowController>(FindObjectsInactive.Include);
#else
            bow = FindObjectOfType<BowController>(true);
#endif
        }

        if (bowHand == null && bow != null)
        {
            bowHand = bow.GetComponent<LeftHandChild>();
        }

        if (groundVisual == null)
        {
            Transform child = transform.Find("ScopeGroundVisual");
            if (child == null && transform.childCount > 0)
            {
                for (int i = 0; i < transform.childCount; i++)
                {
                    Transform c = transform.GetChild(i);
                    if (equippedVisual != null && c.gameObject == equippedVisual)
                    {
                        continue;
                    }

                    child = c;
                    break;
                }
            }

            if (child != null)
            {
                groundVisual = child.gameObject;
            }
        }
    }

    private void CacheGroundPose()
    {
        Transform t = groundVisual != null ? groundVisual.transform : transform;
        groundPosition = t.position;
        groundRotation = t.rotation;
        hasCachedGroundPose = true;
    }

    private void CacheGroundComponents()
    {
        if (groundVisual == null || groundVisual == gameObject)
        {
            groundRenderers = GetComponentsInChildren<Renderer>(true);
            groundColliders = GetComponentsInChildren<Collider>(true);
            return;
        }

        groundRenderers = groundVisual.GetComponentsInChildren<Renderer>(true);
        groundColliders = groundVisual.GetComponentsInChildren<Collider>(true);
    }

    private void RestoreGroundPose()
    {
        if (!hasCachedGroundPose)
        {
            return;
        }

        Transform t = groundVisual != null ? groundVisual.transform : transform;
        t.SetPositionAndRotation(groundPosition, groundRotation);
    }

    /// <summary>
    /// Never deactivate the ScopePickup host GameObject — that stops Update and breaks FindObjectsOfType.
    /// </summary>
    private void SetGroundVisible(bool visible)
    {
        if (groundVisual != null && groundVisual != gameObject)
        {
            groundVisual.SetActive(visible);
            return;
        }

        // Ground visual is missing or is this same object: toggle renderers/colliders only.
        if (groundRenderers == null)
        {
            CacheGroundComponents();
        }

        if (groundRenderers != null)
        {
            for (int i = 0; i < groundRenderers.Length; i++)
            {
                if (groundRenderers[i] != null)
                {
                    // Don't hide the equipped bow-child scope via this path.
                    if (equippedVisual != null
                        && groundRenderers[i].transform.IsChildOf(equippedVisual.transform))
                    {
                        continue;
                    }

                    groundRenderers[i].enabled = visible;
                }
            }
        }

        if (groundColliders != null)
        {
            for (int i = 0; i < groundColliders.Length; i++)
            {
                if (groundColliders[i] != null)
                {
                    groundColliders[i].enabled = visible;
                }
            }
        }
    }

    private bool IsGroundVisuallyActive()
    {
        if (groundVisual != null && groundVisual != gameObject)
        {
            return groundVisual.activeSelf;
        }

        if (groundRenderers != null)
        {
            for (int i = 0; i < groundRenderers.Length; i++)
            {
                if (groundRenderers[i] != null && groundRenderers[i].enabled)
                {
                    return true;
                }
            }
        }

        return groundAvailable;
    }

    private void SetEquippedVisualActive(bool active)
    {
        if (equippedVisual != null)
        {
            equippedVisual.SetActive(active);
        }
    }

    private bool IsBowHandNearPickup()
    {
        Vector3 point = groundVisual != null ? groundVisual.transform.position : transform.position;

        Transform hand = ResolveBowHandTransform();
        if (hand != null && hand.gameObject.activeInHierarchy
            && Vector3.Distance(hand.position, point) <= pickupDistance)
        {
            return true;
        }

        if (!PlayEnvironment.IsDesktopInput || !desktopProximityPickup)
        {
            return false;
        }

        Camera cam = PlayEnvironment.ResolveViewCamera();
        return cam != null && Vector3.Distance(cam.transform.position, point) <= pickupDistance * 1.4f;
    }

    /// <summary>Hand that holds the bow — Hand2 / LeftHandChild (not Hand1 / Controller).</summary>
    private Transform ResolveBowHandTransform()
    {
        if (bowHand == null && bow != null)
        {
            bowHand = bow.GetComponent<LeftHandChild>();
        }

        if (bowHand != null && bowHand.BoundHand != null)
        {
            return bowHand.BoundHand;
        }

        if (string.IsNullOrEmpty(bowHandName))
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
                && candidate.name == bowHandName
                && candidate.gameObject.scene.IsValid())
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Used by BowController when the playable bow leaves the ground.</summary>
    public static void NotifyBowEquipped()
    {
        // Equip tutorial shows the scope after quiver choice, not at bow pickup.
#if UNITY_2023_1_OR_NEWER
        DragonFightEquipStart equip = FindFirstObjectByType<DragonFightEquipStart>();
#else
        DragonFightEquipStart equip = FindObjectOfType<DragonFightEquipStart>();
#endif
        if (equip != null && equip.DefersScopeUntilQuiverChosen)
        {
            return;
        }

#if UNITY_2023_1_OR_NEWER
        ScopePickup[] all = FindObjectsByType<ScopePickup>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
        ScopePickup[] all = Resources.FindObjectsOfTypeAll<ScopePickup>();
#endif
        for (int i = 0; i < all.Length; i++)
        {
            ScopePickup scope = all[i];
            if (scope == null || !scope.gameObject.scene.IsValid())
            {
                continue;
            }

            scope.ShowAfterBowEquipped();
        }
    }

    public static void NotifyBowUnequipped()
    {
#if UNITY_2023_1_OR_NEWER
        ScopePickup[] all = FindObjectsByType<ScopePickup>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
        ScopePickup[] all = Resources.FindObjectsOfTypeAll<ScopePickup>();
#endif
        for (int i = 0; i < all.Length; i++)
        {
            ScopePickup scope = all[i];
            if (scope == null || !scope.gameObject.scene.IsValid())
            {
                continue;
            }

            scope.ResetPickup();
        }
    }
}
