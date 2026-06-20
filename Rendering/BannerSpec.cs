namespace ProceduralBannerGenerator;

/// <summary>Параметры генерации баннера (стиль референса: гранж-плашка + экструдированный заголовок + зелёный подзаголовок).</summary>
public sealed record BannerSpec
{
    public required string Title { get; init; }

    public required string Subtitle { get; init; }

    /// <summary>Третья строка (ник/дата/тег) под подзаголовком; <c>null</c>/пусто — не рисуется.</summary>
    public string? Tagline { get; init; }

    public int Width { get; init; } = 1280;

    public int Height { get; init; } = 720;

    /// <summary>Seed процедурного гранжа: один и тот же seed даёт одинаковую картинку.</summary>
    public int Seed { get; init; } = 290;

    /// <summary>Прозрачный фон вместо тёмной подложки-заглушки (для наложения на реальный кадр).</summary>
    public bool Transparent { get; init; }
}
