using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Secret crystal target practice: enter by clicking the fight panel before picking a bow.
/// No dragon — crystals spawn in an assignable box; the next crystal ghost shows outline only.
/// </summary>
public class CrystalTargetPractice : MonoBehaviour
{
    private enum Phase
    {
        Inactive,
        WaitingForBow,
        Playing,
        Results
    }

    [Header("References")]
    [SerializeField] private DragonBoss dragon;
    [SerializeField] private DragonFightEquipStart equipStart;
    [SerializeField] private DragonFightUI fightUI;
    [SerializeField] private ArrowQuiver quiver;
    [Tooltip("Optional. Auto-found on the dragon if empty.")]
    [SerializeField] private FightAudio fightAudio;
    [Tooltip("World-space box for random crystal spawns (BoxCollider bounds or transform scale).")]
    [SerializeField] private Transform spawnArea;
    [Tooltip("Optional template. If empty, crystals are built at runtime.")]
    [SerializeField] private EnderCrystal crystalPrefab;

    [Header("Round")]
    [SerializeField, Min(1f)] private float roundSeconds = 30f;
    [SerializeField, Min(1)] private int maxCrystalsAtOnce = 1;
    [SerializeField, Min(0)] private int previewCrystalCount = 1;
    [Tooltip("Minimum horizontal (X/Z) distance between crystals/pillars. Y is ignored so tall stacks can still pack.")]
    [SerializeField, Min(0f)] private float minHorizontalDistance = 2.5f;
    [Tooltip(
        "Minimum angle (degrees) between player→crystal sightlines. "
        + "Rejects spawns that sit in the same view cone as an existing crystal/pillar "
        + "(stops one target blocking another). 0 = off.")]
    [SerializeField, Range(0f, 90f)] private float minViewSeparationDegrees = 18f;
    [Tooltip("If on, angle check ignores height (XZ only). Better for pillars stacked in depth.")]
    [SerializeField] private bool viewSeparationHorizontalOnly = true;
    [SerializeField, HideInInspector] private float minPreviewDistance = 2.5f;
    [SerializeField] private bool enableInfiniteArrows = true;
    [SerializeField] private bool clearFlyingArrowsOnRoundEnd = true;
    [Tooltip("Ghost cage grow-in (hard-mode shell phase, sped up).")]
    [SerializeField, Min(0.05f)] private float cageEmergeSeconds = 0.65f;
    [Tooltip("Inner orb grow when a ghost cage becomes live (hard-mode inner phase, sped up).")]
    [SerializeField, Min(0.05f)] private float innerEmergeSeconds = 0.45f;

    [Header("Practice Pillars")]
    [SerializeField] private bool spawnPillars = true;
    [Tooltip(
        "Drag your fight CrystalPillar_* objects (or prefabs) here. "
        + "Uses each pillar's CrystalPillarRiseSettings offset so crystal height matches the base game. "
        + "One is picked at random per spawn. Fight crystals on the template are stripped from the copy.")]
    [SerializeField] private GameObject[] pillarTemplates;
    [Tooltip("Legacy single template — used if Pillar Templates is empty.")]
    [SerializeField] private GameObject pillarPrefab;
    [Tooltip("Fallback only when no templates are assigned.")]
    [SerializeField, Min(0.5f)] private float pillarHeight = 4f;
    [SerializeField, Min(0.1f)] private float pillarRadius = 0.35f;
    [Tooltip("Fallback crystal−pillar Y offset when the template has no CrystalPillarRiseSettings.")]
    [SerializeField] private float crystalOffsetFromPillarY = 2.15f;
    [Tooltip("Fallback bury depth when the template has no CrystalPillarRiseSettings.")]
    [SerializeField, Min(0.5f)] private float pillarBuriedDepth = 8f;
    [SerializeField, Min(0.1f)] private float pillarRiseSpeed = 5f;
    [SerializeField, Min(0.1f)] private float pillarSinkSpeed = 10f;
    [SerializeField] private int pillarLayer = 30;
    [SerializeField] private Color pillarColor = new Color(0.35f, 0.3f, 0.28f, 1f);

    [Header("High Score")]
    [Tooltip("Saved under Application.persistentDataPath.")]
    [SerializeField] private string highScoreFileName = "crystal_target_practice_highscore.txt";

    [Header("Gizmo")]
    [SerializeField] private Color gizmoColor = new Color(0.95f, 0.35f, 1f, 0.25f);

    private Phase phase = Phase.Inactive;
    private float timeRemaining;
    private int score;
    private int highScore;
    private bool dragonWasActive = true;
    private bool savedInfiniteArrows;
    private CrystalPillarRiseController pillarRise;
    private int pendingLiveSpawns;
    private int pendingPreviewSpawns;
    private bool timerStarted;
    private readonly HashSet<EnderCrystal> promotedFromPreview = new HashSet<EnderCrystal>();
    private readonly List<EnderCrystal> liveCrystals = new List<EnderCrystal>(4);
    private readonly List<EnderCrystal> previewCrystals = new List<EnderCrystal>(4);
    private readonly List<EnderCrystal> emergingCrystals = new List<EnderCrystal>(4);
    private readonly Dictionary<EnderCrystal, PracticePillar> crystalPillars =
        new Dictionary<EnderCrystal, PracticePillar>(8);
    private readonly List<PracticePillar> sinkingPillars = new List<PracticePillar>(8);

