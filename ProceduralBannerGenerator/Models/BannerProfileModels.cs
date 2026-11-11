using BannerCore;

namespace ProceduralBannerGenerator.Models;

public sealed record SavedState
{
    public string Title { get; init; } = "290. ЛЕДОКОЛ XIX";
    public string Subtitle { get; init; } = "КОРОЛЬ МАШИННОГО ЗАЛА";
    public string? Tagline { get; init; }
    public int Seed { get; init; } = 290;
    public int Width { get; init; } = 1280;
    public int Height { get; init; } = 720;
    public bool Transparent { get; init; }
    public int RenderDebounceMs { get; init; } = 150;
    public BannerStyle Style { get; init; } = new();
}

public sealed record HistoryEntry
{
    public string Label { get; init; } = "";
    public string Timestamp { get; init; } = "";
    public SavedState State { get; init; } = new();
}

public sealed class ProfileEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public SavedState State { get; set; } = new();
    public List<HistoryEntry> History { get; set; } = [];
}

public sealed class ProfileStore
{
    public bool PreserveTextOnSwitch { get; set; }
    public string ActiveId { get; set; } = "";
    public List<ProfileEntry> Profiles { get; set; } = [];
}
