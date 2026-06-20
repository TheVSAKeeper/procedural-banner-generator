using System.Globalization;
using SkiaSharp;

namespace ProceduralBannerGenerator;

/// <summary>Парсер цвета стиля: #RRGGBB или #RRGGBBAA (CSS-порядок, альфа в конце) —
/// нарочно не SKColor.Parse, у которого 8-значная форма #AARRGGBB и LLM бы путалась.</summary>
public static class StyleColor
{
    public static SKColor Parse(string hex)
    {
        var span = hex.AsSpan().TrimStart('#');

        if (span.Length is not (6 or 8))
            throw new FormatException($"Цвет должен быть #RRGGBB или #RRGGBBAA: {hex}");

        var r = byte.Parse(span[..2], NumberStyles.HexNumber);
        var g = byte.Parse(span[2..4], NumberStyles.HexNumber);
        var b = byte.Parse(span[4..6], NumberStyles.HexNumber);
        var a = span.Length == 8 ? byte.Parse(span[6..8], NumberStyles.HexNumber) : (byte)0xFF;

        return new SKColor(r, g, b, a);
    }
}
