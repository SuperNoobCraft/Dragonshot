using UnityEngine;
using Votanic.vXR.vCast;

/// <summary>
/// World-space config label: click / wand-trigger to toggle arrow supply mode
/// (Always Ready on hand vs pick up from barrel).
/// Setup: create empty GameObject → Add Component ArrowSupplyConfigLabel
/// (or right-click BowController → Create Arrow Supply Config Label).
/// </summary>
[RequireComponent(typeof(Collider))]
public class ArrowSupplyConfigLabel : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private BowController bow;

    [Header("Display")]
    [SerializeField] private TextMesh label;
    [SerializeField] private float characterSize = 0.06f;
    [SerializeField] private Color textColor = Color.white;

    [Header("Interact")]
    [SerializeField] private float wandRayDistance = 8f;
    [SerializeField, Range(0.01f, 0.5f)] private float axisDeadzone = 0.08f;
    [SerializeField] private int maxControllersToScan = 4;
    [SerializeField] private int maxAxesToScan = 16;
    [SerializeField] private int maxButtonsToScan = 16;

    private bool wasTriggerHeld;

    public void AssignBowAndLabel(BowController bowController, TextMesh textMesh)
    {
        bow = bowController;
        label = textMesh;
        EnsureLabel();
        EnsureInteractable();
        RefreshLabel();
    }

    private void Awake()
    {
        if (bow == null)
        {
            bow = FindObjectOfType<BowController>();
        }

        EnsureLabel();
        EnsureInteractable();
        RefreshLabel();
    }

    private void Update()
    {
        if (bow == null)
        {
            bow = FindObjectOfType<BowController>();
            if (bow == null)
            {
                return;
            }
        }

        if (PlayEnvironment.IsDesktopInput)
        {
            if (Input.GetMouseButtonDown(0) && IsCursorOverPanel())
            {
                Toggle();
            }
        }
        else
        {
            bool held = IsTriggerHeld();
            bool pressed = held && !wasTriggerHeld;
            wasTriggerHeld = held;
            if (pressed && IsWandTargetingPanel())
            {
                Toggle();
            }
        }
    }

    private void Toggle()
    {
        if (bow == null)
        {
            return;
        }

        bow.ToggleSupplyMode();
        RefreshLabel();
    }

    private void RefreshLabel()
    {
        EnsureLabel();
        if (label == null)
        {
            return;
        }

        if (bow == null)
        {
            label.text = "Arrow Mode\n(no bow)";
            return;
        }

        if (bow.IsAlwaysReadyMode)
        {
            label.text = "Arrow Mode\nAlways Ready";
        }
        else
        {
            label.text = "Arrow Mode\nFrom Barrel";
        }
    }

    /// <summary>
    /// Rebuilds label content if the panel was created empty.
    /// </summary>
    [ContextMenu("Refresh Label")]
    public void CreateConfigPanelHere()
    {
        EnsureLabel();
        RefreshLabel();
    }

    private void EnsureLabel()
    {
        if (label != null)
        {
            label.color = textColor;
            label.characterSize = characterSize;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            return;
        }

        label = GetComponentInChildren<TextMesh>();
        if (label != null)
        {
            label.color = textColor;
            label.characterSize = characterSize;
            return;
        }

        GameObject textGo = new GameObject("Label");
        textGo.transform.SetParent(transform, false);
        textGo.transform.localPosition = new Vector3(0f, 0f, -0.01f);
        label = textGo.AddComponent<TextMesh>();
        label.anchor = TextAnchor.MiddleCenter;
        label.alignment = TextAlignment.Center;
        label.characterSize = characterSize;
        label.fontSize = 48;
        label.color = textColor;
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

    private bool IsCursorOverPanel()
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

        return hit.transform == transform || hit.transform.IsChildOf(transform);
    }

    private bool IsWandTargetingPanel()
    {
        try
        {
            if (vCast.controller != null)
            {
                if (IsOurs(vCast.controller.selectedObject) || IsOurs(vCast.controller.triggeredObject))
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
            return hit.transform == transform || hit.transform.IsChildOf(transform);
        }

        return false;
    }

    private bool IsOurs(object obj)
    {
        if (obj == null)
        {
            return false;
        }

        Component c = obj as Component;
        if (c != null)
        {
            return c.transform == transform || c.transform.IsChildOf(transform);
        }

        GameObject go = obj as GameObject;
        return go != null && (go.transform == transform || go.transform.IsChildOf(transform));
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

        return null;
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
