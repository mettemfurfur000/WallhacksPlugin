using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Entities;
// C:\msys64\home\mttffr\steamcmd\steamapps\common\Counter-Strike Global Offensive\game\csgo\addons\counterstrikesharp\api\CounterStrikeSharp.API.dll</HintPath>

namespace WallhacksPlugin;

public class WallConfig : BasePluginConfig
{
    [JsonPropertyName("ColorT")] public string ColorT { get; set; } = "#ffc800ff";
    [JsonPropertyName("ColorCT")] public string ColorCT { get; set; } = "#4c00ffff";
}

public class WallhacksPlugin : BasePlugin, IPluginConfig<WallConfig>
{
    public override string ModuleName => "WallhacksPlugin";
    public override string ModuleVersion => "0.2.5";
    public override string ModuleAuthor => "tem";

    public WallConfig Config { get; set; } = null!;
    public void OnConfigParsed(WallConfig config) { Config = config; }

    private CCSGameRules? gameRules = null;
    // private List<CBaseModelEntity> glowModels = new List<CBaseModelEntity>();

    private Dictionary<int, Tuple<CBaseModelEntity, CBaseModelEntity>> pairModels = [];
    private Dictionary<int, bool> users = [];

    public override void Load(bool hotReload)
    {
        RegisterListener<Listeners.OnTick>(() =>
        {
            if (Server.TickCount % 128 != 0)
                return;

            Server.NextWorldUpdate(() =>
            {
                StopGlowing();
                GlowEveryone();
            });

            // check for all players whio don hav the wallhek entity attached
            // CheckGlow();
        });

        RegisterEventHandler<EventGameStart>((@event, info) =>
        {
            gameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").First().GameRules!;

            if (gameRules == null)
            {
                Console.WriteLine("Error: GameRules is null on game start");
                return HookResult.Continue;
            }

            return HookResult.Continue;
        });

        RegisterEventHandler<EventRoundStart>((@event, info) =>
        {
            if (gameRules == null)
            {
                Console.WriteLine("Error: GameRules is null");

                gameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").First().GameRules!;

                if (gameRules == null)
                {
                    Console.WriteLine("Error: GameRules is still null");
                    return HookResult.Continue;
                }
            }

            return HookResult.Continue;
        }, HookMode.Post);

        RegisterListener<Listeners.CheckTransmit>(infoList =>
        {
            if (pairModels.Count == 0)
                return;

            foreach ((CCheckTransmitInfo info, CCSPlayerController? player) in infoList)
            {
                if (player == null) // if player is not real he can see the models
                    continue;

                // if the player is in our user list, he has wallhak and should wee the glow models
                if (users.TryGetValue(player.Slot, out bool value))
                    if (value)
                    {
                        var selfModel = pairModels[player.Slot];

                        info.TransmitEntities.Remove(selfModel.Item1);
                        info.TransmitEntities.Remove(selfModel.Item2);
                        
                        continue;
                    }

                foreach (var models in pairModels)
                {
                    info.TransmitEntities.Remove(models.Value.Item1);
                    info.TransmitEntities.Remove(models.Value.Item2);
                }
            }
        });

        Console.WriteLine("WallhacksPlugin loaded.");
    }

