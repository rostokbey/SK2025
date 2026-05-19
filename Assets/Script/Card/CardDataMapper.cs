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
            baseStr = c.baseStr,
            baseDef = c.baseDef,

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

        return new CardData(
            d.id, d.cardName,
            d.baseHP, d.baseStr, d.baseDef,
            d.rarity, d.ability, d.passive,
            d.level, d.xp, d.skillCooldownMax,
            sp,
            null,
            d.prefabPath
        );
    }
}