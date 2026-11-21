# The box: Claude Code in a container

`container/box` runs Claude Code on this repo inside a rootless podman container. Claude still asks before it acts, as it does outside. The difference is that whatever it runs can only reach what was mounted into the box: the repo, the box's own home folder, this project's Claude memory and the game folder. Your real saves, SSH keys, other projects and the rest of your home aren't in it.

It is for Linux with podman (Fedora has it; elsewhere, install `podman`). It doesn't use docker, because being in the `docker` group is the same as being root.

## Using it

```
container/box                  # Claude in the repo (dev mode)
container/box --game           # Claude, and the box can run the game (game mode)
container/box shell            # a bash shell in the box (add --game for game mode)
container/box -- --resume      # anything after -- is passed on to Claude
container/box remote           # Remote Control in the background (add --game for game mode)
container/box remote-logs      # follow it; Ctrl+C leaves it running
container/box remote-stop      # stop it
container/box refresh-saves    # copy your saves into the box again
container/box build            # build the image again
container/box help
```

The first start builds the image, then installs Claude Code, the .NET 8 SDK and `ilspycmd` into the box's home. That takes a few minutes. After that, start Claude with `/login` once: the box keeps its own login, apart from yours.

## From the browser (Remote Control)

`container/box remote` starts Claude's Remote Control in the box, in a background container (`darkwood-box-remote`) that keeps running after you close the terminal. Open claude.ai/code or the Claude app, pick the box (its sessions are named after the host `darkwood-box`), and start or continue sessions there. They run in the box with the same mounts and the same mode as `container/box`. Remote Control needs a claude.ai subscription, and the box must be logged in first: start `container/box` once and run `/login`. If it stops at once, `container/box remote` prints its log. Anything after `--` goes to `claude remote-control` (for example `-- --name darkwood`).

To use one terminal session from the browser too, start it with `container/box -- --remote-control`.

## The two modes

| | dev (default) | game (`--game`) |
|---|---|---|
| The repo | read-write | read-write |
| The game folder | read-only (the build needs its DLLs) | read-write (the build deploys the mod there) |
| `dotnet build` | builds without deploying (`DeployToGame=false` is set) | builds and deploys |
| Unit tests | yes | yes |
| In-game tests (`tools/run-test.sh`) | no | yes, except `steam` (no Steam client in the box) |
| Your display, GPU, ydotool socket | no | yes |
| Your saves | not there | a copy, in the box's home |

Inside the box, `$DWMP_BOX` is `dev` or `game`.

**Game mode gives the box your X display and the ydotool socket**, so what runs in it can read your screen and keyboard and move your mouse. Use it for test runs, and dev mode the rest of the time. The game needs what it needs outside the box too: an active desktop session and `DISPLAY` (`:0` by default).

## What is kept where

- **The box's home:** `~/.local/share/darkwood-box/home` (`DARKWOOD_BOX_STATE` moves it). It is mounted at the same path as your home, so the repo's paths (`~/.dotnet`, `~/.cache/darkwood-mp`, the game folder) work unchanged. Claude's login, settings and history, .NET, NuGet's cache and the box's git settings live there. Delete the folder to start over.
- **Claude's settings:** your `~/.claude/settings.json` is copied into the box the first time, and after that the box keeps its own.
- **Memory:** this project's memory folder (`~/.claude/projects/<project>/memory`) is mounted from your home, so Claude in the box and outside it share the same notes.
- **Saves:** game mode copies the game's data folder (`~/.config/unity3d/Acid Wizard Studio/Darkwood`, saves and settings) into the box's home the first time. Your real folder is never mounted. `refresh-saves` replaces the copy with a fresh one.

## GitHub

The box has no SSH keys. Git in the box talks to GitHub over HTTPS (the repo's `git@github.com:` remote is rewritten to HTTPS) with a token, through `gh`. Your git name and email are copied from the repo's git settings.

- **Recommended:** a fine-grained token limited to this repository (github.com, Settings, Developer settings, Fine-grained tokens: Contents and Pull requests, read and write). Save it in `~/.config/darkwood-box/gh-token` (`chmod 600` it). `BOX_GH_TOKEN_FILE` points somewhere else.
- **Without that file,** the box borrows your `gh` login's token (`gh auth token`) and says so when it starts. That token reaches every repository you can.
- With neither, the box can commit but not push.

## Notes

- The repo is the same folder inside and outside the box. Don't run Claude on it in both at once.
- SELinux labelling is off for the box's mounts (`--security-opt label=disable`), so no host files get relabelled. The box has the internet, but not your machine's localhost services.
- The image is built again on its own when `Containerfile` or `init.sh` changes.
