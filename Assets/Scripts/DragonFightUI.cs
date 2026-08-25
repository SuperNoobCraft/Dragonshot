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
    [Tooltip("Optional. Auto-added: top-left HUD under vGear/Frame/Info/InfoUI.")]
    [SerializeField] private DragonFightInfoHud infoHud;
    [Tooltip("Secret crystal target practice (arcade Target Test quiver).")]
    [SerializeField] private CrystalTargetPractice targetPractice;
    [Tooltip("Secret survival arcade (Survival quiver).")]
    [SerializeField] private DragonSurvivalMode survivalMode;
    [Tooltip("Secret arcade hub (panel click at START). Optional — entry works without it.")]
    [SerializeField] private ArcadeMode arcadeMode;
    [SerializeField] private DragonFightEquipStart equipStart;

    [Header("Display")]
    [SerializeField] private TextMesh label;
    [SerializeField] private float characterSize = 0.08f;
    [SerializeField] private Color textColor = Color.white;

    [Header("Panel Text")]
    [TextArea(2, 3)]
    [SerializeField] private string startPanelText = "START\nDragon Fight";

    [Header("Equip Tutorial (pre-fight)")]
    [TextArea(2, 3)]
    [SerializeField] private string equipStep1PickUpBow =
        "Step 1\nPick a bow (with or without scope)\nwith your left hand.";
    [TextArea(2, 3)]
    [SerializeField] private string equipStep2PickUpQuiver =
        "Step 2\nPick a quiver (Easy / Normal / Hard).";
    [TextArea(2, 3)]
    [SerializeField] private string equipStep3BehindBack =
        "Step 3\nStrap the quiver on your back.\nClick here to reset.";

    [TextArea(2, 4)]
    [Tooltip("{0} = seconds left, {1} = current HP, {2} = max HP, {3} = difficulty")]
    [SerializeField] private string playingTimerText = "TIME {0}\nHP {1}/{2}\n{3}\n(click to reset)";
    [TextArea(2, 4)]
    [Tooltip("Timer expired — dragon is chasing. {0}=HP, {1}=max HP, {2}=difficulty")]
    [SerializeField] private string overtimeTimerText = "ENRAGED\nHP {0}/{1}\n{2}\n(click to reset)";
    [TextArea(2, 4)]
    [Tooltip("Shown when the dragon dies and the player never equipped the scope. {0}=seconds, {1}=difficulty")]
    [SerializeField] private string victoryTextNoScope = "VICTORY\n{0}s left\nNo Scope\n{1}\nRESET";
    [TextArea(2, 4)]
    [Tooltip("Shown when the dragon dies and the player had the scope equipped. {0}=seconds, {1}=difficulty")]
    [SerializeField] private string victoryTextWithScope = "VICTORY\n{0}s left\nWith Scope\n{1}\nRESET";
    [TextArea(2, 2)]
    [Tooltip("{0} = difficulty")]
    [SerializeField] private string timeoutText = "TIME UP\n{0}\nRESET";
    [TextArea(2, 3)]
    [Tooltip("{0} = cause (e.g. Hit by fireball), {1} = difficulty")]
    [SerializeField] private string defeatText = "DEFEAT\n{0}\n{1}\nRESET";

    [Header("Info HUD (vGear InfoUI)")]
    [TextArea(2, 3)]
    [Tooltip("Top-left InfoUI HUD while fighting. {0}=seconds left, {1}=current HP, {2}=max HP")]
    [SerializeField] private string infoHudPlayingText = "TIME {0}\nHP {1}/{2}";
    [TextArea(2, 3)]
    [Tooltip("Top-left InfoUI HUD during overtime. {0}=current HP, {1}=max HP")]
    [SerializeField] private string infoHudOvertimeText = "ENRAGED\nHP {0}/{1}";

    [Header("Secret Arcade")]
    [TextArea(2, 3)]
    [Tooltip("Panel click at START before bow pickup.")]
    [SerializeField] private string arcadeWaitingText =
        "ARCADE\nPick up the bow\n(no scope)";
    [TextArea(2, 3)]
    [Tooltip("After bow — choose Target Test or Survival quiver.")]
    [SerializeField] private string arcadeQuiverPickText =
        "ARCADE\nTarget Test or Survival";

    [Header("Secret Target Practice")]
    [TextArea(2, 3)]
    [Tooltip("After secret panel click, before bow pickup. {0}=saved high score")]
    [SerializeField] private string targetPracticeWaitingText =
        "TARGET PRACTICE\nPick up the bow\n(no scope)\nBEST {0}";
    [TextArea(2, 4)]
    [Tooltip("World panel during practice. {0}=seconds left, {1}=score, {2}=high score")]
    [SerializeField] private string targetPracticePlayingPanelText =
        "TIME {0}\nSCORE {1}\nBEST {2}\n(click to reset)";
    [TextArea(2, 4)]
    [Tooltip("World panel when time runs out. {0}=score, {1}=high score")]
    [SerializeField] private string targetPracticeResultsPanelText =
        "TIME UP\nSCORE {0}\nBEST {1}\n(click to reset)";
    [TextArea(2, 3)]
    [Tooltip("Info HUD during practice. {0}=seconds left, {1}=score")]
    [SerializeField] private string infoHudTargetPracticeText = "TIME {0}\nSCORE {1}";

    [Header("Secret Survival Arcade")]
    [TextArea(2, 4)]
    [Tooltip("During survival. {0}=seconds survived, {1}=best seconds")]
    [SerializeField] private string survivalPlayingPanelText =
        "SURVIVED {0}s\nBEST {1}s\n(click to reset)";
    [TextArea(2, 4)]
    [Tooltip("When hit by a fireball. {0}=seconds survived, {1}=best seconds")]
    [SerializeField] private string survivalResultsPanelText =
        "ELIMINATED\n{0}s survived\nBEST {1}s\n(click to reset)";
    [TextArea(2, 3)]
    [Tooltip("Info HUD during survival. {0}=seconds survived")]
    [SerializeField] private string infoHudSurvivalText = "SURVIVED {0}s";

    [Header("Interact")]
    [SerializeField] private bool instructionOnlyBeforeFight = true;
    [SerializeField] private float wandRayDistance = 8f;
    [SerializeField, Range(0.01f, 0.5f)] private float axisDeadzone = 0.08f;
    [SerializeField] private int maxControllersToScan = 4;
    [SerializeField] private int maxAxesToScan = 16;
    [SerializeField] private int maxButtonsToScan = 16;

    [Header("Defeat Tint")]
    [Tooltip("Full-screen red wash while defeated (clears on reset). Drawn on every camera for CAVE.")]
    [SerializeField] private Color defeatScreenTint = new Color(0.75f, 0.02f, 0.02f, 1f);
    [Tooltip("Opacity on the frame of impact.")]
    [SerializeField, Range(0.1f, 1f)] private float defeatTintStartAlpha = 0.8f;
    [Tooltip("Opacity after the fade settles (still visible until reset).")]
    [SerializeField, Range(0f, 1f)] private float defeatTintEndAlpha = 0.28f;
    [SerializeField, Min(0.05f)] private float defeatTintFadeSeconds = 1.1f;

    private PanelState state = PanelState.Start;
    private bool wasTriggerHeld;
    private bool instructionOnly;
    private bool allowResetDuringEquip;
    private bool defeatTintActive;
    private float defeatTintStartTime;
    private Material defeatTintMaterial;

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

        if (targetPractice == null)
        {
            targetPractice = FindObjectOfType<CrystalTargetPractice>();
        }

        if (survivalMode == null)
        {
            survivalMode = FindObjectOfType<DragonSurvivalMode>();
        }

        if (arcadeMode == null)
        {
            arcadeMode = FindObjectOfType<ArcadeMode>();
        }

        ResolveEquipStart();
        EnsureLabel();
        EnsureInteractable();
        EnsureInfoHud();
        HideInfoHud();
        Camera.onPostRender += DrawDefeatTint;
    }

    private void OnDestroy()
    {
        Camera.onPostRender -= DrawDefeatTint;
        if (defeatTintMaterial != null)
        {
            Destroy(defeatTintMaterial);
            defeatTintMaterial = null;
        }
    }

    private void Update()
    {
        if (dragon == null)
        {
            dragon = FindObjectOfType<DragonBoss>();
            if (dragon == null && (targetPractice == null || !targetPractice.IsActive))
            {
                return;
            }
        }

        if (targetPractice == null)
        {
            targetPractice = FindObjectOfType<CrystalTargetPractice>();
        }

        if (survivalMode == null)
        {
            survivalMode = FindObjectOfType<DragonSurvivalMode>();
        }

        if (arcadeMode == null)
        {
            arcadeMode = FindObjectOfType<ArcadeMode>();
        }

        ResolveEquipStart();

        if (instructionOnly && !allowResetDuringEquip && state == PanelState.Start)
        {
            HandleArcadeClickInput();
            return;
        }

        if (PlayEnvironment.IsDesktopInput)
        {
            if (instructionOnly && !allowResetDuringEquip && state == PanelState.Start)
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
            if (instructionOnly && !allowResetDuringEquip && state == PanelState.Start)
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

    private void HandleArcadeClickInput()
    {
        if (PlayEnvironment.IsDesktopInput)
        {
            if (Input.GetMouseButtonDown(0) && IsCursorOverPanel())
            {
                TryEnterArcadeFromPanelClick();
            }
        }
        else
        {
            bool held = IsTriggerHeld();
            bool pressed = held && !wasTriggerHeld;
            wasTriggerHeld = held;
            if (pressed && IsWandTargetingPanel())
            {
                TryEnterArcadeFromPanelClick();
            }
        }
    }

    private void ResolveEquipStart()
    {
        if (equipStart == null)
        {
            equipStart = FindObjectOfType<DragonFightEquipStart>();
        }
    }

    private bool CanEnterArcadeFromPanel()
    {
        if (equipStart == null)
        {
            return false;
        }

        if (equipStart.IsArcadeModeActive || equipStart.IsBowEquipped)
        {
            return false;
        }

        if (!equipStart.CanStartArcadeEntry)
        {
            return false;
        }

        if (targetPractice != null && targetPractice.IsActive)
        {
            return false;
        }

        if (survivalMode != null && survivalMode.IsActive)
        {
            return false;
        }

        return true;
    }

    private bool TryEnterArcadeFromPanelClick()
    {
        if (arcadeMode != null && arcadeMode.CanEnterFromPanel)
        {
            return arcadeMode.TryEnterFromPanelClick();
        }

        if (!CanEnterArcadeFromPanel())
        {
            return false;
        }

        equipStart.EnterArcadeMode();
        ShowArcadeWaiting();
        return true;
    }

    public void ShowArcadeWaiting()
    {
        SetDefeatTint(false);
        instructionOnly = true;
        allowResetDuringEquip = false;
        state = PanelState.Start;
        SetText(arcadeWaitingText);
        HideInfoHud();
    }

    public void ShowArcadeQuiverPick()
    {
        SetDefeatTint(false);
        instructionOnly = true;
        allowResetDuringEquip = false;
        state = PanelState.Start;
        SetText(arcadeQuiverPickText);
        HideInfoHud();
    }

    public void ShowTargetPracticeWaiting(int bestScore)
    {
        SetDefeatTint(false);
        instructionOnly = true;
        allowResetDuringEquip = false;
        state = PanelState.Start;
        SetText(FormatTemplate(targetPracticeWaitingText, bestScore));
        HideInfoHud();
    }

    public void ShowTargetPracticePlaying(int secondsRemaining, int currentScore, int bestScore)
    {
        SetDefeatTint(false);
        instructionOnly = false;
        allowResetDuringEquip = false;
        state = PanelState.Playing;
        SetText(FormatTemplate(targetPracticePlayingPanelText, secondsRemaining, currentScore, bestScore));
        EnsureInfoHud().ShowText(FormatTemplate(infoHudTargetPracticeText, secondsRemaining, currentScore));
    }

    public void ShowTargetPracticeResults(int currentScore, int bestScore)
    {
        SetDefeatTint(false);
        instructionOnly = false;
        allowResetDuringEquip = false;
        state = PanelState.Timeout;
        SetText(FormatTemplate(targetPracticeResultsPanelText, currentScore, bestScore));
        HideInfoHud();
    }

    public void ShowSurvivalPlaying(float secondsSurvived, float bestSeconds)
    {
        SetDefeatTint(false);
        instructionOnly = false;
        allowResetDuringEquip = false;
        state = PanelState.Playing;
        string survived = FormatSurvivalTime(secondsSurvived);
        string best = FormatSurvivalTime(bestSeconds);
        SetText(FormatTemplate(survivalPlayingPanelText, survived, best));
        EnsureInfoHud().ShowText(FormatTemplate(infoHudSurvivalText, survived));
    }

    public void ShowSurvivalResults(float secondsSurvived, float bestSeconds)
    {
        SetDefeatTint(true);
        instructionOnly = false;
        allowResetDuringEquip = false;
        state = PanelState.Defeat;
        string survived = FormatSurvivalTime(secondsSurvived);
        string best = FormatSurvivalTime(bestSeconds);
        SetText(FormatTemplate(survivalResultsPanelText, survived, best));
        HideInfoHud();
    }

    public void ShowStart()
    {
        SetDefeatTint(false);
        instructionOnly = false;
        allowResetDuringEquip = false;
        state = PanelState.Start;
        SetText(startPanelText);
        HideInfoHud();
    }

    public void ShowEquipInstructions(string text)
    {
        SetDefeatTint(false);
        instructionOnly = instructionOnlyBeforeFight;
        allowResetDuringEquip = false;
        state = PanelState.Start;
        SetText(text);
        HideInfoHud();
    }

    public void ShowEquipStep(int step)
    {
        SetDefeatTint(false);
        instructionOnly = instructionOnlyBeforeFight;
        // After the bow is equipped, clicking the panel resets so you can re-pick a quiver.
        allowResetDuringEquip = step >= 2;
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
                allowResetDuringEquip = false;
                SetText(equipStep1PickUpBow);
                break;
        }

        HideInfoHud();
    }

    public void ShowTimer(float secondsRemaining, int hp, int maxHp)
    {
        SetDefeatTint(false);
        instructionOnly = false;
        allowResetDuringEquip = false;
        state = PanelState.Playing;
        int whole = Mathf.CeilToInt(Mathf.Max(0f, secondsRemaining));
        SetText(FormatTemplate(playingTimerText, whole, hp, maxHp, DifficultyLabel()));
        EnsureInfoHud().ShowText(FormatTemplate(infoHudPlayingText, whole, hp, maxHp));
    }

    public void ShowTimer(float secondsRemaining)
    {
        ShowTimer(secondsRemaining, 0, 0);
    }

    public void ShowOvertime(int hp, int maxHp)
    {
        SetDefeatTint(false);
        instructionOnly = false;
        allowResetDuringEquip = false;
        state = PanelState.Playing;
        SetText(FormatTemplate(overtimeTimerText, hp, maxHp, DifficultyLabel()));
        EnsureInfoHud().ShowText(FormatTemplate(infoHudOvertimeText, hp, maxHp));
    }

    public void ShowVictory(float secondsLeft, bool usedScope = false)
    {
        SetDefeatTint(false);
        allowResetDuringEquip = false;
        state = PanelState.Victory;
        int whole = Mathf.CeilToInt(Mathf.Max(0f, secondsLeft));
        string template = usedScope ? victoryTextWithScope : victoryTextNoScope;
        SetText(FormatTemplate(template, whole, DifficultyLabel()));
        HideInfoHud();
    }

    public void ShowTimeout()
    {
        SetDefeatTint(false);
        allowResetDuringEquip = false;
        state = PanelState.Timeout;
        SetText(FormatTemplate(timeoutText, DifficultyLabel()));
        HideInfoHud();
    }

    public void ShowDefeat(string cause = "Hit by fireball")
    {
        allowResetDuringEquip = false;
        state = PanelState.Defeat;
        if (string.IsNullOrEmpty(cause))
        {
            cause = "Hit by fireball";
        }

        SetText(FormatTemplate(defeatText, cause, DifficultyLabel()));
        SetDefeatTint(true);
        HideInfoHud();
    }

    private DragonFightInfoHud EnsureInfoHud()
    {
        if (infoHud == null)
        {
            infoHud = GetComponent<DragonFightInfoHud>();
        }

        if (infoHud == null)
        {
            infoHud = gameObject.AddComponent<DragonFightInfoHud>();
        }

        return infoHud;
    }

    /// <summary>Used by DragonBoss so the HUD starts hunting for InfoUI immediately.</summary>
    public void EnsureInfoHudPublic()
    {
        EnsureInfoHud();
    }

    private void HideInfoHud()
    {
        if (infoHud == null)
        {
            infoHud = GetComponent<DragonFightInfoHud>();
        }

        if (infoHud != null)
        {
            infoHud.Hide();
        }
    }

    private void SetDefeatTint(bool active)
    {
        defeatTintActive = active;
        if (active)
        {
            defeatTintStartTime = Time.unscaledTime;
        }
    }

    private Color CurrentDefeatTintColor()
    {
        Color c = defeatScreenTint;
        if (!defeatTintActive)
        {
            c.a = 0f;
            return c;
        }

        float duration = Mathf.Max(0.05f, defeatTintFadeSeconds);
        float t = Mathf.Clamp01((Time.unscaledTime - defeatTintStartTime) / duration);
        // Ease out so the hit flash reads strongly, then settles.
        t = 1f - (1f - t) * (1f - t);
        c.a = Mathf.Lerp(defeatTintStartAlpha, defeatTintEndAlpha, t);
        return c;
    }

    private void DrawDefeatTint(Camera cam)
    {
        if (!defeatTintActive || cam == null || !cam.isActiveAndEnabled)
        {
            return;
        }

        // Skip overlay/preview cameras that shouldn't get the wash.
        if (cam.cameraType != CameraType.Game && cam.cameraType != CameraType.VR)
        {
            return;
        }

        if (defeatTintMaterial == null)
        {
            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
            {
                shader = Shader.Find("UI/Default");
            }

            if (shader == null)
            {
                return;
            }

            defeatTintMaterial = new Material(shader);
            defeatTintMaterial.hideFlags = HideFlags.HideAndDontSave;
            defeatTintMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            defeatTintMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            defeatTintMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            defeatTintMaterial.SetInt("_ZWrite", 0);
        }

        Color tint = CurrentDefeatTintColor();
        defeatTintMaterial.color = tint;
        defeatTintMaterial.SetPass(0);

        GL.PushMatrix();
        GL.LoadOrtho();
        GL.Begin(GL.QUADS);
        GL.Color(tint);
        GL.Vertex3(0f, 0f, 0f);
        GL.Vertex3(1f, 0f, 0f);
        GL.Vertex3(1f, 1f, 0f);
        GL.Vertex3(0f, 1f, 0f);
        GL.End();
        GL.PopMatrix();
    }

    private string DifficultyLabel()
    {
        FightDifficulty difficulty = dragon != null ? dragon.Difficulty : FightDifficulty.Normal;
        switch (difficulty)
        {
            case FightDifficulty.Easy:
                return "EASY";
            case FightDifficulty.Hard:
                return "HARD";
            default:
                return "NORMAL";
        }
    }

    private static string FormatSurvivalTime(float seconds)
    {
        return Mathf.Max(0f, seconds).ToString("F2");
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
        if (targetPractice != null && targetPractice.IsActive)
        {
            targetPractice.OnPanelClicked();
            return;
        }

        if (survivalMode != null && survivalMode.IsActive)
        {
            survivalMode.OnPanelClicked();
            return;
        }

        if (dragon == null)
        {
            return;
        }

        if (state == PanelState.Start)
        {
            if (allowResetDuringEquip)
            {
                dragon.ResetFight();
                return;
            }

            if (TryEnterArcadeFromPanelClick())
            {
                return;
            }

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
