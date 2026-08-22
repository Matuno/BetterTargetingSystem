# Better Targeting System contract

## Identity and purpose

- Internal name: `BetterTargetingSystem`.
- Owner: Detle.
- Purpose: Replace the game's target-selection keybinds with explicit, user-triggered selection policies that prioritize hostile targets in player-centered cones aligned to the active camera's horizontal direction.

## Inputs and triggers

- `IFramework.Update` polls configured keyboard and mouse bindings while the client is logged in.
- `/bts`, Dalamud's configuration UI action, and the plugin-manager action open the configuration window.
- `/btshelp` and Dalamud's main UI action open the help window.
- Configuration controls define three cone widths and ranges, a close-target circle, a default-on PvP player preference, an opt-in diagnostic geometry overlay, and bindings for cycle, closest, lowest-health, and best-AOE selection.
- Target selection reads the local player, object table, hostile/action-target relation, enemy-list UI state, targetability, screen visibility, line of sight, distance, health, camera orientation, and current/previous target.

## Outputs and persistence

- An explicit configured keypress may set `ITargetManager.Target`, clear `ITargetManager.SoftTarget`, and consume the matching `IKeyState` entry so the game's default binding does not also run.
- Cone/circle settings, keybinds, the default-on PvP player preference, and the default-off debug-overlay toggle are persisted through `IDalamudPluginInterface.SavePluginConfig`.
- When enabled, the overlay draws the configured effective distance-band outlines, camera-forward axis, and close-target circle. It does not enumerate, classify, or highlight targets.
- Cycling entity IDs are retained only in memory and cleared on territory change or unload.
- The plugin writes no files other than Dalamud-managed configuration and exports no game state.

## Client-state and unload behavior

- Targeting is inactive while logged out, without a local player, in GPose, or while text input/ImGui keyboard capture is active. Dalamud's UI-hide callback clears the managed ImGui-capture handoff so hidden UI cannot leave key handling blocked.
- PvP is intentionally supported for player characters that the game's action-target classifier identifies as enemies; friendly, allied, and unknown player relations are excluded. By default, an eligible visible enemy player is preferred over battle-NPC targets (including objectives), with battle NPCs retained as a fallback or when the preference is disabled.
- Territory changes clear target-cycle state and any captured debug geometry. The opt-in overlay remains available in combat, duties, flight, and PvP so it can diagnose the states where targeting runs; it clears while logged out, in GPose, disabled, or when required player/camera/projection state is unavailable.
- Checked player, camera, graphics-device, UI-array, native-object, and collision paths reject the candidate or operation when required state is unavailable. Zoning/reload behavior remains a mandatory live check before promotion.
- Unload unregisters framework and territory callbacks, all four UI callbacks, commands, and windows. No background worker, hook, socket, or retained native pointer exists.

## Privacy and retention

- The plugin observes transient object kinds, hostile/combat flags, action-target relation, positions, hitbox sizes, health, entity IDs, targetability, and camera/collision state solely for local target selection.
- It does not retain or export character names, content IDs, account identifiers, world positions, or object snapshots. While the overlay is enabled, it retains only the latest pointer-free array of projected two-dimensional line endpoints until replacement, disablement, territory change, or unload.
- Debug logging contains operational counts and decisions but must not include full character identity, IDs, addresses, or coordinates.

## Explicit non-goals

- No autonomous combat, action execution, movement, input synthesis, arbitrary command execution, network service, telemetry, or external player tracking.
- No native hook, signature scan, raw memory write, or retained native pointer.
- This PvP-enabled fork is for private/custom-repository use and is not represented as eligible for the official Dalamud plugin repository.

## Game integration and risk evidence

