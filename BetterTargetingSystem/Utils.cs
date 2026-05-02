using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Common.Component.BGCollision;
using System;
using System.Numerics;
using DalamudGameObject = Dalamud.Game.ClientState.Objects.Types.IGameObject;
using GameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;
using CameraManager = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.CameraManager;
using CSFramework = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework;
using System.Collections.Generic;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Arrays;

namespace BetterTargetingSystem;

public unsafe class Utils
{
    private static RaptureAtkModule* RaptureAtkModule => CSFramework.Instance()->GetUIModule()->GetRaptureAtkModule();
    internal static bool IsTextInputActive => RaptureAtkModule->AtkModule.IsTextInputActive();

    internal static bool CanAttack(DalamudGameObject obj)
    {
        return ActionManager.CanUseActionOnTarget(142, (GameObject*)obj.Address);
    }

    internal static float DistanceBetweenObjects(DalamudGameObject source, DalamudGameObject target)
    {
        return DistanceBetweenObjects(source.Position, target.Position, target.HitboxRadius);
    }
    internal static float DistanceBetweenObjects(Vector3 sourcePos, Vector3 targetPos, float targetHitboxRadius = 0)
    {
        // Might have to tinker a bit whether or not to include hitbox radius in calculation
        // Keeping the source object hitbox radius outside of the calculation for now
        var distance = Vector3.Distance(sourcePos, targetPos);
        //distance -= source.HitboxRadius;
        distance -= targetHitboxRadius;
        return distance;
    }

    internal unsafe static float GetCameraRotation()
    {
        var cameraManager = CameraManager.Instance();
        if (cameraManager == null || cameraManager->CurrentCamera == null)
            return 0;

        // CurrentCamera->Object is a GameObject, and its Rotation is already in the correct radians
        return cameraManager->CurrentCamera->Object.Rotation;
    }

    internal static bool IsInFrontOfCamera(DalamudGameObject obj, float maxAngle)
    {
        // This is still relying on camera orientation but the cone is from the player's position
        if (Plugin.ObjectTable.LocalPlayer == null)
            return false;

        var rotation = GetCameraRotation();
        
        // FFXIV standard: South is 0, North is PI, West is PI/2, East is -PI/2
        // We use X and Z directly to calculate the angle to the target
        var dir = obj.Position - Plugin.ObjectTable.LocalPlayer.Position;
        var angleToTarget = Math.Atan2(-dir.X, dir.Z); 
        
        // Normalize difference
        var diff = rotation - angleToTarget;
        while (diff > Math.PI) diff -= 2 * Math.PI;
        while (diff < -Math.PI) diff += 2 * Math.PI;
        
        var angle = Math.Abs(diff);
        bool inFront = angle <= Math.PI * maxAngle / 360;
        
        Plugin.PluginLog.Debug($"[BTS] Target: {obj.Name} | CamRot: {rotation:F3} | TargetAngle: {angleToTarget:F3} | Diff: {angle:F3} | InFront: {inFront}");
        return inFront;
    }

    internal static bool IsInLineOfSight(GameObject* target, bool useCamera = false)
    {
        if (target == null)
            return false;

        var framework = CSFramework.Instance();
        if (framework == null || framework->BGCollisionModule == null)
            return false;

        var sourcePos = FFXIVClientStructs.FFXIV.Common.Math.Vector3.Zero;
        if (useCamera)
        {
            // Using the camera's position as origin for raycast
            var cameraManager = CameraManager.Instance();
            if (cameraManager == null || cameraManager->CurrentCamera == null)
                return false;

            sourcePos = cameraManager->CurrentCamera->Object.Position;
        }
        else
        {
            // Using player's position as origin for raycast
            if (Plugin.ObjectTable.LocalPlayer == null) return false;
            var player = (GameObject*)Plugin.ObjectTable.LocalPlayer.Address;
            if (player == null)
                return false;

            sourcePos = player->Position;
            sourcePos.Y += 2;
        }

        var targetPos = target->Position;
        targetPos.Y += 2;

        var direction = targetPos - sourcePos;
        var distance = direction.Magnitude;

        direction = direction.Normalized;

        System.Numerics.Vector3 originVect = new System.Numerics.Vector3(sourcePos.X, sourcePos.Y, sourcePos.Z);
        System.Numerics.Vector3 directionVect = new System.Numerics.Vector3(direction.X, direction.Y, direction.Z);

        RaycastHit hit;
        var flags = stackalloc int[] { 0x4000, 0, 0x4000, 0 };
        var isLoSBlocked = framework->BGCollisionModule->RaycastMaterialFilter(&hit, &originVect, &directionVect, distance, 1, flags);

        return isLoSBlocked == false;
    }

    internal static uint[] GetEnemyListObjectIds()
    {
        var addonByName = Plugin.GameGui.GetAddonByName("_EnemyList", 1);
        if (addonByName == IntPtr.Zero)
            return Array.Empty<uint>();

        var addon = (AddonEnemyList*)addonByName.Address;
        var numArray = RaptureAtkModule->AtkModule.AtkArrayDataHolder.NumberArrays[21];
        var list = new List<uint>(addon->EnemyCount);
        for (var i = 0; i < addon->EnemyCount; i++)
        {
            var id = (uint)numArray->IntArray[8 + (i * 6)];
            list.Add(id);
        }
        return list.ToArray();
    }
}
