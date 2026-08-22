using System;
using System.Numerics;
using Xunit;

namespace BetterTargetingSystem;

public sealed class TargetingMathTests
{
    [Fact]
    public void ExtractsHorizontalForwardFromTranslatedPitchedView()
    {
        var cameraPosition = new Vector3(12f, 8f, -7f);
        var cameraTarget = cameraPosition + new Vector3(6f, -3f, 12f);
        var viewMatrix = Matrix4x4.CreateLookAt(cameraPosition, cameraTarget, Vector3.UnitY);
        var expected = Vector2.Normalize(new Vector2(6f, 12f));

        Assert.True(TargetingMath.TryGetHorizontalCameraForward(viewMatrix, out var actual));
        AssertVectorEqual(expected, actual);
    }

    [Fact]
    public void CameraTranslationDoesNotChangeHorizontalForward()
    {
        var untranslated = Matrix4x4.Identity;
        var translated = Matrix4x4.Identity;
        translated.M41 = 100f;
        translated.M42 = -50f;
        translated.M43 = 25f;

        Assert.True(TargetingMath.TryGetHorizontalCameraForward(untranslated, out var first));
        Assert.True(TargetingMath.TryGetHorizontalCameraForward(translated, out var second));
        AssertVectorEqual(first, second);
    }

    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(1f, 0f)]
    [InlineData(0f, -1f)]
    [InlineData(-1f, 0f)]
    public void DebugYawReconstructsCameraForward(float forwardX, float forwardZ)
    {
        var forward = new Vector2(forwardX, forwardZ);
        var yaw = TargetingMath.GetWorldYawRadians(forward);
        var reconstructed = new Vector2(MathF.Sin(yaw), MathF.Cos(yaw));

        AssertVectorEqual(forward, reconstructed);
    }

    [Fact]
    public void ConeUsesPlayerOriginAndFullAngleSemantics()
    {
        var forward = Vector2.UnitY;
        var player = new Vector3(100f, 20f, -75f);

        Assert.True(TargetingMath.IsWithinHorizontalCone(
            forward,
            player,
            player + DirectionFromYawDegrees(14.9f, 100f),
            30f));
        Assert.False(TargetingMath.IsWithinHorizontalCone(
            forward,
            player,
            player + DirectionFromYawDegrees(15.1f, -100f),
            30f));
        Assert.True(TargetingMath.IsWithinHorizontalCone(
            forward,
            player,
            player + DirectionFromYawDegrees(-14.9f),
            30f));
        Assert.False(TargetingMath.IsWithinHorizontalCone(
            forward,
            player,
            player + DirectionFromYawDegrees(-15.1f),
            30f));
    }

    [Fact]
    public void FullCircleIncludesTargetDirectlyBehindPlayer()
    {
        Assert.True(TargetingMath.IsWithinHorizontalCone(
            Vector2.UnitY,
            Vector3.Zero,
            -Vector3.UnitZ,
            360f));
    }

    [Theory]
    [InlineData(0f, 10f, 1f)]
    [InlineData(90f, 15f, -4f)]
    [InlineData(-90f, 5f, -4f)]
    [InlineData(180f, 10f, -9f)]
    public void ConePointUsesCameraYawAndTranslatedPlayerOrigin(
        float signedAngleDegrees,
        float expectedX,
        float expectedZ)
    {
        var origin = new Vector3(10f, 3f, -4f);

        Assert.True(TargetingMath.TryGetHorizontalConePoint(
            origin,
            Vector2.UnitY,
            signedAngleDegrees,
            5f,
            out var point));
        Assert.InRange(MathF.Abs(expectedX - point.X), 0f, 1e-5f);
        Assert.InRange(MathF.Abs(origin.Y - point.Y), 0f, 1e-5f);
        Assert.InRange(MathF.Abs(expectedZ - point.Z), 0f, 1e-5f);
    }

    [Theory]
    [InlineData(-25f)]
    [InlineData(25f)]
    public void DrawnConeBoundaryMatchesTargetingBoundary(float signedAngleDegrees)
    {
        var origin = new Vector3(40f, -2f, 100f);

        Assert.True(TargetingMath.TryGetHorizontalConePoint(
            origin,
            Vector2.UnitY,
            signedAngleDegrees,
            30f,
            out var boundaryPoint));
        Assert.True(TargetingMath.IsWithinHorizontalCone(
            Vector2.UnitY,
            origin,
            boundaryPoint,
            50f));
    }

    [Theory]
    [InlineData(-70f)]
    [InlineData(70f)]
    public void RotatedDrawnBoundaryRemainsInclusive(float signedAngleDegrees)
    {
        var origin = new Vector3(-20f, 4f, 11f);
        var forwardYaw = 178f * MathF.PI / 180f;
        var forward = new Vector2(MathF.Sin(forwardYaw), MathF.Cos(forwardYaw));

        Assert.True(TargetingMath.TryGetHorizontalConePoint(
            origin,
            forward,
            signedAngleDegrees,
            30f,
            out var boundaryPoint));
        Assert.True(TargetingMath.IsWithinHorizontalCone(
            forward,
            origin,
            boundaryPoint,
            140f));
    }

    [Fact]
    public void DegenerateAndInvalidInputsFailClosed()
    {
        Assert.False(TargetingMath.TryGetHorizontalCameraForward(
            Matrix4x4.CreateLookAt(Vector3.Zero, Vector3.UnitY, Vector3.UnitZ),
            out _));
        Assert.False(TargetingMath.IsWithinHorizontalCone(
            Vector2.UnitY,
            Vector3.Zero,
            Vector3.Zero,
            30f));
        Assert.False(TargetingMath.IsWithinHorizontalCone(
            new Vector2(float.NaN, 1f),
            Vector3.Zero,
            Vector3.UnitZ,
            30f));
        Assert.False(TargetingMath.IsWithinHorizontalCone(
            Vector2.UnitY,
            Vector3.Zero,
            Vector3.UnitZ,
            float.NaN));
        Assert.False(TargetingMath.IsWithinHorizontalCone(
            Vector2.UnitY,
            Vector3.Zero,
            Vector3.UnitZ,
            -1f));
        Assert.False(TargetingMath.TryGetHorizontalConePoint(
            new Vector3(float.NaN, 0f, 0f),
            Vector2.UnitY,
            0f,
            5f,
            out _));
        Assert.False(TargetingMath.TryGetHorizontalConePoint(
            Vector3.Zero,
            Vector2.Zero,
            0f,
            5f,
            out _));
        Assert.False(TargetingMath.TryGetHorizontalConePoint(
            Vector3.Zero,
            Vector2.UnitY,
            0f,
            -1f,
            out _));
    }

    private static Vector3 DirectionFromYawDegrees(float degrees, float y = 0f)
    {
        var radians = degrees * MathF.PI / 180f;
        return new Vector3(MathF.Sin(radians), y, MathF.Cos(radians));
    }

    private static void AssertVectorEqual(Vector2 expected, Vector2 actual)
    {
        Assert.InRange(MathF.Abs(expected.X - actual.X), 0f, 1e-5f);
        Assert.InRange(MathF.Abs(expected.Y - actual.Y), 0f, 1e-5f);
    }
}
