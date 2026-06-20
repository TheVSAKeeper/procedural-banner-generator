namespace BannerCore;

/// <summary>
/// Параметры генерации одного баннера: тексты (заголовок с опциональным ведущим номером,
/// подзаголовок, третья строка), размеры кадра, seed процедурного гранжа и пути к опциональным
/// картинкам фона и лого. Сам стиль оформления вынесен в <see cref="BannerStyle" />.
/// </summary>
public sealed record BannerSpec
{
    public required string Title { get; init; }

    public required string Subtitle { get; init; }

    /// <summary>Третья строка (ник/дата/тег) под подзаголовком; <c>null</c>/пусто – не рисуется.</summary>
    public string? Tagline { get; init; }

    /// <summary>
    /// Путь к загруженной картинке-фону; задан – рисуется как подложка (перебивает
    /// <see cref="Transparent" />), иначе тёмная заглушка/прозрачность.
    /// </summary>
    public string? BackgroundImagePath { get; init; }

    /// <summary>
    /// Путь к картинке логотипа/водяного знака; <c>null</c> – берётся из настроек
    /// (<c>Banners:LogoPath</c>). Лого рисуется только при включённом <see cref="BannerStyle.LogoStyle" />.
    /// </summary>
    public string? LogoImagePath { get; init; }

    public int Width { get; init; } = 1280;

    public int Height { get; init; } = 720;

    /// <summary>Seed процедурного гранжа: один и тот же seed даёт одинаковую картинку.</summary>
    public int Seed { get; init; } = 290;

    /// <summary>Прозрачный фон вместо тёмной подложки-заглушки (для наложения на реальный кадр).</summary>
    public bool Transparent { get; init; }

    /// <summary>Явный путь к шрифту; иначе поиск в assets/fonts и по системным семействам.</summary>
    public string? FontPath { get; init; }
}
