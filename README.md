# DarkwoodMP: online co-op for Darkwood

DarkwoodMP is a mod that lets you play [Darkwood](https://store.steampowered.com/app/274520/Darkwood/) together with up to four friends. One player hosts their world, the others join it, and you survive the forest together: the same enemies, the same nights, the same story.

- Works with the **Steam** and **GOG** versions, on **Windows** and **Linux**.
- Steam and GOG players can play together.
- Join through **Steam invites**, or with a **direct connection** and a built-in server browser.

> **Early version.** The mod is tested a lot by automated tests, but only a few real co-op sessions have been played. Expect bugs, and please [report them](https://github.com/roopezero/darkwood-mp/issues). Back up your saves before you start.

## Features

- **Play the whole game together.** Explore, fight, scavenge, build, and play through the chapters and the ending as a group. The host's world is the shared world.
- **Smarter enemies.** Enemies don't just go for whoever is closest. They go for the player who is the biggest threat to them: the one who hits them, makes noise, or is in plain view.
- **Shared world.** Doors, barricades, traps, containers, traders, furniture, fire, generators, the time of day, nights, weather and quest progress are the same for everyone. What one player takes from a cupboard is gone for the others.
- **Your character is yours.** The host keeps each friend's character (items, health, skills) and gives it back when they rejoin.
- **Shared levels, your own skills.** Everyone cooks into one shared essence bar and gets the same levels, but each player picks their own skills, on a new **Skills panel** (press **K**). The level dreams are dreamt together.
- **The world keeps going.** With others in the game, the pause menu, map and journal don't pause the world.
- **Dying for good** (Nightmare, or Hard with no lives left) lets you watch your friends until the game is over.
- **Your saves are safe.** When you join someone's game, your own saves aren't touched.
- **A new menu.** A cleaner main and pause menu, a Start Game window that shows who you've played each save with, and a full Options window with key rebinding.
- **Quality of life, also when playing alone:** a *Sort & Stack* button at the workbench, faster world loading, better framing on ultrawide screens, and the mouse is free to leave the window in menus.

See [docs/GUIDE.md](docs/GUIDE.md) for everything the mod does, in detail.

## Install

Download the zip for your system from the [Releases page](https://github.com/roopezero/darkwood-mp/releases). **Everyone who plays together needs the same version** of the mod.

### Windows (Steam or GOG)

1. Open the game folder. In Steam: right-click Darkwood, then *Manage*, then *Browse local files*. For GOG, it's where you installed the game, for example `C:\GOG Games\Darkwood`.
2. Extract everything in `DarkwoodMP-win.zip` into that folder, so that `winhttp.dll` ends up next to `Darkwood.exe`.
3. Start the game as usual.

If nothing changes in the game, Windows Defender may have removed `winhttp.dll` (it's the loader that lets mods run, and virus scanners sometimes flag it). Allow it in *Windows Security*, *Virus & threat protection*, *Protection history*, and extract the zip again.

### Linux, Steam

1. Open the game folder: right-click Darkwood, then *Manage*, then *Browse local files*.
2. Extract everything in `DarkwoodMP-linux.zip` into that folder, next to `Darkwood.x86_64`.
3. Right-click Darkwood, choose *Properties*, and set the *Launch options* to:
   ```
   ./darkwood_mp.sh %command%
   ```
4. Start the game from Steam.

This is for the native Linux version of the game, not the Windows version through Proton.

### Linux, GOG

1. Extract everything in `DarkwoodMP-linux.zip` into the folder with `Darkwood.x86_64` (for example `~/GOG Games/Darkwood/game/`).
2. Start the game with `./darkwood_mp.sh` from that folder.

### Did it work?

The main menu looks different: a column of entries in the lower left (*Start Game*, *Join Game*, *Options*, ...) with the mod's version in the bottom right corner.

### Uninstall

- **Windows:** delete `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version`, `changelog.txt`, `DarkwoodMP-README.txt` and the `BepInEx` and `DarkwoodMP-LICENSES` folders from the game folder.
- **Linux:** clear the launch options, then delete the `BepInEx` and `DarkwoodMP-LICENSES` folders, `darkwood_mp.sh`, `run_bepinex.sh`, `libdoorstop.so`, `.doorstop_version`, `changelog.txt` and `DarkwoodMP-README.txt` from the game folder.

## How to play

### Hosting

Choose *Start Game*, pick a save (or *New Game*), tick **Host a multiplayer game**, and choose *Steam* or *Direct*. You can also start hosting from inside your world: press Escape and choose *Multiplayer*.

- **Steam:** once you're in your world, use *Invite Steam friends* in the Multiplayer window. No port forwarding needed.
- **Direct:** set a server name, and a password if you want one (from the pause menu, then click *Start hosting*). The Multiplayer window lists the addresses friends can use. For friends on the same network, that's all. For friends over the internet, forward port **27720**, both **TCP and UDP**, on your router, or all of you can use a VPN such as Tailscale or ZeroTier. Allow the game through your firewall when Windows asks.

### Joining

- **Steam:** accept your friend's invite, or right-click them in your Steam friends list and choose *Join Game*.
- **Direct:** choose **Join Game** in the main menu to open the server browser. The *LAN* tab finds games on your network. To join over the internet, use *Add server...* with the address the host gives you, or *Connect to address...*.

You get the host's world as it is right now. If you join from inside your own game you leave it, so save first. The host's world is played in a separate folder, so your own saves stay as they were.

If things ever look out of sync, open the Multiplayer window (Escape, *Multiplayer*) and choose *Reload the host's world*.

## Troubleshooting

- **The menu looks like the normal game.** The mod didn't load. On Linux, check the launch options are exactly `./darkwood_mp.sh %command%`. On Windows, check that `winhttp.dll` is next to `Darkwood.exe` (see the Windows Defender note above). If there's no `BepInEx\LogOutput.log` in the game folder after starting the game, and your `Darkwood.exe` is 32-bit, use `DarkwoodMP-win_x86.zip` instead.
- **"Different DarkwoodMP version".** Everyone needs the same mod version.
- **"No answer" when joining by address.** The host's router must forward port 27720 (TCP and UDP), and the host's firewall must allow it.
- **"The host is playing the prologue"** (or "in a dream"). Wait: you join by yourself when they're out of it.
- **The LAN tab finds nothing.** The host's firewall must allow UDP port 27720.

More in [docs/GUIDE.md](docs/GUIDE.md#troubleshooting). When reporting a bug, attach `BepInEx/LogOutput.log` from the game folder.

## Known limitations

- Joining sends the whole world to the new player (about 3 MB), and the host's game stutters for a moment while it's prepared.
- The game doesn't pause while anyone else is in it.
- Each player has their own copy of NPCs, which follow the host's; some scripted moments still happen only for the host.
- The ending is played by each player on their own, after leaving the host's game.

The full list is in [docs/GUIDE.md](docs/GUIDE.md#known-limitations).

## More

- [CHANGELOG.md](CHANGELOG.md): what changed in each version, and how it was tested.
- [docs/GUIDE.md](docs/GUIDE.md): everything the mod does, its settings and files, in detail.
- [docs/TECHNICAL.md](docs/TECHNICAL.md) and [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md): how the mod works, and how to build and test it.

## License

DarkwoodMP is under the [MIT license](LICENSE). The zips also contain [BepInEx](https://github.com/BepInEx/BepInEx) and the libraries it comes with, under their own licenses (MIT, and LGPL-2.1 for the Doorstop loader); see [LICENSES/](LICENSES/README.txt).

DarkwoodMP is a fan-made mod, not made or supported by Acid Wizard Studio. You need your own copy of Darkwood.