    private class PracticePillar
    {
        public Transform transform;
        public float buriedY;
        public float peakY;
        public bool cancelRise;
    }

    public bool IsActive => phase != Phase.Inactive;
    public bool IsPlaying => phase == Phase.Playing;
    public bool CanAcceptSecretEntry => phase == Phase.Inactive && equipStart != null && !equipStart.IsBowEquipped;
    public int Score => score;
    public int HighScore => highScore;
    public float TimeRemaining => timeRemaining;

    private void Awake()
    {
        ResolveReferences();
        LoadHighScore();
        // Migrate old serialized field name.
        if (minHorizontalDistance <= 0f && minPreviewDistance > 0f)
        {
            minHorizontalDistance = minPreviewDistance;
        }
    }

    private void Update()
    {
        if (phase != Phase.Playing)
        {
            return;
        }

        liveCrystals.RemoveAll(c => c == null);
        previewCrystals.RemoveAll(c => c == null);
        MaintainCrystalCounts();

        if (!timerStarted)
        {
            RefreshPlayingUi();
            return;
        }

        timeRemaining -= Time.deltaTime;
        RefreshPlayingUi();

        if (timeRemaining <= 0f)
        {
            EndRound();
        }
    }

    /// <summary>Called from DragonFightUI when the panel is clicked during equip step 1.</summary>
    public bool TryEnterFromSecretClick()
    {
        if (!CanAcceptSecretEntry)
        {
            return false;
        }

        EnterMode();
        return true;
    }

    /// <summary>Called from DragonFightEquipStart after quiver is mounted on back.</summary>
    public void OnQuiverMounted()
    {
        if (phase != Phase.WaitingForBow)
        {
            return;
        }

        StartRound();
    }

    /// <summary>Panel click while practice mode is active.</summary>
    public void OnPanelClicked()
    {
        ExitMode();
    }

    public void NotifyCrystalHit(EnderCrystal crystal)
    {
        if (phase != Phase.Playing || crystal == null)
        {
            return;
        }

        score++;
        liveCrystals.Remove(crystal);
        emergingCrystals.Remove(crystal);
        BeginSinkPillar(crystal);
        RefreshPlayingUi();
    }

    public void EnterMode()
    {
        ResolveReferences();
        LoadHighScore();

        phase = Phase.WaitingForBow;
        score = 0;
        timeRemaining = 0f;
        ClearSpawnedCrystals();

        SuppressDragonFight(true);
        EnableInfiniteArrowsIfNeeded();

        if (equipStart != null)
        {
            equipStart.EnterTargetPracticeMode();
        }

        if (fightUI != null)
        {
            fightUI.ShowTargetPracticeWaiting(highScore);
        }
    }

    public void ExitMode()
    {
        ForceStopWithoutEquipReset();

        if (equipStart != null)
        {
            equipStart.ResetForWaiting();
        }
        else if (fightUI != null)
        {
            fightUI.ShowStart();
        }
    }

    /// <summary>Cleanup without resetting equip flow (used when equip start resets).</summary>
    public void ForceStopWithoutEquipReset()
    {
        StopAllCoroutines();
        phase = Phase.Inactive;
        score = 0;
        timeRemaining = 0f;
        pendingLiveSpawns = 0;
        pendingPreviewSpawns = 0;
        timerStarted = false;
        promotedFromPreview.Clear();
        ClearSpawnedCrystals(sinkPillars: false);
        RestoreInfiniteArrows();
        SuppressDragonFight(false);
    }

    private void StartRound()
    {
        StopAllCoroutines();
        ClearSpawnedCrystals(sinkPillars: false);
        score = 0;
        timeRemaining = roundSeconds;
        timerStarted = false;
        pendingLiveSpawns = 0;
        pendingPreviewSpawns = 0;
        promotedFromPreview.Clear();
        phase = Phase.Playing;
        EnableInfiniteArrowsIfNeeded();
        RefreshPlayingUi();
        MaintainCrystalCounts();
    }

    private void BeginTimerIfNeeded()
    {
        if (timerStarted || phase != Phase.Playing)
        {
            return;
        }

        timerStarted = true;
        timeRemaining = roundSeconds;
        RefreshPlayingUi();
    }

    private void EndRound()
    {
        phase = Phase.Results;
        timeRemaining = 0f;
        ClearSpawnedCrystals(sinkPillars: true);
        SaveHighScoreIfNeeded();

        if (clearFlyingArrowsOnRoundEnd)
        {
            ClearLooseArrows();
        }

        if (fightUI != null)
        {
            fightUI.ShowTargetPracticeResults(score, highScore);
        }
    }

