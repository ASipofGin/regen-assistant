# regen-assistant — Development Notes

**Regen Assistant**, a [Dalamud](https://dalamud.dev) plugin (FFXIV) that shows, beside each
player's HP on the party list, the estimated HP that active heal-over-time effects will still
restore and how long until they complete, plus a draining square gauge, a standalone monitor
window, and a config UI. Built from goatcorp's SamplePlugin template.

## Current repo layout

```
RegenAssistant.sln
RegenAssistant/
  RegenAssistant.csproj    # <Project Sdk="Dalamud.NET.Sdk/15.0.0">, AllowUnsafeBlocks
  RegenAssistant.json      # Plugin manifest (Name, Author, Punchline, Tags...)
  Plugin.cs                # IDalamudPlugin entry point; services, /regenassist command, windows
  Configuration.cs         # Overlay toggles/offsets/colors, HpPerPotency, custom statuses
  RegenStatuses.cs         # HoT table (name → potency/tick); ids resolved from Status sheet by English name at load
  RegenTracker.cs          # StatusList → estimated remaining heal + seconds remaining per member
  RegenCalibrator.cs       # IFramework.Update: learns HP-per-potency from the first tick of freshly applied regens (rolling median, persisted)
  PartyMembers.cs          # unsafe: AgentHUD party in HUD order → IBattleChara via IObjectTable (solo fallback via IPlayerState)
  PartyListOverlay.cs      # unsafe: anchors text+square to _PartyList HPGaugeComponent nodes; background drawlist
  Windows/MonitorWindow.cs # per-member table with breakdown tooltip
  Windows/ConfigWindow.cs  # settings incl. built-in status enable/disable + custom id/potency entries
.github/workflows/pr-build.yml  # CI: .NET 10, downloads Dalamud dev distrib, dotnet build (PRs to master)
```

## Building in this (Linux) environment

Works locally despite the `net10.0-windows` TFM:

```
apt-get install dotnet-sdk-10.0          # after apt-get update
curl -LO https://raw.githubusercontent.com/goatcorp/dalamud-distrib/main/latest.zip  # goatcorp.github.io is proxy-blocked
unzip latest.zip -d /root/dalamud-dev
DALAMUD_HOME=/root/dalamud-dev dotnet build -c Release -p:EnableWindowsTargeting=true
```

The distrib's `Dalamud.xml` / `FFXIVClientStructs.xml` + a small reflection dumper are the
fastest way to verify API member names before writing interop code. Verified for v15:
`IGameGui.GetAddonByName` returns `AtkUnitBasePtr` (use `.IsNull/.IsReady/.IsVisible/.Scale`
and cast `.Address`); `AgentHUD.Instance()->PartyMembers` (`HudPartyMember.EntityId/.Index`),
`AddonPartyList.PartyMembers[i].HPGaugeComponent->OwnerNode->AtkResNode.ScreenX/.ScreenY`;
`IObjectTable.SearchByEntityId`; `IStatus.StatusId/.RemainingTime/.SourceId`.
Runtime behavior can only be verified in the actual game on Windows.

## Dalamud plugin fundamentals

### Entry point & service injection
- The plugin class implements `IDalamudPlugin` (constructor = load, `Dispose()` = unload).
- Services are injected via `[PluginService]` attributes on static properties
  (see `Plugin.cs`). Constructor parameters also work.
- Key services:
  - `IDalamudPluginInterface` — plugin config load/save, `UiBuilder` (Draw/OpenConfigUi/OpenMainUi events), assembly location, manifest.
  - `ICommandManager` — register `/slash` commands via `AddHandler(name, new CommandInfo(handler))`; remove in Dispose.
  - `IClientState` — login state, `TerritoryType`, login/logout/territory-change events.
  - `IPlayerState` — local player info (**v15: replaces `IClientState.LocalPlayer` / `LocalContentId`**): `IsLoaded`, `ClassJob` (RowRef), `Level`, etc.
  - `IFramework` — per-frame `Update` event; `IFramework.Run()` to marshal work onto the game main thread (required for main-thread work during load/dispose in v15).
  - `IDataManager` — Lumina Excel sheets: `GetExcelSheet<TerritoryType>().TryGetRow(id, out var row)`.
  - `ITextureProvider` — `GetFromFile(path)`, `GetFromGameIcon(new GameIconLookup(iconId))`, then `.GetWrapOrDefault()/.GetWrapOrEmpty()` for ImGui handles.
  - `IPluginLog` — logging, visible in-game via `/xllog`.
  - `IObjectTable`, `IPartyList`, `ITargetManager`, `ICondition`, `IDutyState`, `IGameGui`, `IChatGui`, `IGameConfig`, `IAddonLifecycle`, `IGameInteropProvider`, and more in `Dalamud.Plugin.Services`.

### UI (ImGui)
- Bindings live in `Dalamud.Bindings.ImGui` (Dalamud's own ImGui bindings; older docs reference ImGuiNET).
- Use `Dalamud.Interface.Windowing`: subclass `Window`, register in a `WindowSystem`, and wire
  `PluginInterface.UiBuilder.Draw += WindowSystem.Draw` (unsubscribe in Dispose).
- `ImRaii` (in `Dalamud.Interface.Utility.Raii`) scope-guards Begin/End pairs (`Child`, tables, indent, colors) — prefer it over manual End calls. v15 adds `ImRaii.Tooltip()`.
- Window title after `##` is a hidden ID; after `###` is a constant ID allowing a dynamic label.
- Scale hardcoded pixel sizes by `ImGuiHelpers.GlobalScale` so non-100% HUD scales work.
- Draw() runs every frame — keep it allocation-light; don't do I/O there.

### Configuration
- Implement `IPluginConfiguration` (`Version` field), save via `PluginInterface.SavePluginConfig(this)`,
  load via `PluginInterface.GetPluginConfig() as Configuration ?? new Configuration()`.

### Reading combat/party state (relevant to a regen assistant)
- Party members: `IPartyList` (IReadOnlyCollection of `IPartyMember`; struct enumerators since v14).
- Any battle character (`IBattleChara`, from object table / party member `GameObject`) exposes
  `StatusList`; iterate for `StatusId`, `RemainingTime`, `SourceId`. `IStatus` is a readonly struct since v14.
  Pattern (from Caraxi/RemindMe): `foreach (var se in battleChara.StatusList) if (se.StatusId == ...) ...`
- Status/action metadata (names, icons, durations) come from Lumina sheets via `IDataManager`
  (`Status`, `Action` sheets). Job icon ids start at offset 62100 + ClassJob row id.
- Prior art for buff/HoT tracking: XIVAuras, RemindMe.

### Hooking game internals (only if needed)
- `IGameInteropProvider` creates hooks: `HookFromSignature<T>(sig, detour)`, `HookFromAddress`,
  and `InitializeFromAttributes(this)` for `[Signature]`-annotated fields. Prefer signatures over
  addresses — they survive game patches.
- [FFXIVClientStructs](https://github.com/aers/FFXIVClientStructs) provides C# bindings to native
  game classes; included with Dalamud. Most read-only needs are covered by Dalamud services first.

### Build & packaging
- `Dalamud.NET.Sdk` (csproj `Sdk="Dalamud.NET.Sdk/15.0.0"`) wires up Dalamud refs and
  DalamudPackager automatically. Release builds emit `bin/x64/Release/<Name>/` with the
  filled-in manifest and `latest.zip` ready for distribution.
- DalamudPackager fills manifest fields (InternalName, AssemblyVersion, DalamudApiLevel) from the
  json template next to the csproj. Since SDK 14, manifest properties can also live in the csproj.
- v15 note: the manifest inside the zip is authoritative (no longer overwritten from the repo on install).
- Dev loop: build DLL → `/xlsettings` → Experimental → add DLL path to Dev Plugin Locations →
  `/xlplugins` → Dev Tools → enable. `/xllog` for logs.

### v15 / API 13 gotchas (this repo's SDK level)
- `IClientState.LocalPlayer`/`LocalContentId` → use `IPlayerState`.
- Main-thread work during load/dispose must go through `IFramework.Run()`.
- `ICharacter.Customize` is `Span<byte>`; `HoverActionKind` → `DetailKind`.
- SeStrings: see https://dalamud.dev/plugin-development/sestring/ (`.ToMacroString()` for macro form).

### Renaming the template
Replace all `SamplePlugin` references (namespace, csproj, sln, json, folder names, WindowSystem
name, command name) with the real plugin name. Update `SamplePlugin.json` metadata — the
InternalName is derived from the assembly name and must stay stable once published.

### Publishing
- Official repo submissions go through [DalamudPluginsD17](https://github.com/goatcorp/DalamudPluginsD17).
- License is AGPL-3.0-or-later (template default).
- Note the [AI usage policy](https://dalamud.dev/plugin-publishing/ai-policy): AI-assisted work
  must be disclosed on submission; fully AI-generated submissions are rejected.

## Reference links
- API docs: https://dalamud.dev/api/ (Dalamud.Plugin.Services namespace lists all injectable services)
- Getting started: https://dalamud.dev/faq/getting-started/
- Project layout: https://dalamud.dev/plugin-development/project-layout/
- v15 changes: https://dalamud.dev/versions/v15/
- Dalamud source: https://github.com/goatcorp/Dalamud

Note: dalamud.dev is not directly fetchable from this remote environment's network policy
(proxy 403); use web search or the Dalamud/dalamud-docs GitHub sources when docs are needed.
