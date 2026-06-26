using System.IO.Compression;
using System.Text;

namespace BannerCore;

/// <summary>
/// Кодек стиля для передачи «бот → сайт» через URL Mini App (обратное направление к <see cref="BannerExport"/>,
/// который идёт «сайт → бот» через <c>sendData</c>). Стиль gzip-сжимается и кодируется в base64url, чтобы влезть
/// в строку ссылки без спецсимволов (<c>+ / =</c> заменены, padding убран). Декод ограничен по размеру —
/// параметр ссылки управляется пользователем (защита от gzip-бомбы в его же вкладке).
/// </summary>
public static class BannerLink
{
    private const int MaxDecodedBytes = 256 * 1024;

    public static string PackStyle(BannerStyle style)
    {
        var raw = Encoding.UTF8.GetBytes(style.ToJson());
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.SmallestSize, leaveOpen: true))
            gz.Write(raw, 0, raw.Length);
        return ToBase64Url(ms.ToArray());
    }

    public static BannerStyle UnpackStyle(string packed)
    {
        using var input = new MemoryStream(FromBase64Url(packed));
        using var gz = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();

        var buffer = new byte[8192];
        var total = 0;
        int read;
        while ((read = gz.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > MaxDecodedBytes)
                throw new InvalidOperationException("Стиль в ссылке слишком большой");
            output.Write(buffer, 0, read);
        }

        return BannerStyle.FromJson(Encoding.UTF8.GetString(output.GetBuffer(), 0, (int)output.Length));
    }

    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        s += (s.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        return Convert.FromBase64String(s);
    }
}
