using UnityEngine;

public static class CardDataMapper
{
    public static CardDTO ToDTO(CardData c)
    {
        return new CardDTO
        {
            id = c.id,
            cardName = c.cardName,

            baseHP = c.baseHP,
            baseDamage = c.baseDamage,
            baseDex = c.baseDex,   // <-- buras�
            dex = c.dex,       // <-- buras�

            rarity = c.rarity,
            ability = c.ability,
            passive = c.passive,

            level = c.level,
            xp = c.xp,
            skillCooldownMax = c.skillCooldownMax,

            className = c.className,
            race = c.race,
            combo = c.combo,

            spritePath = c.characterSprite ? c.characterSprite.name : null,
            prefabPath = c.prefab
        };
    }

    public static CardData FromDTO(CardDTO d)
    {
        Sprite sp = null;
        if (!string.IsNullOrEmpty(d.spritePath))
            sp = Resources.Load<Sprite>("Characters/" + d.spritePath);

        // yeni yap�c�y� kullan�yoruz (baseDex + dex birlikte)
        return new CardData(
            d.id, d.cardName,
            d.baseHP, d.baseDamage,
            d.baseDex, d.dex,                 // <-- buras�
            d.rarity, d.ability, d.passive,
            d.level, d.xp, d.skillCooldownMax,
            sp,
            null,
            d.prefabPath
        );
    }
}