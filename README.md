# Erenshor Raid Commands

A **Lunaris** mod that makes it easier to assemble and manage a SimPlayer raid in Erenshor using slash commands.

**Current version:** 0.4.0

## Commands

| Command | Description |
| --- | --- |
| `/raidstart` | Start a raid using the game's normal raid restrictions, without summoning the entire guild. |
| `/raidend` | End the active raid. |
| `/raidstatus` | Show raid occupancy and available slots. |
| `/raidlist` | List occupied raid positions and SimPlayer names. |
| `/raidadd Scrubby` | Add an available SimPlayer; may summon an eligible guild member if not already active. |
| `/raidadd Scrubby Kitten` | Add multiple SimPlayers in sequence. Commas are also accepted. |
| `/raidadd Scrubby 6` | Add Scrubby to visible raid position **6**, if empty. |
| `/raidremove Scrubby Kitten` | Remove multiple members by name. |
| `/raidremove 5` | Remove the member in visible raid position **5**. |
| `/raidremove 6-9` | Remove members occupying positions **6 through 9**. |
| `/raidreplace Scrubby Kitten` | Replace Scrubby with Kitten in the same raid slot; replacement must already be an active SimPlayer. |

## Behavior and limitations

- Raid position **1** belongs to the player; commands refer to visible positions **2–15**.
- SimPlayer name matching is case-insensitive; status messages use the SimPlayer's canonical name where available.
- Adding a member can automatically start a raid if one is not already active.
- Summoning is intended for eligible **guild SimPlayers** while in **Reliquary Hall**; the plugin uses an approximately **5-second** delay before spawning and inviting.
- The game still enforces its own raid and guild restrictions. A command may fail if the SimPlayer is unavailable, the raid is full, or the selected slot is occupied.
- `/raidreplace` currently requires the incoming SimPlayer to be active in the current zone; it does **not** automatically summon them.

## Example

```text
/raidstart
/raidadd Kitten
/raidadd Scrubby 6
/raidlist
/raidremove 6-9
/raidend
```

## Requirements

- Erenshor (Windows PC)
- Lunaris mod loader with Harmony patching available
- For building: Visual Studio with .NET Framework 4.8.1 development tools

## Installation

1. Install Lunaris according to its documentation.
2. Build this project in **Release**, or obtain the DLL from a release when available.
3. Place the compiled DLL in the Lunaris plugin directory used by your Erenshor installation.
4. Launch Erenshor and confirm the plugin loads in the Lunaris log.

This is a **Lunaris-only** mod. BepInEx and Thunderstore packaging are not supported.

## Building from source

1. Create or open a **Class Library (.NET Framework)** project targeting **.NET Framework 4.8.1**.
2. Add `Plugin.cs` to the project.
3. Add references to `Assembly-CSharp.dll`, `Lunaris.dll`, `0Harmony.dll`, and the Unity DLLs required by the source, from your local Erenshor/Lunaris installation.
4. Set game/loader DLL references to **Copy Local = False**.
5. Select **Release** and build the solution. The output DLL is typically under `bin/Release/`.

Game and loader assemblies are **not included** in this repository. Build references may need updating after Erenshor or Lunaris updates.

## Compatibility

Developed against the Erenshor build current in November 2026. Future game updates may change the methods this plugin patches. No compatibility guarantee is made for later builds.

## Disclaimer

Unofficial fan-made mod; not affiliated with or endorsed by Burgee Media or the Lunaris developers. Use at your own risk and back up your saves before trying new mods.