    [RequiresPermissions("@css/admin")]
    [ConsoleCommand("wh_test", "todo")]
    [CommandHelper(minArgs: 0, usage: "", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnWhTest(CCSPlayerController? player, CommandInfo info)
    {
        StopGlowing();
        GlowEveryone();
    }

    [RequiresPermissions("@css/admin")]
    [ConsoleCommand("wh", "todo")]
    [CommandHelper(minArgs: 2, usage: "<user> {on/off}", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnWhGrant(CCSPlayerController? player, CommandInfo info)
    {
        var pattern = info.GetArg(1);
        var grant = info.GetArg(2);
        bool? setTrue = null;

        if (grant == "on") setTrue = true;
        if (grant == "off") setTrue = false;

        if (setTrue == null)
            info.ReplyToCommand("Toggling...");

        var selected = SelectPlayers(pattern);

        if (!selected.Any())
            info.ReplyToCommand("No players found");

        selected.ToList().ForEach((p) => users[p.Slot] = (bool)(setTrue != null ? setTrue : !users[p.Slot]));
    }

    [RequiresPermissions("@css/admin")]
    [ConsoleCommand("wh_color", "todo")]
    [CommandHelper(minArgs: 2, usage: "{ct/t/both} [hexcode]", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnWhSetColor(CCSPlayerController? player, CommandInfo info)
    {
        string targetTeam = info.ArgByIndex(1).ToLower();

        if (targetTeam != "ct" && targetTeam != "t" && targetTeam != "both")
        {
            info.ReplyToCommand("Invalid team");
            return;
        }

        string colorhex = info.ArgByIndex(2).ToLower();
        Color colorInput = ColorTranslator.FromHtml(colorhex);
        info.ReplyToCommand("Setting color to " + colorInput);

        switch (targetTeam)
        {
            case "ct":
                Config.ColorCT = colorhex;
                break;
            case "t":
                Config.ColorT = colorhex;
                break;
            case "both":
                Config.ColorT = colorhex;
                Config.ColorCT = colorhex;
                break;
        }
        ChangeColorNow();
    }

    [RequiresPermissions("@css/admin")]
    [ConsoleCommand("wh_team", "todo")]
    [CommandHelper(minArgs: 1, usage: "{ct/t}", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnWhTeamCommand(CCSPlayerController? player, CommandInfo info)
    {
        string targetTeam = info.ArgByIndex(1).ToLower();

        if (targetTeam != "ct" && targetTeam != "t")
        {
            info.ReplyToCommand("Invalid option");
            return;
        }

        byte teamNumTarget = (byte)(targetTeam == "t" ? 2 : 3);

        var players = Utilities.GetPlayers();

        if (!players.Any())
            info.ReplyToCommand("No players found");

        players.ToList().ForEach((p) => users[p.Slot] = p.TeamNum == teamNumTarget);
    }

    // public override void Unload(bool hotReload)
    // {
    //     RemoveCommand("wh", (p, i) => { });
    //     RemoveCommand("wh_color", (p, i) => { });
    //     Console.WriteLine("WallhacksPlugin unloaded.");
    // }

    public static bool IsPlayerConnected(CCSPlayerController player)
    {
        return player.Connected == PlayerConnectedState.PlayerConnected;
    }

    public void ChangeColorNow()
    {
        // foreach (var item in glowModels)
        //     if (item.IsValid)
        //         item.Glow.GlowColorOverride = ColorTranslator.FromHtml(item.Glow.GlowTeam == 2 ? Config.ColorCT : Config.ColorT);
        // StopGlowing();
        // if (wallerTeam != null)
        //     GlowThisTeam(wallerTeam);
    }

    public static String WildCardToRegular(String value)
    {
        return "^" + Regex.Escape(value).Replace("\\*", ".*") + "$";
    }

    public static IEnumerable<CCSPlayerController> SelectPlayers(string name_pattern)
    {
        string r_pattern = WildCardToRegular(name_pattern);

        return Utilities.GetPlayers()
            .Where(player => Regex.IsMatch(player.PlayerName, r_pattern, RegexOptions.IgnoreCase));
    }

    private void GlowEveryone()
    {
        foreach (CCSPlayerController ctrl in Utilities.GetPlayers().Where(IsPlayerConnected))
        {
            CCSPlayerPawn? pawn = ctrl.PlayerPawn.Value;
            if (pawn == null)
            {
                Console.WriteLine("Failed to find player pawn for " + ctrl.PlayerName);
                return;
            }

            if (!pairModels.ContainsKey(pawn.OriginalController.Value!.Slot))
                MakePawnGlow(pawn, (byte)(pawn.TeamNum == 2 ? 3 : 2));
        }
    }

    private void MakePawnGlow(CCSPlayerPawn pawn, byte teamNum)
    {
        if (pawn.Controller?.Value == null)
        {
            // Console.WriteLine("Failed to make pawn glow: Controller is null.");
            return;
        }

        CBaseModelEntity? modelGlow = Utilities.CreateEntityByName<CBaseModelEntity>("prop_dynamic");
        CBaseModelEntity? modelRelay = Utilities.CreateEntityByName<CBaseModelEntity>("prop_dynamic");

        if (modelGlow == null || modelRelay == null)
            return;

        if (pawn.CBodyComponent?.SceneNode == null)
        {
            // Console.WriteLine("Failed to make pawn glow: CBodyComponent or SceneNode is null.");
            return;
        }

        string modelName = pawn.CBodyComponent.SceneNode.GetSkeletonInstance().ModelState.ModelName;

        modelRelay.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags = (uint)(modelRelay.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags & ~(1 << 2));
        modelRelay.SetModel(modelName);
        modelRelay.Spawnflags = 256u;
        modelRelay.RenderMode = RenderMode_t.kRenderNone;
        modelRelay.DispatchSpawn();

        modelGlow.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags = (uint)(modelGlow.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags & ~(1 << 2));
        modelGlow.SetModel(modelName);
        modelGlow.Spawnflags = 256u;
        modelGlow.DispatchSpawn();

        // 1 = spectator?
        // 2 = t
        // 3 = ct

        modelGlow.Glow.GlowColorOverride = ColorTranslator.FromHtml(teamNum == 2 ? Config.ColorCT : Config.ColorT);
        //Console.WriteLine($"color: {modelGlow.Glow.GlowColorOverride}");
        modelGlow.Glow.GlowRange = 4096;
        modelGlow.Glow.GlowTeam = teamNum;
        modelGlow.Glow.GlowType = 3;
        modelGlow.Glow.GlowRangeMin = 64;

        modelRelay.AcceptInput("FollowEntity", pawn, modelRelay, "!activator");
        modelGlow.AcceptInput("FollowEntity", modelRelay, modelGlow, "!activator");

        // glowModels.Add(modelRelay);
        // glowModels.Add(modelGlow);
        pairModels[pawn.OriginalController.Value!.Slot] = new Tuple<CBaseModelEntity, CBaseModelEntity>(modelGlow, modelRelay);
    }

    private void CheckGlow()
    {
        var players = Utilities.GetPlayers();
        players.ForEach(p =>
        {
            if (!pairModels.ContainsKey(p.Slot))
            {
                MakePawnGlow(p.PlayerPawn.Value!, (byte)(p.PlayerPawn.Value!.TeamNum == 2 ? 3 : 2));
            }
        });
    }

    private void StopGlowing()
    {
        foreach (var item in pairModels)
        {
            if (item.Value.Item1.IsValid)
                item.Value.Item1.Remove();
            if (item.Value.Item2.IsValid)
                item.Value.Item2.Remove();
        }

        pairModels.Clear();

        // extra code to remove everything that glows like things we spawned

        var entites = Utilities.FindAllEntitiesByDesignerName<CBaseModelEntity>("prop_dynamic");

        entites.ToList().ForEach((e) =>
        {
            if (e.Glow.GlowType == 3)
                e.Remove();
        });
    }
}
