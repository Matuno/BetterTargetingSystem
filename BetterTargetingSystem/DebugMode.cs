using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading;

namespace BetterTargetingSystem;

public sealed class DebugMode
{
    private const int MaximumArcSegments = 72;
    private const int MinimumArcSegments = 8;
    private const int CircleSegments = 48;
    private const float MaximumAbsoluteScreenCoordinate = 100_000f;
    private static readonly long CaptureIntervalTicks = Math.Max(1L, Stopwatch.Frequency / 30L);

    private static readonly Vector4 CameraForwardColor = new(0.1f, 0.9f, 1f, 1f);
    private static readonly Vector4 Cone1Color = new(1f, 0.25f, 0.25f, 0.9f);
    private static readonly Vector4 Cone2Color = new(1f, 0.65f, 0.1f, 0.9f);
    private static readonly Vector4 Cone3Color = new(1f, 1f, 0.2f, 0.9f);
    private static readonly Vector4 CloseCircleColor = new(0.2f, 0.6f, 1f, 0.9f);

    private readonly Plugin Plugin;
    private DebugSnapshot? snapshot;
    private long nextCaptureTimestamp;

    public DebugMode(Plugin plugin)
    {
        this.Plugin = plugin;
    }

    /// <summary>
    /// Copies the current targeting geometry into a pointer-free screen-space snapshot.
    /// This is called only from the framework update callback.
    /// </summary>
    public void CaptureSnapshot()
    {
        if (!Plugin.Configuration.DebugOverlayEnabled
            || !Plugin.Client.IsLoggedIn
            || Plugin.Client.IsGPosing
            || Plugin.ObjectTable.LocalPlayer is not { } localPlayer)
        {
            Clear();
            return;
        }

        var now = Stopwatch.GetTimestamp();
        if (now < Volatile.Read(ref this.nextCaptureTimestamp))
            return;

        Volatile.Write(ref this.nextCaptureTimestamp, now + CaptureIntervalTicks);
        if (!Utils.IsWorldProjectionAvailable()
            || !Utils.TryGetHorizontalCameraForward(out var cameraForward))
        {
            ClearSnapshot();
            return;
        }

        var origin = localPlayer.Position;
        if (!IsFinite(origin))
        {
            ClearSnapshot();
            return;
        }

        Vector2? originScreen = TryProject(origin, out var projectedOrigin)
            ? projectedOrigin
            : null;
        var lines = new List<DebugLine>(160);
        var configuration = Plugin.Configuration;

        AddBand(lines, origin, originScreen, cameraForward,
            configuration.Cone1Angle, 0f, configuration.Cone1Distance, Cone1Color);

        var nextBandStart = configuration.Cone1Distance;
        if (configuration.Cone2Enabled)
        {
            AddBand(lines, origin, originScreen, cameraForward,
                configuration.Cone2Angle, nextBandStart, configuration.Cone2Distance, Cone2Color);
            nextBandStart = configuration.Cone2Distance;
        }

        if (configuration.Cone3Enabled)
        {
            AddBand(lines, origin, originScreen, cameraForward,
                configuration.Cone3Angle, nextBandStart, configuration.Cone3Distance, Cone3Color);
        }

        var maximumDistance = GetMaximumEnabledDistance(configuration);
        if (maximumDistance > 0f
            && TargetingMath.TryGetHorizontalConePoint(
                origin, cameraForward, 0f, maximumDistance, out var forwardPoint)
            && TryProject(forwardPoint, out var forwardScreen))
        {
            var forwardStart = originScreen;
            if (!forwardStart.HasValue)
            {
                var startDistance = MathF.Min(0.5f, maximumDistance / 2f);
                if (TargetingMath.TryGetHorizontalConePoint(
                        origin, cameraForward, 0f, startDistance, out var startPoint)
                    && TryProject(startPoint, out var startScreen))
                {
                    forwardStart = startScreen;
                }
            }

            if (forwardStart.HasValue)
            {
                lines.Add(new DebugLine(
                    forwardStart.Value,
                    forwardScreen,
                    CameraForwardColor,
                    2.5f));
            }
        }

        if (configuration.CloseTargetsCircleEnabled)
        {
            AddCircle(lines, origin, cameraForward,
                configuration.CloseTargetsCircleRadius, CloseCircleColor);
        }

        if (lines.Count == 0 && !originScreen.HasValue)
        {
            ClearSnapshot();
            return;
        }

        Volatile.Write(ref this.snapshot, new DebugSnapshot(lines.ToArray(), originScreen));
    }

    /// <summary>
    /// Renders only the managed snapshot produced by <see cref="CaptureSnapshot"/>.
    /// No game objects, services, or native pointers are read from the UI callback.
    /// </summary>
    public void Draw()
    {
        if (!Plugin.Configuration.DebugOverlayEnabled)
            return;

        var current = Volatile.Read(ref this.snapshot);
        if (current == null)
            return;

        var drawList = ImGui.GetBackgroundDrawList();
        foreach (var line in current.Lines)
        {
            drawList.AddLine(
                line.Start,
                line.End,
                ImGui.ColorConvertFloat4ToU32(line.Color),
                line.Thickness);
        }

        if (current.Origin is { } origin)
        {
            drawList.AddCircleFilled(
                origin,
                3f,
                ImGui.ColorConvertFloat4ToU32(CameraForwardColor));
        }
    }

