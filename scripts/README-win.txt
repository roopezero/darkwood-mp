DarkwoodMP - online co-op for Darkwood
=======================================

Everyone who plays together needs the same version of the mod.


INSTALL (Windows, Steam or GOG)
-------------------------------
1. Open the game folder.
   Steam: right-click Darkwood, then Manage, then Browse local files.
   GOG: the folder you installed it to, for example C:\GOG Games\Darkwood
2. Extract everything from this zip into that folder, so that winhttp.dll
   ends up next to Darkwood.exe.
3. Start the game normally.

To check it works: after starting the game, BepInEx\LogOutput.log in the
game folder should say "DarkwoodMP ... loaded" and "All N patches applied".

If nothing happens, Windows Defender may have removed winhttp.dll (it is the
loader that lets mods run, and scanners sometimes flag it). Open Windows
Security, then Virus & threat protection, then Protection history, allow the
file, and extract the zip again.

To uninstall, delete winhttp.dll, doorstop_config.ini, .doorstop_version,
changelog.txt, DarkwoodMP-README.txt and the BepInEx and
DarkwoodMP-LICENSES folders from the game folder.


PLAYING
-------
Hosting: choose Start Game, pick a save (or New Game), tick "Host a
multiplayer game" and choose Steam or Direct. You can also host from inside
your world: press Esc and choose Multiplayer.

Steam (easiest, no port forwarding):
  The host clicks "Invite Steam friends" in the Multiplayer window.
  Friends accept the invite, or use "Join Game" in the Steam friends list.

Direct (for GOG, or without Steam):
  Host: set a server name and an optional password (from the pause menu,
  then click "Start hosting"). The Multiplayer window shows the addresses
  friends can use. For play over the internet, the host must forward port
  27720 on their router, both TCP and UDP, or everyone can use a VPN such
  as Tailscale or ZeroTier.
  Windows asks to allow the game through the firewall the first time:
  allow it.
  Join: choose Join Game in the main menu. The LAN tab finds games on your
  network; for the internet, click "Add server...", type the host's address
  (host or host:port), pick it and click "Connect".

When you join:
- You get the host's world as it is right now.
- If you are in a game of your own you leave it, so save first.
- Your own saves are not touched while you play in the host's world.
- The host keeps your character (health, inventory, skills and so on) and
  gives it back when you rejoin that world.

If things look out of sync, press Esc, choose Multiplayer and click
"Reload the host's world".


PROBLEMS
--------
If, after starting the game once, there's no BepInEx\LogOutput.log in the
game folder, BepInEx didn't load. DarkwoodMP-win.zip is for a 64-bit
Darkwood.exe; if yours is 32-bit (not yet known for every store and
version), remove winhttp.dll and doorstop_config.ini from the game folder
and extract DarkwoodMP-win_x86.zip instead.

To report a bug, open an issue at
https://github.com/roopezero/darkwood-mp/issues and attach
BepInEx\LogOutput.log from the game folder.
