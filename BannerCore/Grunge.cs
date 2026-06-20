using SkiaSharp;

namespace BannerCore;

/// <summary>
/// Процедурные кисти гранжа поверх SkiaSharp – строительные блоки рендера баннера:
/// мазок кисти с шумными краями (<see cref="BrushStrokePath" />), сухие «зубья» на торцах
/// (<see cref="BrushTeeth" />), мотлинг-фактура поверх нарисованного (<see cref="Mottle" />, SrcATop),
/// брызги с квадратными чипсами (<see cref="Splatter" />, <see cref="ChipsAlongLine" />) и
/// потёртости-«выгрызы» (<see cref="Distress" />, DstOut). Случайность берётся из переданного
/// <see cref="Random" />, форма края – из детерминированного value-шума по seed, поэтому весь
/// гранж воспроизводим.
/// </summary>
public static class Grunge
{
    /// <summary>
    /// Контур мазка кисти: края дрожат многооктавным value-шумом,
    /// торцы (лево/право) рваные сильнее горизонтальных краёв.
    /// </summary>
    public static SKPath BrushStrokePath(SKRect rect, int seed, float edgeAmp, float endAmp)
    {
        var points = new List<SKPoint>();
        var step = MathF.Max(3f, rect.Height * 0.04f);
        var scale = rect.Height * 0.8f;

        for (var x = rect.Left; x <= rect.Right; x += step)
        {
            points.Add(new(x, rect.Top + Fbm(x / scale, seed) * edgeAmp));
        }

        for (var y = rect.Top; y <= rect.Bottom; y += step)
        {
            points.Add(new(rect.Right + Fbm(y / (scale * 0.4f), seed + 7) * endAmp, y));
        }

        for (var x = rect.Right; x >= rect.Left; x -= step)
        {
            points.Add(new(x, rect.Bottom + Fbm(x / scale, seed + 13) * edgeAmp));
        }

        for (var y = rect.Bottom; y >= rect.Top; y -= step)
        {
            points.Add(new(rect.Left + Fbm(y / (scale * 0.4f), seed + 23) * endAmp, y));
        }

        var path = new SKPath();
        path.AddPoly(points.ToArray());
        return path;
    }

    /// <summary>
    /// Сухие «зубья» кисти на торце мазка: горизонтальные штрихи наружу + выгрызы внутрь.
    /// Вызывать внутри SaveLayer мазка – выгрызы идут через DstOut.
    /// </summary>
    public static void BrushTeeth(
        SKCanvas canvas,
        float edgeX,
        float top,
        float bottom,
        int direction,
        SKColor color,
        Random rng,
        float maxLength)
    {
        using var paint = new SKPaint { IsAntialias = true };
        using var bite = new SKPaint { Color = SKColors.Black, IsAntialias = true, BlendMode = SKBlendMode.DstOut };

        for (var y = top; y < bottom;)
        {
            var thickness = 2f + (float)rng.NextDouble() * 2.5f;
            // распределение к середине: зубья плотные и чанковые, а не редкие нити
            var r = (float)rng.NextDouble();
            var length = maxLength * (0.15f + 0.85f * r * r);
            var x2 = edgeX + direction * length;

            paint.Color = color.WithAlpha((byte)(170 + rng.Next(86)));
            canvas.DrawRect(new(MathF.Min(edgeX - direction * 4, x2), y,
                MathF.Max(edgeX - direction * 4, x2), y + thickness), paint);

            // встречный выгрыз внутрь – торец становится «прозрачно-рваным», а не бахромой
            if (rng.NextDouble() < 0.45)
            {
                var biteLength = (float)(rng.NextDouble() * rng.NextDouble()) * maxLength * 0.5f;
                var biteX2 = edgeX - direction * biteLength;
                var biteY = y + thickness;

                canvas.DrawRect(new(MathF.Min(edgeX, biteX2), biteY,
                    MathF.Max(edgeX, biteX2), biteY + 1.5f + (float)rng.NextDouble() * 2f), bite);
            }

            y += thickness + (float)rng.NextDouble() * 1.2f;
        }
    }

    /// <summary>
    /// Мотлинг: полупрозрачные пятна ТОЛЬКО по уже нарисованному (SrcATop) –
    /// фактура потёртой краски на буквах/мазке. Вызывать внутри SaveLayer.
    /// </summary>
    public static void Mottle(SKCanvas canvas, SKRect area, Random rng, SKColor color, int count, float maxRadius)
    {
        using var paint = new SKPaint { Color = color, IsAntialias = true, BlendMode = SKBlendMode.SrcATop };

        for (var i = 0; i < count; i++)
        {
            var x = area.Left + (float)rng.NextDouble() * area.Width;
            var y = area.Top + (float)rng.NextDouble() * area.Height;
            // зерно вытянуто по горизонтали – направление мазка кисти, а не круглый «горошек»
            var rx = (float)(rng.NextDouble() * rng.NextDouble()) * maxRadius + 1.5f;
            var ry = rx * (0.25f + (float)rng.NextDouble() * 0.45f);

            canvas.DrawOval(x, y, rx, ry, paint);
        }
    }

