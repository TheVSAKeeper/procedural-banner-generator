using System.Text.Json;

namespace BannerCore;

/// <summary>
/// Конверт обмена «сайт → бот» через Telegram Mini App (<c>sendData</c>): спека баннера + полный стиль
/// + имя профиля + необязательный промпт правки. Сериализуется <b>компактно</b> (без отступов) – payload
/// <c>sendData</c> ограничен 4096 байтами, дефолтный стиль ~2.4 КБ, запас есть. <see cref="Version" />
/// ловит скос версий ядра между независимыми деплоями сайта и бота. Картинки фона/лого сюда не входят
/// (слишком большие, не перегоняются) – бот рисует процедурно/со своим лого.
/// </summary>
/// <param name="Prompt">Пусто – бот только рисует присланный стиль; непусто – правит его через LLM
/// на присланном стиле (файл стиля бота не трогается) и возвращает результат ссылкой на сайт.</param>
public sealed record BannerExport(int Version, string Profile, BannerSpec Spec, BannerStyle Style, string Prompt = "")
{
    /// <summary>2 – конверт с <see cref="Prompt" />; 1 (сайт без правки промптом) разбирается как пустой промпт.</summary>
    public const int CurrentVersion = 2;

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
