using System.Text.Json.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;

namespace GPBanner;

public class GPBannerConfig : BasePluginConfig
{
    [JsonPropertyName("ImageUrls")]
    public List<string> ImageUrls { get; set; } = new()
    {
        "https://raw.githubusercontent.com/Aimus-uz/gamepoint-assets/main/banner_test.png"
    };

    [JsonPropertyName("ShowToDead")]
    public bool ShowToDead { get; set; } = true;

    // Spectators and players waiting in the retake queue
    [JsonPropertyName("ShowToSpectators")]
    public bool ShowToSpectators { get; set; } = true;

    // With several images: switch every N seconds
    [JsonPropertyName("RotateSeconds")]
    public int RotateSeconds { get; set; } = 15;

    // Re-send the same banner only this often (in case another plugin overwrote the center panel)
    [JsonPropertyName("RefreshSeconds")]
    public int RefreshSeconds { get; set; } = 10;

    // Silence after round start so the A/B bombsite image is not overwritten
    [JsonPropertyName("PauseAfterRoundStart")]
    public float PauseAfterRoundStart { get; set; } = 8f;
}

public class GPBanner : BasePlugin, IPluginConfig<GPBannerConfig>
{
    public override string ModuleName => "GPBanner";
    public override string ModuleVersion => "1.0.2";
    public override string ModuleAuthor => "GAMEPOINT";
    public override string ModuleDescription => "Banner for dead players and spectators";

    public GPBannerConfig Config { get; set; } = new();

    private float _roundStart;

    // slot -> (time sent, image index sent)
    private readonly Dictionary<int, (float Time, int Index)> _shown = new();

    public void OnConfigParsed(GPBannerConfig config) => Config = config;

    public override void Load(bool hotReload)
    {
        RegisterListener<Listeners.OnMapStart>(_ =>
        {
            _roundStart = 0f;
            _shown.Clear();
        });

        RegisterEventHandler<EventRoundStart>((_, _) =>
        {
            _roundStart = Server.CurrentTime;
            return HookResult.Continue;
        });

        // Instantly hide on respawn
        RegisterEventHandler<EventPlayerSpawn>((ev, _) =>
        {
            var p = ev.Userid;
            if (p is { IsValid: true } && !p.IsBot && _shown.Remove(p.Slot))
                Server.NextFrame(() =>
                {
                    if (p.IsValid)
                        p.PrintToCenterHtml(" ", 0);
                });
            return HookResult.Continue;
        });

        RegisterEventHandler<EventPlayerDisconnect>((ev, _) =>
        {
            if (ev.Userid is { IsValid: true } p)
                _shown.Remove(p.Slot);
            return HookResult.Continue;
        });

        AddTimer(1.0f, Tick, TimerFlags.REPEAT);
    }

    private void Tick()
    {
        if (Config.ImageUrls.Count == 0)
            return;

        float now = Server.CurrentTime;
        if (now - _roundStart < Config.PauseAfterRoundStart)
            return;

        int rotate = Math.Max(1, Config.RotateSeconds);
        int index = (int)(now / rotate) % Config.ImageUrls.Count;
        int refresh = Math.Max(2, Config.RefreshSeconds);
        string html = $"<img src='{Config.ImageUrls[index]}'>";

        foreach (var p in Utilities.GetPlayers())
        {
            if (p is not { IsValid: true } || p.IsBot || p.IsHLTV)
                continue;

            bool spectator = p.Team == CsTeam.Spectator || p.Team == CsTeam.None;
            bool dead = !spectator && !p.PawnIsAlive;
            bool eligible = (spectator && Config.ShowToSpectators) || (dead && Config.ShowToDead);

            if (!eligible)
            {
                if (_shown.Remove(p.Slot))
                    p.PrintToCenterHtml(" ", 0);
                continue;
            }

            // Send only if: not shown yet, image changed, or refresh time passed
            if (_shown.TryGetValue(p.Slot, out var s) && s.Index == index && now - s.Time < refresh)
                continue;

            p.PrintToCenterHtml(html, refresh + 5); // lives longer than refresh -> no gap, no flicker
            _shown[p.Slot] = (now, index);
        }
    }
}
