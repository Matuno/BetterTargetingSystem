using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using BetterTargetingSystem.Windows;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Dalamud.Plugin.Services;

using DalamudCharacter = Dalamud.Game.ClientState.Objects.Types.ICharacter;
using DalamudGameObject = Dalamud.Game.ClientState.Objects.Types.IGameObject;
using ObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;
using GameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;
using Dalamud.Bindings.ImGui;

namespace BetterTargetingSystem;

public sealed unsafe class Plugin : IDalamudPlugin
{
    public string Name => "Better Targeting System";
    public string CommandConfig => "/bts";
    public string CommandHelp => "/btshelp";

    internal IReadOnlyList<uint> LastConeTargets { get; private set; } = Array.Empty<uint>();
    internal List<uint> CyclingTargets { get; private set; } = new List<uint>();
    internal DebugMode DebugMode { get; private set; }

    [PluginService] public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] public static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] public static IFramework Framework { get; set; } = null!;
    [PluginService] public static IPluginLog PluginLog { get; set; } = null!;
    public Configuration Configuration { get; init; }

    [PluginService] public static IClientState Client { get; set; } = null!;
    [PluginService] public static IObjectTable ObjectTable { get; set; } = null!;
    [PluginService] public static ITargetManager TargetManager { get; set; } = null!;
    [PluginService] public static IGameGui GameGui { get; set; } = null!;
    [PluginService] public static IKeyState KeyState { get; set; } = null!;

    private ConfigWindow ConfigWindow { get; init; }
    private HelpWindow HelpWindow { get; init; }
    private WindowSystem WindowSystem = new("BetterTargetingSystem");
    private bool uiWantsKeyboard;

    // Shamelessly stolen, not sure what that game function exactly does but it works
    //[Signature("48 89 5C 24 ?? 57 48 83 EC 20 48 8B DA 8B F9 E8 ?? ?? ?? ?? 4C 8B C3")]
    //internal static CanAttackDelegate? CanAttackFunction = null!;
    //internal delegate nint CanAttackDelegate(nint a1, nint objectAddress);

    public Plugin()
    {
        this.Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        this.Configuration.Initialize(PluginInterface);

        ConfigWindow = new ConfigWindow(this);
        WindowSystem.AddWindow(ConfigWindow);
        HelpWindow = new HelpWindow(this);
        WindowSystem.AddWindow(HelpWindow);
        this.DebugMode = new DebugMode(this);

        Framework.Update += Update;
        Client.TerritoryChanged += ClearLists;
        PluginInterface.UiBuilder.Draw += DrawUI;
        PluginInterface.UiBuilder.HideUi += ClearUiKeyboardCapture;
        PluginInterface.UiBuilder.OpenMainUi += DrawHelpUI;
        PluginInterface.UiBuilder.OpenConfigUi += DrawConfigUI;

        CommandManager.AddHandler(CommandConfig, new CommandInfo(ShowConfigWindow)
        { HelpMessage = "Open the configuration window." });
        CommandManager.AddHandler(CommandHelp, new CommandInfo(ShowHelpWindow)
        { HelpMessage = "What does this plugin do?" });
    }

    public void Dispose()
    {
        Framework.Update -= Update;
        Client.TerritoryChanged -= ClearLists;
        PluginInterface.UiBuilder.Draw -= DrawUI;
        PluginInterface.UiBuilder.HideUi -= ClearUiKeyboardCapture;
        PluginInterface.UiBuilder.OpenMainUi -= DrawHelpUI;
        PluginInterface.UiBuilder.OpenConfigUi -= DrawConfigUI;
        CommandManager.RemoveHandler(CommandConfig);
        CommandManager.RemoveHandler(CommandHelp);
        this.DebugMode.Clear();
        this.LastConeTargets = Array.Empty<uint>();
        this.CyclingTargets.Clear();
        this.WindowSystem.RemoveAllWindows();
        ConfigWindow.Dispose();
        HelpWindow.Dispose();
    }

    public static void Log(string message) => PluginLog.Debug(message);
    private void DrawUI()
    {
        try
        {
            Volatile.Write(ref this.uiWantsKeyboard, ImGui.GetIO().WantCaptureKeyboard);
            this.WindowSystem.Draw();
            this.DebugMode.Draw();
        }
        catch
        {
            ClearUiKeyboardCapture();
            throw;
        }
    }
    private void ClearUiKeyboardCapture() => Volatile.Write(ref this.uiWantsKeyboard, false);
    private void DrawHelpUI() => HelpWindow.Toggle();
    private void DrawConfigUI() => ConfigWindow.Toggle();
    private void ShowHelpWindow(string command, string args) => this.DrawHelpUI();
    private void ShowConfigWindow(string command, string args) => this.DrawConfigUI();

    public void ClearLists(uint territoryType)
    {
        // Attempt to fix a very rare bug I can't reproduce
        this.DebugMode.Clear();
        this.LastConeTargets = Array.Empty<uint>();
        this.CyclingTargets = new List<uint>();
    }

    public void Update(IFramework framework)
    {
        this.DebugMode.CaptureSnapshot();

        if (Client.IsLoggedIn == false || ObjectTable.LocalPlayer == null)
            return;

        // Disable in GPose
        if (Client.IsGPosing)
            return;

        // Disable if keyboard is being used to type text, or if native input state is unavailable.
        if (!Utils.TryGetTextInputActive(out var isTextInputActive)
            || isTextInputActive
            || Volatile.Read(ref this.uiWantsKeyboard))
            return;

        Keybinds.Keybind.GetKeyboardState();

        if (Configuration.TabTargetKeybind.IsPressed())
        {
            Log("Pressed keybind");
            try { KeyState[(int)Configuration.TabTargetKeybind.Key!] = false; } catch { }
            CycleTargets();
            return;
        }

        if (Configuration.ClosestTargetKeybind.IsPressed())
        {
            try { KeyState[(int)Configuration.ClosestTargetKeybind.Key!] = false; } catch { }
            TargetClosest();
            return;
        }

        if (Configuration.LowestHealthTargetKeybind.IsPressed())
        {
            try { KeyState[(int)Configuration.LowestHealthTargetKeybind.Key!] = false; } catch { }
            TargetLowestHealth();
            return;
        }

        if (Configuration.BestAOETargetKeybind.IsPressed())
        {
            try { KeyState[(int)Configuration.BestAOETargetKeybind.Key!] = false; } catch { }
            TargetBestAOE();
            return;
        }
    }

    private void SetTarget(DalamudGameObject? target)
    {
        if (target == null)
            return;

        TargetManager.SoftTarget = null;
        TargetManager.Target = target;
    }

    private void TargetLowestHealth() => TargetClosest(true);

    private void TargetClosest(bool lowestHealth = false)
    {
        if (ObjectTable.LocalPlayer == null)
            return;

        var (Targets, CloseTargets, EnemyListTargets, OnScreenTargets) = ApplyPvpPlayerPreference(GetTargets());

        // All objects in Targets and CloseTargets are in OnScreenTargets so it's not necessary to test them
        if (EnemyListTargets.Count == 0 && OnScreenTargets.Count == 0)
            return;

        var _targets = OnScreenTargets.Count > 0 ? OnScreenTargets : EnemyListTargets;

        var _target = lowestHealth
            ? _targets.OrderBy(o => (o as DalamudCharacter)?.CurrentHp).ThenBy(o => Utils.DistanceBetweenObjects(ObjectTable.LocalPlayer, o)).First()
            : _targets.OrderBy(o => Utils.DistanceBetweenObjects(ObjectTable.LocalPlayer, o)).First();

        SetTarget(_target);
    }

    private class AOETarget
    {
        public DalamudGameObject obj;
        public int inRange = 0;
        public AOETarget(DalamudGameObject obj) => this.obj = obj;
    }
    private void TargetBestAOE()
    {
        if (ObjectTable.LocalPlayer == null)
        {
            Log("BestAoE: LocalPlayer is null");
            return;
        }

        var (Targets, CloseTargets, EnemyListTargets, OnScreenTargets) = GetTargets();

        if (OnScreenTargets.Count == 0)
        {
            Log("BestAoE: OnScreenTargets is empty");
            return;
        }

        EnemyListTargets.AddRange(OnScreenTargets.Where(o =>
            EnemyListTargets.Contains(o) == false
            && ((o as DalamudCharacter)?.StatusFlags & StatusFlags.InCombat) != 0));

        if (EnemyListTargets.Count == 0)
        {
            Log("BestAoE: No targets in the EnemyList");
            return;
        }

        var AOETargetsList = new List<AOETarget>();
        foreach (var enemy in EnemyListTargets)
        {
            var AOETarget = new AOETarget(enemy);
            foreach (var other in EnemyListTargets)
            {
                if (other == enemy) continue;
                if (Utils.DistanceBetweenObjects(enemy, other) > 5) continue;
                AOETarget.inRange += 1;
            }
            Log("BestAoE: Found one target from the list");
            AOETargetsList.Add(AOETarget);
        }

        var _targets = AOETargetsList.Where(o => OnScreenTargets.Contains(o.obj)).ToList();

        var visiblePlayerCount = _targets.Count(o => o.obj.ObjectKind == ObjectKind.Pc);
        if (TargetSelectionPolicy.ShouldPreferVisiblePlayers(
                Client.IsPvP,
                Configuration.PrioritizePlayersInPvP,
                visiblePlayerCount))
        {
            _targets = _targets.Where(o => o.obj.ObjectKind == ObjectKind.Pc).ToList();
            Log($"PvP player priority kept {visiblePlayerCount} best-AOE player target(s).");
        }

        if (_targets.Count == 0)
            return;
        Log("BestAoE: More than 0 targets");
        var _target = _targets.OrderByDescending(o => o.inRange).ThenByDescending(o => (o.obj as DalamudCharacter)?.CurrentHp).First().obj;

        SetTarget(_target);
    }

    private void CycleTargets()
    {
        if (ObjectTable.LocalPlayer == null)
        {
            Log("No LocalPlayer");
            return;
        }

        var (Targets, CloseTargets, EnemyListTargets, OnScreenTargets) = ApplyPvpPlayerPreference(GetTargets());

        // All objects in Targets and CloseTargets are in OnScreenTargets so it's not necessary to test them
        if (EnemyListTargets.Count == 0 && OnScreenTargets.Count == 0)
        {
            Log("No Targets");
            return;
        }

        var _currentTarget = TargetManager.Target;
        var _previousTarget = TargetManager.PreviousTarget;
        var _targetObjectId = _currentTarget?.EntityId ?? _previousTarget?.EntityId ?? 0;

        // Targets in the frontal cone
        if (Targets.Count > 0)
        {
            Targets = Targets.OrderBy(o => Utils.DistanceBetweenObjects(ObjectTable.LocalPlayer, o)).ToList();

            var TargetsObjectIds = Targets.Select(o => o.EntityId).ToArray();
            // Same cone targets as last cycle
            if (this.LastConeTargets.ToHashSet().SetEquals(TargetsObjectIds.ToHashSet()))
            {
                // Add the close targets to the list of potential targets
                var _potentialTargets = Targets.UnionBy(CloseTargets, o => o.EntityId).ToList();
                var _potentialTargetsObjectIds = _potentialTargets.Select(o => o.EntityId);

                // New enemies to be added
                if (_potentialTargetsObjectIds.Any(o => this.CyclingTargets.Contains(o) == false))
                    this.CyclingTargets = this.CyclingTargets.Union(_potentialTargetsObjectIds).ToList();

                // We simply select the next target
                this.CyclingTargets = this.CyclingTargets.Intersect(_potentialTargetsObjectIds).ToList();
                var index = this.CyclingTargets.FindIndex(o => o == _targetObjectId);
                if (index == this.CyclingTargets.Count - 1) index = -1;
                SetTarget(_potentialTargets.Find(o => o.EntityId == this.CyclingTargets[index + 1]));
                Log("Set target found");
            }
            else
            {
                var _potentialTargets = Targets;
                var _potentialTargetsObjectIds = _potentialTargets.Select(o => o.EntityId).ToList();
                var index = _potentialTargetsObjectIds.FindIndex(o => o == _targetObjectId);
                if (index == _potentialTargetsObjectIds.Count - 1) index = -1;
                SetTarget(_potentialTargets.Find(o => o.EntityId == _potentialTargetsObjectIds[index + 1]));
                Log("Set target found");
                this.LastConeTargets = TargetsObjectIds;
                this.CyclingTargets = _potentialTargetsObjectIds;
            }

            return;
        }

        this.LastConeTargets = Array.Empty<uint>();

        if (CloseTargets.Count > 0)
        {
            var _potentialTargetsObjectIds = CloseTargets.Select(o => o.EntityId);

            if (_potentialTargetsObjectIds.Any(o => this.CyclingTargets.Contains(o) == false))
                this.CyclingTargets = this.CyclingTargets.Union(_potentialTargetsObjectIds).ToList();

            // We simply select the next target
            this.CyclingTargets = this.CyclingTargets.Intersect(_potentialTargetsObjectIds).ToList();
            var index = this.CyclingTargets.FindIndex(o => o == _targetObjectId);
            if (index == this.CyclingTargets.Count - 1) index = -1;
            SetTarget(CloseTargets.Find(o => o.EntityId == this.CyclingTargets[index + 1]));

            return;
        }

        if (EnemyListTargets.Count > 0)
        {
            var _potentialTargetsObjectIds = EnemyListTargets.Select(o => o.EntityId);

            if (_potentialTargetsObjectIds.Any(o => this.CyclingTargets.Contains(o) == false))
                this.CyclingTargets = this.CyclingTargets.Union(_potentialTargetsObjectIds).ToList();

            // We simply select the next target
            this.CyclingTargets = this.CyclingTargets.Intersect(_potentialTargetsObjectIds).ToList();
            var index = this.CyclingTargets.FindIndex(o => o == _targetObjectId);
            if (index == this.CyclingTargets.Count - 1) index = -1;
            SetTarget(EnemyListTargets.Find(o => o.EntityId == this.CyclingTargets[index + 1]));

            return;
        }

        if (OnScreenTargets.Count > 0)
        {
            OnScreenTargets = OnScreenTargets.OrderBy(o => Utils.DistanceBetweenObjects(ObjectTable.LocalPlayer, o)).ToList();
            var _potentialTargetsObjectIds = OnScreenTargets.Select(o => o.EntityId);

            if (_potentialTargetsObjectIds.Any(o => this.CyclingTargets.Contains(o) == false))
                this.CyclingTargets = this.CyclingTargets.Union(_potentialTargetsObjectIds).ToList();

            // We simply select the next target
            this.CyclingTargets = this.CyclingTargets.Intersect(_potentialTargetsObjectIds).ToList();
            var index = this.CyclingTargets.FindIndex(o => o == _targetObjectId);
            if (index == this.CyclingTargets.Count - 1) index = -1;
            SetTarget(OnScreenTargets.Find(o => o.EntityId == this.CyclingTargets[index + 1]));
        }
    }

    public record ObjectsList(List<DalamudGameObject> Targets, List<DalamudGameObject> CloseTargets, List<DalamudGameObject> TargetsEnemy, List<DalamudGameObject> OnScreenTargets);

    private ObjectsList ApplyPvpPlayerPreference(ObjectsList targets)
    {
        var visiblePlayerTargets = targets.OnScreenTargets
            .Where(o => o.ObjectKind == ObjectKind.Pc)
            .ToList();
        var playerPreferenceActive = TargetSelectionPolicy.ShouldPreferVisiblePlayers(
            Client.IsPvP,
            Configuration.PrioritizePlayersInPvP,
            visiblePlayerTargets.Count);
        if (!playerPreferenceActive)
            return targets;

        var visiblePlayerIds = visiblePlayerTargets.Select(o => o.EntityId).ToHashSet();
        Log($"PvP player priority kept {visiblePlayerTargets.Count} visible player target(s).");
        return new ObjectsList(
            targets.Targets.Where(o => TargetSelectionPolicy.ShouldKeepAfterPlayerPreference(
                playerPreferenceActive,
                visiblePlayerIds.Contains(o.EntityId))).ToList(),
            targets.CloseTargets.Where(o => TargetSelectionPolicy.ShouldKeepAfterPlayerPreference(
                playerPreferenceActive,
                visiblePlayerIds.Contains(o.EntityId))).ToList(),
            targets.TargetsEnemy.Where(o => TargetSelectionPolicy.ShouldKeepAfterPlayerPreference(
                playerPreferenceActive,
                visiblePlayerIds.Contains(o.EntityId))).ToList(),
            visiblePlayerTargets);
    }

    [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
    internal ObjectsList GetTargets()
    {
        /* Always return 4 lists.
         * The enemies in a cone in front of the player
         * The enemies in a close radius around the player
         * The enemies in the Enemy List Addon
         * All the targets on screen
         */
        var TargetsList = new List<DalamudGameObject>();
        var CloseTargetsList = new List<DalamudGameObject>();
        var TargetsEnemyList = new List<DalamudGameObject>();
        var OnScreenTargetsList = new List<DalamudGameObject>();

        var Player = ObjectTable.LocalPlayer != null ? (GameObject*)ObjectTable.LocalPlayer.Address : null;
        if (Player == null)
            return new ObjectsList(TargetsList, CloseTargetsList, TargetsEnemyList, OnScreenTargetsList);

        // There might be a way to store this and just update the values if they actually change
        var device = Device.Instance();
        if (device == null || device->Width == 0 || device->Height == 0)
            return new ObjectsList(TargetsList, CloseTargetsList, TargetsEnemyList, OnScreenTargetsList);

        var cameraManager = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.CameraManager.Instance();
        if (cameraManager == null || cameraManager->CurrentCamera == null)
            return new ObjectsList(TargetsList, CloseTargetsList, TargetsEnemyList, OnScreenTargetsList);

        float deviceWidth = device->Width;
        float deviceHeight = device->Height;

        var PotentialTargets = ObjectTable.Where(o =>
            o != ObjectTable.LocalPlayer
            && (ObjectKind.BattleNpc.Equals(o.ObjectKind) && Utils.CanAttack(o)
                || ObjectKind.Pc.Equals(o.ObjectKind)
                && Utils.IsHostilePlayer(o)))
            .ToList();

        var potentialPlayerCount = PotentialTargets.Count(o => o.ObjectKind == ObjectKind.Pc);
        Log($"{PotentialTargets.Count} potential target(s) found "
            + $"({potentialPlayerCount} player(s), {PotentialTargets.Count - potentialPlayerCount} battle NPC(s)).");

        var EnemyList = Utils.GetEnemyListObjectIds();

        var targetIndex = 0;
        foreach (var obj in PotentialTargets)
        {
            Log($"Processing target {++targetIndex}/{PotentialTargets.Count}");
            var o = (GameObject*)obj.Address;
            if (o == null)
            {
                Log("This object is null, skipping.");
                continue;
            }

            if (o->GetIsTargetable() == false)
            {
                Log("Can't target this object, skipping.");
                continue;
            }

            // If the object is part of another party's treasure hunt/leve, we ignore it
            if ((o->EventId.ContentId == EventHandlerContent.TreasureHuntDirector || o->EventId.ContentId == EventHandlerContent.BattleLeveDirector)
                && o->EventId.Id != Player->EventId.Id)
            {
                Log("Can't target this object because it belongs to another player, skipping.");
                continue;
            }

            // Keep targetable enemy-list entries as a fallback even when they are off screen.
            if (EnemyList.Contains(obj.EntityId))
                TargetsEnemyList.Add(obj);

            var distance = Utils.DistanceBetweenObjects(ObjectTable.LocalPlayer!, obj);

            // This is a bit less than the max distance to target something the vanilla way
            if (distance > 49)
            {
                Log($"Max distance exceeded ({distance}), skipping.");
                continue;
            }

            /*
             * Check if object is visible on screen or not.
             * Using both WorldToScreenPoint and WorldToScreen because
             *  - the former is more accurate for actual X,Y position on screen
             *  - the latter returns whether or not the object is "in front" of the camera
             * This isn't exactly how I'd like to do it but since I couldn't find how to get
             * the "bounding box" of a game object or the dimensions of its model, this will have to do.
             */
            FFXIVClientStructs.FFXIV.Common.Math.Vector2 screenPos = new();
            FFXIVClientStructs.FFXIV.Common.Math.Vector3 worldPos = o->Position;
            FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Camera.WorldToScreenPoint(&screenPos, &worldPos);
            if (screenPos.X < 0
                || screenPos.X > deviceWidth
                || screenPos.Y < 0
                || screenPos.Y > deviceHeight) continue;
            if (GameGui.WorldToScreen(o->Position, out _) == false)
            {
                Log("Target can't be seen on the screen. Skipping.");
                continue;
            }

            // Check actual line of sight from camera to object (blocked by walls, etc)
            if (Utils.IsInLineOfSight(o, true) == false)
            {
                Log("Target is not in line of sight. Skipping.");
                continue;
            }

            // On screen and in light of sight of the camera, adding it to the On Screen list
            OnScreenTargetsList.Add(obj);

            // Close to the player, adding it to the Close targets list
            if (Configuration.CloseTargetsCircleEnabled && Utils.DistanceBetweenObjects(ObjectTable.LocalPlayer!, obj) < Configuration.CloseTargetsCircleRadius)
            {
                Log($"Target is in the close circle ({Utils.DistanceBetweenObjects(ObjectTable.LocalPlayer!, obj)} inside {Configuration.CloseTargetsCircleRadius}).");
                CloseTargetsList.Add(obj);
            }

            // Further than the bigger cone, don't care about targeting it
            if (Configuration.Cone3Enabled)
            {
                if (Utils.DistanceBetweenObjects(ObjectTable.LocalPlayer!, obj) > Configuration.Cone3Distance)
                {
                    Log("Target is not in cone 3.");
                    continue;
                }
            }
            else if (Configuration.Cone2Enabled)
            {
                if (Utils.DistanceBetweenObjects(ObjectTable.LocalPlayer!, obj) > Configuration.Cone2Distance)
                {
                    Log("Target is not in cone 2.");
                    continue;
                }
            }
            else if (Utils.DistanceBetweenObjects(ObjectTable.LocalPlayer!, obj) > Configuration.Cone1Distance)
            {
                Log("Target is not in cone 1.");
                continue;
            }

            // Default cone angle for very close targets, getting wider the closer the target is
            var angle = Configuration.Cone1Angle;
            if (Configuration.Cone3Enabled)
            {
                if (Configuration.Cone2Enabled)
                {
                    if (Utils.DistanceBetweenObjects(ObjectTable.LocalPlayer!, obj) > Configuration.Cone2Distance)
                        angle = Configuration.Cone3Angle;
                    else if (Utils.DistanceBetweenObjects(ObjectTable.LocalPlayer!, obj) > Configuration.Cone1Distance)
                        angle = Configuration.Cone2Angle;
                }
                else if (Utils.DistanceBetweenObjects(ObjectTable.LocalPlayer!, obj) > Configuration.Cone1Distance)
                    angle = Configuration.Cone3Angle;
            }
            else if (Configuration.Cone2Enabled && Utils.DistanceBetweenObjects(ObjectTable.LocalPlayer!, obj) > Configuration.Cone1Distance)
                angle = Configuration.Cone2Angle;

            if (Utils.IsInFrontOfCamera(obj, angle) == false)
            {
                Log("Target is not in front of camera. Skipping.");
                continue;
            }

            // In front of the player, adding it to the default list
            TargetsList.Add(obj);
            Log("Target can be targeted.");
        }

        return new ObjectsList(TargetsList, CloseTargetsList, TargetsEnemyList, OnScreenTargetsList);
    }
}
