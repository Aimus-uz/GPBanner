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

    [JsonPropertyName("RotateSeconds")]
    public int RotateSeconds { get; set; } = 15;

    // Silence after round start so the A/B bombsite image is not overwritten
    [JsonPropertyName("PauseAfterRoundStart")]
    public float PauseAfterRoundStart { get; set; } = 8f;
}

public class GPBanner : BasePlugin, IPluginConfig<GPBannerConfig>
{
    public override string ModuleName => "GPBanner";
    public override string ModuleVersion => "1.0.1";
    public override string ModuleAuthor => "GAMEPOINT";
    public override string ModuleDescription => "Banner for dead players and spectators";

    public GPBannerConfig Config { get; set; } = new();

    private float _roundStart;

    public void OnConfigParsed(GPBannerConfig config) => Config = config;

    public override void Load(bool hotReload)
    {
        RegisterListener<Listeners.OnMapStart>(_ => _roundStart = 0f);

        RegisterEventHandler<EventRoundStart>((_, _) =>
        {
            _roundStart = Server.CurrentTime;
            return HookResult.Continue;
        });

        // Instantly hide the banner when the player respawns
        RegisterEventHandler<EventPlayerSpawn>((ev, _) =>
        {
            var p = ev.Userid;
            if (p is { IsValid: true } && !p.IsBot)
                Server.NextFrame(() =>
                {
                    if (p.IsValid)
                        p.PrintToCenterHtml(" ", 0);
                });
            return HookResult.Continue;
        });

        // Twice per second; each banner lives 1s -> fallback hide < 1s
        AddTimer(0.5f, Tick, TimerFlags.REPEAT);
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
        string html = $"<img src='{Config.ImageUrls[index]}'>";

        foreach (var p in Utilities.GetPlayers())
        {
            if (p is not { IsValid: true } || p.IsBot || p.IsHLTV)
                continue;

            bool spectator = p.Team == CsTeam.Spectator || p.Team == CsTeam.None;
            bool dead = !spectator && !p.PawnIsAlive;

            if ((spectator && Config.ShowToSpectators) || (dead && Config.ShowToDead))
                p.PrintToCenterHtml(html, 1);
        }
    }
}
