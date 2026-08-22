namespace BetterTargetingSystem;

internal static class TargetSelectionPolicy
{
    internal static bool IsHostilePlayer(
        bool isPvP,
        bool publicHostileFlag,
        bool classifiedAsEnemyForActionTargeting)
        => isPvP ? classifiedAsEnemyForActionTargeting : publicHostileFlag;

    internal static bool ShouldPreferVisiblePlayers(
        bool isPvP,
        bool preferenceEnabled,
        int visiblePlayerCount)
        => isPvP && preferenceEnabled && visiblePlayerCount > 0;

    internal static bool ShouldKeepAfterPlayerPreference(
        bool preferenceActive,
        bool isVisiblePlayer)
        => !preferenceActive || isVisiblePlayer;
}
