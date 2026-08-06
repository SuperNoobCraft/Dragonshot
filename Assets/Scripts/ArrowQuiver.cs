using UnityEngine;
using Votanic.vXR.vCast;

/// <summary>
/// Quiver / barrel: give one arrow to the bow hand.
/// Desktop: E when near, or left-click the collider.
/// CAVE: wand point + trigger, or hand within range + trigger (no near-bow block).
/// </summary>
public class ArrowQuiver : MonoBehaviour
{
    [Header("Setup")]
    [SerializeField] private ArrowProjectile arrowPrefab;
    [SerializeField] private BowController bow;
    [SerializeField, Min(1)] private int capacity = 3;
    [Tooltip("Never run out — taking an arrow does not reduce remaining count.")]
    [SerializeField] private bool infiniteArrows;
    [Tooltip("Optional visuals in the quiver; disabled one-by-one as arrows are taken.")]
    [SerializeField] private GameObject[] quiverArrowVisuals;

    [Header("Interact")]
    [Tooltip("Hand / player distance to pick up (meters). Barrel is large — keep generous.")]
    [SerializeField] private float interactDistance = 1.5f;
    [SerializeField] private float wandRayDistance = 8f;
    [SerializeField] private KeyCode desktopPickupKey = KeyCode.E;
    [SerializeField] private bool desktopClickToPickup = true;
    [SerializeField, Range(0.01f, 0.5f)] private float axisDeadzone = 0.08f;
    [SerializeField] private int maxControllersToScan = 4;
    [SerializeField] private int maxAxesToScan = 16;
    [SerializeField] private int maxButtonsToScan = 16;
    [SerializeField] private bool logPickup = true;

    private int remaining;
    private bool wasTriggerHeld;
    private float nextFailLog;

    public int Remaining => remaining;
    public int Capacity => capacity;
    public bool InfiniteArrows
    {
        get => infiniteArrows;
        set
        {
            infiniteArrows = value;
            if (infiniteArrows)
            {
                remaining = capacity;
                RefreshVisuals();
            }
        }
    }

    private void Awake()
    {
        bow = ResolveSceneBow(bow);
        remaining = capacity;
        RefreshVisuals();
        EnsureInteractable();
    }

    private void Update()
    {
        bow = ResolveSceneBow(bow);

        if (arrowPrefab == null || bow == null)
        {
            wasTriggerHeld = IsTriggerHeld();
            return;
        }

        if (bow != null && bow.IsBackQuiverMode)
        {
            wasTriggerHeld = IsTriggerHeld();
            return;
        }

        if (PlayEnvironment.IsDesktopInput)
        {
            TryDesktopInteract();
        }
        else
        {
            TryTrackedInteract();
        }
    }

    /// <summary>
    /// Inspector often accidentally references the Project prefab asset.
    /// Parenting / pose must run on the scene instance only.
    /// </summary>
    private static BowController ResolveSceneBow(BowController current)
    {
        if (IsSceneObject(current))
        {
            return current;
        }

        if (current != null)
        {
            Debug.LogWarning(
                "ArrowQuiver: Bow reference was a Prefab asset ('"
                + current.name
                + "'). Rebinding to the scene BowController.",
                current);
        }

#if UNITY_2023_1_OR_NEWER
        BowController[] bows = FindObjectsByType<BowController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
        BowController[] bows = FindObjectsOfType<BowController>();
#endif
        for (int i = 0; i < bows.Length; i++)
        {
            if (IsSceneObject(bows[i]))
            {
                return bows[i];
            }
        }

        return null;
    }

    private static bool IsSceneObject(Component component)
    {
        return component != null
               && component.gameObject.scene.IsValid()
               && component.gameObject.scene.isLoaded;
    }

    private void EnsureInteractable()
    {
        vCast_Interactables interactable = GetComponent<vCast_Interactables>();
        if (interactable == null)
        {
            interactable = gameObject.AddComponent<vCast_Interactables>();
        }

        try
        {
            interactable.interactive = Votanic.vXR.Interactive.Functional;
        }
        catch
        {
        }
    }

