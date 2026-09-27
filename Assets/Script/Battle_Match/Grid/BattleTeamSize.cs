/// <summary>
/// Maç formatı. Her formatın taraf/takım başına farklı sütun (genişlik) sayısı
/// vardır (tasarım dokümanı: 1v1=6, 2v2=8, 3v3=10). Satır sayısı (3: Ön/Orta/
/// Arka) tüm formatlarda sabittir (bkz. BattleGrid).
///
/// NOT: Şu an ağ katmanı (RelayManager/NetworkManagerController/BattleManager'ın
/// TryResolveSenderSide'ı) sadece 1v1'i (tam 2 client) destekliyor. Bu enum,
/// 2v2/3v3 matchmaking eklendiğinde BattleManager.gridColumns'un buradan
/// (GetColumns()) türetilmesi için hazır bir sözleşme olarak duruyor; o
/// zamana kadar BattleManager.gridColumns Inspector'da sabit 6 olarak
/// bırakılabilir.
/// </summary>
public enum BattleTeamSize
{
    OneVOne,
    TwoVTwo,
    ThreeVThree
}

public static class BattleTeamSizeExtensions
{
    public static int GetColumns(this BattleTeamSize size)
    {
        switch (size)
        {
            case BattleTeamSize.OneVOne: return 6;
            case BattleTeamSize.TwoVTwo: return 8;
            case BattleTeamSize.ThreeVThree: return 10;
            default: return 6;
        }
    }

    public static int GetPlayersPerSide(this BattleTeamSize size)
    {
        switch (size)
        {
            case BattleTeamSize.OneVOne: return 1;
            case BattleTeamSize.TwoVTwo: return 2;
            case BattleTeamSize.ThreeVThree: return 3;
            default: return 1;
        }
    }
}
