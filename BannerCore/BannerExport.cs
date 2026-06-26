using System.Text.Json;

namespace BannerCore;

/// <summary>
/// Конверт обмена «сайт → бот» через Telegram Mini App (<c>sendData</c>): спека баннера + полный стиль
/// + имя профиля. Сериализуется <b>компактно</b> (без отступов) – payload <c>sendData</c> ограничен
/// 4096 байтами, дефолтный стиль ~2.4 КБ, запас есть. <see cref="Version" /> ловит скос версий ядра
/// между независимыми деплоями сайта и бота. Картинки фона/лого сюда не входят (слишком большие, не
/// перегоняются) – бот рисует процедурно/со своим лого.
/// </summary>
public sealed record BannerExport(int Version, string Profile, BannerSpec Spec, BannerStyle Style)
{
    public const int CurrentVersion = 1;

    public const int MaxPayloadBytes = 4096;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public string ToJson()
    {
        return JsonSerializer.Serialize(this, JsonOptions);
    }

    public static BannerExport FromJson(string json)
    {
        return JsonSerializer.Deserialize<BannerExport>(json, JsonOptions)
               ?? throw new InvalidOperationException("Пустой JSON конверта баннера");
    }
}
