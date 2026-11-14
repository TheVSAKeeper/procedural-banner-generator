using BannerCore;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using ProceduralBannerGenerator.Models;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProceduralBannerGenerator.Services;

public sealed record McpToolResult(string ResponseJson, SavedState? ActiveStateToApply = null, string? Notice = null, string? Error = null);

public sealed class BannerMcpService(BannerProfileService profiles, IJSRuntime js, NavigationManager nav)
{
    private const string McpTokenKey = "banner-mcp-token-v1";

    private static readonly JsonSerializerOptions McpJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    private static readonly HashSet<string> AllowedSpecKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "title",
        "subtitle",
        "tagline",
        "seed",
        "width",
        "height",
        "transparent",
    };

    public string Token { get; private set; } = RandomNumberGenerator.GetHexString(32, true);

    public async Task EnsureTokenAsync()
    {
        try
        {
            var stored = await js.InvokeAsync<string?>("localStorage.getItem", McpTokenKey);
            if (!string.IsNullOrWhiteSpace(stored) && stored.Trim().Length >= 16)
            {
                Token = stored.Trim();
                return;
            }

            await js.InvokeVoidAsync("localStorage.setItem", McpTokenKey, Token);
        }
        catch
        {
            // ponytail: при недоступном localStorage используется одноразовый токен текущей вкладки
        }
    }

    public async Task CopyAgentPromptAsync()
    {
        await js.InvokeAsync<bool>("bannerMcp.copyText", BuildAgentPrompt());
    }

    public string BuildAgentPrompt()
    {
        var baseUri = nav.BaseUri.EndsWith('/') ? nav.BaseUri : nav.BaseUri + "/";
        var origin = new Uri(baseUri).GetLeftPart(UriPartial.Authority);
        var bridgeUrl = baseUri + "mcp-bridge.js";
        return $$"""
            Подключись к открытому в моём браузере сайту процедурного генератора баннеров ({{baseUri}}) через MCP для создания и редактирования шаблонов:

            0. Перед установкой (или сразу после проверки связи) коротко уточни у меня два вопроса (если я не указал это сразу – используй вариант по умолчанию):
            - **Куда ставить скрипт моста и прописывать MCP-сервер:** по умолчанию – локально в текущую директорию, откуда запущен агент (`./.banner-mcp/mcp-bridge.js` и локальная/проектная конфигурация MCP), либо глобально в профиль пользователя (`~/.banner-mcp` и глобальный конфиг агента).
            - **Делать ли резервные копии профилей на диск:** так как сайт хранит шаблоны в `localStorage` браузера, спроси, нужно ли сохранять JSON-бэкапы шаблонов (через `get_template`) и итоговые PNG на диск в рабочей папке (например, в `./banner-backups/`).

            1. Скачай скрипт локального MCP-моста (без внешних зависимостей) в текущую рабочую директорию `./.banner-mcp` (или в выбранную папку) и запусти фоновый демон с токеном сопряжения вкладки (команда выведет полные пути `nodePath`, `scriptPath` и адрес `mcpHttpUrl`):
            node -e "const fs=require('fs'),p=require('path'),cp=require('child_process'),d=p.join(process.cwd(),'.banner-mcp'),f=p.join(d,'mcp-bridge.js');fs.mkdirSync(d,{recursive:true});fs.writeFileSync(p.join(d,'package.json'),JSON.stringify({type:'commonjs'}));fetch('{{bridgeUrl}}').then(r=>{if(!r.ok)throw new Error(r.status);return r.text()}).then(t=>{fs.writeFileSync(f,t);console.log(cp.execFileSync(process.execPath,[f,'--daemon','--token','{{Token}}','--origin','{{origin}}'],{encoding:'utf8'}))})"
            - Примечание по окружению и оболочке: скачивание скрипта и доступ к локальному мосту на `127.0.0.1:5219` требуют разрешения на сеть и loopback (вне изолированной песочницы, если среда блокирует `127.0.0.1`). Проверь `node --version`: если `node` не найден в `PATH` неинтерактивной оболочки или обёртка менеджера версий (`nvm`, `fnm`, `volta` и т.п.) не инициализирована, найди установленный `node`/`node.exe` в стандартных путях системы (`Program Files\nodejs`, `/usr/local/bin`, `/opt/homebrew/bin`, папки менеджеров версий) и используй его полный путь `nodePath` (при необходимости скачай `{{bridgeUrl}}` в `.banner-mcp/mcp-bridge.js` штатными средствами оболочки и запусти `"<nodePath>" "<scriptPath>" --daemon --token {{Token}} --origin {{origin}}`).

            2. Зарегистрируй MCP-сервер `banner-generator` в конфигурации агента (по умолчанию – локально для текущей директории/проекта, либо глобально в пользовательской конфигурации по выбору пользователя или если агент поддерживает только единый конфиг):
            - Предпочтительный транспорт – streamable HTTP по адресу `mcpHttpUrl` из вывода шага 1 (`http://127.0.0.1:5219/mcp?token={{Token}}`).
            - Если среда поддерживает только `stdio` – укажи полные абсолютные пути `nodePath` и `scriptPath` из вывода шага 1 (без символа `~`, так как `spawn` на Windows его не раскрывает): команда `<nodePath>`, аргументы `[ "<scriptPath>" ]`.
            - Если только что добавленный MCP-сервер (для **любого** транспорта – и HTTP, и stdio) не появился среди инструментов текущей сессии без перезапуска, вызывай инструменты прямо сейчас через CLI по полным путям:
              `"<nodePath>" "<scriptPath>" call <tool> '<json>' [--out preview.png]`
              (в PowerShell сложный JSON можно передавать через файл: `"<nodePath>" "<scriptPath>" call <tool> --args-file patch.json --out preview.png`).

            Доступные инструменты MCP:
            - `list_templates` – список всех сохранённых шаблонов на сайте и какой сейчас активен (`isActive: true`);
            - `get_template` – полный JSON шаблона (`spec` + `style`) и история шагов (`history`);
            - `get_style_guide` – описание всех 10 секций и ручек `BannerStyle` и дефолтных значений;
            - `create_template` – создать новый шаблон (по умолчанию клонируй активный шаблон через `baseTemplateId`/`baseTemplateName`, если нужно сохранить текущий стиль, явно задавай или очищай `title`/`subtitle`/`tagline` под новую задачу и оставляй `activate: true`, если не просили иначе);
            - `update_template` – частично пропатчить `spec` и `style` шаблона (достаточно передать только меняемые секции/поля и краткий `summary` для истории шагов) с мгновенной перерисовкой в браузере;
            - `undo_template` – откатить изменения шаблона на 1 или несколько шагов назад (`steps`) по общей истории правок;
            - `duplicate_template`, `rename_template`, `delete_template`, `set_active_template`, `reset_template_style`, `render_banner`.

            После каждой правки сохраняй превью PNG в доступный файл (`--out preview.png` или из ответа MCP), открывай его визуально для проверки и при неудаче докручивай параметры через `update_template` или откатывай шаг через `undo_template` до отчёта.
            Проверь связь вызовом `list_templates`: выведи полный список сохранённых шаблонов, отдельно назови активный (`isActive: true`), сообщи статус подключения (`connectedClients`), зарегистрированный транспорт и полный путь `scriptPath`, и уточни у меня настройки размещения и бэкапа профилей на диск.
            """;
    }

    public async Task<McpToolResult> InvokeToolAsync(
        string tool,
        string argsJson,
        int defaultDebounceMs,
        Func<SavedState, string, string?> renderPreviewBase64)
    {
        try
        {
            var args = string.IsNullOrWhiteSpace(argsJson)
                ? new()
                : JsonNode.Parse(argsJson)?.AsObject() ?? new JsonObject();

            var (payload, activeState, notice) = tool switch
            {
                "list_templates" => (McpListTemplates(), null, null),
                "get_template" => (McpGetTemplate(args, renderPreviewBase64), null, null),
                "get_style_guide" => (McpGetStyleGuide(), null, null),
                "create_template" => await McpCreateTemplateAsync(args, defaultDebounceMs, renderPreviewBase64),
                "update_template" => await McpUpdateTemplateAsync(args, renderPreviewBase64),
                "undo_template" => await McpUndoTemplateAsync(args, renderPreviewBase64),
                "duplicate_template" => await McpDuplicateTemplateAsync(args, renderPreviewBase64),
                "rename_template" => await McpRenameTemplateAsync(args, renderPreviewBase64),
                "delete_template" => await McpDeleteTemplateAsync(args),
                "set_active_template" => await McpSetActiveTemplateAsync(args, renderPreviewBase64),
                "reset_template_style" => await McpResetTemplateStyleAsync(args, renderPreviewBase64),
                "render_banner" => (McpRenderBanner(args, renderPreviewBase64), null, null),
                _ => throw new InvalidOperationException($"Неизвестный инструмент MCP: {tool}"),
            };

            return new(JsonSerializer.Serialize(payload, McpJsonOptions), activeState, notice);
        }
        catch (Exception ex)
        {
            var errorJson = JsonSerializer.Serialize(
                new
                {
                    error = ex.Message,
                },
                McpJsonOptions);

            return new(errorJson, Error: "MCP: " + ex.Message);
        }
    }

    private static object McpGetStyleGuide()
    {
        return new
        {
            summary = "Декларативный стиль баннера (BannerStyle) из 10 секций. В update_template и create_template достаточно передавать только изменяемые секции и поля.",
            conventions = new
            {
                colors = "#RRGGBB или #RRGGBBAA (альфа в конце, CSS-порядок)",
                fractions = "*Fraction – доля от размеров кадра (Width/Height)",
                factors = "*Factor – доля от кегля заголовка (или высоты соответствующего элемента)",
                numericPrefix = "Ведущий номер в заголовке ('290. ...') автоматически рисуется зелёным 3D с подмазкой Smear, остальной текст – белым 3D",
            },
            sections = new
            {
                palette =
                    "Чистые цвета: грани зелёного номера (greenFaceTop/Mid/Bottom, greenExtrude), грани белого заголовка (whiteFaceTop/Mid/Bottom, whiteExtrude), полоса (bandTop/Bottom), подмазка (smear), плашка (plate), подзаголовок (subtitleText), обводка (titleStroke), чипсы и брызги (chips, darkSplatter).",
                layout = "Кегли, конденс, базовая линия, наклон блока (rotationDegrees -10..10), поля плашки и полосы.",
                titleFx =
                    "Глубина (extrudeDepthFactor) и наклон (extrudeStepX) 3D-экструзии, обводка (strokeWidthFactor), градиент грани, зерно (mottle) и выгрызы (distressDensity).",
                plate = "Чёрная рваная плашка за заголовком: рваность краёв/торцов, зубья кисти слева/справа, пятна краски, выгрызы, тень.",
                smear = "Зелёная подмазка под номером выпуска: enabled, вынос за плашку, рваность, зубья, зерно, выгрызы.",
                band = "Полоса под подзаголовком: рваность краёв/торцов, зубья слева/справа, зерно, выгрызы, потёртость текста подзаголовка.",
                accents = "Чипсы вдоль верхней кромки (chipsLeftCount, chipsRightCount, chipsSpread), зелёная крошка (splatterGreenCount), тёмные брызги (splatterDarkCount).",
                tagline = "Третья строка под подзаголовком: enabled, sizeFactor, color, baselineGapFactor, scaleX, distressDensity, плашка-фон (backgroundEnabled, backgroundColor, backgroundPaddingXFactor, backgroundPaddingYFactor, backgroundEdgeAmpFactor).",
                logo = "Логотип в углу: enabled, corner (tl|tr|bl|br), sizeFraction, opacity, marginFraction.",
                background = "Обработка загруженной картинки-фона: darkenAlpha (0..1), blurSigma (0..30).",
            },
            defaultStyle = new BannerStyle(),
        };
    }

    private static SavedState ApplyPartialPatch(SavedState current, JsonObject args, out IReadOnlyList<string> changedSections)
    {
        var next = current;

        if (TryGetPropertyIgnoreCase(args, "spec", out var specNode) && specNode is not null)
        {
            if (specNode is not JsonObject specObj)
            {
                throw new InvalidOperationException("Поле 'spec' должно быть JSON-объектом.");
            }

            foreach (var kv in specObj)
            {
                if (!AllowedSpecKeys.Contains(kv.Key))
                {
                    throw new InvalidOperationException($"Неизвестное поле spec '{kv.Key}'. Допустимые поля: title, subtitle, tagline, seed, width, height, transparent.");
                }
            }

            if (GetString(specObj, "title") is { } title)
            {
                next = next with
                {
                    Title = title,
                };
            }

            if (GetString(specObj, "subtitle") is { } subtitle)
            {
                next = next with
                {
                    Subtitle = subtitle,
                };
            }

            if (TryGetPropertyIgnoreCase(specObj, "tagline", out var tagNode))
            {
                next = next with
                {
                    Tagline = tagNode?.GetValue<string>() ?? "",
                };
            }

            if (GetInt(specObj, "seed") is { } seed)
            {
                next = next with
                {
                    Seed = seed,
                };
            }

            if (GetInt(specObj, "width") is { } width)
            {
                next = next with
                {
                    Width = Math.Clamp(width, 320, 3840),
                };
            }

            if (GetInt(specObj, "height") is { } height)
            {
                next = next with
                {
                    Height = Math.Clamp(height, 180, 2160),
                };
            }

            if (TryGetPropertyIgnoreCase(specObj, "transparent", out var trNode) && trNode is not null)
            {
                next = next with
                {
                    Transparent = GetBool(specObj, "transparent", next.Transparent),
                };
            }
        }

        var beforeStyle = next.Style;
        if (TryGetPropertyIgnoreCase(args, "style", out var styleNode) && styleNode is not null)
        {
            if (styleNode is not JsonObject stylePatch)
            {
                throw new InvalidOperationException("Поле 'style' должно быть JSON-объектом.");
            }

            var targetObj = JsonNode.Parse(beforeStyle.ToJson())!.AsObject();
            MergeJsonObjects(targetObj, stylePatch);
            var mergedStyle = BannerStyle.FromJson(targetObj.ToJsonString());
            mergedStyle.Validate();
            next = next with
            {
                Style = mergedStyle,
            };
        }

        changedSections = BannerProfileService.GetChangedSections(beforeStyle, next.Style);
        return next;
    }

    private static void MergeJsonObjects(JsonObject target, JsonObject patch)
    {
        foreach (var kv in patch)
        {
            if (kv.Value is null)
            {
                continue;
            }

            var existingKey = target.Select(p => p.Key)
                .FirstOrDefault(k => string.Equals(k, kv.Key, StringComparison.OrdinalIgnoreCase));

            if (existingKey is null)
            {
                throw new InvalidOperationException($"Неизвестное поле или секция стиля '{kv.Key}'. Вызовите get_style_guide для списка секций и ручек.");
            }

            if (target[existingKey] is JsonObject targetChild)
            {
                if (kv.Value is not JsonObject patchChild)
                {
                    throw new InvalidOperationException($"Секция стиля '{existingKey}' должна быть JSON-объектом.");
                }

                MergeJsonObjects(targetChild, patchChild);
            }
            else
            {
                target[existingKey] = kv.Value.DeepClone();
            }
        }
    }

    private static bool TryGetPropertyIgnoreCase(JsonObject obj, string key, out JsonNode? value)
    {
        foreach (var kv in obj)
        {
            if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = kv.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static string? GetString(JsonObject obj, string key)
    {
        return TryGetPropertyIgnoreCase(obj, key, out var node) && node is not null ? node.GetValue<string>() : null;
    }

    private static int? GetInt(JsonObject obj, string key)
    {
        if (!TryGetPropertyIgnoreCase(obj, key, out var node) || node is null)
        {
            return null;
        }

        if (node is JsonValue jv && jv.TryGetValue<int>(out var directInt))
        {
            return directInt;
        }

        if (node.GetValueKind() == JsonValueKind.String && int.TryParse(node.GetValue<string>(), out var parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException($"Поле '{key}' должно быть целым числом.");
    }

    private static bool GetBool(JsonObject obj, string key, bool defaultValue)
    {
        if (!TryGetPropertyIgnoreCase(obj, key, out var node) || node is null)
        {
            return defaultValue;
        }

        if (node is JsonValue jv && jv.TryGetValue<bool>(out var directBool))
        {
            return directBool;
        }

        if (node.GetValueKind() == JsonValueKind.String && bool.TryParse(node.GetValue<string>(), out var parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException($"Поле '{key}' должно быть булевым значением (true/false).");
    }

    private object McpListTemplates()
    {
        return new
        {
            activeId = profiles.ActiveId,
            activeName = profiles.ActiveProfile?.Name ?? "",
            count = profiles.Profiles.Count,
            templates = profiles.Profiles.Select(p => new
                {
                    id = p.Id,
                    name = p.Name,
                    isActive = p.Id == profiles.ActiveId,
                    historyCount = p.History.Count,
                    title = p.State.Title,
                    subtitle = p.State.Subtitle,
                    tagline = p.State.Tagline ?? "",
                    seed = p.State.Seed,
                    width = p.State.Width,
                    height = p.State.Height,
                    transparent = p.State.Transparent,
                })
                .ToArray(),
        };
    }

    private object McpGetTemplate(JsonObject args, Func<SavedState, string, string?> renderPreviewBase64)
    {
        var entry = FindProfile(args, true)!;
        var includePreview = GetBool(args, "includePreviewPng", false);
        return BuildTemplatePayload(entry, null, includePreview, renderPreviewBase64);
    }

    private async Task<(object Payload, SavedState? ActiveState, string Notice)> McpCreateTemplateAsync(
        JsonObject args,
        int defaultDebounceMs,
        Func<SavedState, string, string?> renderPreviewBase64)
    {
        var requestedName = GetString(args, "name")?.Trim();
        var name = string.IsNullOrWhiteSpace(requestedName)
            ? profiles.NextProfileName()
            : profiles.UniqueProfileName(requestedName);

        ProfileEntry? baseProfile = null;
        var baseId = GetString(args, "baseTemplateId");
        var baseName = GetString(args, "baseTemplateName");
        if (!string.IsNullOrEmpty(baseId) || !string.IsNullOrEmpty(baseName))
        {
            baseProfile = profiles.FindProfile(baseId, baseName, true);
        }

        var baseState = baseProfile is not null
            ? baseProfile.State with
            {
            }
            : new()
            {
                Title = "290. ЛЕДОКОЛ XIX",
                Subtitle = "КОРОЛЬ МАШИННОГО ЗАЛА",
                Tagline = "",
                Seed = 290,
                Width = 1280,
                Height = 720,
                Transparent = false,
                RenderDebounceMs = defaultDebounceMs,
                Style = new(),
            };

        var updatedState = ApplyPartialPatch(baseState, args, out var changedSections);
        var activate = GetBool(args, "activate", true);
        var entry = profiles.AddProfile(name, updatedState, activate);
        await profiles.PersistAsync();

        var includePreview = GetBool(args, "includePreviewPng", true);
        var payload = BuildTemplatePayload(entry, changedSections, includePreview, renderPreviewBase64);
        return (payload, activate ? entry.State : null, $"MCP: создан шаблон «{entry.Name}»");
    }

    private async Task<(object Payload, SavedState? ActiveState, string Notice)> McpUpdateTemplateAsync(
        JsonObject args,
        Func<SavedState, string, string?> renderPreviewBase64)
    {
        var entry = FindProfile(args, true)!;
        if (GetString(args, "newName") is { } newName && !string.IsNullOrWhiteSpace(newName))
        {
            var trimmedName = newName.Trim();
            profiles.EnsureUniqueProfileName(trimmedName, entry.Id);
            entry.Name = trimmedName;
        }

        var beforeState = entry.State;
        var updatedState = ApplyPartialPatch(beforeState, args, out var changedSections);
        var summary = GetString(args, "summary")?.Trim();

        if (BannerProfileService.HasTemplateChanged(beforeState, updatedState))
        {
            BannerProfileService.PushHistory(
                entry,
                BannerProfileService.DescribeChange("MCP: правка", beforeState, updatedState, summary),
                beforeState);

            profiles.ResetManualCoalesce();
        }

        entry.State = updatedState;

        var activate = GetBool(args, "activate", true);
        SavedState? activeState = null;
        if (activate || entry.Id == profiles.ActiveId)
        {
            profiles.SetActive(entry);
            activeState = entry.State;
        }

        var notice = string.IsNullOrWhiteSpace(summary)
            ? $"MCP: обновлён шаблон «{entry.Name}»"
            : $"MCP: «{entry.Name}» – {summary}";

        await profiles.PersistAsync();

        var includePreview = GetBool(args, "includePreviewPng", true);
        var payload = BuildTemplatePayload(entry, changedSections, includePreview, renderPreviewBase64);
        return (payload, activeState, notice);
    }

    private async Task<(object Payload, SavedState? ActiveState, string Notice)> McpUndoTemplateAsync(
        JsonObject args,
        Func<SavedState, string, string?> renderPreviewBase64)
    {
        var entry = FindProfile(args, true)!;
        if (entry.History.Count == 0)
        {
            throw new InvalidOperationException($"История изменений шаблона «{entry.Name}» пуста – откатывать нечего.");
        }

        var requestedSteps = GetInt(args, "steps") ?? 1;
        if (requestedSteps < 1)
        {
            throw new InvalidOperationException("Параметр 'steps' должен быть целым числом >= 1.");
        }

        var count = Math.Clamp(requestedSteps, 1, entry.History.Count);
        var beforeUndo = entry.State;
        var targetEntry = entry.History[^count];
        var undoneSteps = entry.History
            .Skip(entry.History.Count - count)
            .Reverse()
            .Select(h => h.Label)
            .ToArray();

        entry.History.RemoveRange(entry.History.Count - count, count);
        entry.State = targetEntry.State;
        profiles.ResetManualCoalesce();

        var changedSections = BannerProfileService.GetChangedSections(beforeUndo.Style, entry.State.Style);
        var activeState = entry.Id == profiles.ActiveId ? entry.State : null;

        var notice = count == 1
            ? $"MCP: откат «{entry.Name}» ({targetEntry.Label})"
            : $"MCP: откат «{entry.Name}» на {count} шаг.";

        await profiles.PersistAsync();

        var includePreview = GetBool(args, "includePreviewPng", true);
        var payload = BuildTemplatePayload(entry, changedSections, includePreview, renderPreviewBase64, undoneSteps);
        return (payload, activeState, notice);
    }

    private async Task<(object Payload, SavedState? ActiveState, string Notice)> McpDuplicateTemplateAsync(
        JsonObject args,
        Func<SavedState, string, string?> renderPreviewBase64)
    {
        var src = FindProfile(args, true)!;
        var requestedName = GetString(args, "newName")?.Trim();
        var copyName = string.IsNullOrWhiteSpace(requestedName)
            ? profiles.UniqueProfileName(src.Name + " копия")
            : profiles.UniqueProfileName(requestedName);

        var activate = GetBool(args, "activate", true);
        var copy = profiles.AddProfile(
            copyName,
            src.State with
            {
            },
            activate);

        await profiles.PersistAsync();

        var payload = BuildTemplatePayload(copy, null, true, renderPreviewBase64);
        return (payload, activate ? copy.State : null, $"MCP: дублирован шаблон «{copy.Name}»");
    }

    private async Task<(object Payload, SavedState? ActiveState, string Notice)> McpRenameTemplateAsync(
        JsonObject args,
        Func<SavedState, string, string?> renderPreviewBase64)
    {
        var entry = FindProfile(args, true)!;
        var newName = GetString(args, "newName")?.Trim();
        if (string.IsNullOrWhiteSpace(newName))
        {
            throw new InvalidOperationException("Не указано новое имя шаблона (newName)");
        }

        profiles.EnsureUniqueProfileName(newName, entry.Id);
        entry.Name = newName;
        await profiles.PersistAsync();

        var payload = BuildTemplatePayload(entry, null, false, renderPreviewBase64);
        return (payload, null, $"MCP: шаблон переименован в «{entry.Name}»");
    }

    private async Task<(object Payload, SavedState? ActiveState, string Notice)> McpDeleteTemplateAsync(JsonObject args)
    {
        if (profiles.Profiles.Count <= 1)
        {
            throw new InvalidOperationException("Нельзя удалить последний оставшийся шаблон");
        }

        var entry = FindProfile(args, true)!;
        var deletedName = entry.Name;
        var deletedId = entry.Id;

        var (active, activeChanged) = profiles.RemoveProfile(deletedId);
        await profiles.PersistAsync();

        var payload = new
        {
            deletedId,
            deletedName,
            activeId = active.Id,
            activeName = active.Name,
            remainingCount = profiles.Profiles.Count,
        };

        return (payload, activeChanged ? active.State : null, $"MCP: удалён шаблон «{deletedName}»");
    }

    private async Task<(object Payload, SavedState? ActiveState, string Notice)> McpSetActiveTemplateAsync(
        JsonObject args,
        Func<SavedState, string, string?> renderPreviewBase64)
    {
        var entry = FindProfile(args, true)!;
        profiles.SetActive(entry);
        await profiles.PersistAsync();

        var includePreview = GetBool(args, "includePreviewPng", true);
        var payload = BuildTemplatePayload(entry, null, includePreview, renderPreviewBase64);
        return (payload, entry.State, $"MCP: выбран шаблон «{entry.Name}»");
    }

    private async Task<(object Payload, SavedState? ActiveState, string Notice)> McpResetTemplateStyleAsync(
        JsonObject args,
        Func<SavedState, string, string?> renderPreviewBase64)
    {
        var entry = FindProfile(args, true)!;
        var beforeState = entry.State;
        var after = new BannerStyle();
        var changed = BannerProfileService.GetChangedSections(beforeState.Style, after);
        var nextState = beforeState with
        {
            Style = after,
        };

        if (BannerProfileService.HasTemplateChanged(beforeState, nextState))
        {
            BannerProfileService.PushHistory(
                entry,
                BannerProfileService.DescribeChange("MCP: сброс стиля", beforeState, nextState),
                beforeState);

            profiles.ResetManualCoalesce();
        }

        entry.State = nextState;
        var activeState = entry.Id == profiles.ActiveId ? entry.State : null;
        await profiles.PersistAsync();

        var includePreview = GetBool(args, "includePreviewPng", true);
        var payload = BuildTemplatePayload(entry, changed, includePreview, renderPreviewBase64);
        return (payload, activeState, $"MCP: сброшен стиль шаблона «{entry.Name}»");
    }

    private object McpRenderBanner(JsonObject args, Func<SavedState, string, string?> renderPreviewBase64)
    {
        var entry = FindProfile(args, true)!;
        var tempState = ApplyPartialPatch(entry.State, args, out _);
        var pngBase64 = renderPreviewBase64(tempState, entry.Id);
        return new
        {
            templateId = entry.Id,
            templateName = entry.Name,
            width = Math.Clamp(tempState.Width, 320, 3840),
            height = Math.Clamp(tempState.Height, 180, 2160),
            seed = tempState.Seed,
            previewPngBase64 = pngBase64,
        };
    }

    private ProfileEntry? FindProfile(JsonObject args, bool required)
    {
        var id = GetString(args, "id")?.Trim();
        var name = GetString(args, "name")?.Trim();
        return profiles.FindProfile(id, name, required);
    }

    private object BuildTemplatePayload(
        ProfileEntry entry,
        IReadOnlyList<string>? changedSections,
        bool includePreview,
        Func<SavedState, string, string?> renderPreviewBase64,
        IReadOnlyList<string>? undoneSteps = null)
    {
        var s = entry.State;
        return new
        {
            id = entry.Id,
            name = entry.Name,
            isActive = entry.Id == profiles.ActiveId,
            changedSections = changedSections ?? [],
            undoneSteps = undoneSteps ?? [],
            historyCount = entry.History.Count,
            history = entry.History
                .AsEnumerable()
                .Reverse()
                .Select((h, idx) => new
                {
                    stepsBack = idx + 1,
                    label = h.Label,
                    timestamp = h.Timestamp,
                })
                .ToArray(),
            spec = new
            {
                title = s.Title,
                subtitle = s.Subtitle,
                tagline = s.Tagline ?? "",
                seed = s.Seed,
                width = s.Width,
                height = s.Height,
                transparent = s.Transparent,
            },
            style = s.Style,
            previewPngBase64 = includePreview ? renderPreviewBase64(s, entry.Id) : null,
        };
    }
}
