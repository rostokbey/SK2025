using UnityEngine;

[System.Serializable]
public class CardData
{
    // ---------------- ENUM ----------------
    public enum CardType
    {
        Attack,
        Defense,
        Buff,
        Debuff,
        Utility,
        Special
    }

    // ---------------- KİMLİK ----------------
    public string id;
    public string cardId;
    public string cardName;

    // ---------------- BASE STAT (kalıcı) ----------------
    public int baseHP;
    public int baseStr;
    public int baseDef;

    // ---------------- CURRENT STAT (combat) ----------------
    public int hp;
    public int str;
    public int def;

    // ---------------- META ----------------
    public string rarity;
    public string ability;
    public string passive;

    public int level;
    public int xp;
    public int skillCooldownMax;

    public string className;
    public string race;
    public string combo;

    // ---------------- ASSET ----------------
    public Sprite characterSprite;
    public GameObject characterPrefab3D;
    public string prefab;

    // ---------------- TYPE ----------------
    public CardType type;

    // ---------------- MEKANİK ----------------
    public int damage;
    public int heal;
    public int duration;

    public float critChance;
    public float critDamage;
    public float reflect;

    public bool stun;
    public bool banish;
    public int summonCount;
    public bool combine;
    public bool disableAttack;

    public CardData(string id, string name, string ability)
    {
        this.id = id;
        this.cardId = id;
        this.cardName = name;
        this.ability = ability;

        this.baseHP = 0;
        this.baseStr = 0;
        this.baseDef = 0;

        ResetStats();

        this.rarity = "Common";
        this.passive = "";
        this.level = 1;
        this.xp = 0;
        this.skillCooldownMax = 0;

        this.characterSprite = null;
        this.characterPrefab3D = null;
        this.prefab = null;

        this.type = CardType.Special;
    }

    // ---------------- CONSTRUCTOR ----------------
    public CardData(
        string id, string name,
        int baseHP, int baseStr, int baseDef,
        string rarity, string ability, string passive,
        int level, int xp, int skillCdMax,
        Sprite sp, GameObject prefab3d, string prefabPath = null)
    {
        this.id = id;
        this.cardId = id;
        this.cardName = name;

        this.baseHP = baseHP;
        this.baseStr = baseStr;
        this.baseDef = baseDef;

        ResetStats(); // 🔥 önemli

        this.rarity = rarity;
        this.ability = ability;
        this.passive = passive;

        this.level = level;
        this.xp = xp;
        this.skillCooldownMax = skillCdMax;

        characterSprite = sp;
        characterPrefab3D = prefab3d;
        prefab = prefabPath;

        type = CardType.Attack; // default
    }

    // ---------------- STAT RESET ----------------
    public void ResetStats()
    {
        hp = baseHP;
        str = baseStr;
        def = baseDef;
    }

    // ---------------- CLONE ----------------
    public CardData Clone()
    {
        return new CardData(
            id, cardName,
            baseHP, baseStr, baseDef,
            rarity, ability, passive,
            level, xp, skillCooldownMax,
            characterSprite, characterPrefab3D, prefab
        )
        {
            className = this.className,
            race = this.race,
            combo = this.combo,

            // combat state
            hp = this.hp,
            str = this.str,
            def = this.def,

            // type
            type = this.type,

            // mekanik alanlar
            damage = this.damage,
            heal = this.heal,
            duration = this.duration,
            critChance = this.critChance,
            critDamage = this.critDamage,
            reflect = this.reflect,
            stun = this.stun,
            banish = this.banish,
            summonCount = this.summonCount,
            combine = this.combine,
            disableAttack = this.disableAttack
        };
    }
}