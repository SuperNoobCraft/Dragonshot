using UnityEngine;
using Votanic.vXR.vCast;

/// <summary>
/// World-space panel for the dragon fight: instructions → live timer → reset.
/// All panel copy is editable in the Inspector under Panel Text.
/// </summary>
[RequireComponent(typeof(Collider))]
public class DragonFightUI : MonoBehaviour
{
    public enum PanelState
    {
        Start,
        Playing,
        Victory,
        Timeout,
        Defeat
    }

    [Header("References")]
    [SerializeField] private DragonBoss dragon;

    [Header("Display")]
    [SerializeField] private TextMesh label;
    [SerializeField] private float characterSize = 0.08f;
    [SerializeField] private Color textColor = Color.white;

    [Header("Panel Text")]
    [TextArea(2, 3)]
    [SerializeField] private string startPanelText = "START\nDragon Fight";

    [Header("Equip Tutorial (pre-fight)")]
    [TextArea(2, 3)]
    [SerializeField] private string equipStep1PickUpBow = "Step 1\nPick up the bow\nwith your left hand.";
    [TextArea(2, 3)]
    [SerializeField] private string equipStep2PickUpQuiver = "Step 2\nPick up the quiver\nwith your right hand.";
    [TextArea(2, 3)]
    [SerializeField] private string equipStep3BehindBack = "Step 3\nReach behind your back\nto strap on the quiver.";

    [TextArea(2, 4)]
    [Tooltip("{0} = seconds left, {1} = current HP, {2} = max HP")]
    [SerializeField] private string playingTimerText = "TIME {0}\nHP {1}/{2}\n(click to reset)";
    [TextArea(2, 3)]
    [Tooltip("{0} = seconds left when the dragon was defeated")]
    [SerializeField] private string victoryText = "VICTORY\n{0}s left\nRESET";
    [TextArea(2, 2)]
    [SerializeField] private string timeoutText = "TIME UP\nRESET";
    [TextArea(2, 2)]
    [SerializeField] private string defeatText = "DEFEAT\nHit by fireball\nRESET";

    [Header("Interact")]
    [SerializeField] private bool instructionOnlyBeforeFight = true;
    [SerializeField] private float wandRayDistance = 8f;
    [SerializeField, Range(0.01f, 0.5f)] private float axisDeadzone = 0.08f;
    [SerializeField] private int maxControllersToScan = 4;
    [SerializeField] private int maxAxesToScan = 16;
    [SerializeField] private int maxButtonsToScan = 16;

    private PanelState state = PanelState.Start;
    private bool wasTriggerHeld;
    private bool instructionOnly;

    public void Assign(DragonBoss boss, TextMesh textMesh)
    {
        dragon = boss;
        label = textMesh;
        EnsureLabel();
        EnsureInteractable();
        ShowStart();
    }

    public void Bind(DragonBoss boss)
    {
        dragon = boss;
        EnsureLabel();
        EnsureInteractable();
        ShowStart();
    }

    private void Awake()
    {
        if (dragon == null)
        {
            dragon = FindObjectOfType<DragonBoss>();
        }

        EnsureLabel();
        EnsureInteractable();
    }

    private void Update()
    {
        if (dragon == null)
        {
            dragon = FindObjectOfType<DragonBoss>();
            if (dragon == null)
            {
                return;
            }
        }

        if (PlayEnvironment.IsDesktopInput)
        {
            if (instructionOnly && state == PanelState.Start)
            {
                return;
            }

            if (Input.GetMouseButtonDown(0) && IsCursorOverPanel())
            {
                HandleClick();
            }
        }
        else
        {
            if (instructionOnly && state == PanelState.Start)
            {
                wasTriggerHeld = IsTriggerHeld();
                return;
            }

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
        instructionOnly = false;
        state = PanelState.Start;
        SetText(startPanelText);
    }

    public void ShowEquipInstructions(string text)
    {
        instructionOnly = instructionOnlyBeforeFight;
        state = PanelState.Start;
        SetText(text);
    }

    public void ShowEquipStep(int step)
    {
        instructionOnly = instructionOnlyBeforeFight;
        state = PanelState.Start;
        switch (step)
        {
            case 2:
                SetText(equipStep2PickUpQuiver);
                break;
            case 3:
                SetText(equipStep3BehindBack);
                break;
            default:
                SetText(equipStep1PickUpBow);
                break;
        }
    }

    public void ShowTimer(float secondsRemaining, int hp, int maxHp)
    {
        instructionOnly = false;
        state = PanelState.Playing;
        int whole = Mathf.CeilToInt(Mathf.Max(0f, secondsRemaining));
        SetText(FormatTemplate(playingTimerText, whole, hp, maxHp));
    }

    public void ShowTimer(float secondsRemaining)
    {
        ShowTimer(secondsRemaining, 0, 0);
    }

    public void ShowVictory(float secondsLeft)
    {
        state = PanelState.Victory;
        int whole = Mathf.CeilToInt(Mathf.Max(0f, secondsLeft));
        SetText(FormatTemplate(victoryText, whole));
    }

    public void ShowTimeout()
    {
        state = PanelState.Timeout;
        SetText(timeoutText);
    }

    public void ShowDefeat()
    {
        state = PanelState.Defeat;
        SetText(defeatText);
    }

    private static string FormatTemplate(string template, params object[] args)
    {
        if (string.IsNullOrEmpty(template))
        {
            return string.Empty;
        }

        try
        {
            return string.Format(template, args);
        }
        catch
        {
            return template;
        }
    }

    private void HandleClick()
    {
        if (dragon == null)
        {
            return;
        }

        if (state == PanelState.Start)
        {
            if (!instructionOnly)
            {
                dragon.StartFight();
            }

            return;
        }

        dragon.ResetFight();
    }

    private void SetText(string value)
    {
        EnsureLabel();
        if (label != null)
        {
            label.text = value;
        }
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
        label.fontSize = 64;
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
