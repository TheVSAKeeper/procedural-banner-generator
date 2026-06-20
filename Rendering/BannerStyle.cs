using System.Text.Json;

namespace ProceduralBannerGenerator;

/// <summary>Декларативный стиль баннера: все ручки рендера в одном JSON-документе.
/// Дефолты — значения, подобранные под референс. Цвета — #RRGGBB или #RRGGBBAA (альфа в конце, CSS-порядок).
/// Доли: *Fraction — от размеров кадра, *Factor — от кегля заголовка (если не сказано иное).</summary>
public sealed record BannerStyle
{
    public PaletteStyle Palette { get; init; } = new();
    public LayoutStyle Layout { get; init; } = new();
    public TitleFxStyle TitleFx { get; init; } = new();
    public PlateStyle Plate { get; init; } = new();
    public SmearStyle Smear { get; init; } = new();
    public BandStyle Band { get; init; } = new();
    public AccentsStyle Accents { get; init; } = new();
    public TaglineStyle Tagline { get; init; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static BannerStyle FromJson(string json) =>
        JsonSerializer.Deserialize<BannerStyle>(json, JsonOptions)
            ?? throw new InvalidOperationException("Пустой JSON стиля");

    /// <summary>Чистые цвета баннера.</summary>
    public sealed record PaletteStyle
    {
        public string GreenFaceTop { get; init; } = "#D8F046";
        public string GreenFaceMid { get; init; } = "#B8D628";
        public string GreenFaceBottom { get; init; } = "#9CBB14";
        public string GreenExtrude { get; init; } = "#232E07";
        public string WhiteFaceTop { get; init; } = "#FFFFFF";
        public string WhiteFaceMid { get; init; } = "#E2E2E2";
        public string WhiteFaceBottom { get; init; } = "#A6A6A6";
        public string WhiteExtrude { get; init; } = "#1C1C1C";
        public string BandTop { get; init; } = "#B6D426";
        public string BandBottom { get; init; } = "#96B118";
        public string Smear { get; init; } = "#5E7012";
        public string Plate { get; init; } = "#0E0E0EF5";
        public string SubtitleText { get; init; } = "#0C0C0C";
        public string TitleStroke { get; init; } = "#121212";
        public string Chips { get; init; } = "#B6D426";
        public string DarkSplatter { get; init; } = "#0E0E0EC8";
    }

    /// <summary>Раскладка: размеры и позиции блока.</summary>
    public sealed record LayoutStyle
    {
        public float TitleSizeFraction { get; init; } = 0.066f;       // кегль заголовка, доля ширины кадра
        public float MaxTitleWidthFraction { get; init; } = 0.55f;    // автоужатие длинного заголовка
        public float TitleScaleX { get; init; } = 0.90f;              // конденс шрифта заголовка
        public float TitleBaselineFraction { get; init; } = 0.845f;   // базовая линия заголовка, доля высоты кадра
        public float SubtitleSizeFactor { get; init; } = 0.50f;       // кегль подзаголовка от кегля заголовка
        public float SubtitleScaleX { get; init; } = 0.92f;
        public float SubtitleBaselineOffsetFactor { get; init; } = 0.86f; // смещение базовой линии подзаголовка вниз
        public float RotationDegrees { get; init; } = -1.2f;          // наклон всего блока
        public float PlateSidePaddingFactor { get; init; } = 0.45f;
        public float PlateTopPaddingFactor { get; init; } = 0.22f;
        public float PlateBottomFactor { get; init; } = 0.88f;        // низ плашки от базовой линии заголовка
        public float BandHeightFactor { get; init; } = 1.18f;         // высота зелёной полосы от кегля подзаголовка
        public float BandLeftPaddingFactor { get; init; } = 1.0f;     // поля полосы (от кегля подзаголовка)
        public float BandRightPaddingFactor { get; init; } = 0.8f;
    }

    /// <summary>Эффекты 3D-текста заголовка (номер + основной текст).</summary>
    public sealed record TitleFxStyle
    {
        public float ExtrudeDepthFactor { get; init; } = 0.09f;       // глубина экструзии от кегля
        public float ExtrudeStepX { get; init; } = 0.75f;             // наклон экструзии (px вправо на 1px вниз)
        public float StrokeWidthFactor { get; init; } = 0.020f;       // толщина обводки от кегля
        public float FaceGradientMidStop { get; init; } = 0.55f;      // позиция средней точки градиента грани 0..1
        public string MainMottleColor { get; init; } = "#60606042";   // зерно потёртости белых букв
        public string PrefixMottleColor { get; init; } = "#2F3A0650"; // зерно потёртости зелёного номера
        public float MottleDensity { get; init; } = 1.0f;             // множитель плотности зерна
        public float MottleMaxRadiusFactor { get; init; } = 0.13f;    // макс. размер зерна от кегля
        public float DistressDensity { get; init; } = 0.45f;          // плотность выгрызов/царапин
    }

    /// <summary>Чёрная плашка-мазок за заголовком.</summary>
    public sealed record PlateStyle
    {
        public float EdgeAmpFactor { get; init; } = 0.055f;           // рваность горизонтальных краёв (доли высоты плашки)
        public float EndAmpFactor { get; init; } = 0.12f;             // рваность торцов
        public float TeethLeftFactor { get; init; } = 0.28f;          // длина сухих «зубьев» слева (доли высоты)
        public float TeethRightFactor { get; init; } = 0.30f;
        public string MottleColor { get; init; } = "#FFFFFF06";       // светлая неравномерность краски
        public int MottleCount { get; init; } = 10;
        public float MottleMaxRadiusFactor { get; init; } = 0.50f;
        public float DistressDensity { get; init; } = 0.30f;
        public float ShadowOffsetY { get; init; } = 5f;
        public float ShadowBlur { get; init; } = 7f;
        public int ShadowAlpha { get; init; } = 130;                  // 0..255
    }

    /// <summary>Зелёная подмазка за номером (рисуется только при числовом префиксе заголовка).</summary>
    public sealed record SmearStyle
    {
        public bool Enabled { get; init; } = true;
        public float LeftExtendFactor { get; init; } = 0.5f;          // вынос влево за плашку (доли кегля)
        public float TopExtendFactor { get; init; } = 0.12f;          // вынос вверх за плашку
        public float RightPadFactor { get; init; } = 0.15f;           // запас правее номера
        public float BaselineDropFactor { get; init; } = 0.30f;       // низ подмазки ниже базовой линии заголовка
        public float EdgeAmpFactor { get; init; } = 0.10f;
        public float EndAmpFactor { get; init; } = 0.18f;
        public float TeethFactor { get; init; } = 0.40f;
        public string MottleColor { get; init; } = "#0E1202D0";       // тёмное зерно, съедающее мазок
        public float MottleDensity { get; init; } = 1.0f;
        public float MottleMaxRadiusFactor { get; init; } = 0.075f;
        public float DistressDensity { get; init; } = 2.4f;
    }

    /// <summary>Зелёная полоса-мазок под подзаголовком.</summary>
    public sealed record BandStyle
    {
        public float EdgeAmpFactor { get; init; } = 0.07f;
        public float EndAmpFactor { get; init; } = 0.10f;
        public float TeethLeftFactor { get; init; } = 0.70f;          // длина «зубьев» (доли высоты полосы)
        public float TeethRightFactor { get; init; } = 1.60f;         // правый торец рваный сильнее
        public string MottleColor { get; init; } = "#1A200348";
        public float MottleDensity { get; init; } = 1.0f;
        public float MottleMaxRadiusFactor { get; init; } = 0.22f;
        public float DistressDensity { get; init; } = 0.90f;
        public float SubtitleDistressDensity { get; init; } = 0.50f;  // потёртость чёрного текста подзаголовка
    }

    /// <summary>Мелкие акценты: чипсы вдоль кромки и брызги.</summary>
    public sealed record AccentsStyle
    {
        public int ChipsLeftCount { get; init; } = 7;
        public int ChipsRightCount { get; init; } = 6;
        public float ChipsSpread { get; init; } = 15f;                // разброс чипсов от кромки, px
        public int SplatterGreenCount { get; init; } = 16;            // зелёная крошка на правой части плашки
        public int SplatterDarkCount { get; init; } = 14;             // тёмные брызги слева-снизу
    }

    /// <summary>Третья строка текста (ник/дата/тег) под подзаголовком. Рисуется, только если у баннера
    /// задан текст третьей строки (<see cref="BannerSpec.Tagline"/>) и <see cref="Enabled"/>.</summary>
    public sealed record TaglineStyle
    {
        public bool Enabled { get; init; } = true;
        public float SizeFactor { get; init; } = 0.40f;              // кегль от кегля заголовка
        public string Color { get; init; } = "#C8C8C8";
        public float BaselineGapFactor { get; init; } = 0.34f;       // отступ базовой линии ниже подзаголовка (доли кегля заголовка)
        public float ScaleX { get; init; } = 0.92f;                  // конденс шрифта
        public float DistressDensity { get; init; } = 0.4f;          // потёртость текста
    }
}