    private void TryDesktopInteract()
    {
        bool key = Input.GetKeyDown(desktopPickupKey);
        bool click = desktopClickToPickup && Input.GetMouseButtonDown(0) && IsCursorOverQuiver();
        bool near = IsDesktopPlayerNear();

        if ((key && near) || click)
        {
            TryInteract();
        }
    }

    private void TryTrackedInteract()
    {
        bool held = IsTriggerHeld();
        bool pressed = held && !wasTriggerHeld;
        wasTriggerHeld = held;

        if (!pressed)
        {
            return;
        }

        bool wandHit = IsWandTargetingQuiver();
        bool handNear = IsAnyTrackedHandNear();

        if (!wandHit && !handNear)
        {
            LogFail("trigger pressed but wand not on barrel and hands too far.");
            return;
        }

        TryInteract();
    }

    /// <summary>
    /// Empty barrel → refill (unless infinite). Otherwise give one arrow if the hand is free.
    /// </summary>
    private void TryInteract()
    {
        if (!infiniteArrows && remaining <= 0)
        {
            Refill();
            return;
        }

        if (bow.HasArrowInHand)
        {
            return;
        }

        TryGiveArrow();
    }

    public bool TryGiveArrow()
    {
        bow = ResolveSceneBow(bow);

        if (arrowPrefab == null || bow == null || bow.HasArrowInHand)
        {
            if (bow == null)
            {
                LogFail("no scene BowController found (prefab-only reference?).");
            }

            return false;
        }

        if (!infiniteArrows && remaining <= 0)
        {
            return false;
        }

        ArrowProjectile instance = Instantiate(arrowPrefab);
        if (!IsSceneObject(instance))
        {
            Destroy(instance.gameObject);
            LogFail("Instantiate did not create a scene arrow.");
            return false;
        }

        if (!bow.EquipArrow(instance))
        {
            Destroy(instance.gameObject);
            LogFail("EquipArrow failed.");
            return false;
        }

        if (!infiniteArrows)
        {
            remaining--;
            RefreshVisuals();
        }

        if (logPickup)
        {
            string left = infiniteArrows ? "∞" : remaining + "/" + capacity;
            Debug.Log($"Quiver: picked up arrow ({left} left).", this);
        }

        return true;
    }

    public void Refill()
    {
        remaining = capacity;
        RefreshVisuals();

        if (logPickup)
        {
            Debug.Log($"Quiver: refilled ({remaining}/{capacity}).", this);
        }
    }

    private void RefreshVisuals()
    {
        if (quiverArrowVisuals == null)
        {
            return;
        }

        for (int i = 0; i < quiverArrowVisuals.Length; i++)
        {
            if (quiverArrowVisuals[i] != null)
            {
                quiverArrowVisuals[i].SetActive(i < remaining);
            }
        }
    }

    private bool IsDesktopPlayerNear()
    {
        Camera cam = PlayEnvironment.ResolveViewCamera();
        if (cam == null)
        {
            return true;
        }

        return Vector3.Distance(cam.transform.position, transform.position)
               <= Mathf.Max(interactDistance, 3f);
    }

