public static class CardKeyUtil
{
    // Ayný karakteri tekilleþtirmek için anahtar: prefab varsa prefab, yoksa isim
    public static string CharacterKey(CardData c)
    {
        if (c == null) return null;
        var key = string.IsNullOrEmpty(c.prefab) ? c.cardName : c.prefab;
        return string.IsNullOrEmpty(key) ? null : key.Trim().ToLowerInvariant();
    }

    public static string CharacterKey(string prefab, string name)
    {
        var key = string.IsNullOrEmpty(prefab) ? name : prefab;
        return string.IsNullOrEmpty(key) ? null : key.Trim().ToLowerInvariant();
    }
}