    /// <summary>
    /// Кластер брызг краски вокруг точки: облако кружков убывающего радиуса с примесью квадратных
    /// «чипсов»; разброс гуще к центру (грубое подобие гауссова распределения).
    /// </summary>
    public static void Splatter(SKCanvas canvas, SKPoint center, SKColor color, Random rng, int count, float spread)
    {
        using var paint = new SKPaint { Color = color, IsAntialias = true };

        for (var i = 0; i < count; i++)
        {
            // среднее двух равномерных – грубое подобие гауссова разброса (гуще к центру)
            var dx = (float)(rng.NextDouble() + rng.NextDouble() - 1) * spread;
            var dy = (float)(rng.NextDouble() + rng.NextDouble() - 1) * spread * 0.7f;

            if (rng.NextDouble() < 0.25)
            {
                Chip(canvas, new(center.X + dx, center.Y + dy), color,
                    2f + (float)rng.NextDouble() * 4f, rng);
            }
            else
            {
                var radius = (float)(rng.NextDouble() * rng.NextDouble()) * 5f + 0.8f;
                canvas.DrawCircle(center.X + dx, center.Y + dy, radius, paint);
            }
        }
    }

    /// <summary>
    /// Раскидывает квадратные «чипсы» вдоль отрезка <paramref name="from" />–<paramref name="to" />
    /// со случайным разбросом <paramref name="spread" /> – характерные осколки цифровых кистей.
    /// </summary>
    public static void ChipsAlongLine(
        SKCanvas canvas,
        SKPoint from,
        SKPoint to,
        SKColor color,
        Random rng,
        int count,
        float spread)
    {
        for (var i = 0; i < count; i++)
        {
            var t = (float)rng.NextDouble();
            var x = from.X + (to.X - from.X) * t + (float)(rng.NextDouble() - 0.5) * spread;
            var y = from.Y + (to.Y - from.Y) * t + (float)(rng.NextDouble() - 0.5) * spread;

            Chip(canvas, new(x, y), color, 2f + (float)rng.NextDouble() * 4.5f, rng);
        }
    }

    /// <summary>
    /// Потёртости: точки и царапины «выгрызаются» из уже нарисованного через DstOut.
    /// Вызывать ВНУТРИ SaveLayer вокруг объекта – иначе выест и фон под ним.
    /// </summary>
    public static void Distress(SKCanvas canvas, SKRect area, Random rng, float density = 1f)
    {
        var dots = (int)(area.Width * area.Height / 550f * density);

        using var bite = new SKPaint { Color = SKColors.Black, IsAntialias = true, BlendMode = SKBlendMode.DstOut };

        for (var i = 0; i < dots; i++)
        {
            var x = area.Left + (float)rng.NextDouble() * area.Width;
            var y = area.Top + (float)rng.NextDouble() * area.Height;
            var radius = (float)(rng.NextDouble() * rng.NextDouble()) * 2.2f + 0.4f;

            canvas.DrawCircle(x, y, radius, bite);
        }

        using var scratch = new SKPaint
        {
            Color = SKColors.Black,
            IsAntialias = true,
            BlendMode = SKBlendMode.DstOut,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.2f,
        };

        var scratches = (int)(6 * density);

        for (var i = 0; i < scratches; i++)
        {
            var x = area.Left + (float)rng.NextDouble() * area.Width;
            var y = area.Top + (float)rng.NextDouble() * area.Height;
            var length = 10f + (float)rng.NextDouble() * 28f;
            var angle = (float)(rng.NextDouble() * Math.PI);

            canvas.DrawLine(x, y, x + length * MathF.Cos(angle), y + length * MathF.Sin(angle), scratch);
        }
    }

    private static void Chip(SKCanvas canvas, SKPoint center, SKColor color, float size, Random rng)
    {
        using var paint = new SKPaint { Color = color, IsAntialias = true };

        canvas.Save();
        canvas.RotateDegrees((float)rng.NextDouble() * 20f - 10f, center.X, center.Y);
        canvas.DrawRect(center.X - size / 2, center.Y - size / 2, size, size, paint);
        canvas.Restore();
    }

    /// <summary>
    /// Многооктавный value-шум в [-1, 1]: низкие частоты дают крупные «лохмотья» края,
    /// высокие – мелкую рваность.
    /// </summary>
    private static float Fbm(float t, int seed, int octaves = 3)
    {
        float sum = 0, amplitude = 1, frequency = 1, norm = 0;

        for (var o = 0; o < octaves; o++)
        {
            sum += amplitude * ValueNoise(t * frequency, seed + o * 101);
            norm += amplitude;
            amplitude *= 0.5f;
            frequency *= 2.1f;
        }

        return sum / norm;
    }

    private static float ValueNoise(float t, int seed)
    {
        var i = (int)MathF.Floor(t);
        var f = t - i;
        var s = f * f * (3 - 2 * f); // smoothstep

        return Hash(i, seed) * (1 - s) + Hash(i + 1, seed) * s;
    }

    private static float Hash(int i, int seed)
    {
        unchecked
        {
            var n = i * 374761393 + seed * 668265263;
            n = n << 13 ^ n;
            return 1f - (n * (n * n * 15731 + 789221) + 1376312589 & 0x7fffffff) / 1073741824f;
        }
    }
}
