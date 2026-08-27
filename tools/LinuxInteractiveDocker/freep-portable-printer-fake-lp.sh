#!/usr/bin/env bash
set -euo pipefail

output="${FREEP_PORTABLE_PRINTER_OUTPUT:-/work/portable-printer}"
pdf_path="${@: -1}"
mkdir -p "$output"
if [[ ! -f "$pdf_path" ]]; then
    printf 'FreeP fake lp received no PDF path: %s\n' "$pdf_path" >&2
    exit 2
fi

python3 - "$output/last-invocation.json" "$@" <<'PY'
import json
import sys

path, *args = sys.argv[1:]
with open(path, "w", encoding="utf-8") as handle:
    json.dump({"executable": "lp", "arguments": args, "pdfPath": args[-1]}, handle, indent=2)
    handle.write("\n")
PY
cp -- "$pdf_path" "$output/last-submitted.pdf"
printf 'request id is FreeP-Secondary-1 (1 file(s))\n'
