using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using System.Reflection;
using System.Text.Json.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Entities;
// C:\msys64\home\mttffr\steamcmd\steamapps\common\Counter-Strike Global Offensive\game\csgo\addons\counterstrikesharp\api\CounterStrikeSharp.API.dll</HintPath>

namespace WallhacksPlugin;

public class WallConfig : BasePluginConfig
{
    [JsonPropertyName("ColorT")] public string ColorT { get; set; } = "#FAFAD2";
    [JsonPropertyName("ColorCT")] public string ColorCT { get; set; } = "#ADD8E6";
    [JsonPropertyName("SetDefaultWalleTeamOnStart")] public bool SetDefaultWalleTeamOnStart { get; set; } = false;
    [JsonPropertyName("DefaultWallerTeam")] public string DefaultWallerTeam { get; set; } = "t";
}

public class WallhacksPlugin : BasePlugin, IPluginConfig<WallConfig>
{
    public override string ModuleName => "WallhacksPlugin";
    public override string ModuleVersion => "0.2.5";
    public override string ModuleAuthor => "tem";
    public WallConfig Config { get; set; } = null!;
    public void OnConfigParsed(WallConfig config) { Config = config; }
    private string? wallerTeam = null;
    private CCSGameRules? gameRules = null;
    private List<CBaseModelEntity> glowModels = new List<CBaseModelEntity>();
    private int flipTeam(int num)
    {
        return num == 2 ? 3 : 2;
    }

    private void printSomewhere(CCSPlayerController? player, string s)
    {
        if (player == null)
            Console.WriteLine(s);
        else
            player.PrintToChat(s);
    }

    public override void Load(bool hotReload)
    {
        RegisterEventHandler<EventGameStart>((@event, info) =>
        {
            gameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").First().GameRules!;

            if (Config.SetDefaultWalleTeamOnStart)
                wallerTeam = Config.DefaultWallerTeam;

            return HookResult.Continue;
        });

        RegisterEventHandler<EventRoundStart>((@event, info) =>
        {
            if (gameRules == null)
                return HookResult.Continue;

            if (gameRules.TotalRoundsPlayed == 12)
            {/* Server.PrintToChatAll("Match point event");*/
            }
            else
                return HookResult.Continue;

            if (wallerTeam != null)
                wallerTeam = wallerTeam == "ct" ? "t" : "ct";

            return HookResult.Continue;
        }, HookMode.Post);

        RegisterEventHandler<EventRoundStart>((@event, info) =>
        {
            if (wallerTeam != null)
            {
                //StopGlowing();
                GlowThisTeam(wallerTeam);
                // Server.PrintToChatAll("Reapplied glow effect");
            }
            return HookResult.Continue;
        }, HookMode.Post);

        Console.WriteLine("WallhacksPlugin loaded.");
    }

