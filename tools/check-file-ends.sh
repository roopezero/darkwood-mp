#!/bin/bash
# tools/check-file-ends.sh
# Fails unless every tracked text file ends with one line break: a last line
# with a line break, and no blank lines after it. The build checks C# files
# (rule DW0008); this covers the rest (docs, scripts, Python tests, project
# files). LICENSES/ holds other projects' licence texts, kept as they came.
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"
bad=0
while IFS= read -r -d '' f; do
    case "$f" in
        LICENSES/*) continue ;;
    esac
    [ -s "$f" ] || continue
    if [ -n "$(tail -c 1 "$f")" ]; then
        echo "$f: the last line has no line break"
        bad=1
    elif [ -z "$(tail -n 1 "$f" | tr -d ' \t\r')" ]; then
        echo "$f: blank lines after the last line"
        bad=1
    fi
done < <(git grep -I -l -z '')
if [ $bad -ne 0 ]; then
    echo "FAIL: end these files with one line break"
fi
exit $bad
