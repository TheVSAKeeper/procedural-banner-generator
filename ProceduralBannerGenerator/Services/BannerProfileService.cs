using BannerCore;
using Microsoft.JSInterop;
using ProceduralBannerGenerator.Models;
using System.Text.Json;

namespace ProceduralBannerGenerator.Services;

public sealed class BannerProfileService(IJSRuntime js)
{
    private const string StorageKey = "banner-state-v1";
    private const string ProfilesKey = "banner-profiles-v1";
    private const int MaxHistorySteps = 20;
    private const long ManualCoalesceWindowMs = 1500;

    private List<ProfileEntry> _profiles = [];
    private long _lastManualPushTicks;
    private string _lastManualPushProfileId = "";

    public IReadOnlyList<ProfileEntry> Profiles => _profiles;

    public string ActiveId { get; private set; } = "";

    public ProfileEntry? ActiveProfile => _profiles.FirstOrDefault(p => p.Id == ActiveId);

    public IReadOnlyList<HistoryEntry> ActiveHistory => ActiveProfile?.History ?? [];

    public static void PushHistory(ProfileEntry profile, string label, SavedState previousState)
    {
        profile.History.Add(
            new()
            {
                Label = label,
                Timestamp = DateTime.Now.ToString("HH:mm:ss"),
                State = previousState,
            });

        while (profile.History.Count > MaxHistorySteps)
        {
            profile.History.RemoveAt(0);
        }
    }

    public static bool HasTemplateChanged(SavedState a, SavedState b)
    {
        return a.Title != b.Title
            || a.Subtitle != b.Subtitle
            || (a.Tagline ?? "") != (b.Tagline ?? "")
            || a.Seed != b.Seed
            || a.Width != b.Width
            || a.Height != b.Height
            || a.Transparent != b.Transparent
            || a.Style != b.Style;
    }

    public static string DescribeChange(string prefix, SavedState before, SavedState after, string? customSummary = null)
    {
        var parts = new List<string>();
        if (before.Title != after.Title || before.Subtitle != after.Subtitle || (before.Tagline ?? "") != (after.Tagline ?? ""))
        {
            parts.Add("текст");
        }

        if (before.Seed != after.Seed)
        {
            parts.Add("seed");
        }

        if (before.Width != after.Width || before.Height != after.Height || before.Transparent != after.Transparent)
        {
            parts.Add("кадр");
        }

        parts.AddRange(GetChangedSections(before.Style, after.Style));

        var suffix = parts.Count > 0 ? $" ({string.Join(", ", parts)})" : "";
        if (!string.IsNullOrWhiteSpace(customSummary))
        {
            return $"MCP: {customSummary.Trim()}{suffix}";
        }

        return $"{prefix}{suffix}";
    }

    public static IReadOnlyList<string> GetChangedSections(BannerStyle before, BannerStyle after)
    {
        var changed = new List<string>();
        if (before.Palette != after.Palette)
        {
            changed.Add(nameof(BannerStyle.Palette));
        }

        if (before.Layout != after.Layout)
        {
            changed.Add(nameof(BannerStyle.Layout));
        }

        if (before.TitleFx != after.TitleFx)
        {
            changed.Add(nameof(BannerStyle.TitleFx));
        }

        if (before.Plate != after.Plate)
        {
            changed.Add(nameof(BannerStyle.Plate));
        }

        if (before.Smear != after.Smear)
        {
            changed.Add(nameof(BannerStyle.Smear));
        }

        if (before.Band != after.Band)
        {
            changed.Add(nameof(BannerStyle.Band));
        }

        if (before.Accents != after.Accents)
        {
            changed.Add(nameof(BannerStyle.Accents));
        }

        if (before.Tagline != after.Tagline)
        {
            changed.Add(nameof(BannerStyle.Tagline));
        }

        if (before.Logo != after.Logo)
        {
            changed.Add(nameof(BannerStyle.Logo));
        }

        if (before.Background != after.Background)
        {
            changed.Add(nameof(BannerStyle.Background));
        }

        return changed;
    }

    public static string NewId()
    {
        return Guid.NewGuid().ToString("N")[..8];
    }

    public void ResetManualCoalesce()
    {
        _lastManualPushTicks = 0;
    }

