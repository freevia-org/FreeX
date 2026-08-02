#!/usr/bin/env bash
set -euo pipefail

output="${FREEP_PORTABLE_PRINTER_OUTPUT:-/work/portable-printer}"
mkdir -p "$output"
printf '%q ' lpstat "$@" >> "$output/lpstat-calls.txt"
printf '\n' >> "$output/lpstat-calls.txt"

case " $* " in
    *" -p "*)
        printf 'printer FreeP-Default is idle.\n'
        printf 'printer FreeP-Secondary is idle.\n'
        ;;
    *" -d "*) printf 'system default destination: FreeP-Default\n' ;;
    *)
        printf 'FreeP fake lpstat received unsupported arguments: %s\n' "$*" >&2
        exit 2
        ;;
esac
