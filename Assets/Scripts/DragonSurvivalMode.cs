using System.IO;
using UnityEngine;

/// <summary>
/// Secret arcade survival: dragon keeps shooting fireballs, crystals stay shielded,
/// fireball rate/speed ramp up — survive as long as you can.
/// Enter via <see cref="ArcadeMode"/> → Survival quiver.
/// </summary>
public class DragonSurvivalMode : MonoBehaviour
{
    private enum Phase
    {
        Inactive,
        Playing,
        Results
    }

    [Header("References")]
    [SerializeField] private DragonBoss dragon;
    [SerializeField] private DragonFightEquipStart equipStart;
    [SerializeField] private DragonFightUI fightUI;

    [Header("Fireball Escalation")]
    [SerializeField, Min(0.5f)] private float startFireballInterval = 5.5f;
    [SerializeField, Min(0.3f)] private float minFireballInterval = 1.2f;
    [SerializeField, Min(0.5f)] private float startFireballSpeed = 3.2f;
    [SerializeField, Min(0.5f)] private float maxFireballSpeed = 6.5f;
    [Tooltip("Seconds to reach min interval / max speed.")]
    [SerializeField, Min(10f)] private float escalationRampSeconds = 120f;
    [Tooltip("First shot delay — ignores the dragon's normal pre-fight fireball quiet period.")]
    [SerializeField, Min(0f)] private float firstFireballDelay = 2f;

    [Header("Randomness")]
    [Tooltip("± fraction applied to each fireball cooldown (0.25 = 25% faster or slower).")]
    [SerializeField, Range(0f, 0.5f)] private float fireballIntervalVariance = 0.25f;
    [Tooltip("± fraction applied to each fireball's travel speed.")]
    [SerializeField, Range(0f, 0.5f)] private float fireballSpeedVariance = 0.22f;
    [Tooltip("± fraction applied to dragon path speed; refreshes every few seconds.")]
    [SerializeField, Range(0f, 0.5f)] private float dragonPathSpeedVariance = 0.18f;
    [Tooltip("Random offset (meters) around the player's aim point per shot.")]
    [SerializeField, Min(0f)] private float aimOffsetRadius = 0.4f;
    [Tooltip("Random offset (meters) around the fireball spawn point per shot.")]
    [SerializeField, Min(0f)] private float spawnOffsetRadius = 0.2f;
    [Tooltip("± fraction applied to the first-shot delay.")]
    [SerializeField, Range(0f, 0.5f)] private float firstFireballDelayVariance = 0.2f;

    [Header("Best Time")]
    [SerializeField] private string bestTimeFileName = "dragon_survival_best_seconds.txt";

    private Phase phase = Phase.Inactive;
    private float lastSurvivalSeconds;
    private float bestSurvivalSeconds;

    public bool IsActive => phase != Phase.Inactive;
    public bool IsPlaying => phase == Phase.Playing;
    public float LastSurvivalSeconds => lastSurvivalSeconds;
    public float BestSurvivalSeconds => bestSurvivalSeconds;
    public float StartFireballInterval => startFireballInterval;
    public float MinFireballInterval => minFireballInterval;
    public float StartFireballSpeed => startFireballSpeed;
    public float MaxFireballSpeed => maxFireballSpeed;
    public float EscalationRampSeconds => escalationRampSeconds;
    public float FirstFireballDelay => firstFireballDelay;
    public float FireballIntervalVariance => fireballIntervalVariance;
    public float FireballSpeedVariance => fireballSpeedVariance;
    public float DragonPathSpeedVariance => dragonPathSpeedVariance;
    public float AimOffsetRadius => aimOffsetRadius;
    public float SpawnOffsetRadius => spawnOffsetRadius;
    public float FirstFireballDelayVariance => firstFireballDelayVariance;

    private void Awake()
    {
        ResolveReferences();
        LoadBestTime();
    }

    private void Update()
    {
        if (phase != Phase.Playing || dragon == null)
        {
            return;
        }

        RefreshPlayingUi();
    }

    public void OnQuiverMounted()
    {
        if (phase != Phase.Inactive || dragon == null)
        {
            return;
        }

        phase = Phase.Playing;
        lastSurvivalSeconds = 0f;
        dragon.StartSurvivalFight(this);
        RefreshPlayingUi();
    }

    public void OnPanelClicked()
    {
        ExitMode();
    }

    public void OnPlayerDefeated(float survivedSeconds)
    {
        if (phase != Phase.Playing)
        {
            return;
        }

        lastSurvivalSeconds = Mathf.Max(0f, survivedSeconds);
        SaveBestTimeIfNeeded();
        phase = Phase.Results;

        if (fightUI != null)
        {
            fightUI.ShowSurvivalResults(lastSurvivalSeconds, bestSurvivalSeconds);
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

    public void ForceStopWithoutEquipReset()
    {
        if (dragon != null)
        {
            dragon.EndSurvivalFight();
        }

        phase = Phase.Inactive;
        lastSurvivalSeconds = 0f;
    }

    private void RefreshPlayingUi()
    {
        if (fightUI == null || dragon == null || phase != Phase.Playing)
        {
            return;
        }

        fightUI.ShowSurvivalPlaying(dragon.SurvivalElapsedSeconds, bestSurvivalSeconds);
    }

    private void LoadBestTime()
    {
        bestSurvivalSeconds = 0f;
        string path = BestTimeFilePath;
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            string text = File.ReadAllText(path).Trim();
            if (float.TryParse(text, out float parsed))
            {
                bestSurvivalSeconds = Mathf.Max(0f, parsed);
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("DragonSurvivalMode: could not read best time — " + ex.Message, this);
        }
    }

    private void SaveBestTimeIfNeeded()
    {
        if (lastSurvivalSeconds <= bestSurvivalSeconds)
        {
            return;
        }

        bestSurvivalSeconds = lastSurvivalSeconds;
        try
        {
            File.WriteAllText(BestTimeFilePath, bestSurvivalSeconds.ToString("F2"));
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("DragonSurvivalMode: could not save best time — " + ex.Message, this);
        }
    }

    private string BestTimeFilePath => Path.Combine(Application.persistentDataPath, bestTimeFileName);

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
    }
}
