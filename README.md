# Ringtail Rush

A 3D endless runner built in Unity. You play a raccoon sprinting through changing zones, dodging obstacles, grabbing Takeout coins, and picking up power-ups. Now and then a special event run twists the rules.

This is my first game project. I built it to learn Unity and C#, so it's still growing.

Pause Screen

<img width="1586" height="895" alt="image" src="https://github.com/user-attachments/assets/ae8d93ec-e5ce-4a31-b494-e72db5f23215" />

Game Over Screen

<img width="1581" height="881" alt="image" src="https://github.com/user-attachments/assets/9936f266-da30-4ef4-960b-0d133a29eb76" />



## Features

- **Three-lane running.** Dodge left and right, jump, and hold on as the speed climbs along a smooth ramp the further you go.
- **Graze system.** Your first hit in a window is forgiven and gives you a short burst of invincibility. Hit again too soon and you need armor or a revive, or the run ends. Head-on and glancing hits are told apart using the collision surface normal.
- **Endless world.** Ground, obstacles, coins, and power-ups spawn ahead of you. A trailing trigger deletes everything you've passed, so the scene stays light.
- **Four zones.** Forest, Frontier, Suburbs, and City play in a bouncing loop and swap on exact ground-piece boundaries.
- **Event runs.** Seven special run types change the rules: extra armor, revives, faster speed, denser obstacles, bigger coin payouts, and more. Rascal's Gambit rolls a random blessing or curse. The chance of an event run is set in the Inspector (5% by default).
- **Dumpster Dive power-up.** The raccoon hops into a bin and plows through obstacles while collecting coins, at a big speed boost.
- **Saved progress.** Takeout coins, high score, and owned shop items persist between sessions using PlayerPrefs.
- **Menus.** Main menu, start screen ("press any button"), pause menu (Escape), game over screen, and a shop scene.

## Scenes

| Scene | Purpose |
| --- | --- |
| `MainMenu` | Play, Shop, and Quit buttons |
| `World` | The run itself |
| `Shop` | Where Takeout will be spent (see status below) |

## Scripts

| Script | What it does |
| --- | --- |
| `Movement.cs` | Forward speed curve, lane changes, jumping, animation priorities, and the Dumpster Dive sequence |
| `Collision.cs` (`PlayerCollision`) | Hits, armor, revives, coin pickup, power-up pickup, and game over |
| `Spawner.cs` (`GroundSpawner`) | Spawns ground, obstacles, coins, power-ups, and scenery per zone |
| `ZoneData.cs` | Holds one zone's prefabs and placement settings |
| `EventRunManager.cs` | Rolls and stores event run traits and their modifiers |
| `GameManager.cs` | Score HUD, high score, pause, start screen, game over, and event banners |
| `DataManager.cs` | Persistent save data: coins, high score, owned items |
| `DumpsterBinCollector.cs` | Lets the bin destroy obstacles and collect coins during Dumpster Dive |
| `PowerUpPickup.cs` | Marks a pickup's type and duration |
| `Trashbin.cs` | Trails the player and cleans up passed objects |
| `FollowPlayer.cs` | Camera follow |
| `CoinSpin.cs` | Spins coins |
| `MainMenuManager.cs`, `ShopManager.cs` | Button handlers for scene changes |
| `FrontTrigger.cs` | Empty leftover from an older hit system |

## Running it

1. Install Unity Hub and the Unity version listed in `ProjectSettings/ProjectVersion.txt`.
2. Clone this repo and add the folder in Unity Hub.
3. Open the `MainMenu` scene and press Play.

Controls:
A/D and  J/L to move Left/Right
Space Bar to Jump

## Status

This is a work in progress. Here's what works and what doesn't yet.

**Working:** the core run, hit system, zones, event runs, Dumpster Dive, saving, and menus.

**Not done yet:**
- The shop has a return button, but no purchase flow. The save system already supports owned items.
- Only one of four planned power-ups is built. Jung Magnet, Trash Tornado, and Bubble Wrap are placeholders.
- `FrontTrigger.cs` is unused and can be removed.

## How I built it

I built most of this by following Unity tutorials. When I got stuck, I asked Claude for help, then tested and adjusted the results in my own project. Using both is how I learned the basics quickly.

## What I learned

- Splitting a game into small scripts that each do one job.
- Singletons and `DontDestroyOnLoad` for data that survives scene loads.
- Procedural spawning, and cleaning it up so it doesn't hurt performance.
- Working around Unity quirks, like why `CharacterController` needs `OnControllerColliderHit` instead of normal collision callbacks.
