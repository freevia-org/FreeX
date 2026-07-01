#!/usr/bin/env bash
# Assemble a self-contained FreeW Linux distribution tarball from a published
# .NET runtime directory, following freedesktop / XDG conventions. Mirrors the FreeX
# packaging script; FreeW opens standard Word and OpenDocument files so it
# relies on shared-mime-info definitions rather than registering custom MIME types.
#
# Usage:
#   package-linux-app.sh --runtime linux-x64 --published <dir> --version 0.1.0 --output <dir>
set -euo pipefail

runtime=""
published=""
version="0.1.0"
output=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --runtime) runtime="$2"; shift 2;;
    --published) published="$2"; shift 2;;
    --version) version="$2"; shift 2;;
    --output) output="$2"; shift 2;;
    *) echo "Unknown argument: $1" >&2; exit 2;;
  esac
done

if [[ -z "$runtime" || -z "$published" || -z "$output" ]]; then
  echo "Usage: package-linux-app.sh --runtime <rid> --published <dir> --version <v> --output <dir>" >&2
  exit 2
fi

if [[ ! -x "$published/FreeW" ]]; then
  echo "Published directory '$published' does not contain an executable FreeW apphost." >&2
  exit 1
fi

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
app_id="io.github.tony-xmelon.freew"
stage_name="freew-$version-$runtime"
stage="$output/$stage_name"

rm -rf "$stage"
mkdir -p "$stage/lib/freew"
mkdir -p "$stage/bin"
mkdir -p "$stage/share/applications"
mkdir -p "$stage/share/icons/hicolor/scalable/apps"
mkdir -p "$stage/share/metainfo"

# Runtime payload.
cp -a "$published/." "$stage/lib/freew/"
chmod +x "$stage/lib/freew/FreeW"

# Relocatable launcher wrapper resolved relative to its own location.
cat > "$stage/bin/freew" <<'LAUNCH'
#!/usr/bin/env bash
set -euo pipefail
here="$(cd "$(dirname "$(readlink -f "${BASH_SOURCE[0]}")")" && pwd)"
exec "$here/../lib/freew/FreeW" "$@"
LAUNCH
chmod +x "$stage/bin/freew"

# Desktop integration assets.
cp "$script_dir/$app_id.desktop" "$stage/share/applications/$app_id.desktop"
cp "$script_dir/$app_id.svg" "$stage/share/icons/hicolor/scalable/apps/$app_id.svg"
cp "$script_dir/$app_id.metainfo.xml" "$stage/share/metainfo/$app_id.metainfo.xml"

# install.sh — register into a prefix (default per-user).
cat > "$stage/install.sh" <<'INSTALL'
#!/usr/bin/env bash
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
prefix="${1:-$HOME/.local}"
app_id="io.github.tony-xmelon.freew"

mkdir -p "$prefix/lib/freew" "$prefix/bin" \
  "$prefix/share/applications" \
  "$prefix/share/icons/hicolor/scalable/apps" \
  "$prefix/share/metainfo"

cp -a "$here/lib/freew/." "$prefix/lib/freew/"
chmod +x "$prefix/lib/freew/FreeW"
ln -sf "$prefix/lib/freew/FreeW" "$prefix/bin/freew"
cp "$here/share/applications/$app_id.desktop" "$prefix/share/applications/$app_id.desktop"
cp "$here/share/icons/hicolor/scalable/apps/$app_id.svg" "$prefix/share/icons/hicolor/scalable/apps/$app_id.svg"
cp "$here/share/metainfo/$app_id.metainfo.xml" "$prefix/share/metainfo/$app_id.metainfo.xml"

update-desktop-database "$prefix/share/applications" >/dev/null 2>&1 || true
gtk-update-icon-cache -f -t "$prefix/share/icons/hicolor" >/dev/null 2>&1 || true

echo "FreeW installed to $prefix. Ensure $prefix/bin is on PATH, then launch 'freew'."
INSTALL
chmod +x "$stage/install.sh"

# uninstall.sh — remove what install.sh added.
cat > "$stage/uninstall.sh" <<'UNINSTALL'
#!/usr/bin/env bash
set -euo pipefail
prefix="${1:-$HOME/.local}"
app_id="io.github.tony-xmelon.freew"

rm -rf "$prefix/lib/freew"
rm -f "$prefix/bin/freew"
rm -f "$prefix/share/applications/$app_id.desktop"
rm -f "$prefix/share/icons/hicolor/scalable/apps/$app_id.svg"
rm -f "$prefix/share/metainfo/$app_id.metainfo.xml"

update-desktop-database "$prefix/share/applications" >/dev/null 2>&1 || true
gtk-update-icon-cache -f -t "$prefix/share/icons/hicolor" >/dev/null 2>&1 || true

echo "FreeW removed from $prefix."
UNINSTALL
chmod +x "$stage/uninstall.sh"

cp "$script_dir/README.md" "$stage/README.md" 2>/dev/null || true

tarball="$output/$stage_name.tar.gz"
rm -f "$tarball"
tar -C "$output" -czf "$tarball" "$stage_name"
echo "$tarball"
