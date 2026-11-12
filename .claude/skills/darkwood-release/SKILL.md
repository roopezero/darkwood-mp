---
name: darkwood-release
description: Make a DarkwoodMP release - bump the mod and protocol versions as needed, build, run the tests, build the Windows and Linux zips, update the docs and changelog, and commit. Use when finishing a change that players will install, or when asked to package or release the mod.
---

# Releasing DarkwoodMP

1. **Versions.**
   - `PluginInfo.Version` in `src/DarkwoodMP/Plugin.cs`: bump it for any change players install (minor for features, patch for fixes).
   - `Protocol.Version` in `src/DarkwoodMP/Net/Packets.cs`: bump it **only** if a message's bytes changed (a new type, or a field added or removed). Hosts refuse other protocol versions, so everyone must update together. Say so in the release notes.
1. **Build:** `export DOTNET_ROOT=~/.dotnet PATH=~/.dotnet:$PATH; dotnet build src/DarkwoodMP -c Release`. It must have no errors or warnings, and it deploys to the user's game.
1. **Test:** at least `tools/run-test.sh capture handshake host-combat client-enemies client-chars`, plus whatever covers the change (see the `darkwood-test` skill). Each must end with `no exceptions` and `your saves: unchanged`.
1. **Package:** `./pack.sh` builds `dist/DarkwoodMP-win.zip` (BepInEx win_x64 + the DLL) and `dist/DarkwoodMP-linux.zip` (BepInEx linux_x64 + the DLL + `darkwood_mp.sh`). It downloads BepInEx 5.4.23.2 into `.cache/` the first time. Check the zips: `unzip -l dist/DarkwoodMP-win.zip | grep -E "winhttp|DarkwoodMP.dll"`.
1. **Docs** (see "Docs" in `CLAUDE.md`): add the release to `CHANGELOG.md` (protocol version, whether it plays with the last release, what changed, and how it was tested). Keep `docs/GUIDE.md` (the "What's shared" list, the settings, the known limitations) and the protocol table in `docs/TECHNICAL.md` true. Touch `README.md` only if a player would notice (a new feature line, install steps, a limitation fixed), and keep it short. Say how the change was tested (scripted peers, never two real players unless the user says they played).
1. **Commit** with a message that says what changed and why, ending with the attribution trailer from the system reminder.
1. **Push and tag** (always, as part of a release; the user asked for this): `git tag -a vX.Y.Z -m "DarkwoodMP X.Y.Z (protocol N)"`, then `git push origin main` and `git push origin vX.Y.Z`.
1. **Close the fixed issues** (always; the user asked for this): `gh issue close N --comment "Fixed in X.Y.Z (protocol N, tag vX.Y.Z). Tested with ..."` for every GitHub issue the release fixes, saying honestly how it was tested.
1. **Tell the user** which zips friends need, and whether the protocol changed (everyone must update).
