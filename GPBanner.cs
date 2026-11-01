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
        "https://raw.githubusercontent.com/Aimus-uz/gamepoint-assets/main/banner_test_v2.png"
    };

    [JsonPropertyName("ShowToDead")]
    public bool ShowToDead { get; set; } = true;

    // Spectators and players waiting in the retake queue
    [JsonPropertyName("ShowToSpectators")]
    public bool ShowToSpectators { get; set; } = true;

    // With several images: switch every N seconds
    [JsonPropertyName("RotateSeconds")]
    public int RotateSeconds { get; set; } = 15;

    // Silence after round start so the A/B bombsite image is not overwritten
    [JsonPropertyName("PauseAfterRoundStart")]
    public float PauseAfterRoundStart { get; set; } = 8f;
}

public class GPBanner : BasePlugin, IPluginConfig<GPBannerConfig>
{
    public override string ModuleName => "GPBanner";
    public override string ModuleVersion => "1.0.3";
    public override string ModuleAuthor => "GAMEPOINT";
    public override string ModuleDescription => "Banner for dead players and spectators";

    public GPBannerConfig Config { get; set; } = new();

    private float _roundStart;
    private string _html = "";

    // Recomputed every 0.5s; OnTick only walks this short list
    private readonly List<CCSPlayerController> _targets = new();

    public void OnConfigParsed(GPBannerConfig config) => Config = config;

    public override void Load(bool hotReload)
    {
        RegisterListener<Listeners.OnMapStart>(_ =>
        {
            _roundStart = 0f;
            _targets.Clear();
        });

        RegisterEventHandler<EventRoundStart>((_, _) =>
        {
            _roundStart = Server.CurrentTime;
            _targets.Clear();
            return HookResult.Continue;
        });

        // Instantly stop on respawn
        RegisterEventHandler<EventPlayerSpawn>((ev, _) =>
        {
            if (ev.Userid is { IsValid: true } p)
                _targets.RemoveAll(t => t.Slot == p.Slot);
            return HookResult.Continue;
        });

        RegisterEventHandler<EventPlayerDisconnect>((ev, _) =>
        {
            if (ev.Userid is { IsValid: true } p)
                _targets.RemoveAll(t => t.Slot == p.Slot);
            return HookResult.Continue;
        });

        RegisterListener<Listeners.OnTick>(OnTick);
        AddTimer(0.5f, Refresh, TimerFlags.REPEAT);
    }

    private void Refresh()
    {
        _targets.Clear();

        if (Config.ImageUrls.Count == 0)
            return;

        float now = Server.CurrentTime;
        if (now - _roundStart < Config.PauseAfterRoundStart)
            return;

        int rotate = Math.Max(1, Config.RotateSeconds);
        int index = (int)(now / rotate) % Config.ImageUrls.Count;
        _html = $"<img src='{Config.ImageUrls[index]}'>";

        foreach (var p in Utilities.GetPlayers())
        {
            if (p is not { IsValid: true } || p.IsBot || p.IsHLTV)
                continue;

            bool spectator = p.Team == CsTeam.Spectator || p.Team == CsTeam.None;
            bool dead = !spectator && !p.PawnIsAlive;

            if ((spectator && Config.ShowToSpectators) || (dead && Config.ShowToDead))
                _targets.Add(p);
        }
    }

    private void OnTick()
    {
        if (_targets.Count == 0)
            return;

        for (int i = _targets.Count - 1; i >= 0; i--)
        {
            var p = _targets[i];
            if (!p.IsValid)
            {
                _targets.RemoveAt(i);
                continue;
            }
            p.PrintToCenterHtml(_html);
        }
    }
}