    private void RefreshPlayingUi()
    {
        if (fightUI == null || phase != Phase.Playing)
        {
            return;
        }

        int whole = Mathf.CeilToInt(Mathf.Max(0f, timeRemaining));
        fightUI.ShowTargetPracticePlaying(whole, score, highScore);
    }

    private void MaintainCrystalCounts()
    {
        while (liveCrystals.Count + pendingLiveSpawns < maxCrystalsAtOnce && previewCrystals.Count > 0)
        {
            PromotePreview();
        }

        // If a ghost is still forming, wait for it to finish its shell and elevate
        // to live — do not start a second live spawn that races the cage grow.
        if (liveCrystals.Count + pendingLiveSpawns < maxCrystalsAtOnce
            && pendingPreviewSpawns > 0)
        {
            // Forming preview will continue into live when its cage finishes.
        }
        else
        {
            while (liveCrystals.Count + pendingLiveSpawns < maxCrystalsAtOnce)
            {
                SpawnCrystal(preview: false);
            }
        }

        // Hold ghost pillars until the first live crystal is fully ready,
        // so the opening target is unambiguous.
        if (!timerStarted)
        {
            return;
        }

        while (previewCrystals.Count + pendingPreviewSpawns < previewCrystalCount)
        {
            SpawnCrystal(preview: true);
        }
    }

    private void PromotePreview()
    {
        if (previewCrystals.Count == 0)
        {
            return;
        }

        EnderCrystal next = previewCrystals[0];
        previewCrystals.RemoveAt(0);
        if (next == null)
        {
            return;
        }

        // Only fully finished ghosts are in previewCrystals; mark so any stale
        // spawn coroutine cannot reset them back to cage-only.
        promotedFromPreview.Add(next);
        pendingLiveSpawns++;
        emergingCrystals.Add(next);
        StartCoroutine(CompleteInnerEmergeRoutine(next, addToLive: true));
    }

    private void SpawnCrystal(bool preview)
    {
        if (!TryRandomSpawnPoint(out Vector3 point))
        {
            return;
        }

        EnderCrystal crystal = CreateCrystalAt(point);
        if (crystal == null)
        {
            return;
        }

        ApplyPracticeCrystalSetup(crystal, preview: true);

        if (preview)
        {
            // Do NOT add to previewCrystals until cage grow finishes — otherwise
            // MaintainCrystalCounts can promote mid-animation and corrupt scales.
            pendingPreviewSpawns++;
            emergingCrystals.Add(crystal);
            StartCoroutine(SpawnSequenceRoutine(crystal, promoteToLive: false));
            return;
        }

        pendingLiveSpawns++;
        emergingCrystals.Add(crystal);
        StartCoroutine(SpawnSequenceRoutine(crystal, promoteToLive: true));
    }

    private IEnumerator SpawnSequenceRoutine(EnderCrystal crystal, bool promoteToLive)
    {
        if (crystal == null)
        {
            if (promoteToLive)
            {
                pendingLiveSpawns = Mathf.Max(0, pendingLiveSpawns - 1);
                emergingCrystals.Remove(crystal);
            }
            else
            {
                pendingPreviewSpawns = Mathf.Max(0, pendingPreviewSpawns - 1);
                emergingCrystals.Remove(crystal);
            }

            yield break;
        }

        // Pillar rises first, then ghost cage, then (if live) inner orb.
        if (spawnPillars)
        {
            PracticePillar pillar = CreatePillarForCrystal(crystal);
            if (pillar != null)
            {
                crystalPillars[crystal] = pillar;
                yield return RisePillarRoutine(pillar);
            }
        }

        if (crystal == null || WasPromotedOrCleared(crystal))
        {
            FinishSpawnBookkeeping(crystal, promoteToLive);
            yield break;
        }

        BeginCrystalCageGrow(crystal);
        yield return AnimateCageEmerge(crystal);

        if (crystal == null || WasPromotedOrCleared(crystal))
        {
            FinishSpawnBookkeeping(crystal, promoteToLive);
            yield break;
        }

        // Snap cage to full size so a mid-frame promote/elevate never leaves a baby shell.
        crystal.SetPracticeCageEmergeProgress(1f);

        if (!promoteToLive)
        {
            // Live slot empty while this ghost was forming — finish shell, then grow core
            // as the live target instead of parking as a preview.
            bool elevateToLive = liveCrystals.Count + pendingLiveSpawns < maxCrystalsAtOnce;
            if (elevateToLive)
            {
                pendingPreviewSpawns = Mathf.Max(0, pendingPreviewSpawns - 1);
                pendingLiveSpawns++;
                promoteToLive = true;

                crystal.CompletePracticeCageShell(asPreview: false);
                yield return AnimateInnerEmerge(crystal);

                if (crystal == null || WasPromotedOrCleared(crystal))
                {
                    FinishSpawnBookkeeping(crystal, promoteToLive: true);
                    yield break;
                }

                crystal.CompletePracticeEmerge();
                crystal.RefreshPracticeVisualState();
                pendingLiveSpawns = Mathf.Max(0, pendingLiveSpawns - 1);
                emergingCrystals.Remove(crystal);
                if (!liveCrystals.Contains(crystal))
                {
                    liveCrystals.Add(crystal);
                }

                BeginTimerIfNeeded();
                yield break;
            }

            crystal.CompletePracticeCagePreview();
            pendingPreviewSpawns = Mathf.Max(0, pendingPreviewSpawns - 1);
            emergingCrystals.Remove(crystal);
            if (!previewCrystals.Contains(crystal))
            {
                previewCrystals.Add(crystal);
            }

            yield break;
        }

        crystal.CompletePracticeCageShell(asPreview: false);
        yield return AnimateInnerEmerge(crystal);

        if (crystal == null || WasPromotedOrCleared(crystal))
        {
            FinishSpawnBookkeeping(crystal, promoteToLive);
            yield break;
        }

        crystal.CompletePracticeEmerge();
        crystal.RefreshPracticeVisualState();
        pendingLiveSpawns = Mathf.Max(0, pendingLiveSpawns - 1);
        emergingCrystals.Remove(crystal);
        if (!liveCrystals.Contains(crystal))
        {
            liveCrystals.Add(crystal);
        }

        BeginTimerIfNeeded();
    }

