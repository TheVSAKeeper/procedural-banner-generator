using SkiaSharp;
using System.Text.RegularExpressions;

namespace BannerCore;

/// <summary>
/// Рендерит баннер в стиле превью Tarkov-стримов по декларативному <see cref="BannerStyle" />.
/// Слои строятся снизу вверх:
/// <list type="number">
///     <item>фон – загруженная картинка, прозрачность или тёмная заглушка-градиент;</item>
///     <item>чёрная плашка-мазок за заголовком и зелёная подмазка за ведущим номером;</item>
///     <item>зелёная полоса-мазок с «зубьями» под подзаголовок;</item>
///     <item>3D-заголовок – зелёный номер и белый текст, оба с мотлингом и потёртостями;</item>
///     <item>чёрный подзаголовок на полосе, опциональная третья строка и лого в углу.</item>
/// </list>
/// Текстовый блок слегка наклонён; лого рисуется уже без наклона, привязанным к углу кадра.
/// Рендер детерминирован по <see cref="BannerSpec.Seed" /> и не трогает файловую систему –
/// картинки фона и лого передаёт вызывающая сторона уже декодированными.
/// </summary>
public sealed partial class BannerRenderer(BannerStyle style)
{
    /// <summary>
    /// Рисует баннер на off-screen-поверхности и кодирует результат в PNG.
    /// </summary>
    /// <param name="spec">Тексты, размеры кадра, seed и опциональные пути к картинкам.</param>
    /// <param name="typeface">Шрифт заголовка, подзаголовка и третьей строки.</param>
    /// <param name="background">
    /// Декодированная картинка-подложка. Если задана – перебивает <see cref="BannerSpec.Transparent" />;
    /// если <c>null</c> – фон берётся из прозрачности или тёмной заглушки.
    /// </param>
    /// <param name="logo">
    /// Декодированное лого/водяной знак для угла кадра. Рисуется, только когда задано
    /// и включён <see cref="BannerStyle.LogoStyle" />.
    /// </param>
    /// <returns>PNG-кодированные байты готового баннера.</returns>
    public byte[] RenderPng(BannerSpec spec, SKTypeface typeface, SKImage? background = null, SKImage? logo = null)
    {
        var info = new SKImageInfo(spec.Width, spec.Height);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        var rng = new Random(spec.Seed);
        var layout = style.Layout;

        DrawBackground(canvas, spec, background);

        var (prefix, main) = SplitTitle(spec.Title);

        var titleSize = spec.Width * Math.Clamp(layout.TitleSizeFraction, 0.02f, 0.15f);
        using var titleFont = new SKFont(typeface, titleSize) { ScaleX = layout.TitleScaleX, Subpixel = true };
        var maxTitleWidth = spec.Width * layout.MaxTitleWidthFraction;
        var totalWidth = MeasureTitleRow(titleFont, prefix, main, out var spaceWidth);

        if (totalWidth > maxTitleWidth)
        {
            titleSize *= maxTitleWidth / totalWidth;
            titleFont.Size = titleSize;
            totalWidth = MeasureTitleRow(titleFont, prefix, main, out spaceWidth);
        }

        using var subFont = new SKFont(typeface, titleSize * layout.SubtitleSizeFactor)
        {
            ScaleX = layout.SubtitleScaleX,
            Subpixel = true,
        };

        var subWidth = subFont.MeasureText(spec.Subtitle, out var subBounds);

        var centerX = spec.Width / 2f;
        var titleBaseline = spec.Height * layout.TitleBaselineFraction;
        var subBaseline = titleBaseline + titleSize * layout.SubtitleBaselineOffsetFactor;
        var titleLeft = centerX - totalWidth / 2;
        titleFont.MeasureText(main, out var mainBounds);

        var plate = new SKRect(titleLeft - titleSize * layout.PlateSidePaddingFactor,
            titleBaseline + mainBounds.Top - titleSize * layout.PlateTopPaddingFactor,
            titleLeft + totalWidth + titleSize * layout.PlateSidePaddingFactor,
            titleBaseline + titleSize * layout.PlateBottomFactor);

        var bandHeight = subFont.Size * layout.BandHeightFactor;
        var bandCenterY = subBaseline + (subBounds.Top + subBounds.Bottom) / 2;
        var band = new SKRect(centerX - subWidth / 2 - subFont.Size * layout.BandLeftPaddingFactor,
            bandCenterY - bandHeight / 2,
            centerX + subWidth / 2 + subFont.Size * layout.BandRightPaddingFactor,
            bandCenterY + bandHeight / 2);

        canvas.Save();
        canvas.RotateDegrees(Math.Clamp(layout.RotationDegrees, -10f, 10f), centerX, plate.MidY);

        DrawBlackPlate(canvas, plate, spec.Seed, rng);
        DrawPrefixSmear(canvas, plate, titleFont, prefix, titleLeft, titleBaseline, titleSize, spec.Seed,
            rng);

        DrawChipsAndSplatter(canvas, plate, rng);
        DrawGreenBand(canvas, band, spec.Seed, rng);
        DrawTitleRow(canvas, prefix, main, titleFont, titleLeft, titleBaseline, spaceWidth, titleSize,
            rng);

        DrawSubtitle(canvas, spec.Subtitle, subFont, centerX, subBaseline, subWidth, subBounds, rng);

        if (!string.IsNullOrWhiteSpace(spec.Tagline) && style.Tagline.Enabled)
        {
            DrawTagline(canvas, spec.Tagline!, typeface, centerX, subBaseline, titleSize, rng);
        }

        canvas.Restore();

        if (logo is not null && style.Logo.Enabled)
        {
            DrawLogo(canvas, logo, spec.Width, spec.Height);
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>
    /// Отделяет ведущий номер выпуска («290.» / «290:» / «290») от остального заголовка:
    /// номер потом рендерится зелёным 3D, основной текст – белым.
    /// Вынесено в internal-функцию ради характеризационного юнит-теста.
    /// </summary>
    internal static (string? Prefix, string Main) SplitTitle(string title)
    {
        var match = TitlePrefixRegex().Match(title);
        return match.Success ? (match.Groups[1].Value, match.Groups[2].Value) : (null, title);
    }

    [GeneratedRegex(@"^(\d+[.:]?)\s+(.+)$")]
    private static partial Regex TitlePrefixRegex();

    private static float MeasureTitleRow(SKFont font, string? prefix, string main, out float spaceWidth)
    {
        spaceWidth = prefix is null ? 0 : font.MeasureText(" ") * 1.6f;
        var prefixWidth = prefix is null ? 0 : font.MeasureText(prefix);
        return prefixWidth + spaceWidth + font.MeasureText(main);
    }

    /// <summary>
    /// Тёмная подложка-заглушка вместо скриншота игры: вертикальный градиент,
    /// на котором можно оценить контраст текста баннера.
    /// </summary>
    private static void DrawPlaceholderBackground(SKCanvas canvas, int width, int height)
    {
        using var paint = new SKPaint
        {
            Shader = SKShader.CreateLinearGradient(new(0, 0), new(0, height),
                [new(0x3A, 0x3A, 0x3A), new(0x17, 0x17, 0x17)],
                SKShaderTileMode.Clamp),
        };

        canvas.DrawRect(0, 0, width, height, paint);
    }

    /// <summary>
    /// Выбирает подложку баннера по приоритету: загруженная картинка перебивает всё; иначе –
    /// прозрачный фон (для наложения на реальный кадр) или тёмная заглушка-градиент.
    /// </summary>
    private void DrawBackground(SKCanvas canvas, BannerSpec spec, SKImage? background)
    {
        if (background is not null)
        {
            DrawBackgroundImage(canvas, background, spec.Width, spec.Height);
        }
        else if (spec.Transparent)
        {
            canvas.Clear(SKColors.Transparent);
        }
        else
        {
            DrawPlaceholderBackground(canvas, spec.Width, spec.Height);
        }
    }

    /// <summary>
    /// Загруженная картинка-фон: cover-fit (заполняет кадр, лишнее обрезается), опциональный блюр,
    /// поверх – тёмная вуаль для контраста текста. Все параметры – в <see cref="BannerStyle.BackgroundStyle" />.
    /// </summary>
    private void DrawBackgroundImage(SKCanvas canvas, SKImage image, int width, int height)
    {
        var bg = style.Background;
        var scale = Math.Max((float)width / image.Width, (float)height / image.Height);
        var w = image.Width * scale;
        var h = image.Height * scale;
        var dest = new SKRect((width - w) / 2, (height - h) / 2, (width + w) / 2, (height + h) / 2);

        using (var paint = new SKPaint { IsAntialias = true })
        {
            if (bg.BlurSigma > 0.01f)
            {
                paint.ImageFilter = SKImageFilter.CreateBlur(bg.BlurSigma, bg.BlurSigma);
            }

            canvas.DrawImage(image, dest, paint);
        }

        var darken = Math.Clamp(bg.DarkenAlpha, 0f, 1f);
        if (darken > 0.001f)
        {
            using var veil = new SKPaint { Color = new(0, 0, 0, (byte)(darken * 255)) };
            canvas.DrawRect(0, 0, width, height, veil);
        }
    }

    /// <summary>
    /// Чёрная плашка-мазок за заголовком: рваный контур кисти с drop-shadow, сухими «зубьями»
    /// по торцам, светлой неравномерностью краски (мотлинг) и мелкими потёртостями.
    /// </summary>
    private void DrawBlackPlate(SKCanvas canvas, SKRect plate, int seed, Random rng)
    {
        var s = style.Plate;
        var plateColor = StyleColor.Parse(style.Palette.Plate);

        using var shadowPaint = new SKPaint
        {
            ImageFilter = SKImageFilter.CreateDropShadow(0, s.ShadowOffsetY, s.ShadowBlur, s.ShadowBlur,
                new(0, 0, 0, (byte)Math.Clamp(s.ShadowAlpha, 0, 255))),
        };

        canvas.SaveLayer(shadowPaint);

        using (var paint = new SKPaint { Color = plateColor, IsAntialias = true })
        using (var path = Grunge.BrushStrokePath(plate, seed,
                   plate.Height * s.EdgeAmpFactor, plate.Height * s.EndAmpFactor))
        {
            canvas.DrawPath(path, paint);
        }

        Grunge.BrushTeeth(canvas, plate.Left, plate.Top, plate.Bottom, -1, plateColor, rng,
            plate.Height * s.TeethLeftFactor);

        Grunge.BrushTeeth(canvas, plate.Right, plate.Top, plate.Bottom, +1, plateColor, rng,
            plate.Height * s.TeethRightFactor);

        Grunge.Mottle(canvas, plate, rng, StyleColor.Parse(s.MottleColor),
            s.MottleCount, plate.Height * s.MottleMaxRadiusFactor);

        Grunge.Distress(canvas, plate, rng, s.DistressDensity);

        canvas.Restore();
    }

    /// <summary>
    /// Зелёная подмазка под ведущим номером: рисуется только при числовом префиксе заголовка
    /// и включённом <see cref="BannerStyle.SmearStyle" />. Прямоугольник подмазки выносится за
    /// левый край плашки и под базовую линию номера.
    /// </summary>
    private void DrawPrefixSmear(
        SKCanvas canvas,
        SKRect plate,
        SKFont titleFont,
        string? prefix,
        float titleLeft,
        float titleBaseline,
        float titleSize,
        int seed,
        Random rng)
    {
        if (prefix is null || !style.Smear.Enabled)
        {
            return;
        }

        var s = style.Smear;
        var prefixWidth = titleFont.MeasureText(prefix);

        DrawGreenSmear(canvas, new(plate.Left - titleSize * s.LeftExtendFactor,
            plate.Top - titleSize * s.TopExtendFactor,
            titleLeft + prefixWidth + titleSize * s.RightPadFactor,
            titleBaseline + titleSize * s.BaselineDropFactor), seed, rng);
    }

    /// <summary>
    /// Рваный зелёный мазок с тяжёлым тёмным мотлингом – в оригинале выглядит как полустёртая
    /// зелёная краска под цифрами и левее плашки. Второй клочковатый мазок у левого торца
    /// добавляет рваную массу вместо ровных полос.
    /// </summary>
    private void DrawGreenSmear(SKCanvas canvas, SKRect area, int seed, Random rng)
    {
        var s = style.Smear;
        var smearColor = StyleColor.Parse(style.Palette.Smear);

        canvas.SaveLayer(null);

        using (var paint = new SKPaint { Color = smearColor, IsAntialias = true })
        {
            using (var path = Grunge.BrushStrokePath(area, seed + 31,
                       area.Height * s.EdgeAmpFactor, area.Height * s.EndAmpFactor))
            {
                canvas.DrawPath(path, paint);
            }

            var clump = new SKRect(area.Left - area.Height * 0.25f, area.Top + area.Height * 0.15f,
                area.Left + area.Width * 0.45f, area.Bottom + area.Height * 0.10f);

            using (var clumpPath = Grunge.BrushStrokePath(clump, seed + 47,
                       clump.Height * 0.14f, clump.Height * 0.22f))
            {
                canvas.DrawPath(clumpPath, paint);
            }
        }

        Grunge.BrushTeeth(canvas, area.Left, area.Top, area.Bottom, -1, smearColor, rng,
            area.Height * s.TeethFactor);

        Grunge.Mottle(canvas, area, rng, StyleColor.Parse(s.MottleColor),
            (int)(area.Width * area.Height / 260f * s.MottleDensity),
            area.Height * s.MottleMaxRadiusFactor);

        Grunge.Distress(canvas, area, rng, s.DistressDensity);

        canvas.Restore();
    }

    /// <summary>
    /// Мелкие акценты вокруг плашки: квадратные «чипсы» вдоль верхней кромки (гуще к концам),
    /// зелёная крошка на правой части плашки и тёмные брызги в левом нижнем углу.
    /// </summary>
    private void DrawChipsAndSplatter(SKCanvas canvas, SKRect plate, Random rng)
    {
        var accents = style.Accents;
        var chipsColor = StyleColor.Parse(style.Palette.Chips);

        Grunge.ChipsAlongLine(canvas, new(plate.Left - 10, plate.Top - 4),
            new(plate.Left + plate.Width * 0.35f, plate.Top - 4), chipsColor, rng,
            accents.ChipsLeftCount, accents.ChipsSpread);

        Grunge.ChipsAlongLine(canvas, new(plate.Right - plate.Width * 0.25f, plate.Top - 4),
            new(plate.Right + 14, plate.Top + 2), chipsColor, rng,
            accents.ChipsRightCount, accents.ChipsSpread);

        Grunge.Splatter(canvas, new(plate.Right - plate.Width * 0.08f, plate.MidY), chipsColor, rng,
            accents.SplatterGreenCount, plate.Height * 0.45f);

        Grunge.Splatter(canvas, new(plate.Left - 24, plate.Bottom - 8),
            StyleColor.Parse(style.Palette.DarkSplatter), rng,
            accents.SplatterDarkCount, plate.Height * 0.40f);
    }

    /// <summary>
    /// Зелёная полоса-мазок под подзаголовок: вертикальный градиент в теле полосы,
    /// «зубья» по торцам (правый рваный сильнее), мотлинг и потёртости.
    /// </summary>
    private void DrawGreenBand(SKCanvas canvas, SKRect band, int seed, Random rng)
    {
        var s = style.Band;
        var bandTop = StyleColor.Parse(style.Palette.BandTop);
        var bandBottom = StyleColor.Parse(style.Palette.BandBottom);

        canvas.SaveLayer(null);

        using (var paint = new SKPaint
               {
                   IsAntialias = true,
                   Shader = SKShader.CreateLinearGradient(new(0, band.Top), new(0, band.Bottom),
                       [bandTop, bandBottom],
                       SKShaderTileMode.Clamp),
               })
        using (var path = Grunge.BrushStrokePath(band, seed + 57,
                   band.Height * s.EdgeAmpFactor, band.Height * s.EndAmpFactor))
        {
            canvas.DrawPath(path, paint);
        }

        Grunge.BrushTeeth(canvas, band.Left, band.Top, band.Bottom, -1, bandBottom, rng,
            band.Height * s.TeethLeftFactor);

        Grunge.BrushTeeth(canvas, band.Right, band.Top, band.Bottom, +1, bandTop, rng,
            band.Height * s.TeethRightFactor);

        Grunge.Mottle(canvas, band, rng, StyleColor.Parse(s.MottleColor),
            (int)(band.Width / 9f * s.MottleDensity), band.Height * s.MottleMaxRadiusFactor);

        Grunge.Distress(canvas, band, rng, s.DistressDensity);

        canvas.Restore();
    }

    /// <summary>
    /// Рисует строку заголовка: зелёный ведущий номер (если есть) и белый основной текст,
    /// каждый – экструдированным 3D-текстом со своей палитрой грани, выдавливания и мотлинга.
    /// </summary>
    private void DrawTitleRow(
        SKCanvas canvas,
        string? prefix,
        string main,
        SKFont font,
        float left,
        float baseline,
        float spaceWidth,
        float size,
        Random rng)
    {
        var palette = style.Palette;
        var x = left;

        if (prefix is not null)
        {
            var prefixWidth = font.MeasureText(prefix, out var prefixBounds);
            Draw3DText(canvas, prefix, font, x, baseline, prefixBounds, size, rng,
                [
                    StyleColor.Parse(palette.GreenFaceTop),
                    StyleColor.Parse(palette.GreenFaceMid),
                    StyleColor.Parse(palette.GreenFaceBottom),
                ],
                StyleColor.Parse(palette.GreenExtrude),
                StyleColor.Parse(style.TitleFx.PrefixMottleColor));

            x += prefixWidth + spaceWidth;
        }

        font.MeasureText(main, out var mainBounds);
        Draw3DText(canvas, main, font, x, baseline, mainBounds, size, rng,
            [
                StyleColor.Parse(palette.WhiteFaceTop),
                StyleColor.Parse(palette.WhiteFaceMid),
                StyleColor.Parse(palette.WhiteFaceBottom),
            ],
            StyleColor.Parse(palette.WhiteExtrude),
            StyleColor.Parse(style.TitleFx.MainMottleColor));
    }

    /// <summary>
    /// Экструдированный 3D-текст с фактурой, по слоям:
    /// глубина выдавливания → обводка → градиентная грань → мотлинг по буквам → мелкие выгрызы.
    /// Всё внутри SaveLayer, чтобы потёртости и мотлинг не задели фон под текстом.
    /// </summary>
    private void Draw3DText(
        SKCanvas canvas,
        string text,
        SKFont font,
        float x,
        float baseline,
        SKRect bounds,
        float size,
        Random rng,
        SKColor[] faceColors,
        SKColor extrudeColor,
        SKColor mottleColor)
    {
        var fx = style.TitleFx;
        var depth = MathF.Max(4f, size * fx.ExtrudeDepthFactor);

        canvas.SaveLayer(null);

        using (var extrude = new SKPaint { Color = extrudeColor, IsAntialias = true })
        {
            var steps = (int)MathF.Ceiling(depth);

            for (var i = steps; i >= 1; i--)
            {
                canvas.DrawText(text, x + i * fx.ExtrudeStepX, baseline + i, SKTextAlign.Left, font, extrude);
            }
        }

        using (var stroke = new SKPaint
               {
                   Color = StyleColor.Parse(style.Palette.TitleStroke),
                   IsAntialias = true,
                   Style = SKPaintStyle.Stroke,
                   StrokeWidth = size * fx.StrokeWidthFactor,
                   StrokeJoin = SKStrokeJoin.Round,
               })
        {
            canvas.DrawText(text, x, baseline, SKTextAlign.Left, font, stroke);
        }

        using (var fill = new SKPaint
               {
                   IsAntialias = true,
                   Shader = SKShader.CreateLinearGradient(new(0, baseline + bounds.Top), new(0, baseline),
                       faceColors, [0f, Math.Clamp(fx.FaceGradientMidStop, 0.05f, 0.95f), 1f],
                       SKShaderTileMode.Clamp),
               })
        {
            canvas.DrawText(text, x, baseline, SKTextAlign.Left, font, fill);
        }

        var textRect = new SKRect(x + bounds.Left, baseline + bounds.Top,
            x + bounds.Right + depth, baseline + depth);

        Grunge.Mottle(canvas, textRect, rng, mottleColor,
            (int)(textRect.Width * textRect.Height / 480f * fx.MottleDensity),
            size * fx.MottleMaxRadiusFactor);

        Grunge.Distress(canvas, textRect, rng, fx.DistressDensity);

        canvas.Restore();
    }

    /// <summary>
    /// Подзаголовок – чёрный текст по центру зелёной полосы (как в оригинале), слегка потёртый.
    /// Потёртости ограничены прямоугольником текста, чтобы не выесть саму полосу.
    /// </summary>
    private void DrawSubtitle(
        SKCanvas canvas,
        string subtitle,
        SKFont font,
        float centerX,
        float baseline,
        float width,
        SKRect bounds,
        Random rng)
    {
        canvas.SaveLayer(null);

        using (var fill = new SKPaint { Color = StyleColor.Parse(style.Palette.SubtitleText), IsAntialias = true })
        {
            canvas.DrawText(subtitle, centerX, baseline, SKTextAlign.Center, font, fill);
        }

        var textRect = new SKRect(centerX - width / 2, baseline + bounds.Top, centerX + width / 2, baseline + bounds.Bottom);
        Grunge.Distress(canvas, textRect, rng, style.Band.SubtitleDistressDensity);

        canvas.Restore();
    }

    /// <summary>
    /// Третья строка (ник/дата/тег) под подзаголовком – слегка потёртый текст внутри наклонённого
    /// блока. Кегль, цвет, конденс и потёртость – в <see cref="BannerStyle.TaglineStyle" />.
    /// </summary>
    private void DrawTagline(
        SKCanvas canvas,
        string tagline,
        SKTypeface typeface,
        float centerX,
        float subBaseline,
        float titleSize,
        Random rng)
    {
        var t = style.Tagline;
        using var font = new SKFont(typeface, titleSize * Math.Clamp(t.SizeFactor, 0.1f, 1f))
        {
            ScaleX = t.ScaleX,
            Subpixel = true,
        };

        var baseline = subBaseline + titleSize * t.BaselineGapFactor;
        var width = font.MeasureText(tagline, out var bounds);

        canvas.SaveLayer(null);
        using (var fill = new SKPaint { Color = StyleColor.Parse(t.Color), IsAntialias = true })
        {
            canvas.DrawText(tagline, centerX, baseline, SKTextAlign.Center, font, fill);
        }

        var rect = new SKRect(centerX - width / 2, baseline + bounds.Top, centerX + width / 2, baseline + bounds.Bottom);
        Grunge.Distress(canvas, rect, rng, t.DistressDensity);
        canvas.Restore();
    }

    /// <summary>
    /// Лого/водяной знак в углу кадра – поверх всего и без наклона блока, поэтому вынесен за
    /// общий Save/Restore. Размер (cover по ширине), угол, отступ и прозрачность –
    /// в <see cref="BannerStyle.LogoStyle" />.
    /// </summary>
    private void DrawLogo(SKCanvas canvas, SKImage logo, int width, int height)
    {
        var l = style.Logo;
        var targetW = width * Math.Clamp(l.SizeFraction, 0.02f, 0.5f);
        var scale = targetW / logo.Width;
        var w = targetW;
        var h = logo.Height * scale;
        var margin = width * Math.Max(0f, l.MarginFraction);

        var corner = (l.Corner ?? "br").Trim().ToLowerInvariant();
        var left = corner is "tr" or "br" ? width - margin - w : margin;
        var top = corner is "bl" or "br" ? height - margin - h : margin;
        var dest = new SKRect(left, top, left + w, top + h);

        var alpha = (byte)(Math.Clamp(l.Opacity, 0f, 1f) * 255);
        using var paint = new SKPaint { IsAntialias = true, Color = SKColors.White.WithAlpha(alpha) };
        canvas.DrawImage(logo, dest, paint);
    }
}