    [ConsoleCommand("wh_color", "todo")]
    [CommandHelper(minArgs: 2, usage: "{ct/t/both}", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnWhSetColor(CCSPlayerController? player, CommandInfo info)
    {
        string targetTeam = info.ArgByIndex(1).ToLower();

        if (targetTeam != "ct" && targetTeam != "t" && targetTeam != "both")
        {
            printSomewhere(player, "Invalid team");
            return;
        }

        string colorhex = info.ArgByIndex(2).ToLower();
        Color colorInput = ColorTranslator.FromHtml(colorhex);
        printSomewhere(player, "Setting color to " + colorInput);

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
    }

    [ConsoleCommand("wh", "todo")]
    [CommandHelper(minArgs: 1, usage: "{ct/t/both/off}", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnWhCommand(CCSPlayerController? player, CommandInfo info)
    {
        string targetTeam = info.ArgByIndex(1).ToLower();

        if (targetTeam != "ct" && targetTeam != "t" && targetTeam != "off" && targetTeam != "both")
        {
            printSomewhere(player, "Invalid option");
            return;
        }

        switch (targetTeam)
        {
            case "ct":
            case "t":
            case "both":
                wallerTeam = targetTeam;
                GlowThisTeam(wallerTeam);
                break;
            case "off":
                StopGlowing();
                wallerTeam = null;
                break;
        }

        var status = wallerTeam != null ? "Enabled" : "Disabled";
        targetTeam = targetTeam == "off" ? "all" : targetTeam;
        printSomewhere(player, $"XRay {status} for {targetTeam}.");
    }

    public override void Unload(bool hotReload)
    {
        RemoveCommand("wh", (p, i) => { });
        Console.WriteLine("WallhacksPlugin unloaded.");
    }

    public static bool IsPlayerConnected(CCSPlayerController player)
    {
        return player.Connected == PlayerConnectedState.PlayerConnected;
    }

    private void GlowThisTeam(string team)
    {
        if (team == "both")
        {
            GlowThisTeam("ct");
            GlowThisTeam("t");
            return;
        }


        int teamNum = team == "ct" ? 3 : 2;
        // if (flipTeams)
        //     // Server.PrintToChatAll($"Flipping teamNum {teamNum}");

        foreach (CCSPlayerController ctrl in Utilities.GetPlayers().Where(IsPlayerConnected))
        {
            if (ctrl.TeamNum == teamNum)
            {
                // Server.PrintToChatAll($"Ignoring {ctrl.PlayerName}");
                continue;
            }

            CCSPlayerPawn? pawn = ctrl.PlayerPawn.Value;
            if (pawn == null)
            {
                // Server.PrintToChatAll("Failed to find player pawn.");
                return;
            }

            MakePawnGlow(pawn, (byte)(teamNum));
        }
    }

    private void MakePawnGlow(CCSPlayerPawn pawn, byte teamNum)
    {
        if (pawn.Controller?.Value == null)
        {
            Server.PrintToChatAll("Failed to make pawn glow: Controller is null.");
            return;
        }

        // Server.PrintToChatAll($"Making {pawn.Controller.Value.PlayerName} glow.");
        CBaseModelEntity? modelGlow = Utilities.CreateEntityByName<CBaseModelEntity>("prop_dynamic");
        CBaseModelEntity? modelRelay = Utilities.CreateEntityByName<CBaseModelEntity>("prop_dynamic");

        if (modelGlow == null || modelRelay == null)
            return;


        if (pawn.CBodyComponent?.SceneNode == null)
        {
            Server.PrintToChatAll("Failed to make pawn glow: CBodyComponent or SceneNode is null.");
            return;
        }

        string modelName = pawn.CBodyComponent.SceneNode.GetSkeletonInstance().ModelState.ModelName;

        modelRelay.SetModel(modelName);
        modelRelay.Spawnflags = 256u;
        modelRelay.RenderMode = RenderMode_t.kRenderNone;
        modelRelay.DispatchSpawn();

        modelGlow.SetModel(modelName);
        modelGlow.Spawnflags = 256u;
        modelGlow.DispatchSpawn();

        // 1 = spectator?
        // 2 = t
        // 3 = ct

        modelGlow.Glow.GlowColorOverride = ColorTranslator.FromHtml(teamNum == 2 ? Config.ColorCT : Config.ColorT);
        Server.PrintToChatAll($"color: {modelGlow.Glow.GlowColorOverride}");
        modelGlow.Glow.GlowRange = 5000;
        modelGlow.Glow.GlowTeam = teamNum;
        modelGlow.Glow.GlowType = 3;
        modelGlow.Glow.GlowRangeMin = 100;

        modelRelay.AcceptInput("FollowEntity", pawn, modelRelay, "!activator");
        modelGlow.AcceptInput("FollowEntity", modelRelay, modelGlow, "!activator");

        glowModels.Add(modelRelay);
        glowModels.Add(modelGlow);
    }

    private void StopGlowing()
    {
        foreach (var item in glowModels)
            if (item.IsValid)
                item.Remove();

        glowModels.Clear();
    }
}
