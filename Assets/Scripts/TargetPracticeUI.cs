using UnityEngine;
using Votanic.vXR.vCast;

/// <summary>
/// World-space panel for target practice: start → live timer → score / retry.
/// Click with mouse (desktop) or wand+trigger (CAVE), same pattern as the quiver.
/// </summary>
[RequireComponent(typeof(Collider))]
public class TargetPracticeUI : MonoBehaviour
{
    [Header("Display")]
    [SerializeField] private TextMesh label;
    [SerializeField] private float characterSize = 0.08f;
    [SerializeField] private Color textColor = Color.white;

    [Header("Interact")]
    [SerializeField] private float wandRayDistance = 8f;
    [SerializeField, Range(0.01f, 0.5f)] private float axisDeadzone = 0.08f;
    [SerializeField] private int maxControllersToScan = 4;
    [SerializeField] private int maxAxesToScan = 16;
    [SerializeField] private int maxButtonsToScan = 16;

    private TargetPracticeGame game;
    private bool wasTriggerHeld;
    private string cachedText = "Shoot the center\ntarget to start";

    /// <summary>Assign a TextMesh from the editor setup helper.</summary>
    public void SetLabel(TextMesh textMesh)
    {
        label = textMesh;
    }

    public void Bind(TargetPracticeGame owner)
    {
        game = owner;
        EnsureLabel();
        EnsureInteractable();
        ShowStart();
    }

    private void Update()
    {
        if (game == null)
        {
            return;
        }

        if (PlayEnvironment.IsDesktopInput)
        {
            if (Input.GetMouseButtonDown(0) && IsCursorOverPanel())
            {
                HandleClick();
            }
        }
        else
        {
            bool held = IsTriggerHeld();
            bool pressed = held && !wasTriggerHeld;
            wasTriggerHeld = held;
            if (pressed && IsWandTargetingPanel())
            {
                HandleClick();
            }
        }
    }

    public void ShowStart()
    {
        SetText("Shoot the center\ntarget to start");
    }

    public void ShowTimer(float secondsRemaining, int score)
    {
        int whole = Mathf.CeilToInt(Mathf.Max(0f, secondsRemaining));
        SetText("Time " + whole + "\nScore " + score);
    }

    public void ShowResults(int score, bool showRetryPrompt = true)
    {
        if (showRetryPrompt)
        {
            SetText("Score " + score + "\nShoot center target\nto try again");
        }
        else
        {
            SetText("Score " + score);
        }
    }

    private void HandleClick()
    {
        // Start / retry is by shooting the center target — panel is display-only.
    }

    private void EnsureLabel()
    {
        if (label != null)
        {
            ApplyLabelStyle();
            return;
        }

        label = GetComponentInChildren<TextMesh>();
        if (label != null)
        {
            ApplyLabelStyle();
            return;
        }

        GameObject textGo = new GameObject("Label");
        textGo.transform.SetParent(transform, false);
        textGo.transform.localPosition = Vector3.zero;
        textGo.transform.localRotation = Quaternion.identity;
        textGo.transform.localScale = Vector3.one;

        label = textGo.AddComponent<TextMesh>();
        label.anchor = TextAnchor.MiddleCenter;
        label.alignment = TextAlignment.Center;
        label.fontSize = 64;
        ApplyLabelStyle();
        SetText(cachedText);
    }

    private void ApplyLabelStyle()
    {
        if (label == null)
        {
            return;
        }

        label.color = textColor;
        label.characterSize = characterSize;
        label.anchor = TextAnchor.MiddleCenter;
        label.alignment = TextAlignment.Center;
    }

    private void SetText(string value)
    {
        cachedText = value;
        EnsureLabel();
        if (label != null)
        {
            label.text = value;
        }
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
            return hit.transform == transform || hit.transform.IsChildOf(transform);
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
            return component.transform == transform || component.transform.IsChildOf(transform);
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
