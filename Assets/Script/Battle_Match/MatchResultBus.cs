using System.Collections.Generic;

public static class MatchResultBus
{
    public static readonly List<CardXpDelta> LastCards = new List<CardXpDelta>();
    public static bool LastWin;
    public static bool LastWasSeason;

    public static void Clear()
    {
        LastCards.Clear();
        LastWin = false;
        LastWasSeason = false;
    }
}
