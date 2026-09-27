/// <summary>
/// Bir saldırının/yeteneğin hedef seçim kuralını belirler.
/// Normal: sütun + önden-arkaya blok kuralına tabi (standart saldırı).
/// IgnoreFrontline: "arka hatta saldır" - önhat/orta hattı atlayıp arka hattı hedefler.
/// AnyTarget: "herhangi bir hedefi seç" - sütun/blok kuralını tamamen yok sayar.
/// AreaOfEffect: "alan hasarı" - sütundaki (veya sahadaki) birden fazla hedefi etkiler.
/// </summary>
public enum AbilityTargetingMode
{
    Normal = 0,
    IgnoreFrontline = 1,
    AnyTarget = 2,
    AreaOfEffect = 3
}