    private bool IsCursorOverQuiver()
    {
        Camera cam = PlayEnvironment.ResolveViewCamera();
        if (cam == null)
        {
            return false;
        }

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, 20f))
        {
            return false;
        }

        return IsOurTransform(hit.transform);
    }

    private bool IsAnyTrackedHandNear()
    {
        Transform right = bow.RightHandTransform;
        if (right != null && Vector3.Distance(right.position, transform.position) <= interactDistance)
        {
            return true;
        }

        try
        {
            if (vCast.hand != null
                && Vector3.Distance(vCast.hand.transform.position, transform.position) <= interactDistance)
            {
                return true;
            }
        }
        catch
        {
        }

        Transform hand1 = FindNamed("Hand1");
        Transform hand2 = FindNamed("Hand2");
        if (hand1 != null && Vector3.Distance(hand1.position, transform.position) <= interactDistance)
        {
            return true;
        }

        if (hand2 != null && Vector3.Distance(hand2.position, transform.position) <= interactDistance)
        {
            return true;
        }

        return false;
    }

    private bool IsWandTargetingQuiver()
    {
        try
        {
            if (vCast.controller != null)
            {
                if (IsOurInteractable(vCast.controller.selectedObject)
                    || IsOurInteractable(vCast.controller.triggeredObject))
                {
                    return true;
                }
            }
        }
        catch
        {
        }

        Transform wand = ResolveWandTransform();
        if (wand == null)
        {
            return false;
        }

        if (Physics.Raycast(wand.position, wand.forward, out RaycastHit hit, wandRayDistance))
        {
            return IsOurTransform(hit.transform);
        }

        return false;
    }

    private bool IsOurInteractable(object obj)
    {
        if (obj == null)
        {
            return false;
        }

        Component component = obj as Component;
        if (component != null)
        {
            return IsOurTransform(component.transform);
        }

        GameObject go = obj as GameObject;
        return go != null && IsOurTransform(go.transform);
    }

    private bool IsOurTransform(Transform t)
    {
        return t != null && (t == transform || t.IsChildOf(transform));
    }

    private Transform ResolveWandTransform()
    {
        Transform vGear = PlayEnvironment.ResolveVGearTransform();
        if (vGear != null)
        {
            Transform byPath = FindChildPath(vGear, "Frame", "User", "Head", "Hand", "Controller", "Wand");
            if (byPath != null)
            {
                return byPath;
            }

            byPath = FindChildPath(vGear, "Frame", "User", "Hand", "Controller", "Wand");
            if (byPath != null)
            {
                return byPath;
            }
        }

        try
        {
            if (vCast.controller != null)
            {
                return vCast.controller.transform;
            }
        }
        catch
        {
        }

        return FindNamed("Wand");
    }

    private bool IsTriggerHeld()
    {
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
                try
                {
                    if (Mathf.Abs(vCast.Ctrl.AxisValue(a, c)) > axisDeadzone)
                    {
                        return true;
                    }
                }
                catch
                {
                }
            }

            int buttonCount = maxButtonsToScan;
            try
            {
                int reported = vCast.Ctrl.NumberOfButton(c);
                if (reported > 0)
                {
                    buttonCount = Mathf.Max(reported, maxButtonsToScan);
                }
            }
            catch
            {
            }

            for (int b = 0; b < buttonCount; b++)
            {
                try
                {
                    if (vCast.Ctrl.ButtonPress(b, c, false))
                    {
                        return true;
                    }
                }
                catch
                {
                }
            }
        }

        return false;
    }

    private void LogFail(string reason)
    {
        if (!logPickup || Time.unscaledTime < nextFailLog)
        {
            return;
        }

        nextFailLog = Time.unscaledTime + 1f;
        Debug.Log("Quiver: " + reason, this);
    }

    private static Transform FindNamed(string name)
    {
#if UNITY_2023_1_OR_NEWER
        Transform[] all = FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
        Transform[] all = FindObjectsOfType<Transform>();
#endif
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].name == name)
            {
                return all[i];
            }
        }

        return null;
    }

    private static Transform FindChildPath(Transform root, params string[] path)
    {
        Transform current = root;
        for (int i = 0; i < path.Length; i++)
        {
            if (current == null)
            {
                return null;
            }

            Transform next = null;
            for (int c = 0; c < current.childCount; c++)
            {
                Transform child = current.GetChild(c);
                if (string.Equals(child.name, path[i], System.StringComparison.OrdinalIgnoreCase))
                {
                    next = child;
                    break;
                }
            }

            current = next;
        }

        return current;
    }
}
