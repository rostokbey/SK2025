using UnityEngine;

[System.Serializable]
public class CardData
{
    // Kimlik / İsim
    public string id;
    public string cardName;

    // Base istatistikler
    public int baseHP;
    public int baseDamage;

    // DEX: base ve anlık
    public int baseDex;
    public int dex;

    // Diğer özellikler
    public string rarity;
    public string ability;
    public string passive;

    // RPG ilerleme
    public int level;
    public int xp;
    public int skillCooldownMax;

    // Sınıf/ırk/kombo (opsiyonel)
    public string className;
    public string race;
    public string combo;

    // Görseller / Prefab’lar
    public Sprite characterSprite;
    public GameObject characterPrefab3D;
    public string prefab;

    //CharacterPlacer.cs için eklendi
    public string cardId;

    // ---------------- Yeni Mekanik Alanlar ----------------
    public enum CardType { Attack, Defense, Buff, Debuff, Utility, Special }
    public CardType type;

    public int damage;          // Doğrudan hasar
    public int heal;            // İyileştirme miktarı
    public int duration;        // Etkinin süresi (tur cinsinden)

    public float critChance;    // Kritik ihtimali
    public float critDamage;    // Kritik hasar çarpanı
    public float reflect;       // Yansıtma yüzdesi

    public bool stun;           // Sersemletme/dondurma
    public bool banish;         // Zindana atma
    public int summonCount;     // Klon sayısı
    public bool combine;        // Kombine kartı mı
    public bool disableAttack; // Kart hedefinin saldırmasını engeller


    // ---------------- Constructors ----------------

    // Basitleştirilmiş constructor (Savaş kartları için)
    public CardData(string id, string name, string ability)
    {
        this.id = id;
        this.cardId = id; // Düzeltme burada
        this.cardName = name;
        this.ability = ability;

        this.baseHP = 0;
        this.baseDamage = 0;
        this.baseDex = 0;
        this.dex = 0;
        this.rarity = "Common";
        this.passive = "";
        this.level = 1;
        this.xp = 0;
        this.skillCooldownMax = 0;
        this.characterSprite = null;
        this.characterPrefab3D = null;
        this.prefab = null;
    }

    // Eski sürüm (tek dex parametresi alır)
    public CardData(
        string id, string name, int hp, int dmg, int dex,
        string rarity, string ability, string passive,
        int level, int xp, int skillCdMax,
        Sprite sp, GameObject prefab3d, string prefabPath = null)
    {
        this.id = id;
        this.cardId = id; // Düzeltme burada
        this.cardName = name;

        this.baseHP = hp;
        this.baseDamage = dmg;

        this.baseDex = dex;
        this.dex = dex;

        this.rarity = rarity;
        this.ability = ability;
        this.passive = passive;

        this.level = level;
        this.xp = xp;
        this.skillCooldownMax = skillCdMax;

        this.characterSprite = sp;
        this.characterPrefab3D = prefab3d;
        this.prefab = prefabPath;
    }

    // Yeni sürüm (baseDex + dex birlikte)
    public CardData(
        string id, string name, int hp, int dmg, int baseDex, int dex,
        string rarity, string ability, string passive,
        int level, int xp, int skillCdMax,
        Sprite sp, GameObject prefab3d, string prefabPath = null)
    {
        this.id = id;
        this.cardId = id; // Düzeltme burada
        this.cardName = name;

        this.baseHP = hp;
        this.baseDamage = dmg;

        this.baseDex = baseDex;
        this.dex = dex;

        this.rarity = rarity;
        this.ability = ability;
        this.passive = passive;

        this.level = level;
        this.xp = xp;
        this.skillCooldownMax = skillCdMax;

        this.characterSprite = sp;
        this.characterPrefab3D = prefab3d;
        this.prefab = prefabPath;
    }

    // Clone - DÜZELTME: SİZİN ORİJİNAL, TAM ÇALIŞAN METODUNUZ GERİ GETİRİLDİ
    public CardData Clone()
    {
        return new CardData(
            id, cardName,
            baseHP, baseDamage,
            baseDex, dex,
            rarity, ability, passive,
            level, xp, skillCooldownMax,
            characterSprite, characterPrefab3D, prefab
        )
        {
            className = this.className,
            race = this.race,
            combo = this.combo,

            // Mekanik alanlar da kopyalansın
            type = this.type,
            damage = this.damage,
            heal = this.heal,
            duration = this.duration,
            critChance = this.critChance,
            critDamage = this.critDamage,
            reflect = this.reflect,
            stun = this.stun,
            banish = this.banish,
            summonCount = this.summonCount,
            combine = this.combine
            // cardId zaten constructor'da atandığı için burada tekrar atamaya gerek yok.
        };
    }
    public int hp
    {
        get => baseHP;
        set => baseHP = value;
    }

    public int dmg
    {
        get => baseDamage;
        set => baseDamage = value;
    }

    public int str
    {
        get => baseDamage;
        set => baseDamage = value;
    }

    public int dexterity
    {
        get => baseDex;
        set => baseDex = value;
    }
}