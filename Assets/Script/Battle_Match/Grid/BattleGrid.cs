using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Bir tarafın (player YA DA enemy) sahasını 3 sıra (Front/Mid/Back) x N sütun
/// olarak tutan, tamamen SUNUCU TARAFINDA çalışan mantıksal veri yapısı.
/// </summary>
public class BattleGrid
{
    public readonly int Columns;
    private readonly Character[,] _slots;

    public BattleGrid(int columns)
    {
        Columns = Math.Max(1, columns);
        _slots = new Character[3, Columns];
    }

    public bool IsValidCell(BattleRow row, int column) => column >= 0 && column < Columns;
    public bool IsOccupied(BattleRow row, int column) => IsValidCell(row, column) && _slots[(int)row, column] != null;
    public Character GetOccupant(BattleRow row, int column) => IsValidCell(row, column) ? _slots[(int)row, column] : null;

    public bool TryOccupy(Character character, BattleRow row, int column)
    {
        if (character == null || !IsValidCell(row, column) || IsOccupied(row, column))
            return false;
        _slots[(int)row, column] = character;
        character.SetGridPosition(row, column);
        return true;
    }

    public bool TryOccupyNextFreeSlot(Character character, out BattleRow row, out int column)
    {
        foreach (BattleRow r in new[] { BattleRow.Front, BattleRow.Mid, BattleRow.Back })
        {
            for (int c = 0; c < Columns; c++)
            {
                if (!IsOccupied(r, c))
                {
                    row = r;
                    column = c;
                    TryOccupy(character, r, c);
                    return true;
                }
            }
        }
        row = BattleRow.Front;
        column = -1;
        return false;
    }

    public void Vacate(Character character)
    {
        if (character == null) return;
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < Columns; c++)
                if (_slots[r, c] == character)
                    _slots[r, c] = null;
    }

    public static (BattleRow row, int column) ResolveCellFromWorldPosition(
        UnityEngine.Bounds bounds, UnityEngine.Vector3 worldPos, int columns, bool frontRowIsMaxZ)
    {
        float tX = UnityEngine.Mathf.InverseLerp(bounds.min.x, bounds.max.x, worldPos.x);
        float tZ = UnityEngine.Mathf.InverseLerp(bounds.min.z, bounds.max.z, worldPos.z);
        int column = UnityEngine.Mathf.Clamp(UnityEngine.Mathf.FloorToInt(tX * columns), 0, columns - 1);
        float rowT = frontRowIsMaxZ ? (1f - tZ) : tZ;
        int rowIndex = UnityEngine.Mathf.Clamp(UnityEngine.Mathf.FloorToInt(rowT * 3f), 0, 2);
        return ((BattleRow)rowIndex, column);
    }

    /// <summary>Taunt tekil hedeflemeyi ezer; AoE ise taunt tarafından yönlendirilmez.</summary>
    public Character ResolveTarget(int column, AbilityTargetingMode targetingMode)
    {
        if (!IsValidCell(BattleRow.Front, column))
            return null;

        if (targetingMode != AbilityTargetingMode.AreaOfEffect)
        {
            Character taunter = FirstLivingTaunter();
            if (taunter != null)
                return taunter;
        }

        switch (targetingMode)
        {
            case AbilityTargetingMode.IgnoreFrontline:
                return FirstAliveInColumn(column, BattleRow.Back, BattleRow.Mid, BattleRow.Front);
            case AbilityTargetingMode.AnyTarget:
                return FirstAliveInColumn(column, BattleRow.Front, BattleRow.Mid, BattleRow.Back)
                       ?? AnyVisibleAliveOnGrid();
            case AbilityTargetingMode.AreaOfEffect:
                // Asıl dağıtım yapan taraf AllAliveInColumn/GetAllAlive listesini kullanır.
                return FirstLivingInColumnIncludingInvisible(column, BattleRow.Front, BattleRow.Mid, BattleRow.Back);
            case AbilityTargetingMode.Normal:
            default:
                return FirstAliveInColumn(column, BattleRow.Front, BattleRow.Mid, BattleRow.Back);
        }
    }

    private Character FirstLivingTaunter()
    {
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < Columns; c++)
            {
                Character occupant = _slots[r, c];
                if (occupant != null && occupant.IsAlive && occupant.IsTaunting && !occupant.IsInvisible)
                    return occupant;
            }
        return null;
    }

    private Character FirstAliveInColumn(int column, params BattleRow[] order)
    {
        foreach (BattleRow row in order)
        {
            Character occupant = GetOccupant(row, column);
            if (occupant != null && occupant.IsAlive && !occupant.IsInvisible)
                return occupant;
        }
        return null;
    }

    private Character FirstLivingInColumnIncludingInvisible(int column, params BattleRow[] order)
    {
        foreach (BattleRow row in order)
        {
            Character occupant = GetOccupant(row, column);
            if (occupant != null && occupant.IsAlive)
                return occupant;
        }
        return null;
    }

    private Character AnyVisibleAliveOnGrid()
    {
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < Columns; c++)
                if (_slots[r, c] != null && _slots[r, c].IsAlive && !_slots[r, c].IsInvisible)
                    return _slots[r, c];
        return null;
    }

    public List<Character> GetAllAliveInColumn(int column)
    {
        var result = new List<Character>();
        if (!IsValidCell(BattleRow.Front, column)) return result;
        foreach (BattleRow row in new[] { BattleRow.Front, BattleRow.Mid, BattleRow.Back })
        {
            Character occupant = GetOccupant(row, column);
            if (occupant != null && occupant.IsAlive)
                result.Add(occupant); // AoE can hit Invisible.
        }
        return result;
    }

    public List<Character> GetAllAlive()
    {
        var result = new List<Character>();
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < Columns; c++)
                if (_slots[r, c] != null && _slots[r, c].IsAlive)
                    result.Add(_slots[r, c]); // AoE/team effects include Invisible.
        return result;
    }
}