    private bool WasPromotedOrCleared(EnderCrystal crystal)
    {
        return crystal == null || promotedFromPreview.Contains(crystal);
    }

    private void FinishSpawnBookkeeping(EnderCrystal crystal, bool promoteToLive)
    {
        if (promoteToLive)
        {
            pendingLiveSpawns = Mathf.Max(0, pendingLiveSpawns - 1);
        }
        else
        {
            pendingPreviewSpawns = Mathf.Max(0, pendingPreviewSpawns - 1);
        }

        if (crystal != null)
        {
            emergingCrystals.Remove(crystal);
        }
    }

    private IEnumerator AnimateCageEmerge(EnderCrystal crystal)
    {
        if (crystal == null)
        {
            yield break;
        }

        float duration = Mathf.Max(0.05f, cageEmergeSeconds);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            if (crystal == null || WasPromotedOrCleared(crystal))
            {
                yield break;
            }

            crystal.SetPracticeCageEmergeProgress(Mathf.Clamp01(elapsed / duration));
            yield return null;
        }

        if (crystal != null && !WasPromotedOrCleared(crystal))
        {
            crystal.SetPracticeCageEmergeProgress(1f);
        }
    }

    private IEnumerator AnimateInnerEmerge(EnderCrystal crystal)
    {
        if (crystal == null)
        {
            yield break;
        }

        crystal.BeginPracticeInnerEmerge();

        float duration = Mathf.Max(0.05f, innerEmergeSeconds);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            if (crystal == null)
            {
                yield break;
            }

            crystal.SetPracticeInnerEmergeProgress(Mathf.Clamp01(elapsed / duration));
            yield return null;
        }
    }

    private IEnumerator CompleteInnerEmergeRoutine(EnderCrystal crystal, bool addToLive)
    {
        if (crystal == null)
        {
            pendingLiveSpawns = Mathf.Max(0, pendingLiveSpawns - 1);
            yield break;
        }

        yield return AnimateInnerEmerge(crystal);

        if (crystal == null)
        {
            pendingLiveSpawns = Mathf.Max(0, pendingLiveSpawns - 1);
            emergingCrystals.Remove(crystal);
            yield break;
        }

        crystal.CompletePracticeEmerge();
        crystal.RefreshPracticeVisualState();
        pendingLiveSpawns = Mathf.Max(0, pendingLiveSpawns - 1);
        emergingCrystals.Remove(crystal);
        promotedFromPreview.Remove(crystal);
        if (addToLive && !liveCrystals.Contains(crystal))
        {
            liveCrystals.Add(crystal);
        }

        if (addToLive)
        {
            BeginTimerIfNeeded();
        }
    }

    private PracticePillar CreatePillarForCrystal(EnderCrystal crystal)
    {
        if (crystal == null)
        {
            return null;
        }

        Vector3 crystalPos = crystal.transform.position;
        GameObject template = PickPillarTemplate();

        float offsetY = crystalOffsetFromPillarY;
        float buryDepth = Mathf.Abs(pillarBuriedDepth);
        if (template != null)
        {
            CrystalPillarRiseSettings templateSettings =
                template.GetComponent<CrystalPillarRiseSettings>();
            if (templateSettings != null)
            {
                templateSettings.EnsureRestPose();
                offsetY = templateSettings.CrystalOffsetFromPillarY;
                buryDepth = templateSettings.BuriedDepthBelowCrystal;
            }
        }

        float peakY = crystalPos.y - offsetY;
        float buriedY = peakY - buryDepth;

        GameObject pillarGo;
        if (template != null)
        {
            // Instantiate even if the template is a scene object (e.g. CrystalPillar_1).
            bool wasActive = template.activeSelf;
            pillarGo = Instantiate(template, transform);
            pillarGo.name = "PracticePillar_" + (template.name);
            pillarGo.SetActive(true);
            StripFightCrystalParts(pillarGo);
            if (!wasActive)
            {
                // Leave original alone; copy is active for practice.
            }
        }
        else
        {
            pillarGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pillarGo.name = "PracticePillar";
            pillarGo.transform.SetParent(transform, true);
            float halfHeight = Mathf.Max(0.25f, pillarHeight * 0.5f);
            pillarGo.transform.localScale = new Vector3(pillarRadius * 2f, halfHeight, pillarRadius * 2f);

            Renderer renderer = pillarGo.GetComponent<Renderer>();
            if (renderer != null)
            {
                Material mat = renderer.material;
                if (mat.HasProperty("_Color"))
                {
                    mat.color = pillarColor;
                }
                else if (mat.HasProperty("_BaseColor"))
                {
                    mat.SetColor("_BaseColor", pillarColor);
                }
            }
        }

        pillarGo.transform.position = new Vector3(crystalPos.x, buriedY, crystalPos.z);
        pillarGo.transform.rotation = Quaternion.identity;
        SetLayerRecursive(pillarGo.transform, pillarLayer);

        // Practice drives rise/sink manually — keep settings for offset data only.
        CrystalPillarRiseSettings instanceSettings =
            pillarGo.GetComponent<CrystalPillarRiseSettings>();
        if (instanceSettings != null)
        {
            instanceSettings.enabled = false;
        }

        return new PracticePillar
        {
            transform = pillarGo.transform,
            buriedY = buriedY,
            peakY = peakY
        };
    }

    private GameObject PickPillarTemplate()
    {
        if (pillarTemplates != null && pillarTemplates.Length > 0)
        {
            int usable = 0;
            for (int i = 0; i < pillarTemplates.Length; i++)
            {
                if (pillarTemplates[i] != null)
                {
                    usable++;
                }
            }

            if (usable > 0)
            {
                int pick = Random.Range(0, usable);
                for (int i = 0; i < pillarTemplates.Length; i++)
                {
                    if (pillarTemplates[i] == null)
                    {
                        continue;
                    }

                    if (pick == 0)
                    {
                        return pillarTemplates[i];
                    }

                    pick--;
                }
            }
        }

        return pillarPrefab;
    }

    private static void StripFightCrystalParts(GameObject pillarGo)
    {
        if (pillarGo == null)
        {
            return;
        }

        EnderCrystal[] crystals = pillarGo.GetComponentsInChildren<EnderCrystal>(true);
        for (int i = 0; i < crystals.Length; i++)
        {
            if (crystals[i] != null)
            {
                Destroy(crystals[i].gameObject);
            }
        }
    }

    private IEnumerator RisePillarRoutine(PracticePillar pillar)
    {
        if (pillar == null || pillar.transform == null)
        {
            yield break;
        }

        Vector3 pos = pillar.transform.position;
        pos.y = pillar.buriedY;
        pillar.transform.position = pos;

        float speed = Mathf.Max(0.1f, pillarRiseSpeed);
        while (pillar.transform != null
            && !pillar.cancelRise
            && pos.y < pillar.peakY - 0.001f)
        {
            pos.y = Mathf.MoveTowards(pos.y, pillar.peakY, speed * Time.deltaTime);
            pillar.transform.position = pos;
            yield return null;
        }

        if (pillar.transform != null && !pillar.cancelRise)
        {
            pos.y = pillar.peakY;
            pillar.transform.position = pos;
        }
    }

    private void BeginSinkPillar(EnderCrystal crystal)
    {
        if (crystal == null || !crystalPillars.TryGetValue(crystal, out PracticePillar pillar))
        {
            return;
        }

        crystalPillars.Remove(crystal);
        BeginSinkPillarInstance(pillar);
    }

    private void BeginSinkPillarInstance(PracticePillar pillar)
    {
        if (pillar == null || pillar.transform == null)
        {
            return;
        }

        pillar.cancelRise = true;
        if (sinkingPillars.Contains(pillar))
        {
            return;
        }

        sinkingPillars.Add(pillar);
        StartCoroutine(SinkPillarRoutine(pillar));
    }

    private void SinkAllPillars(List<PracticePillar> pillars)
    {
        if (pillars == null)
        {
            return;
        }

        for (int i = 0; i < pillars.Count; i++)
        {
            BeginSinkPillarInstance(pillars[i]);
        }
    }

    private IEnumerator SinkPillarRoutine(PracticePillar pillar)
    {
        if (pillar == null || pillar.transform == null)
        {
            if (pillar != null)
            {
                sinkingPillars.Remove(pillar);
            }

            yield break;
        }

        Vector3 pos = pillar.transform.position;
        float speed = Mathf.Max(0.1f, pillarSinkSpeed);
        while (pillar.transform != null && pos.y > pillar.buriedY + 0.001f)
        {
            pos.y = Mathf.MoveTowards(pos.y, pillar.buriedY, speed * Time.deltaTime);
            pillar.transform.position = pos;
            yield return null;
        }

        sinkingPillars.Remove(pillar);
        if (pillar.transform != null)
        {
            Destroy(pillar.transform.gameObject);
        }
    }

    private static void SetLayerRecursive(Transform root, int layer)
    {
        if (root == null)
        {
            return;
        }

        root.gameObject.layer = layer;
        for (int i = 0; i < root.childCount; i++)
        {
            SetLayerRecursive(root.GetChild(i), layer);
        }
    }

    private EnderCrystal CreateCrystalAt(Vector3 point)
    {
        EnderCrystal crystal;
        if (crystalPrefab != null)
        {
            crystal = Instantiate(crystalPrefab, point, Random.rotation, transform);
        }
        else
        {
            GameObject go = new GameObject("PracticeCrystal");
            go.transform.SetParent(transform, true);
            go.transform.position = point;
            go.transform.rotation = Random.rotation;

            SphereCollider col = go.AddComponent<SphereCollider>();
            col.isTrigger = false;
            col.radius = 0.55f;

            crystal = go.AddComponent<EnderCrystal>();
        }

        return crystal;
    }

    private void ApplyPracticeCrystalSetup(EnderCrystal crystal, bool preview)
    {
        if (crystal == null)
        {
            return;
        }

        crystal.BindForPractice(this, preview);
        crystal.PreparePracticeCageSpawnHidden();
    }

    private static void BeginCrystalCageGrow(EnderCrystal crystal)
    {
        if (crystal == null)
        {
            return;
        }

        crystal.BeginPracticeCageEmerge();
        crystal.SetPracticeCageEmergeProgress(0f);
    }

    private bool TryRandomSpawnPoint(out Vector3 point, EnderCrystal ignore = null)
    {
        Bounds bounds = GetSpawnBounds();
        if (bounds.size.sqrMagnitude < 1e-6f)
        {
            point = transform.position + Vector3.forward * 3f + Vector3.up * 1.5f;
            return IsValidSpawnPoint(point, ignore);
        }

        const int maxAttempts = 48;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            Vector3 candidate = new Vector3(
                Random.Range(bounds.min.x, bounds.max.x),
                Random.Range(bounds.min.y, bounds.max.y),
                Random.Range(bounds.min.z, bounds.max.z));

            if (IsValidSpawnPoint(candidate, ignore))
            {
                point = candidate;
                return true;
            }
        }

        point = default;
        return false;
    }

    private bool IsValidSpawnPoint(Vector3 point, EnderCrystal ignore)
    {
        return IsFarEnoughHorizontally(point, ignore)
               && IsClearOfViewSectors(point, ignore);
    }

    private bool IsFarEnoughHorizontally(Vector3 point, EnderCrystal ignore)
    {
        if (minHorizontalDistance <= 0f)
        {
            return true;
        }

        float minDistSq = minHorizontalDistance * minHorizontalDistance;
        List<Vector3> occupied = CollectOccupiedPositions(ignore);
        for (int i = 0; i < occupied.Count; i++)
        {
            if (HorizontalDistanceSq(occupied[i], point) < minDistSq)
            {
                return false;
            }
        }

        return true;
    }

    private bool IsClearOfViewSectors(Vector3 point, EnderCrystal ignore)
    {
        if (minViewSeparationDegrees <= 0.01f)
        {
            return true;
        }

        Vector3 player = ResolvePlayerViewOrigin();
        Vector3 toCandidate = point - player;
        if (viewSeparationHorizontalOnly)
        {
            toCandidate.y = 0f;
        }

        if (toCandidate.sqrMagnitude < 0.01f)
        {
            return false;
        }

        float minAngle = minViewSeparationDegrees;
        List<Vector3> occupied = CollectOccupiedPositions(ignore);
        for (int i = 0; i < occupied.Count; i++)
        {
            Vector3 toOther = occupied[i] - player;
            if (viewSeparationHorizontalOnly)
            {
                toOther.y = 0f;
            }

            if (toOther.sqrMagnitude < 0.01f)
            {
                continue;
            }

            if (Vector3.Angle(toCandidate, toOther) < minAngle)
            {
                return false;
            }
        }

        return true;
    }

    private List<Vector3> CollectOccupiedPositions(EnderCrystal ignore)
    {
        List<Vector3> occupied = new List<Vector3>(16);

        for (int i = 0; i < liveCrystals.Count; i++)
        {
            EnderCrystal other = liveCrystals[i];
            if (other == null || other == ignore)
            {
                continue;
            }

            occupied.Add(GetCrystalWorldPosition(other));
        }

        for (int i = 0; i < previewCrystals.Count; i++)
        {
            EnderCrystal other = previewCrystals[i];
            if (other == null || other == ignore)
            {
                continue;
            }

            occupied.Add(GetCrystalWorldPosition(other));
        }

        for (int i = 0; i < emergingCrystals.Count; i++)
        {
            EnderCrystal other = emergingCrystals[i];
            if (other == null || other == ignore)
            {
                continue;
            }

            occupied.Add(GetCrystalWorldPosition(other));
        }

        foreach (KeyValuePair<EnderCrystal, PracticePillar> pair in crystalPillars)
        {
            if (pair.Key == ignore || pair.Value == null || pair.Value.transform == null)
            {
                continue;
            }

            occupied.Add(pair.Value.transform.position);
        }

        for (int i = 0; i < sinkingPillars.Count; i++)
        {
            PracticePillar pillar = sinkingPillars[i];
            if (pillar == null || pillar.transform == null)
            {
                continue;
            }

            occupied.Add(pillar.transform.position);
        }

        return occupied;
    }

    private static Vector3 ResolvePlayerViewOrigin()
    {
        Vector3 aim = PlayEnvironment.ResolvePlayerAimPosition();
        if (aim.sqrMagnitude > 1e-6f || PlayEnvironment.ResolvePlayerTransform() != null)
        {
            return aim;
        }

        Camera cam = PlayEnvironment.ResolveViewCamera();
        if (cam != null)
        {
            return cam.transform.position;
        }

        return Vector3.zero;
    }

    private static float HorizontalDistanceSq(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return dx * dx + dz * dz;
    }

    private static Vector3 GetCrystalWorldPosition(EnderCrystal crystal)
    {
        return crystal != null ? crystal.transform.position : Vector3.zero;
    }

    private void ClearSpawnedCrystals(bool sinkPillars = false)
    {
        pendingLiveSpawns = 0;
        pendingPreviewSpawns = 0;
        promotedFromPreview.Clear();

        // Snapshot live + ghost + already-sinking pillars before crystals are wiped.
        List<PracticePillar> ownedPillars = CollectOwnedPillars();
        crystalPillars.Clear();

        for (int i = 0; i < ownedPillars.Count; i++)
        {
            if (ownedPillars[i] != null)
            {
                ownedPillars[i].cancelRise = true;
            }
        }

        DestroyCrystalList(liveCrystals);
        DestroyCrystalList(previewCrystals);
        DestroyCrystalList(emergingCrystals);
        liveCrystals.Clear();
        previewCrystals.Clear();
        emergingCrystals.Clear();

        if (sinkPillars)
        {
            SinkAllPillars(ownedPillars);
            return;
        }

        DestroyPillarsImmediate(ownedPillars);
    }

    private List<PracticePillar> CollectOwnedPillars()
    {
        List<PracticePillar> owned = new List<PracticePillar>(
            crystalPillars.Count + sinkingPillars.Count);

        foreach (PracticePillar pillar in crystalPillars.Values)
        {
            if (pillar != null && !owned.Contains(pillar))
            {
                owned.Add(pillar);
            }
        }

        for (int i = 0; i < sinkingPillars.Count; i++)
        {
            PracticePillar pillar = sinkingPillars[i];
            if (pillar != null && !owned.Contains(pillar))
            {
                owned.Add(pillar);
            }
        }

        return owned;
    }

    private static void DestroyCrystalList(List<EnderCrystal> crystals)
    {
        for (int i = 0; i < crystals.Count; i++)
        {
            if (crystals[i] != null)
            {
                Destroy(crystals[i].gameObject);
            }
        }
    }

    private void DestroyPillarsImmediate(List<PracticePillar> pillars)
    {
        for (int i = 0; i < pillars.Count; i++)
        {
            PracticePillar pillar = pillars[i];
            if (pillar != null && pillar.transform != null)
            {
                Destroy(pillar.transform.gameObject);
            }
        }

        crystalPillars.Clear();
        sinkingPillars.Clear();
    }

    private void SuppressDragonFight(bool suppress)
    {
        if (dragon == null)
        {
            return;
        }

        if (pillarRise == null)
        {
            pillarRise = FindObjectOfType<CrystalPillarRiseController>();
        }

        if (suppress)
        {
            dragonWasActive = dragon.gameObject.activeSelf;
            if (pillarRise != null)
            {
                pillarRise.SnapBuriedAndDisableCrystals();
            }

            EnsureFightAudioForPractice();
            dragon.gameObject.SetActive(false);
            return;
        }

        dragon.gameObject.SetActive(dragonWasActive);
        RestoreFightAudioAfterPractice();
        if (pillarRise != null)
        {
            pillarRise.SnapBuriedAndDisableCrystals();
        }
    }

    private void EnsureFightAudioForPractice()
    {
        if (fightAudio == null && dragon != null)
        {
            fightAudio = dragon.GetComponent<FightAudio>();
        }

        if (fightAudio == null)
        {
            fightAudio = FightAudio.Resolve();
        }

        if (fightAudio != null)
        {
            fightAudio.ReparentPlaybackTo(transform);
        }
    }

    private void RestoreFightAudioAfterPractice()
    {
        if (fightAudio == null)
        {
            fightAudio = FightAudio.Resolve();
        }

        if (fightAudio != null)
        {
            fightAudio.RestorePlaybackParent();
        }
    }

    private void EnableInfiniteArrowsIfNeeded()
    {
        if (quiver == null || !enableInfiniteArrows)
        {
            return;
        }

        savedInfiniteArrows = quiver.InfiniteArrows;
        quiver.InfiniteArrows = true;
    }

    private void RestoreInfiniteArrows()
    {
        if (quiver == null || !enableInfiniteArrows)
        {
            return;
        }

        quiver.InfiniteArrows = savedInfiniteArrows;
    }

    private void LoadHighScore()
    {
        highScore = 0;
        string path = ScoreFilePath;
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            string text = File.ReadAllText(path).Trim();
            if (int.TryParse(text, out int parsed))
            {
                highScore = Mathf.Max(0, parsed);
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("CrystalTargetPractice: could not read high score — " + ex.Message, this);
        }
    }

    private void SaveHighScoreIfNeeded()
    {
        if (score <= highScore)
        {
            return;
        }

        highScore = score;
        try
        {
            File.WriteAllText(ScoreFilePath, highScore.ToString());
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("CrystalTargetPractice: could not save high score — " + ex.Message, this);
        }
    }

    private string ScoreFilePath => Path.Combine(Application.persistentDataPath, highScoreFileName);

    private void ResolveReferences()
    {
        if (dragon == null)
        {
            dragon = FindObjectOfType<DragonBoss>();
        }

        if (equipStart == null)
        {
            equipStart = FindObjectOfType<DragonFightEquipStart>();
        }

        if (fightUI == null)
        {
            fightUI = FindObjectOfType<DragonFightUI>();
        }

        if (quiver == null)
        {
            quiver = FindObjectOfType<ArrowQuiver>();
        }

        if (fightAudio == null && dragon != null)
        {
            fightAudio = dragon.GetComponent<FightAudio>();
        }

        if (spawnArea == null)
        {
            Transform found = transform.Find("SpawnArea");
            if (found != null)
            {
                spawnArea = found;
            }
        }
    }

    private bool TryRandomPointInSpawnArea(out Vector3 point)
    {
        Bounds bounds = GetSpawnBounds();
        if (bounds.size.sqrMagnitude < 1e-6f)
        {
            point = transform.position + Vector3.forward * 3f + Vector3.up * 1.5f;
            return true;
        }

        point = new Vector3(
            Random.Range(bounds.min.x, bounds.max.x),
            Random.Range(bounds.min.y, bounds.max.y),
            Random.Range(bounds.min.z, bounds.max.z));
        return true;
    }

    private Bounds GetSpawnBounds()
    {
        if (spawnArea == null)
        {
            return new Bounds(transform.position + Vector3.forward * 4f + Vector3.up * 1.5f, new Vector3(4f, 2f, 2f));
        }

        Collider col = spawnArea.GetComponent<Collider>();
        if (col != null)
        {
            return col.bounds;
        }

        Vector3 center = spawnArea.position;
        Vector3 size = spawnArea.lossyScale;
        if (size.x < 0.1f) size.x = 2f;
        if (size.y < 0.1f) size.y = 2f;
        if (size.z < 0.1f) size.z = 2f;
        return new Bounds(center, size);
    }

    private static void ClearLooseArrows()
    {
#if UNITY_2023_1_OR_NEWER
        ArrowProjectile[] arrows = FindObjectsByType<ArrowProjectile>(FindObjectsSortMode.None);
#else
        ArrowProjectile[] arrows = FindObjectsOfType<ArrowProjectile>();
#endif
        for (int i = 0; i < arrows.Length; i++)
        {
            ArrowProjectile arrow = arrows[i];
            if (arrow == null)
            {
                continue;
            }

            if (!arrow.IsInFlight && !arrow.HasStuck)
            {
                continue;
            }

            Destroy(arrow.gameObject);
        }
    }

    [ContextMenu("Create Spawn Area")]
    private void CreateSpawnArea()
    {
        if (spawnArea != null)
        {
            return;
        }

        GameObject area = new GameObject("SpawnArea");
        area.transform.SetParent(transform, false);
        area.transform.localPosition = new Vector3(0f, 1.5f, 5f);
        area.transform.localRotation = Quaternion.identity;
        area.transform.localScale = new Vector3(4f, 2.5f, 2f);
        BoxCollider box = area.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = Vector3.one;
        spawnArea = area.transform;

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    private void OnDrawGizmosSelected()
    {
        Bounds bounds = Application.isPlaying ? GetSpawnBounds() : PreviewSpawnBounds();
        Gizmos.color = gizmoColor;
        Gizmos.DrawCube(bounds.center, bounds.size);
        Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 0.9f);
        Gizmos.DrawWireCube(bounds.center, bounds.size);
    }

    private Bounds PreviewSpawnBounds()
    {
        if (spawnArea == null)
        {
            return new Bounds(transform.position + Vector3.forward * 4f + Vector3.up * 1.5f, new Vector3(4f, 2f, 2f));
        }

        Collider col = spawnArea.GetComponent<Collider>();
        if (col != null)
        {
            return col.bounds;
        }

        return new Bounds(spawnArea.position, spawnArea.lossyScale);
    }
}
