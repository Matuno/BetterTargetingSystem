using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Common.Component.BGCollision;
using System;
using System.Numerics;
using DalamudGameObject = Dalamud.Game.ClientState.Objects.Types.IGameObject;
using GameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;
using CameraManager = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.CameraManager;
using Control = FFXIVClientStructs.FFXIV.Client.Game.Control.Control;
using Device = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Device;
using CSFramework = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Arrays;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace BetterTargetingSystem;

public unsafe class Utils
{
    internal static bool TryGetTextInputActive(out bool isTextInputActive)
    {
        isTextInputActive = true;
        var raptureAtkModule = RaptureAtkModule.Instance();
        if (raptureAtkModule == null)
            return false;

        isTextInputActive = raptureAtkModule->AtkModule.IsTextInputActive();
        return true;
    }

    internal static bool CanAttack(DalamudGameObject obj)
    {
        if (obj.Address == nint.Zero)
            return false;

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
        Plugin.Log($"Distance: {distance}");
        return distance;
    }



    internal static bool TryGetHorizontalCameraForward(out Vector2 forward)
    {
        forward = default;
        var cameraManager = CameraManager.Instance();
        if (cameraManager == null || cameraManager->CurrentCamera == null)
            return false;

        Matrix4x4 viewMatrix = cameraManager->CurrentCamera->ViewMatrix;
        return TargetingMath.TryGetHorizontalCameraForward(viewMatrix, out forward);
    }

    internal static bool IsWorldProjectionAvailable()
    {
        var device = Device.Instance();
        return Control.Instance() != null
               && device != null
               && device->Width > 0
               && device->Height > 0;
    }

    internal static bool IsInFrontOfCamera(DalamudGameObject obj, float maxAngle)
    {
        var localPlayer = Plugin.ObjectTable.LocalPlayer;
        if (localPlayer == null || !TryGetHorizontalCameraForward(out var cameraForward))
            return false;

        return TargetingMath.IsWithinHorizontalCone(
            cameraForward,
            localPlayer.Position,
            obj.Position,
            maxAngle);
    }

    internal static bool IsInLineOfSight(GameObject* target, bool useCamera = false)
    {
        if (target == null)
            return false;

        var framework = CSFramework.Instance();
        if (framework == null || framework->BGCollisionModule == null)
        {
            Plugin.Log("Framework is null. Returning false.");
            return false;
        }

        var sourcePos = FFXIVClientStructs.FFXIV.Common.Math.Vector3.Zero;
        if (useCamera)
        {
            // Using the camera's position as origin for raycast
            var cameraManager = CameraManager.Instance();
            if (cameraManager == null || cameraManager->CurrentCamera == null)
            {
                Plugin.Log("Camera is null. Returning false.");
                return false;
            }

            sourcePos = cameraManager->CurrentCamera->Object.Position;
        }
        else
        {
            // Using player's position as origin for raycast
            if (Plugin.ObjectTable.LocalPlayer == null) return false;
            var player = (GameObject*)Plugin.ObjectTable.LocalPlayer.Address;
            if (player == null)
            {
                Plugin.Log("Player is null. Returning false.");
                return false;
            }

            sourcePos = player->Position;
            sourcePos.Y += 2;
        }

        var targetPos = target->Position;
        targetPos.Y += 2;

        if (!float.IsFinite(sourcePos.X) || !float.IsFinite(sourcePos.Y) || !float.IsFinite(sourcePos.Z)
            || !float.IsFinite(targetPos.X) || !float.IsFinite(targetPos.Y) || !float.IsFinite(targetPos.Z))
            return false;

        var direction = targetPos - sourcePos;
        var distance = direction.Magnitude;

        if (!float.IsFinite(distance) || distance <= 1e-4f)
            return false;

        direction = direction.Normalized;
        if (!float.IsFinite(direction.X) || !float.IsFinite(direction.Y) || !float.IsFinite(direction.Z))
            return false;

        System.Numerics.Vector3 originVect = new System.Numerics.Vector3(sourcePos.X, sourcePos.Y, sourcePos.Z);
        System.Numerics.Vector3 directionVect = new System.Numerics.Vector3(direction.X, direction.Y, direction.Z);

        RaycastHit hit;
        var flags = stackalloc int[] { 0x4000, 0, 0x4000, 0 };
        var isLoSBlocked = framework->BGCollisionModule->RaycastMaterialFilter(&hit, &originVect, &directionVect, distance, 1, flags);
        Plugin.Log($"LoS: {!isLoSBlocked}");
        return isLoSBlocked == false;
    }

    internal static uint[] GetEnemyListObjectIds()
    {
        var atkStage = AtkStage.Instance();
        if (atkStage == null)
            return Array.Empty<uint>();

        var numberArray = atkStage->GetNumberArrayData(NumberArrayType.EnemyList);
        if (numberArray == null || numberArray->IntArray == null)
            return Array.Empty<uint>();

        var enemyList = (EnemyListNumberArray*)numberArray->IntArray;
        var enemyCount = Math.Clamp(enemyList->EnemyCount, 0, 8);
        var list = new List<uint>(enemyCount);
        var enemies = enemyList->Enemies;
        for (var i = 0; i < enemyCount; i++)
            list.Add((uint)enemies[i].EntityId);

        return list.ToArray();
    }
}
