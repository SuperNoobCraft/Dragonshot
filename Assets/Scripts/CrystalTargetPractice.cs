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
    [SerializeField, Min(0f)] private float minPreviewDistance = 2.5f;
    [SerializeField] private bool enableInfiniteArrows = true;
    [SerializeField] private bool clearFlyingArrowsOnRoundEnd = true;
    [Tooltip("Ghost cage grow-in (hard-mode shell phase, sped up).")]
    [SerializeField, Min(0.05f)] private float cageEmergeSeconds = 0.65f;
    [Tooltip("Inner orb grow when a ghost cage becomes live (hard-mode inner phase, sped up).")]
    [SerializeField, Min(0.05f)] private float innerEmergeSeconds = 0.45f;

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
    private readonly List<EnderCrystal> liveCrystals = new List<EnderCrystal>(4);
    private readonly List<EnderCrystal> previewCrystals = new List<EnderCrystal>(4);
    private readonly List<EnderCrystal> emergingCrystals = new List<EnderCrystal>(4);

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
    }

    private void Update()
    {
        if (phase != Phase.Playing)
        {
            return;
        }

        timeRemaining -= Time.deltaTime;
        RefreshPlayingUi();

        liveCrystals.RemoveAll(c => c == null);
        previewCrystals.RemoveAll(c => c == null);
        MaintainCrystalCounts();

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
        ClearSpawnedCrystals();
        RestoreInfiniteArrows();
        SuppressDragonFight(false);
    }

    private void StartRound()
    {
        StopAllCoroutines();
        ClearSpawnedCrystals();
        score = 0;
        timeRemaining = roundSeconds;
        phase = Phase.Playing;
        EnableInfiniteArrowsIfNeeded();
        RefreshPlayingUi();
        MaintainCrystalCounts();
    }

    private void EndRound()
    {
        phase = Phase.Results;
        timeRemaining = 0f;
        ClearSpawnedCrystals();
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

        while (liveCrystals.Count + pendingLiveSpawns < maxCrystalsAtOnce)
        {
            SpawnCrystal(preview: false);
        }

        while (previewCrystals.Count < previewCrystalCount)
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

        // Always start cage-only; ghosts grow the shell, live crystals then grow the inner orb.
        ApplyPracticeCrystalSetup(crystal, preview: true);
        BeginCrystalCageGrow(crystal);
        if (preview)
        {
            previewCrystals.Add(crystal);
            StartCoroutine(CompleteCageEmergeRoutine(crystal));
            return;
        }

        pendingLiveSpawns++;
        emergingCrystals.Add(crystal);
        StartCoroutine(CompleteLiveSpawnRoutine(crystal));
    }

    private IEnumerator CompleteCageEmergeRoutine(EnderCrystal crystal)
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
            if (crystal == null)
            {
                yield break;
            }

            crystal.SetPracticeCageEmergeProgress(Mathf.Clamp01(elapsed / duration));
            yield return null;
        }

        if (crystal == null)
        {
            yield break;
        }

        crystal.CompletePracticeCagePreview();
    }

    private IEnumerator CompleteLiveSpawnRoutine(EnderCrystal crystal)
    {
        if (crystal == null)
        {
            pendingLiveSpawns = Mathf.Max(0, pendingLiveSpawns - 1);
            emergingCrystals.Remove(crystal);
            yield break;
        }

        float cageDuration = Mathf.Max(0.05f, cageEmergeSeconds);
        float cageElapsed = 0f;
        while (cageElapsed < cageDuration)
        {
            cageElapsed += Time.deltaTime;
            if (crystal == null)
            {
                pendingLiveSpawns = Mathf.Max(0, pendingLiveSpawns - 1);
                emergingCrystals.Remove(crystal);
                yield break;
            }

            crystal.SetPracticeCageEmergeProgress(Mathf.Clamp01(cageElapsed / cageDuration));
            yield return null;
        }

        if (crystal == null)
        {
            pendingLiveSpawns = Mathf.Max(0, pendingLiveSpawns - 1);
            emergingCrystals.Remove(crystal);
            yield break;
        }

        crystal.CompletePracticeCageShell(asPreview: false);
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
        if (!liveCrystals.Contains(crystal))
        {
            liveCrystals.Add(crystal);
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

        yield return null;

        if (crystal == null)
        {
            pendingLiveSpawns = Mathf.Max(0, pendingLiveSpawns - 1);
            emergingCrystals.Remove(crystal);
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
        if (addToLive && !liveCrystals.Contains(crystal))
        {
            liveCrystals.Add(crystal);
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
            return IsFarEnoughFromOtherCrystals(point, ignore);
        }

        if (minPreviewDistance <= 0f)
        {
            point = new Vector3(
                Random.Range(bounds.min.x, bounds.max.x),
                Random.Range(bounds.min.y, bounds.max.y),
                Random.Range(bounds.min.z, bounds.max.z));
            return true;
        }

        const int maxAttempts = 32;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            Vector3 candidate = new Vector3(
                Random.Range(bounds.min.x, bounds.max.x),
                Random.Range(bounds.min.y, bounds.max.y),
                Random.Range(bounds.min.z, bounds.max.z));

            if (IsFarEnoughFromOtherCrystals(candidate, ignore))
            {
                point = candidate;
                return true;
            }
        }

        point = default;
        return false;
    }

    private bool IsFarEnoughFromOtherCrystals(Vector3 point, EnderCrystal ignore)
    {
        if (minPreviewDistance <= 0f)
        {
            return true;
        }

        float minDistSq = minPreviewDistance * minPreviewDistance;

        for (int i = 0; i < liveCrystals.Count; i++)
        {
            EnderCrystal other = liveCrystals[i];
            if (other == null || other == ignore)
            {
                continue;
            }

            if ((GetCrystalWorldPosition(other) - point).sqrMagnitude < minDistSq)
            {
                return false;
            }
        }

        for (int i = 0; i < previewCrystals.Count; i++)
        {
            EnderCrystal other = previewCrystals[i];
            if (other == null || other == ignore)
            {
                continue;
            }

            if ((GetCrystalWorldPosition(other) - point).sqrMagnitude < minDistSq)
            {
                return false;
            }
        }

        for (int i = 0; i < emergingCrystals.Count; i++)
        {
            EnderCrystal other = emergingCrystals[i];
            if (other == null || other == ignore)
            {
                continue;
            }

            if ((GetCrystalWorldPosition(other) - point).sqrMagnitude < minDistSq)
            {
                return false;
            }
        }

        return true;
    }

    private static Vector3 GetCrystalWorldPosition(EnderCrystal crystal)
    {
        return crystal != null ? crystal.transform.position : Vector3.zero;
    }

    private void ClearSpawnedCrystals()
    {
        pendingLiveSpawns = 0;

        for (int i = 0; i < liveCrystals.Count; i++)
        {
            if (liveCrystals[i] != null)
            {
                Destroy(liveCrystals[i].gameObject);
            }
        }

        liveCrystals.Clear();

        for (int i = 0; i < previewCrystals.Count; i++)
        {
            if (previewCrystals[i] != null)
            {
                Destroy(previewCrystals[i].gameObject);
            }
        }

        previewCrystals.Clear();

        for (int i = 0; i < emergingCrystals.Count; i++)
        {
            if (emergingCrystals[i] != null
                && !liveCrystals.Contains(emergingCrystals[i])
                && !previewCrystals.Contains(emergingCrystals[i]))
            {
                Destroy(emergingCrystals[i].gameObject);
            }
        }

        emergingCrystals.Clear();
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