    public async Task<SavedState> LoadAsync(SavedState fallbackState)
    {
        try
        {
            var json = await js.InvokeAsync<string?>("localStorage.getItem", ProfilesKey);
            if (!string.IsNullOrEmpty(json)
                && JsonSerializer.Deserialize<ProfileStore>(json) is { Profiles.Count: > 0 } store)
            {
                foreach (var p in store.Profiles)
                {
                    p.State.Style.Validate();
                }

                _profiles = store.Profiles;
                ActiveId = _profiles.Any(p => p.Id == store.ActiveId) ? store.ActiveId : _profiles[0].Id;
                return _profiles.First(p => p.Id == ActiveId).State;
            }

            // Миграция одиночного снимка banner-state-v1 → профиль «Локальный» (ничего не теряем).
            var legacy = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            var migrated = string.IsNullOrEmpty(legacy) ? null : JsonSerializer.Deserialize<SavedState>(legacy);
            migrated?.Style.Validate();
            return SeedSingleProfile(migrated ?? fallbackState);
        }
        catch
        {
            // битый снимок или недоступное хранилище – один профиль из текущих дефолтов
            if (_profiles.Count == 0)
            {
                return SeedSingleProfile(fallbackState);
            }

            return ActiveProfile?.State ?? fallbackState;
        }
    }

    public ProfileEntry EnsureActiveProfile(SavedState fallbackState)
    {
        var active = ActiveProfile;
        if (active is null)
        {
            active = new()
            {
                Id = NewId(),
                Name = "Локальный",
                State = fallbackState,
            };

            _profiles.Add(active);
            ActiveId = active.Id;
        }

        return active;
    }

    // Фиксирует ручные правки редактора в истории активного профиля и сохраняет реестр в localStorage.
    public async Task CommitUiStateAsync(SavedState current, string? explicitLabel = null)
    {
        var active = EnsureActiveProfile(current);
        if (HasTemplateChanged(active.State, current))
        {
            var now = Environment.TickCount64;
            if (explicitLabel is null
                && _lastManualPushProfileId == active.Id
                && (now - _lastManualPushTicks) <= ManualCoalesceWindowMs
                && active.History.Count > 0)
            {
                var top = active.History[^1];
                if (!HasTemplateChanged(top.State, current))
                {
                    active.History.RemoveAt(active.History.Count - 1);
                }
                else
                {
                    active.History[^1] = top with
                    {
                        Label = DescribeChange("Ручная правка", top.State, current),
                        Timestamp = DateTime.Now.ToString("HH:mm:ss"),
                    };
                }

                _lastManualPushTicks = now;
            }
            else
            {
                var prefix = explicitLabel ?? "Ручная правка";
                PushHistory(active, DescribeChange(prefix, active.State, current), active.State);
                if (explicitLabel is null)
                {
                    _lastManualPushProfileId = active.Id;
                    _lastManualPushTicks = now;
                }
                else
                {
                    _lastManualPushTicks = 0;
                }
            }
        }

        active.State = current;
        await PersistAsync();
    }

    public async Task<bool> FlushActiveProfileAsync(SavedState current)
    {
        var active = ActiveProfile;
        if (active is null)
        {
            return false;
        }

        var changed = false;
        if (HasTemplateChanged(active.State, current))
        {
            PushHistory(active, DescribeChange("Ручная правка", active.State, current), active.State);
            active.State = current;
            await PersistAsync();
            changed = true;
        }

        _lastManualPushTicks = 0;
        return changed;
    }

    public async Task<(SavedState State, string Notice)?> UndoStepsAsync(int steps)
    {
        var active = ActiveProfile;
        if (active is null || active.History.Count == 0)
        {
            return null;
        }

        var count = Math.Clamp(steps, 1, active.History.Count);
        var targetEntry = active.History[^count];
        active.History.RemoveRange(active.History.Count - count, count);
        active.State = targetEntry.State;
        _lastManualPushTicks = 0;

        var notice = count == 1
            ? $"Откат: {targetEntry.Label}"
            : $"Откат на {count} шаг. ({targetEntry.Label})";

        await PersistAsync();
        return (active.State, notice);
    }

    public async Task<SavedState?> SelectProfileAsync(string id)
    {
        if (string.IsNullOrEmpty(id) || id == ActiveId)
        {
            return null;
        }

        var target = _profiles.FirstOrDefault(p => p.Id == id);
        if (target is null)
        {
            return null;
        }

        ActiveId = id;
        await PersistAsync();
        return target.State;
    }

    public async Task<SavedState> CreateNewProfileAsync()
    {
        var entry = new ProfileEntry
        {
            Id = NewId(),
            Name = NextProfileName(),
            State = new(),
        };

        _profiles.Add(entry);
        ActiveId = entry.Id;
        await PersistAsync();
        return entry.State;
    }

