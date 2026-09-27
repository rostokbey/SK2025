/// <summary>
/// AKTİF yetenek: oyuncu tarafından elle tetiklenir, sunucu tarafında cooldown'a
/// tabidir (bkz. Character.cs - 3 raund hazırlık / kullanınca 4 raund bekleme).
/// Uygulama SADECE efekti uygular (hasar, iyileştirme, buff vb.); cooldown/raund
/// muhasebesi Character.TryUseActiveAbilityServerSide içinde, bu arayüzden BAĞIMSIZ
/// olarak yönetilir - yeni bir yetenek eklerken cooldown mantığıyla uğraşmanıza
/// gerek yok, sadece Apply(context) içine efekti yazarsınız.
/// </summary>
public interface IActiveAbility
{
    /// <summary>Bu yeteneğin CardData.ability alanındaki metinle eşleşen benzersiz kimliği.</summary>
    string Id { get; }

    /// <summary>Varsayılan hedefleme kuralı (front-blok, arka hat, herhangi, alan).</summary>
    AbilityTargetingMode TargetingMode { get; }

    /// <summary>Sunucu tarafında, hedef zaten çözümlenmiş context ile efekti uygular.</summary>
    void Apply(AbilityContext context);
}

/// <summary>
/// PASİF yetenek: sürekli devrede olan, elle tetiklenmeyen, cooldown'suz bir efekt.
/// OnRoundStart her raund başında (bu karakter hayattaysa) çağrılır - örn. "her raund
/// başında %5 can yenile" gibi sürekli etkiler için. Anlık/tek seferlik pasifler
/// (ör. "aldığı hasarın %10'unu azalt") HealthSystem gibi ilgili sistemlerde
/// AbilityCatalog.GetPassive(...) sorgusuyla ayrıca uygulanmalıdır; bu arayüz sadece
/// raund-bazlı tetiklenen pasifler için ortak bir giriş noktasıdır.
/// </summary>
public interface IPassiveAbility
{
    string Id { get; }

    /// <summary>Karakter sahaya girdiğinde (Setup/spawn anında) bir kere çağrılır.</summary>
    void OnSpawn(AbilityContext context);

    /// <summary>Her yeni raund başında, karakter hayattaysa çağrılır.</summary>
    void OnRoundStart(AbilityContext context);
}
