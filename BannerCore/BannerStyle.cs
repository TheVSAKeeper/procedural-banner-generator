using System.Text.Json;

namespace BannerCore;

/// <summary>
/// Декларативный стиль баннера: все ручки рендера собраны в одном JSON-документе.
/// Дефолты – значения, подобранные под референс. Соглашения по полям:
/// <list type="bullet">
///     <item>цвета – строка <c>#RRGGBB</c> или <c>#RRGGBBAA</c> (альфа в конце, CSS-порядок);</item>
///     <item><c>*Fraction</c> – доля от размеров кадра;</item>
///     <item><c>*Factor</c> – доля от кегля заголовка, если в комментарии не сказано иное.</item>
/// </list>
/// Файловый IO сюда нарочно не входит: стиль – чистые данные, а его чтение/запись остаётся за
/// вызывающей стороной (бот – через свою файловую абстракцию, лаборатория BannerLab – через <c>File</c>).
/// </summary>
public sealed record BannerStyle
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public PaletteStyle Palette { get; init; } = new();
    public LayoutStyle Layout { get; init; } = new();
    public TitleFxStyle TitleFx { get; init; } = new();
    public PlateStyle Plate { get; init; } = new();
    public SmearStyle Smear { get; init; } = new();
    public BandStyle Band { get; init; } = new();
    public AccentsStyle Accents { get; init; } = new();
    public TaglineStyle Tagline { get; init; } = new();
    public LogoStyle Logo { get; init; } = new();
    public BackgroundStyle Background { get; init; } = new();

    public static BannerStyle FromJson(string json)
    {
        var style = JsonSerializer.Deserialize<BannerStyle>(json, JsonOptions)
            ?? throw new InvalidOperationException("Пустой JSON стиля");

        style.Validate();
        return style;
    }

    public string ToJson()
    {
        return JsonSerializer.Serialize(this, JsonOptions);
    }

    public void Validate()
    {
        if (Palette is null
            || Layout is null
            || TitleFx is null
            || Plate is null
            || Smear is null
            || Band is null
            || Accents is null
            || Tagline is null
            || Logo is null
            || Background is null)
        {
            throw new InvalidOperationException("Секции стиля не могут быть пустыми (null).");
        }

        StyleColor.Parse(Palette.GreenFaceTop);
        StyleColor.Parse(Palette.GreenFaceMid);
        StyleColor.Parse(Palette.GreenFaceBottom);
        StyleColor.Parse(Palette.GreenExtrude);
        StyleColor.Parse(Palette.WhiteFaceTop);
        StyleColor.Parse(Palette.WhiteFaceMid);
        StyleColor.Parse(Palette.WhiteFaceBottom);
        StyleColor.Parse(Palette.WhiteExtrude);
        StyleColor.Parse(Palette.BandTop);
        StyleColor.Parse(Palette.BandBottom);
        StyleColor.Parse(Palette.Smear);
        StyleColor.Parse(Palette.Plate);
        StyleColor.Parse(Palette.SubtitleText);
        StyleColor.Parse(Palette.TitleStroke);
        StyleColor.Parse(Palette.Chips);
        StyleColor.Parse(Palette.DarkSplatter);
        StyleColor.Parse(TitleFx.MainMottleColor);
        StyleColor.Parse(TitleFx.PrefixMottleColor);
        StyleColor.Parse(Plate.MottleColor);
        StyleColor.Parse(Smear.MottleColor);
        StyleColor.Parse(Band.MottleColor);
        StyleColor.Parse(Tagline.Color);

        CheckRange(Layout.TitleSizeFraction, 0f, 0.5f, "layout.titleSizeFraction");
        CheckRange(Layout.MaxTitleWidthFraction, 0.05f, 1f, "layout.maxTitleWidthFraction");
        CheckRange(Layout.TitleScaleX, 0.1f, 3f, "layout.titleScaleX");
        CheckRange(Layout.TitleBaselineFraction, -0.5f, 1.5f, "layout.titleBaselineFraction");
        CheckRange(Layout.SubtitleSizeFactor, 0.05f, 2f, "layout.subtitleSizeFactor");
        CheckRange(Layout.SubtitleScaleX, 0.1f, 3f, "layout.subtitleScaleX");
        CheckRange(Layout.SubtitleBaselineOffsetFactor, -2f, 4f, "layout.subtitleBaselineOffsetFactor");
        CheckRange(Layout.RotationDegrees, -45f, 45f, "layout.rotationDegrees");
        CheckRange(Layout.PlateSidePaddingFactor, 0f, 3f, "layout.plateSidePaddingFactor");
        CheckRange(Layout.PlateTopPaddingFactor, 0f, 3f, "layout.plateTopPaddingFactor");
        CheckRange(Layout.PlateBottomFactor, 0f, 4f, "layout.plateBottomFactor");
        CheckRange(Layout.BandHeightFactor, 0f, 5f, "layout.bandHeightFactor");
        CheckRange(Layout.BandLeftPaddingFactor, 0f, 5f, "layout.bandLeftPaddingFactor");
        CheckRange(Layout.BandRightPaddingFactor, 0f, 5f, "layout.bandRightPaddingFactor");

        CheckRange(TitleFx.ExtrudeDepthFactor, 0f, 0.5f, "titleFx.extrudeDepthFactor");
        CheckRange(TitleFx.ExtrudeStepX, -5f, 5f, "titleFx.extrudeStepX");
        CheckRange(TitleFx.StrokeWidthFactor, 0f, 0.5f, "titleFx.strokeWidthFactor");
        CheckRange(TitleFx.FaceGradientMidStop, 0f, 1f, "titleFx.faceGradientMidStop");
        CheckRange(TitleFx.MottleDensity, 0f, 10f, "titleFx.mottleDensity");
        CheckRange(TitleFx.MottleMaxRadiusFactor, 0f, 2f, "titleFx.mottleMaxRadiusFactor");
        CheckRange(TitleFx.DistressDensity, 0f, 10f, "titleFx.distressDensity");

        CheckRange(Plate.EdgeAmpFactor, 0f, 1f, "plate.edgeAmpFactor");
        CheckRange(Plate.EndAmpFactor, 0f, 1f, "plate.endAmpFactor");
        CheckRange(Plate.TeethLeftFactor, 0f, 3f, "plate.teethLeftFactor");
        CheckRange(Plate.TeethRightFactor, 0f, 3f, "plate.teethRightFactor");
        CheckRange(Plate.MottleCount, 0, 200, "plate.mottleCount");
        CheckRange(Plate.MottleMaxRadiusFactor, 0f, 3f, "plate.mottleMaxRadiusFactor");
        CheckRange(Plate.DistressDensity, 0f, 10f, "plate.distressDensity");
        CheckRange(Plate.ShadowOffsetY, -100f, 100f, "plate.shadowOffsetY");
        CheckRange(Plate.ShadowBlur, 0f, 100f, "plate.shadowBlur");
        CheckRange(Plate.ShadowAlpha, 0, 255, "plate.shadowAlpha");

        CheckRange(Smear.LeftExtendFactor, 0f, 5f, "smear.leftExtendFactor");
        CheckRange(Smear.TopExtendFactor, 0f, 3f, "smear.topExtendFactor");
        CheckRange(Smear.RightPadFactor, 0f, 3f, "smear.rightPadFactor");
        CheckRange(Smear.BaselineDropFactor, 0f, 3f, "smear.baselineDropFactor");
        CheckRange(Smear.EdgeAmpFactor, 0f, 1f, "smear.edgeAmpFactor");
        CheckRange(Smear.EndAmpFactor, 0f, 1f, "smear.endAmpFactor");
        CheckRange(Smear.TeethFactor, 0f, 3f, "smear.teethFactor");
        CheckRange(Smear.MottleDensity, 0f, 10f, "smear.mottleDensity");
        CheckRange(Smear.MottleMaxRadiusFactor, 0f, 2f, "smear.mottleMaxRadiusFactor");
        CheckRange(Smear.DistressDensity, 0f, 10f, "smear.distressDensity");

        CheckRange(Band.EdgeAmpFactor, 0f, 1f, "band.edgeAmpFactor");
        CheckRange(Band.EndAmpFactor, 0f, 1f, "band.endAmpFactor");
        CheckRange(Band.TeethLeftFactor, 0f, 5f, "band.teethLeftFactor");
        CheckRange(Band.TeethRightFactor, 0f, 5f, "band.teethRightFactor");
        CheckRange(Band.MottleDensity, 0f, 10f, "band.mottleDensity");
        CheckRange(Band.MottleMaxRadiusFactor, 0f, 2f, "band.mottleMaxRadiusFactor");
        CheckRange(Band.DistressDensity, 0f, 10f, "band.distressDensity");
        CheckRange(Band.SubtitleDistressDensity, 0f, 10f, "band.subtitleDistressDensity");

        CheckRange(Accents.ChipsLeftCount, 0, 200, "accents.chipsLeftCount");
        CheckRange(Accents.ChipsRightCount, 0, 200, "accents.chipsRightCount");
        CheckRange(Accents.ChipsSpread, 0f, 300f, "accents.chipsSpread");
        CheckRange(Accents.SplatterGreenCount, 0, 300, "accents.splatterGreenCount");
        CheckRange(Accents.SplatterDarkCount, 0, 300, "accents.splatterDarkCount");

        CheckRange(Tagline.SizeFactor, 0.05f, 2f, "tagline.sizeFactor");
        CheckRange(Tagline.BaselineGapFactor, -1f, 3f, "tagline.baselineGapFactor");
        CheckRange(Tagline.ScaleX, 0.1f, 3f, "tagline.scaleX");
        CheckRange(Tagline.DistressDensity, 0f, 10f, "tagline.distressDensity");

        var corner = (Logo.Corner ?? "br").Trim().ToLowerInvariant();
        if (corner is not ("tl" or "tr" or "bl" or "br"))
        {
            throw new InvalidOperationException($"Недопустимый угол логотипа 'logo.corner': '{Logo.Corner}'. Допустимые: tl, tr, bl, br.");
        }

        CheckRange(Logo.SizeFraction, 0.01f, 1f, "logo.sizeFraction");
        CheckRange(Logo.Opacity, 0f, 1f, "logo.opacity");
        CheckRange(Logo.MarginFraction, 0f, 0.5f, "logo.marginFraction");

        CheckRange(Background.DarkenAlpha, 0f, 1f, "background.darkenAlpha");
        CheckRange(Background.BlurSigma, 0f, 100f, "background.blurSigma");
    }

    private static void CheckRange(float value, float min, float max, string name)
    {
        if (!float.IsFinite(value) || value < min || value > max)
        {
            throw new InvalidOperationException($"Параметр стиля '{name}' вне допустимого диапазона [{min}..{max}]: {value}");
        }
    }

    private static void CheckRange(int value, int min, int max, string name)
    {
        if (value < min || value > max)
        {
            throw new InvalidOperationException($"Параметр стиля '{name}' вне допустимого диапазона [{min}..{max}]: {value}");
        }
    }

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
        public float TitleSizeFraction { get; init; } = 0.066f; // кегль заголовка, доля ширины кадра
        public float MaxTitleWidthFraction { get; init; } = 0.55f; // автоужатие длинного заголовка
        public float TitleScaleX { get; init; } = 0.90f; // конденс шрифта заголовка
        public float TitleBaselineFraction { get; init; } = 0.845f; // базовая линия заголовка, доля высоты кадра
        public float SubtitleSizeFactor { get; init; } = 0.50f; // кегль подзаголовка от кегля заголовка
        public float SubtitleScaleX { get; init; } = 0.92f;
        public float SubtitleBaselineOffsetFactor { get; init; } = 0.86f; // смещение базовой линии подзаголовка вниз
        public float RotationDegrees { get; init; } = -1.2f; // наклон всего блока
        public float PlateSidePaddingFactor { get; init; } = 0.45f;
        public float PlateTopPaddingFactor { get; init; } = 0.22f;
        public float PlateBottomFactor { get; init; } = 0.88f; // низ плашки от базовой линии заголовка
        public float BandHeightFactor { get; init; } = 1.18f; // высота зелёной полосы от кегля подзаголовка
        public float BandLeftPaddingFactor { get; init; } = 1.0f; // поля полосы (от кегля подзаголовка)
        public float BandRightPaddingFactor { get; init; } = 0.8f;
    }

    /// <summary>Эффекты 3D-текста заголовка (номер + основной текст).</summary>
    public sealed record TitleFxStyle
    {
        public float ExtrudeDepthFactor { get; init; } = 0.09f; // глубина экструзии от кегля
        public float ExtrudeStepX { get; init; } = 0.75f; // наклон экструзии (px вправо на 1px вниз)
        public float StrokeWidthFactor { get; init; } = 0.020f; // толщина обводки от кегля
        public float FaceGradientMidStop { get; init; } = 0.55f; // позиция средней точки градиента грани 0..1
        public string MainMottleColor { get; init; } = "#60606042"; // зерно потёртости белых букв
        public string PrefixMottleColor { get; init; } = "#2F3A0650"; // зерно потёртости зелёного номера
        public float MottleDensity { get; init; } = 1.0f; // множитель плотности зерна
        public float MottleMaxRadiusFactor { get; init; } = 0.13f; // макс. размер зерна от кегля
        public float DistressDensity { get; init; } = 0.45f; // плотность выгрызов/царапин
    }

    /// <summary>Чёрная плашка-мазок за заголовком.</summary>
    public sealed record PlateStyle
    {
        public float EdgeAmpFactor { get; init; } = 0.055f; // рваность горизонтальных краёв (доли высоты плашки)
        public float EndAmpFactor { get; init; } = 0.12f; // рваность торцов
        public float TeethLeftFactor { get; init; } = 0.28f; // длина сухих «зубьев» слева (доли высоты)
        public float TeethRightFactor { get; init; } = 0.30f;
        public string MottleColor { get; init; } = "#FFFFFF06"; // светлая неравномерность краски
        public int MottleCount { get; init; } = 10;
        public float MottleMaxRadiusFactor { get; init; } = 0.50f;
        public float DistressDensity { get; init; } = 0.30f;
        public float ShadowOffsetY { get; init; } = 5f;
        public float ShadowBlur { get; init; } = 7f;
        public int ShadowAlpha { get; init; } = 130; // 0..255
    }

    /// <summary>Зелёная подмазка за номером (рисуется только при числовом префиксе заголовка).</summary>
    public sealed record SmearStyle
    {
        public bool Enabled { get; init; } = true;
        public float LeftExtendFactor { get; init; } = 0.5f; // вынос влево за плашку (доли кегля)
        public float TopExtendFactor { get; init; } = 0.12f; // вынос вверх за плашку
        public float RightPadFactor { get; init; } = 0.15f; // запас правее номера
        public float BaselineDropFactor { get; init; } = 0.30f; // низ подмазки ниже базовой линии заголовка
        public float EdgeAmpFactor { get; init; } = 0.10f;
        public float EndAmpFactor { get; init; } = 0.18f;
        public float TeethFactor { get; init; } = 0.40f;
        public string MottleColor { get; init; } = "#0E1202D0"; // тёмное зерно, съедающее мазок
        public float MottleDensity { get; init; } = 1.0f;
        public float MottleMaxRadiusFactor { get; init; } = 0.075f;
        public float DistressDensity { get; init; } = 2.4f;
    }

    /// <summary>Зелёная полоса-мазок под подзаголовком.</summary>
    public sealed record BandStyle
    {
        public float EdgeAmpFactor { get; init; } = 0.07f;
        public float EndAmpFactor { get; init; } = 0.10f;
        public float TeethLeftFactor { get; init; } = 0.70f; // длина «зубьев» (доли высоты полосы)
        public float TeethRightFactor { get; init; } = 1.60f; // правый торец рваный сильнее
        public string MottleColor { get; init; } = "#1A200348";
        public float MottleDensity { get; init; } = 1.0f;
        public float MottleMaxRadiusFactor { get; init; } = 0.22f;
        public float DistressDensity { get; init; } = 0.90f;
        public float SubtitleDistressDensity { get; init; } = 0.50f; // потёртость чёрного текста подзаголовка
    }

    /// <summary>Мелкие акценты: чипсы вдоль кромки и брызги.</summary>
    public sealed record AccentsStyle
    {
        public int ChipsLeftCount { get; init; } = 7;
        public int ChipsRightCount { get; init; } = 6;
        public float ChipsSpread { get; init; } = 15f; // разброс чипсов от кромки, px
        public int SplatterGreenCount { get; init; } = 16; // зелёная крошка на правой части плашки
        public int SplatterDarkCount { get; init; } = 14; // тёмные брызги слева-снизу
    }

    /// <summary>
    /// Третья строка текста (ник/дата/тег) под подзаголовком. Рисуется, только если у баннера
    /// задан текст третьей строки (<see cref="BannerSpec.Tagline" />) и <see cref="Enabled" />.
    /// </summary>
    public sealed record TaglineStyle
    {
        public bool Enabled { get; init; } = true;
        public float SizeFactor { get; init; } = 0.40f; // кегль от кегля заголовка
        public string Color { get; init; } = "#C8C8C8";
        public float BaselineGapFactor { get; init; } = 0.34f; // отступ базовой линии ниже подзаголовка (доли кегля заголовка)
        public float ScaleX { get; init; } = 0.92f; // конденс шрифта
        public float DistressDensity { get; init; } = 0.4f; // потёртость текста
    }

    /// <summary>
    /// Логотип/водяной знак в углу кадра. Рисуется, только если <see cref="Enabled" /> и
    /// файл картинки лого реально существует (иначе молча пропускается).
    /// </summary>
    public sealed record LogoStyle
    {
        public bool Enabled { get; init; } // по умолчанию выкл – нужен файл лого
        public string Corner { get; init; } = "br"; // tl | tr | bl | br
        public float SizeFraction { get; init; } = 0.12f; // ширина лого, доля ширины кадра
        public float Opacity { get; init; } = 0.85f; // 0..1
        public float MarginFraction { get; init; } = 0.03f; // отступ от края, доля ширины кадра
    }

    /// <summary>
    /// Обработка загруженной картинки-фона (применяется только когда у баннера задан фон).
    /// Сама картинка – файл (<see cref="BannerSpec.BackgroundImagePath" />), не ручка стиля.
    /// </summary>
    public sealed record BackgroundStyle
    {
        public float DarkenAlpha { get; init; } = 0.35f; // тёмная вуаль поверх фона для контраста текста, 0..1
        public float BlurSigma { get; init; } // размытие фона, px (0 – без блюра)
    }
}
