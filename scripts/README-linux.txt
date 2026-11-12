DarkwoodMP - online co-op for Darkwood
=======================================

Everyone who plays together needs the same version of the mod.


INSTALL (Linux, native build)
-----------------------------
Steam:
1. Open the game folder: in Steam, right-click Darkwood, then Manage, then
   Browse local files.
2. Extract everything from this zip into that folder (next to
   Darkwood.x86_64).
3. In Steam, open Darkwood's Properties and set the Launch options to:
       ./darkwood_mp.sh %command%
   Use darkwood_mp.sh, not run_bepinex.sh: the latter loads into Steam's
   runtime container instead of the game, and the game quits at once.
4. Start the game from Steam.

GOG:
1. Extract everything from this zip into the folder with Darkwood.x86_64
   (for example ~/GOG Games/Darkwood/game/).
2. Start the game with ./darkwood_mp.sh from that folder.

To check it works: after starting the game, BepInEx/LogOutput.log in the
game folder should say "DarkwoodMP ... loaded" and "All N patches applied".

To uninstall, clear the launch options and delete the BepInEx and
DarkwoodMP-LICENSES folders, darkwood_mp.sh, run_bepinex.sh, libdoorstop.so,
DarkwoodMP-README.txt, .doorstop_version and changelog.txt from the game
folder.


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
  The host's firewall must allow the port too, for example:
      sudo firewall-cmd --permanent --add-port=27720/tcp --add-port=27720/udp
      sudo firewall-cmd --reload
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
To report a bug, open an issue at
https://github.com/roopezero/darkwood-mp/issues and attach
BepInEx/LogOutput.log from the game folder.
