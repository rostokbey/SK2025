using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Bir tarafın (player YA DA enemy) sahasını 3 sıra (Front/Mid/Back) x N sütun
/// olarak tutan, tamamen SUNUCU TARAFINDA çalışan mantıksal veri yapısı.
///
/// Sütun sayısı takım boyutuna göre değişir (tasarım dokümanına göre):
///   1v1: 6 sütun   2v2: 8 sütun (takım başına)   3v3: 10 sütun (takım başına)
/// Şu an ağ katmanı (Relay/BattleManager) yalnızca 1v1'i destekliyor; 2v2/3v3
/// için matchmaking henüz yok (bkz. README - "backend gerektirmeyenler" notu).
/// Bu sınıf sütun sayısını parametre olarak aldığı için o kısım eklendiğinde
/// yeniden yazılmasına gerek kalmayacak.
///
/// ÖNEMLİ: Bu, güvenin istemciye VERİLMEDİĞİ bir sistemdir - hangi karakterin
/// hangi hücrede olduğu, hedef çözümlemesi (kim kimi bloklar) tamamen
/// BattleManager (sunucu) içinde hesaplanır; istemciler sadece sonucu görür.
/// </summary>
public class BattleGrid
{
    public readonly int Columns;
    private readonly Character[,] _slots; // [row, column]

    public BattleGrid(int columns)
    {
        Columns = Math.Max(1, columns);
        _slots = new Character[3, Columns];
    }

    public bool IsValidCell(BattleRow row, int column) =>
        column >= 0 && column < Columns;

    public bool IsOccupied(BattleRow row, int column) =>
        IsValidCell(row, column) && _slots[(int)row, column] != null;

    public Character GetOccupant(BattleRow row, int column) =>
        IsValidCell(row, column) ? _slots[(int)row, column] : null;

    /// <summary>Karakteri belirli bir hücreye yerleştirir. Hücre doluysa false döner.</summary>
    public bool TryOccupy(Character character, BattleRow row, int column)
    {
        if (character == null || !IsValidCell(row, column) || IsOccupied(row, column))
            return false;

        _slots[(int)row, column] = character;
        character.SetGridPosition(row, column);
        return true;
    }

    /// <summary>
    /// Boş bir hücre bulup karakteri oraya yerleştirir. Sırayla Front -> Mid -> Back,
    /// her sırada soldan sağa ilk boş hücre denenir (kart tek tıkla oynatıldığında -
    /// SpawnCharacter - kullanılır; sürükle-bırak yerleşimde hedef hücre bellidir).
    /// </summary>
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
        {
            for (int c = 0; c < Columns; c++)
            {
                if (_slots[r, c] == character)
                    _slots[r, c] = null;
            }
        }
    }

    /// <summary>
    /// Belirli bir 3D dünya konumuna en yakın hücreyi, verilen sınırlar (zone.bounds)
    /// içinde X eksenini sütuna, Z eksenini sıraya bölerek hesaplar.
    /// frontRowIsMaxZ: sahnede "ön hat"ın Z ekseninde min mi max mı olduğunu belirtir
    /// (bkz. BattleManager üzerindeki [Tooltip] - sahnenizi test edip gerekirse çevirin).
    /// </summary>
    public static (BattleRow row, int column) ResolveCellFromWorldPosition(
        UnityEngine.Bounds bounds, UnityEngine.Vector3 worldPos, int columns, bool frontRowIsMaxZ)
    {
        float tX = UnityEngine.Mathf.InverseLerp(bounds.min.x, bounds.max.x, worldPos.x);
        float tZ = UnityEngine.Mathf.InverseLerp(bounds.min.z, bounds.max.z, worldPos.z);

        int column = UnityEngine.Mathf.Clamp(
            UnityEngine.Mathf.FloorToInt(tX * columns), 0, columns - 1);

        float rowT = frontRowIsMaxZ ? (1f - tZ) : tZ; // 0 = Front, 1 = Back
        int rowIndex = UnityEngine.Mathf.Clamp(UnityEngine.Mathf.FloorToInt(rowT * 3f), 0, 2);

        return ((BattleRow)rowIndex, column);
    }

    /// <summary>
    /// Bir sütunda saldırı hedefi çözümlemesi. Kural: önden arkaya doğru ilk canlı
    /// karakter hedeftir; önü boşsa bir sonraki sıraya geçilir ("front boşsa arka
    /// hedeflenebilir" kuralı). targetingMode özel yeteneklerin bu kuralı
    /// esnetmesini/kırmasını sağlar.
    /// </summary>
    public Character ResolveTarget(int column, AbilityTargetingMode targetingMode)
    {
        if (!IsValidCell(BattleRow.Front, column))
            return null;

        switch (targetingMode)
        {
            case AbilityTargetingMode.IgnoreFrontline:
                // Arka hattı doğrudan hedefleyebilir: Back -> Mid -> Front sırasıyla ilk canlıyı bul.
                return FirstAliveInColumn(column, BattleRow.Back, BattleRow.Mid, BattleRow.Front);

            case AbilityTargetingMode.AnyTarget:
                // Sütun kuralını tamamen yok sayar: bu sütunda kimse yoksa tüm sahadaki
                // ilk canlıyı döner (çağıran taraf, "herhangi bir hedefi seç" UI'ından
                // gerçek hedefi zaten belirlemiş olmalı; bu sadece güvenli bir fallback'tir).
                return FirstAliveInColumn(column, BattleRow.Front, BattleRow.Mid, BattleRow.Back)
                       ?? AnyAliveOnGrid();

            case AbilityTargetingMode.AreaOfEffect:
                // AoE'de tek bir "ana" hedef döneriz (görsel/log amaçlı); asıl hasar
                // dağıtımı çağıran tarafta GetAllAliveInColumn / GetAllAlive ile yapılmalı.
                return FirstAliveInColumn(column, BattleRow.Front, BattleRow.Mid, BattleRow.Back);

            case AbilityTargetingMode.Normal:
            default:
                return FirstAliveInColumn(column, BattleRow.Front, BattleRow.Mid, BattleRow.Back);
        }
    }

    private Character FirstAliveInColumn(int column, params BattleRow[] order)
    {
        foreach (var row in order)
        {
            var occupant = GetOccupant(row, column);
            if (occupant != null && occupant.IsAlive)
                return occupant;
        }
        return null;
    }

    private Character AnyAliveOnGrid()
    {
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < Columns; c++)
                if (_slots[r, c] != null && _slots[r, c].IsAlive)
                    return _slots[r, c];
        return null;
    }

    public List<Character> GetAllAliveInColumn(int column)
    {
        var result = new List<Character>();
        if (!IsValidCell(BattleRow.Front, column)) return result;

        foreach (BattleRow r in new[] { BattleRow.Front, BattleRow.Mid, BattleRow.Back })
        {
            var occupant = GetOccupant(r, column);
            if (occupant != null && occupant.IsAlive)
                result.Add(occupant);
        }
        return result;
    }

    public List<Character> GetAllAlive()
    {
        var result = new List<Character>();
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < Columns; c++)
                if (_slots[r, c] != null && _slots[r, c].IsAlive)
                    result.Add(_slots[r, c]);
        return result;
    }
}