    public async Task<SavedState> DuplicateActiveProfileAsync()
    {
        var src = _profiles.First(p => p.Id == ActiveId);
        var copy = new ProfileEntry
        {
            Id = NewId(),
            Name = UniqueProfileName(src.Name + " копия"),
            State = src.State,
        };

        _profiles.Add(copy);
        ActiveId = copy.Id;
        await PersistAsync();
        return copy.State;
    }

    public async Task<string?> PromptAndRenameActiveProfileAsync()
    {
        var current = ActiveProfile;
        if (current is null)
        {
            return null;
        }

        var name = await js.InvokeAsync<string?>("prompt", "Имя профиля:", current.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var trimmed = name.Trim();
        if (_profiles.Any(p => p.Id != current.Id && string.Equals(p.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            return $"Профиль с именем «{trimmed}» уже существует";
        }

        current.Name = trimmed;
        await PersistAsync();
        return null;
    }

    public async Task<SavedState?> DeleteActiveProfileAsync()
    {
        if (_profiles.Count <= 1)
        {
            return null;
        }

        _profiles.RemoveAll(p => p.Id == ActiveId);
        ActiveId = _profiles[0].Id;
        _lastManualPushTicks = 0;
        await PersistAsync();
        return _profiles[0].State;
    }

    public ProfileEntry AddProfile(string name, SavedState state, bool activate)
    {
        var entry = new ProfileEntry
        {
            Id = NewId(),
            Name = name,
            State = state,
        };

        _profiles.Add(entry);
        if (activate)
        {
            ActiveId = entry.Id;
        }

        return entry;
    }

    public void SetActive(ProfileEntry entry)
    {
        ActiveId = entry.Id;
    }

    public (ProfileEntry Active, bool ActiveChanged) RemoveProfile(string deletedId)
    {
        _profiles.RemoveAll(p => p.Id == deletedId);
        var activeChanged = ActiveId == deletedId;
        if (activeChanged)
        {
            ActiveId = _profiles[0].Id;
        }

        return (_profiles.First(p => p.Id == ActiveId), activeChanged);
    }

    public ProfileEntry? FindProfile(string? id, string? name, bool required)
    {
        ProfileEntry? found = null;
        if (!string.IsNullOrEmpty(id))
        {
            found = _profiles.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        if (found is null && !string.IsNullOrEmpty(name))
        {
            found = _profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        if (found is null && string.IsNullOrEmpty(id) && string.IsNullOrEmpty(name))
        {
            found = ActiveProfile ?? _profiles.FirstOrDefault();
        }

        if (found is null && required)
        {
            throw new InvalidOperationException($"Шаблон не найден (id={id ?? "–"}, name={name ?? "–"}). Вызовите list_templates для списка доступных.");
        }

        return found;
    }

    public async Task PersistAsync()
    {
        try
        {
            var store = new ProfileStore
            {
                ActiveId = ActiveId,
                Profiles = _profiles,
            };

            await js.InvokeVoidAsync("localStorage.setItem", ProfilesKey, JsonSerializer.Serialize(store));
        }
        catch
        {
            // ponytail: localStorage может быть недоступен (private mode / quota / отключён) – тихо без сохранения
        }
    }

    public string NextProfileName()
    {
        for (var n = _profiles.Count + 1;; n++)
        {
            var name = $"Профиль {n}";
            if (_profiles.All(p => !string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return name;
            }
        }
    }

    public void EnsureUniqueProfileName(string desiredName, string excludeId)
    {
        if (_profiles.Any(p => p.Id != excludeId && string.Equals(p.Name, desiredName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Шаблон с именем «{desiredName}» уже существует.");
        }
    }

    public string UniqueProfileName(string baseName)
    {
        if (_profiles.All(p => !string.Equals(p.Name, baseName, StringComparison.OrdinalIgnoreCase)))
        {
            return baseName;
        }

        for (var n = 2;; n++)
        {
            var candidate = $"{baseName} ({n})";
            if (_profiles.All(p => !string.Equals(p.Name, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                return candidate;
            }
        }
    }

    private SavedState SeedSingleProfile(SavedState state)
    {
        var entry = new ProfileEntry
        {
            Id = NewId(),
            Name = "Локальный",
            State = state,
        };

        _profiles = [entry];
        ActiveId = entry.Id;
        return state;
    }
}
