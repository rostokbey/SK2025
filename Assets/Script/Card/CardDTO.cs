using System;
using UnityEngine;

[Serializable]
public class CardDTO
{
    public string id;
    public string cardName;
    public string className;
    public string race;
    public string combo;

    public int baseHP;
    public int baseDamage;
    public int baseDex;  // was baseDEX
    public int dex;


    public string rarity;
    public string ability;
    public string passive;

    public int level;
    public int xp;
    public int skillCooldownMax;

    // İsteğe bağlı: sprite ve prefab yolları (Resources için isim/path)
    public string spritePath;
    public string prefabPath;
}