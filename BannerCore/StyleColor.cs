using SkiaSharp;
using System.Globalization;

namespace BannerCore;

/// <summary>
/// Парсер цвета для стиля баннера в форме <c>#RRGGBB</c> или <c>#RRGGBBAA</c> (CSS-порядок,
/// альфа в конце). Намеренно не <see cref="SKColor.Parse(string)" />: у того 8-значная форма –
/// <c>#AARRGGBB</c> (альфа в начале), и правящая стиль LLM в ней путалась бы.
/// </summary>
public static class StyleColor
{
    /// <summary>
    /// Разбирает hex-строку цвета (с ведущим <c>#</c> или без) в <see cref="SKColor" />.
    /// Для 6-значной формы альфа равна <c>0xFF</c>.
    /// </summary>
    /// <param name="hex">Цвет в формате <c>#RRGGBB</c> или <c>#RRGGBBAA</c>.</param>
    /// <returns>Разобранный непрозрачный или полупрозрачный цвет.</returns>
    /// <exception cref="FormatException">Длина не равна 6 или 8 hex-символам.</exception>
    public static SKColor Parse(string hex)
    {
        var span = hex.AsSpan().TrimStart('#');

        if (span.Length is not (6 or 8))
        {
            throw new FormatException($"Цвет должен быть #RRGGBB или #RRGGBBAA: {hex}");
        }

        var r = byte.Parse(span[..2], NumberStyles.HexNumber);
        var g = byte.Parse(span[2..4], NumberStyles.HexNumber);
        var b = byte.Parse(span[4..6], NumberStyles.HexNumber);
        var a = span.Length == 8 ? byte.Parse(span[6..8], NumberStyles.HexNumber) : (byte)0xFF;

        return new(r, g, b, a);
    }
}
