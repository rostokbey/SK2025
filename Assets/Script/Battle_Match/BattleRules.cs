public static class BattleRules
{
    public const int DeckSize = 25;
    // Geçici içerik testi: şu anda 11 hazır karakter görseli bulunuyor.
    // Tam sürümde MinimumDeckSize değerini ReleaseMinimumDeckSize'a eşitleyin.
    public const int MinimumDeckSize = 11;
    public const int ReleaseMinimumDeckSize = 12;
    public const int StartingHandSize = 5;
    public const int MaxActiveCardsPerPlayer = 12;
    public const int OneVsOneColumns = 6;
    public const float SelectionSeconds = 15f;
    public const int AbilityWarmupRounds = 3;
    public const int AbilityCooldownRounds = 4;
    public const float BaseCriticalChance = 0.02f;
    public const float CriticalMultiplier = 2f;
}
