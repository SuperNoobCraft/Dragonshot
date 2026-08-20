using UnityEngine;

/// <summary>
/// Secret arcade hub: panel click at START → no-scope bow → pick Target Test or Survival quiver.
/// </summary>
public class ArcadeMode : MonoBehaviour
{
    [SerializeField] private DragonFightEquipStart equipStart;
    [SerializeField] private DragonFightUI fightUI;

    public bool IsActive => equipStart != null && equipStart.IsArcadeModeActive;

    public bool CanEnterFromPanel =>
        !IsActive
        && equipStart != null
        && !equipStart.IsBowEquipped
        && equipStart.CanStartArcadeEntry;

    public bool TryEnterFromPanelClick()
    {
        if (!CanEnterFromPanel)
        {
            return false;
        }

        if (equipStart != null)
        {
            equipStart.EnterArcadeMode();
        }

        if (fightUI != null)
        {
            fightUI.ShowArcadeWaiting();
        }

        return true;
    }

    private void Awake()
    {
        ResolveReferences();
    }

    private void ResolveReferences()
    {
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
