using System;
using System.Numerics;

namespace BetterTargetingSystem;

internal static class TargetingMath
{
    private const float MinimumHorizontalLengthSquared = 1e-12f;
    private const float AngularToleranceRadians = 1e-6f;

    internal static bool TryGetHorizontalCameraForward(Matrix4x4 viewMatrix, out Vector2 forward)
    {
        // FFXIV uses a row-vector view matrix with camera-forward along negative Z.
        // The negated third column is therefore the camera's world-space forward axis.
        var horizontalForward = new Vector2(-viewMatrix.M13, -viewMatrix.M33);
        return TryNormalize(horizontalForward, out forward);
    }

    internal static bool IsWithinHorizontalCone(
        Vector2 cameraForward,
        Vector3 playerPosition,
        Vector3 targetPosition,
        float fullConeAngleDegrees)
    {
        if (!float.IsFinite(fullConeAngleDegrees) || fullConeAngleDegrees < 0f || fullConeAngleDegrees > 360f)
            return false;

        if (!TryNormalize(cameraForward, out var normalizedForward))
            return false;

        var playerToTarget = new Vector2(
            targetPosition.X - playerPosition.X,
            targetPosition.Z - playerPosition.Z);
        if (!TryNormalize(playerToTarget, out var normalizedTargetDirection))
            return false;

        var cross = (normalizedForward.X * normalizedTargetDirection.Y)
                    - (normalizedForward.Y * normalizedTargetDirection.X);
        var dot = Vector2.Dot(normalizedForward, normalizedTargetDirection);
        var angle = MathF.Abs(MathF.Atan2(cross, dot));
        var halfConeAngle = MathF.PI * fullConeAngleDegrees / 360f;

        return angle <= MathF.Min(MathF.PI, halfConeAngle + AngularToleranceRadians);
    }

    internal static float GetWorldYawRadians(Vector2 cameraForward)
        => MathF.Atan2(cameraForward.X, cameraForward.Y);

    internal static bool TryGetHorizontalConePoint(
        Vector3 origin,
        Vector2 cameraForward,
        float signedAngleDegrees,
        float distance,
        out Vector3 point)
    {
        point = default;
        if (!IsFinite(origin)
            || !float.IsFinite(signedAngleDegrees)
            || !float.IsFinite(distance)
            || distance < 0f
            || !TryNormalize(cameraForward, out var normalizedForward))
            return false;

        var yaw = GetWorldYawRadians(normalizedForward)
                  + (signedAngleDegrees * MathF.PI / 180f);
        point = new Vector3(
            origin.X + (MathF.Sin(yaw) * distance),
            origin.Y,
            origin.Z + (MathF.Cos(yaw) * distance));

        return IsFinite(point);
    }

    private static bool TryNormalize(Vector2 value, out Vector2 normalized)
    {
        normalized = default;
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y))
            return false;

        var lengthSquared = value.LengthSquared();
        if (!float.IsFinite(lengthSquared) || lengthSquared <= MinimumHorizontalLengthSquared)
            return false;

        normalized = value / MathF.Sqrt(lengthSquared);
        return true;
    }

    private static bool IsFinite(Vector3 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
