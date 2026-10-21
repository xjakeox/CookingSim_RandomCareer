# CookingSim_RandomCareer

A BepInEx mod for Cooking Simulator that adds a **Random start** option to Career mode. Instead of the usual three starting recipes, a new Career begins with three random ones, and every ingredient they need is unlocked.

Everything else is normal Career: the same kitchen, day flow, fame levels, cooking points, end-of-day recipe picks and saves.

## Requirements

- Cooking Simulator (Steam, PC)
- [BepInEx 5](https://github.com/BepInEx/BepInEx/releases) (tested with 5.4.23.5, x64)

## Installation

1. Install BepInEx 5 into the game folder, then start the game once so it creates its folders.
2. Copy `CookingSim_RandomCareer.dll` into `CookingSimulator\BepInEx\plugins`.

## Usage

1. Open **Career** from the game modes menu.
2. Under **Settings**, set **Random start** to **On**.
3. Press **Play**.

The tutorial is always skipped in a random start.
The option is only shown for the base kitchen.

### The recipe pool

The three recipes are picked from every first-tier recipe (not an upgraded version) up to fame level 1. Event-only recipes are never picked. FoodNetwork recipes are included only if you own that DLC; it is detected automatically.

## Configuration

Settings are in `BepInEx\config\cookingsim_randomcareer.cfg`, created on first launch.

| Setting | Default | Description |
|---|---|---|
| `RandomCareer` | `false` | Last choice of the Random start option. Set from the Career panel. |
| `Seed` | `0` | `0` gives a new roll every Career. Any other number always rolls the same three recipes. The seed used is written to the log. |
| `MaxStartFame` | `1` | Highest recipe fame level allowed in the starting pick. |
| `ExcludedPackages` | *(empty)* | Comma-separated recipe packages to leave out, e.g. `FoodNetwork`. Packages: Classic, American, Chinese, Oktoberfest, Halloween, FoodNetwork, Christmas, Valentines2022, BBQ. |
| `DiscoveryLogging` | `false` | Developer logging of recipe and ingredient state during a Career start. |

Each random start writes its seed and picks to `BepInEx\LogOutput.log`.

## Multiplayer (CSMP)

Only the host needs this mod. The roll happens when the host starts the Career, and CSMP sends the host's world to the client.

If the client doesn't own FoodNetwork, the host should add `FoodNetwork` to `ExcludedPackages`, so a FoodNetwork recipe the client can't load is never rolled. (This will have no effect on the host picking a FoodNetwork recipe at day end).

## Building

Requires the .NET SDK and the game installed. The project references the game's own DLLs; set `GameDir` in `CookingSim_RandomCareer.csproj` if your game isn't in the default Steam folder.

```bash
dotnet build -c Release
```

The build copies the DLL into `BepInEx\plugins`. Close the game first, since a running game locks the file.

## AI Disclaimer
Yes, this was built using AI. No, (for the most part) I do not know what I am doing. I know minimal coding, enough to just get by, but couldn't have made this on my own. I made this for me, but figured some other people might find use in it.