- Public services: `IFramework`, `IClientState`, `IObjectTable`, `ITargetManager`, `IGameGui`, `IKeyState`, `ICommandManager`, `IDalamudPluginInterface`, and `IPluginLog`.
- Native layout evidence is pinned to FFXIVClientStructs revision `8c9ef2876f2d50190bba094b875add984ea88f55`, matching `context/current.json`; the relevant camera, enemy-list, collision, input, object, device, and action definitions are unchanged from the API 15 distribution baseline, and every assumption below must be re-reviewed when that identity changes.
- `CameraManager.CurrentCamera.ViewMatrix` supplies the active render camera's horizontal forward axis because the public API exposes screen projection but not camera orientation. The matrix is copied immediately into `System.Numerics.Matrix4x4`; cone math consumes its `M13` and `M33` fields and retains no pointer.
- `CameraManager.CurrentCamera.Object.Position` and `Framework.BGCollisionModule.RaycastMaterialFilter` provide the existing camera-origin line-of-sight test because no equivalent public collision-ray service exists. The current ray raises the target/player endpoint by two world units, rejects non-finite or zero-length rays, and uses material flags `{ 0x4000, 0, 0x4000, 0 }`; the offsets and flags are brittle live-tested assumptions.
- `Camera.WorldToScreenPoint` and `Device.Width`/`Device.Height` provide the existing strict viewport bounds check in addition to public `IGameGui.WorldToScreen`; singleton paths and positive device dimensions are checked. Debug projection also checks `Control.Instance` and `Device.Instance` before calling the public projection service because its current implementation reads their view-projection/device state internally.
- `GameObject.GetIsTargetable`, `EventId.ContentId`/`Id`, and `Position` support targetability, leve/treasure ownership, and line of sight where public object interfaces are insufficient.
- Enemy-list lookup uses `AtkStage.GetNumberArrayData(NumberArrayType.EnemyList)` and the typed `EnemyListNumberArray.EnemyCount`/`Enemies`/`EntityId` layout, clamping the count to its fixed eight entries. `RaptureAtkModule.AtkModule.IsTextInputActive` and `Framework.CursorInputs.MouseButtonPressedFlags` supply text/mouse state not otherwise exposed by the existing keybind implementation; unavailable text-input state blocks key handling.
- `ActionManager.CanUseActionOnTarget` uses action ID 142 only to filter battle NPCs. In PvP, `ActionManager.ClassifyTarget(Character*) == TargetCategory.Enemy` supplies the game's action-independent enemy relation for player characters because public `ICharacter.StatusFlags.Hostile` did not reliably identify Frontlines opponents; outside PvP the public hostile flag remains the player predicate. Native pointers and classifier results are not retained between calls.
- `user32!GetKeyboardState` reads configured desktop key state. Public `ITargetManager` and `IKeyState` setters are the only client-state mutations and run only in response to the user's configured keypress.
- Registry risk flags therefore record ClientStructs and client/input mutation use, with no hooks or network dependency.

## Verification plan

- `BetterTargetingSystem.Tests` deterministically validates ViewMatrix forward extraction, camera-translation independence, debug yaw, player-origin cone boundaries, overlay endpoint geometry, elevation independence, full-circle behavior, fail-closed invalid inputs, PvP relation selection, and player-preference gating.
- Restore in locked mode, build Debug and Release for x64 against the current release and staging distributions, run tests, inspect the release package, and run `git diff --check`.
- A future enumerated read-only IPC test must confirm the loaded DLL hash and bounded adapter/invariant state without setting targets or consuming keys; the registry keeps live tests disabled until that provider exists.
- Target selection, key consumption, plugin reload/unload, and PvP behavior require personal live validation plus targeted Dalamud log inspection; they are intentionally outside the read-only IPC contract.
- `DebugMode.CaptureSnapshot` runs at a maximum of 30 Hz from `IFramework.Update`, reads the player/camera and projects geometry there, and publishes only an immutable managed screen-line snapshot. `UiBuilder.Draw` reads that snapshot and calls ImGui only; it performs no target enumeration, game-object read, native read, or collision raycast. Origin-dependent edges are omitted when the player origin cannot project, while independently visible arcs remain available for first-person and tight-camera diagnosis.

## Manual checks

- Personally verify cardinal camera headings, minimum/default/maximum zoom, camera collision, high/low pitch, first-person and third-person views, exact cone boundaries, and that the default-off overlay toggle persists and remains aligned in PvE, duties, and PvP.
- Verify PvE enemies and hostile PvP players at the front, side, rear, and configured distance boundaries; confirm friendly players never enter target lists.
- Verify screen visibility and line-of-sight behavior around walls and large/tall targets.
- Verify text entry, GPose, zoning, logout/login, configuration persistence, hot reload, and unload leave no stale callbacks or target actions.

## Promotion gate

- [x] No unresolved contract placeholder remains.
- [x] Owner, role, risk flags, compatibility channels, tests, and write allowlist are recorded accurately for incubation.
- [x] Locked release/staging restore and builds, deterministic tests, package inspection, and `git diff --check` pass.
- [ ] The incubator DLL and a registered read-only IPC test pass live validation with zero targeted errors.
- [ ] The maintainer understands and personally verifies UI/native/game-dependent behavior.
- [ ] Promotion moves the directory and updates the existing registry entry as one rollback-safe operation without creating a duplicate.
