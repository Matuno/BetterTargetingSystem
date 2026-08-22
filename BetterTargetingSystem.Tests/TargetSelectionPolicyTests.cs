using Xunit;

namespace BetterTargetingSystem;

public sealed class TargetSelectionPolicyTests
{
    [Theory]
    [InlineData(false, true, false, true)]
    [InlineData(false, false, true, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, true)]
    public void UsesGameActionClassificationInPvp(
        bool isPvP,
        bool publicHostileFlag,
        bool classifiedAsEnemy,
        bool expected)
    {
        Assert.Equal(
            expected,
            TargetSelectionPolicy.IsHostilePlayer(
                isPvP,
                publicHostileFlag,
                classifiedAsEnemy));
    }

    [Theory]
    [InlineData(true, true, 1, true)]
    [InlineData(true, true, 0, false)]
    [InlineData(true, false, 1, false)]
    [InlineData(false, true, 1, false)]
    public void PrefersPlayersOnlyWhenConfiguredAndAvailable(
        bool isPvP,
        bool preferenceEnabled,
        int visiblePlayerCount,
        bool expected)
    {
        Assert.Equal(
            expected,
            TargetSelectionPolicy.ShouldPreferVisiblePlayers(
                isPvP,
                preferenceEnabled,
                visiblePlayerCount));
    }

    [Fact]
    public void ActivePlayerPreferenceKeepsOnlyVisiblePlayersAcrossTargetLists()
    {
        var visiblePlayerIds = new HashSet<uint> { 20 };
        var coneTargets = new uint[] { 10, 20 };
        var closeTargets = new uint[] { 20, 30 };
        var enemyListTargets = new uint[] { 10, 20, 30 };
        var onScreenTargets = new uint[] { 10, 20, 30 };

        Assert.Equal(new uint[] { 20 }, Filter(coneTargets));
        Assert.Equal(new uint[] { 20 }, Filter(closeTargets));
        Assert.Equal(new uint[] { 20 }, Filter(enemyListTargets));
        Assert.Equal(new uint[] { 20 }, Filter(onScreenTargets));

        uint[] Filter(IEnumerable<uint> targets)
            => targets.Where(id => TargetSelectionPolicy.ShouldKeepAfterPlayerPreference(
                true,
                visiblePlayerIds.Contains(id))).ToArray();
    }

    [Fact]
    public void InactivePlayerPreferenceLeavesMixedTargetsUntouched()
    {
        var targets = new uint[] { 10, 20, 30 };

        Assert.Equal(
            targets,
            targets.Where(_ => TargetSelectionPolicy.ShouldKeepAfterPlayerPreference(
                false,
                false)).ToArray());
    }
}
