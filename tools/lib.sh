# Helpers shared by the shell tools (sourced by tools/run-test.sh, tools/setup-client-game.sh and pack.sh).
# Linux only: it needs GNU realpath.

# refuse_root: stop when run as root. The tools only ever touch your own files, and as root a mistake in a path
# could reach anyone's.
refuse_root() {
  [ "$(id -u)" != 0 ] || { echo "FAIL: don't run this as root (or with sudo): run it as yourself" >&2; exit 1; }
}

_safe_rm_refuse() { echo "FAIL: safe_rm refused to delete '$2' (allowed folder '$1'): $3" >&2; exit 1; }

# is_under PATH DIR: whether PATH is strictly inside DIR, both already resolved (realpath).
is_under() { case "$1" in "${2%/}"/?*) return 0 ;; *) return 1 ;; esac; }

# safe_rm ROOT PATH...: deletes each PATH (a folder with everything in it, a file or a link), but only when it is
# strictly inside ROOT. It exits the script (status 1) instead of deleting when: it runs as root; ROOT is empty,
# "/", $HOME, a folder that holds $HOME, or is itself a link (links in the folders above it are fine); a PATH is
# empty, ends in "." or "..", is "/" or $HOME, or isn't inside ROOT once the links in its folders are resolved.
# A PATH that is a link is deleted as a link: what it points to is never touched. A PATH that doesn't exist is
# skipped. Wildcards are the caller's: expand them and pass each entry (or use safe_rm_in).
safe_rm() {
  local root="${1:-}" home rr p base parent rp
  [ "$(id -u)" != 0 ] || _safe_rm_refuse "$root" "${2:-}" "running as root"
  [ $# -ge 2 ] || _safe_rm_refuse "$root" "" "no path given"
  shift
  [ -n "$root" ] || _safe_rm_refuse "$root" "${1:-}" "the allowed folder is empty"
  home="$(realpath -m -- "${HOME:-/}")"
  rr="$(realpath -m -- "$root")"
  [ "$rr" != / ] && [ "$rr" != "$home" ] || _safe_rm_refuse "$root" "${1:-}" "the allowed folder is / or your home"
  ! is_under "$home" "$rr" || _safe_rm_refuse "$root" "${1:-}" "the allowed folder holds your home"
  [ ! -L "${root%/}" ] || _safe_rm_refuse "$root" "${1:-}" "the allowed folder is a link"
  local -a ok=() # every path is checked before anything is deleted: one bad path deletes nothing
  for p in "$@"; do
    [ -n "$p" ] || _safe_rm_refuse "$root" "$p" "empty path"
    base="$(basename -- "$p")"
    case "$base" in .|..|/) _safe_rm_refuse "$root" "$p" "the path ends in '.', '..' or is /" ;; esac
    # The folder it is in, with every link resolved; the last part itself is not followed (a link stays a link).
    parent="$(realpath -m -- "$(dirname -- "$p")")"
    rp="${parent%/}/$base"
    [ "$rp" != / ] && [ "$rp" != "$home" ] || _safe_rm_refuse "$root" "$p" "the path is / or your home"
    is_under "$rp" "$rr" || _safe_rm_refuse "$root" "$p" "it isn't inside the allowed folder ($rp)"
    ok+=("$rp")
  done
  for rp in "${ok[@]}"; do
    if [ -L "$rp" ]; then rm -f -- "$rp"
    elif [ -e "$rp" ]; then rm -rf -- "$rp"
    fi
  done
}

# safe_rm_in DIR: deletes everything inside DIR (dotfiles too), but not DIR itself, with safe_rm limited to DIR.
safe_rm_in() {
  local d="${1:-}" f
  [ -n "$d" ] || _safe_rm_refuse "" "" "safe_rm_in: no folder given"
  [ -d "$d" ] || return 0
  for f in "$d"/* "$d"/.*; do
    case "${f##*/}" in .|..) continue ;; esac
    [ -e "$f" ] || [ -L "$f" ] || continue # an unmatched pattern stays as it is
    safe_rm "$d" "$f"
  done
}
