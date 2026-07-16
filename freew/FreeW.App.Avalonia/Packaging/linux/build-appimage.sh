#!/usr/bin/env bash
# FreeW compatibility entrypoint; implementation lives in tools/packaging/linux.
set -euo pipefail
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../../../../" && pwd)"
exec "$repo_root/tools/packaging/linux/package-linux.sh" \
  --operation appimage \
  --config "$repo_root/tools/packaging/linux/freew.conf" \
  --asset-dir "$script_dir" \
  "$@"
