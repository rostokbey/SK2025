// CardArtResolver.cs
using UnityEngine;

public static class CardArtResolver
{
    public static Sprite GetSprite(CardData card, Sprite fallback = null)
    {
        if (card == null || string.IsNullOrEmpty(card.cardName)) return fallback;

        // Assets/Resources/Characters/AgirZirh_insan.png
        Sprite Try(string path) => Resources.Load<Sprite>(path);

        return Try("Characters/" + card.cardName)
             ?? Try("Characters/" + card.cardName.Replace(" ", "_"))
             ?? Try("Characters/" + card.cardName.Replace("_", " "))
             ?? fallback;
    }
}
