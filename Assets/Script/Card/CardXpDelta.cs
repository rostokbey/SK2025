using UnityEngine;

[System.Serializable]
public struct CardXpDelta
{
    public CardData card;
    public int oldLevel, newLevel;
    public int oldXp, newXp;
}
