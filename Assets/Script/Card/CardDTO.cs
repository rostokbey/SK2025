using System;

[Serializable]
public class CardDTO
{
    public string id;
    public string jti;
    public string cardName;

    public int baseHP;
    public int baseStr;
    public int baseDef;

    public string rarity;
    public string ability;
    public string passive;

    public int level;
    public int xp;
    public int skillCooldownMax;

    public string className;
    public string race;
    public string combo;

    public string spritePath;
    public string prefabPath;
}