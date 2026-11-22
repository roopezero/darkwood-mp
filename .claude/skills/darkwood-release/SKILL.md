---
name: darkwood-release
description: Make a DarkwoodMP release - bump the mod and protocol versions as needed, build, run the tests, build the Windows and Linux zips, update the docs and changelog, and commit. Use when finishing a change that players will install, or when asked to package or release the mod.
---

# Releasing DarkwoodMP

1. **Versions.**
   - `<Version>` in `Directory.Build.props` at the repo's root: bump it for any change players install (minor for features, patch for fixes). Both DLLs get it, and the plugin's `PluginInfo.Version` is generated from it.
   - `Protocol.Version` in `src/DarkwoodMP.Core/Net/Protocol.cs`: bump it **only** if a message's bytes changed (a new type, or a field added or removed). Hosts refuse other protocol versions, so everyone must update together. Say so in the release notes.
1. **Build (the release check):** `export DOTNET_ROOT=~/.dotnet PATH=~/.dotnet:$PATH; dotnet build src/DarkwoodMP -c Release --no-incremental -warnaserror -clp:ErrorsOnly`. It must exit 0: any compiler warning fails it, and the style rules are errors already. `--no-incremental` is required, because a second build skips the compiler and shows no warnings at all. It deploys both DLLs to the user's game.
1. **Unit tests:** `dotnet test tests/DarkwoodMP.Core.Tests -c Release --blame-hang-timeout 60s --blame-hang-dump-type none`, then `dotnet test tests/DarkwoodMP.Analyzers.Tests` with the same options for the formatting rules. Every test must pass.
1. **Test:** at least `tools/run-test.sh capture handshake host-combat client-enemies client-chars`, plus whatever covers the change (see the `darkwood-test` skill). Each must end with `no exceptions` and `your saves: unchanged`.
1. **Package:** `./pack.sh` builds `dist/DarkwoodMP-win.zip` (BepInEx win_x64 + both DLLs, `DarkwoodMP.dll` and `DarkwoodMP.Core.dll`), `dist/DarkwoodMP-win_x86.zip` (the same with BepInEx win_x86) and `dist/DarkwoodMP-linux.zip` (BepInEx linux_x64 + both DLLs + `darkwood_mp.sh`). It downloads BepInEx 5.4.23.2 into `.cache/` the first time, and it stops if a zip lacks either DLL or holds the map tests. Check the zips: `unzip -l dist/DarkwoodMP-win.zip | grep -E "winhttp|DarkwoodMP(\.Core)?\.dll"`.
1. **Docs** (see "Docs" in `CLAUDE.md`): add the release to `CHANGELOG.md` (protocol version, whether it plays with the last release, what changed, and how it was tested). Keep `docs/GUIDE.md` (the "What's shared" list, the settings, the known limitations) and the protocol table in `docs/TECHNICAL.md` true. Touch `README.md` only if a player would notice (a new feature line, install steps, a limitation fixed), and keep it short. Say how the change was tested (scripted peers, never two real players unless the user says they played).
1. **Commit** with a message that says what changed and why, ending with the attribution trailer from the system reminder.
1. **Push and tag** (always, as part of a release; the user asked for this): `git tag -a vX.Y.Z -m "DarkwoodMP X.Y.Z (protocol N)"`, then `git push origin main` and `git push origin vX.Y.Z`.
1. **GitHub release** (always; players download from it): `gh release create vX.Y.Z dist/DarkwoodMP-win.zip dist/DarkwoodMP-win_x86.zip dist/DarkwoodMP-linux.zip --title "DarkwoodMP X.Y.Z" --notes-file NOTES --latest`. Write the notes like the previous release's (`gh release view` it): the intro line with README and CHANGELOG links, which versions it plays with, the zip list, the BepInEx and licenses line, what's new or fixed for players, and honestly how it was tested.
1. **Close the fixed issues** (always; the user asked for this): `gh issue close N --comment "Fixed in X.Y.Z (protocol N, tag vX.Y.Z). Tested with ..."` for every GitHub issue the release fixes, saying honestly how it was tested.
1. **Tell the user** the release link, which zips friends need, and whether the protocol changed (everyone must update).
