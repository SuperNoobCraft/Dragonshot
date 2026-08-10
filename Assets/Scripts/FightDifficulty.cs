using UnityEngine;

/// <summary>
/// Pre-fight difficulty chosen by which quiver the player straps on.
/// </summary>
public enum FightDifficulty
{
    Easy = 0,
    Normal = 1,
    Hard = 2
}

/// <summary>
/// Per-difficulty dragon HP, fight time limit, and fireball pacing (set on DragonBoss).
/// </summary>
[System.Serializable]
public struct DifficultyFightTuning
{
    [Min(1)] public int maxHp;
    [Tooltip("Fight time limit in seconds.")]
    [Min(1f)] public float roundSeconds;
    [Tooltip("Seconds between fireball spawn attempts.")]
    [Min(1f)] public float fireballInterval;
}
