#!/bin/bash
# Runs inside the box each time it starts (container/box starts it). The
# first time, it installs Claude, the .NET 8 SDK and ilspycmd into the box's
# home, which is kept between runs. Then it starts Claude, or a shell.
#
# Usage (from container/box): box-init claude [ARGS...] | box-init shell
set -euo pipefail

export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.local/bin:$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1

say() { echo "box: $*" >&2; }

if ! [ -x "$HOME/.local/bin/claude" ]; then
  say "installing Claude Code (first start)"
  curl -fsSL https://claude.ai/install.sh | bash
fi

if ! [ -x "$HOME/.dotnet/dotnet" ]; then
  say "installing the .NET 8 SDK into ~/.dotnet (first start)"
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  bash /tmp/dotnet-install.sh --channel 8.0 --install-dir "$HOME/.dotnet"
  rm -f /tmp/dotnet-install.sh
fi

if ! [ -x "$HOME/.dotnet/tools/ilspycmd" ]; then
  say "installing ilspycmd (first start)"
  dotnet tool install -g ilspycmd --version 8.2.0.7535 >/dev/null ||
    say "couldn't install ilspycmd; the darkwood-game-code skill needs it"
fi

# The box's own git settings: the name and email from the host, and GitHub
# over HTTPS with the token, so the repo's SSH remote works without keys.
if [ -n "${BOX_GIT_NAME:-}" ]; then
  git config --global user.name "$BOX_GIT_NAME"
fi

if [ -n "${BOX_GIT_EMAIL:-}" ]; then
  git config --global user.email "$BOX_GIT_EMAIL"
fi

git config --global --unset-all url."https://github.com/".insteadOf || true
git config --global --add url."https://github.com/".insteadOf "git@github.com:"
git config --global --add url."https://github.com/".insteadOf \
  "ssh://git@github.com/"
if [ -n "${GH_TOKEN:-}" ]; then
  gh auth setup-git >/dev/null 2>&1 || say "gh auth setup-git failed"
fi

# Shell settings for `container/box shell` and for Claude's own shells.
if ! grep -q "box-init paths" "$HOME/.bashrc" 2>/dev/null; then
  cat >>"$HOME/.bashrc" <<'RC'
# box-init paths
export DOTNET_ROOT="$HOME/.dotnet" DOTNET_CLI_TELEMETRY_OPTOUT=1
export PATH="$HOME/.local/bin:$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
PS1='[box:$DWMP_BOX \W]\$ '
RC
fi

what="${1:-claude}"
shift || true
case "$what" in
  claude)
    say "mode: ${DWMP_BOX:-unknown} (container/README.md says what that allows)"
    exec claude "$@"
    ;;
  remote)
    # Remote Control: sessions on this box, started and used from
    # claude.ai/code or the Claude app. It needs Claude logged in (/login).
    say "mode: ${DWMP_BOX:-unknown}, Remote Control"
    exec claude remote-control "$@"
    ;;
  shell)
    exec bash "$@"
    ;;
  *)
    exec "$what" "$@"
    ;;
esac