    public void Clear()
    {
        ClearSnapshot();
        Volatile.Write(ref this.nextCaptureTimestamp, 0L);
    }

    private void AddBand(
        List<DebugLine> lines,
        Vector3 origin,
        Vector2? originScreen,
        Vector2 cameraForward,
        float fullAngleDegrees,
        float innerDistance,
        float outerDistance,
        Vector4 color)
    {
        if (!IsValidAngle(fullAngleDegrees)
            || !float.IsFinite(innerDistance)
            || innerDistance < 0f
            || !IsValidDistance(outerDistance)
            || outerDistance <= innerDistance)
            return;

        var halfAngle = fullAngleDegrees / 2f;
        AddArc(lines, origin, cameraForward, fullAngleDegrees, outerDistance, color);

        if (innerDistance > 0f)
            AddArc(lines, origin, cameraForward, fullAngleDegrees, innerDistance, color);

        // A 360-degree band is a disk/annulus and has no radial seam.
        if (fullAngleDegrees >= 359.999f)
            return;

        AddRadialBoundary(lines, origin, originScreen, cameraForward,
            -halfAngle, innerDistance, outerDistance, color);
        AddRadialBoundary(lines, origin, originScreen, cameraForward,
            halfAngle, innerDistance, outerDistance, color);
    }

    private void AddArc(
        List<DebugLine> lines,
        Vector3 origin,
        Vector2 cameraForward,
        float fullAngleDegrees,
        float distance,
        Vector4 color,
        int? fixedSegmentCount = null)
    {
        var halfAngle = fullAngleDegrees / 2f;
        var segmentCount = fixedSegmentCount ?? Math.Clamp(
                (int)MathF.Ceiling(fullAngleDegrees / 5f),
                MinimumArcSegments,
                MaximumArcSegments);

        var hasPrevious = false;
        var previous = Vector2.Zero;
        for (var index = 0; index <= segmentCount; index++)
        {
            var signedAngle = -halfAngle + (fullAngleDegrees * index / segmentCount);
            if (!TargetingMath.TryGetHorizontalConePoint(
                    origin, cameraForward, signedAngle, distance, out var worldPoint)
                || !TryProject(worldPoint, out var screenPoint))
            {
                hasPrevious = false;
                continue;
            }

            if (hasPrevious)
                lines.Add(new DebugLine(previous, screenPoint, color, 2f));

            previous = screenPoint;
            hasPrevious = true;
        }
    }

    private void AddRadialBoundary(
        List<DebugLine> lines,
        Vector3 origin,
        Vector2? originScreen,
        Vector2 cameraForward,
        float signedAngle,
        float innerDistance,
        float outerDistance,
        Vector4 color)
    {
        var innerScreen = originScreen;
        if (innerDistance > 0f)
        {
            if (!TargetingMath.TryGetHorizontalConePoint(
                    origin, cameraForward, signedAngle, innerDistance, out var innerPoint)
                || !TryProject(innerPoint, out var projectedInner))
                return;

            innerScreen = projectedInner;
        }

        if (!innerScreen.HasValue)
            return;

        if (TargetingMath.TryGetHorizontalConePoint(
                origin, cameraForward, signedAngle, outerDistance, out var worldPoint)
            && TryProject(worldPoint, out var screenPoint))
        {
            lines.Add(new DebugLine(innerScreen.Value, screenPoint, color, 2f));
        }
    }

    private void AddCircle(
        List<DebugLine> lines,
        Vector3 origin,
        Vector2 cameraForward,
        float radius,
        Vector4 color)
    {
        if (!IsValidDistance(radius))
            return;

        AddArc(lines, origin, cameraForward, 360f, radius, color, CircleSegments);
    }

    private bool TryProject(Vector3 worldPoint, out Vector2 screenPoint)
    {
        screenPoint = default;
        return IsFinite(worldPoint)
               && Plugin.GameGui.WorldToScreen(worldPoint, out screenPoint, out _)
               && IsUsableScreenPoint(screenPoint);
    }

    private static float GetMaximumEnabledDistance(Configuration configuration)
    {
        var maximum = IsValidDistance(configuration.Cone1Distance)
            ? configuration.Cone1Distance
            : 0f;

        if (configuration.Cone2Enabled && IsValidDistance(configuration.Cone2Distance))
            maximum = MathF.Max(maximum, configuration.Cone2Distance);

        if (configuration.Cone3Enabled && IsValidDistance(configuration.Cone3Distance))
            maximum = MathF.Max(maximum, configuration.Cone3Distance);

        return maximum;
    }

    private static bool IsValidAngle(float angle)
        => float.IsFinite(angle) && angle >= 0f && angle <= 360f;

    private static bool IsValidDistance(float distance)
        => float.IsFinite(distance) && distance > 0f;

    private static bool IsUsableScreenPoint(Vector2 value)
        => float.IsFinite(value.X)
           && float.IsFinite(value.Y)
           && MathF.Abs(value.X) <= MaximumAbsoluteScreenCoordinate
           && MathF.Abs(value.Y) <= MaximumAbsoluteScreenCoordinate;

    private static bool IsFinite(Vector3 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private void ClearSnapshot() => Volatile.Write(ref this.snapshot, null);

    private sealed record DebugSnapshot(DebugLine[] Lines, Vector2? Origin);

    private readonly record struct DebugLine(Vector2 Start, Vector2 End, Vector4 Color, float Thickness);
}
