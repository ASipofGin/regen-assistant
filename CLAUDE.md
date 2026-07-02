# regen-assistant — Development Notes

This repo was created from goatcorp's **SamplePlugin** template for [Dalamud](https://dalamud.dev),
the FFXIV plugin framework loaded by XIVLauncher. The intended plugin ("regen assistant")
will be built on top of this template.

## Current repo layout

```
SamplePlugin.sln
SamplePlugin/
  SamplePlugin.csproj      # Uses <Project Sdk="Dalamud.NET.Sdk/15.0.0">
  SamplePlugin.json        # Plugin manifest template (Name, Author, Punchline, Tags...)
  Plugin.cs                # IDalamudPlugin entry point; service injection, command, windows
  Configuration.cs         # IPluginConfiguration persisted via SavePluginConfig
  Windows/MainWindow.cs    # ImGui window (Dalamud.Interface.Windowing.Window)
  Windows/ConfigWindow.cs  # Settings window
Data/goat.png              # Example asset copied to output dir
.github/workflows/pr-build.yml  # CI: .NET 10, downloads Dalamud dev distrib, dotnet build
```

Targets `net10.0-windows` (implied by SDK 15 / API level 13). The build resolves Dalamud
assemblies from `$env:AppData\XIVLauncher\addon\Hooks\dev` (or `DALAMUD_HOME`); CI downloads
https://goatcorp.github.io/dalamud-distrib/latest.zip into that path. This Linux container
cannot build the plugin — Windows-only TFM and Dalamud refs — so verification happens via CI
(PR builder) or on a Windows machine.

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
