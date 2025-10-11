using UnityEngine;
using Unity.Netcode;
[System.Serializable]
public class OwnedCard
{
    public CardData card;
    public int level;

    public int GetHealth()
    {
        return card.baseHP + (level * 10); // her seviye +10 HP
    }

    public int GetDamage()
    {
        return card.baseDamage + (level * 2); // her seviye +2 dmg
    }
}
