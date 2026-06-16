#!/usr/bin/env bash
# Assemble a self-contained FreeX Linux distribution tarball from a published
# .NET runtime directory. The layout follows the freedesktop / XDG conventions so
# the bundled install.sh can drop FreeX into a prefix (default: per-user ~/.local)
# and register the desktop entry, icon, and MIME type.
#
# Usage:
#   package-linux-app.sh --runtime linux-x64 --published <dir> --version 0.1.0 --output <dir>
#
# The published directory must already contain the self-contained publish output,
# including the native apphost named "FreeX".
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

if [[ ! -x "$published/FreeX" ]]; then
  echo "Published directory '$published' does not contain an executable FreeX apphost." >&2
  exit 1
fi

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
app_id="io.github.tony-xmelon.freex"
stage_name="freex-$version-$runtime"
stage="$output/$stage_name"

rm -rf "$stage"
mkdir -p "$stage/lib/freex"
mkdir -p "$stage/bin"
mkdir -p "$stage/share/applications"
mkdir -p "$stage/share/icons/hicolor/scalable/apps"
mkdir -p "$stage/share/mime/packages"
mkdir -p "$stage/share/metainfo"

# Runtime payload.
cp -a "$published/." "$stage/lib/freex/"
chmod +x "$stage/lib/freex/FreeX"

# Launcher wrapper resolved relative to its own location so the tarball is
# relocatable before install.
cat > "$stage/bin/freex" <<'LAUNCH'
#!/usr/bin/env bash
set -euo pipefail
here="$(cd "$(dirname "$(readlink -f "${BASH_SOURCE[0]}")")" && pwd)"
exec "$here/../lib/freex/FreeX" "$@"
LAUNCH
chmod +x "$stage/bin/freex"

# Desktop integration assets.
cp "$script_dir/$app_id.desktop" "$stage/share/applications/$app_id.desktop"
cp "$script_dir/$app_id.svg" "$stage/share/icons/hicolor/scalable/apps/$app_id.svg"
cp "$script_dir/$app_id.xml" "$stage/share/mime/packages/$app_id.xml"
cp "$script_dir/$app_id.metainfo.xml" "$stage/share/metainfo/$app_id.metainfo.xml"

# install.sh — register into a prefix (default per-user).
cat > "$stage/install.sh" <<'INSTALL'
#!/usr/bin/env bash
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
prefix="${1:-$HOME/.local}"
app_id="io.github.tony-xmelon.freex"

mkdir -p "$prefix/lib/freex" "$prefix/bin" \
  "$prefix/share/applications" \
  "$prefix/share/icons/hicolor/scalable/apps" \
  "$prefix/share/mime/packages" \
  "$prefix/share/metainfo"

cp -a "$here/lib/freex/." "$prefix/lib/freex/"
chmod +x "$prefix/lib/freex/FreeX"
ln -sf "$prefix/lib/freex/FreeX" "$prefix/bin/freex"
cp "$here/share/applications/$app_id.desktop" "$prefix/share/applications/$app_id.desktop"
cp "$here/share/icons/hicolor/scalable/apps/$app_id.svg" "$prefix/share/icons/hicolor/scalable/apps/$app_id.svg"
cp "$here/share/mime/packages/$app_id.xml" "$prefix/share/mime/packages/$app_id.xml"
cp "$here/share/metainfo/$app_id.metainfo.xml" "$prefix/share/metainfo/$app_id.metainfo.xml"

update-desktop-database "$prefix/share/applications" >/dev/null 2>&1 || true
update-mime-database "$prefix/share/mime" >/dev/null 2>&1 || true
gtk-update-icon-cache -f -t "$prefix/share/icons/hicolor" >/dev/null 2>&1 || true

echo "FreeX installed to $prefix. Ensure $prefix/bin is on PATH, then launch 'freex'."
INSTALL
chmod +x "$stage/install.sh"

# uninstall.sh — remove what install.sh added.
cat > "$stage/uninstall.sh" <<'UNINSTALL'
#!/usr/bin/env bash
set -euo pipefail
prefix="${1:-$HOME/.local}"
app_id="io.github.tony-xmelon.freex"

rm -rf "$prefix/lib/freex"
rm -f "$prefix/bin/freex"
rm -f "$prefix/share/applications/$app_id.desktop"
rm -f "$prefix/share/icons/hicolor/scalable/apps/$app_id.svg"
rm -f "$prefix/share/mime/packages/$app_id.xml"
rm -f "$prefix/share/metainfo/$app_id.metainfo.xml"

update-desktop-database "$prefix/share/applications" >/dev/null 2>&1 || true
update-mime-database "$prefix/share/mime" >/dev/null 2>&1 || true
gtk-update-icon-cache -f -t "$prefix/share/icons/hicolor" >/dev/null 2>&1 || true

echo "FreeX removed from $prefix."
UNINSTALL
chmod +x "$stage/uninstall.sh"

cp "$script_dir/README.md" "$stage/README.md" 2>/dev/null || true

tarball="$output/$stage_name.tar.gz"
rm -f "$tarball"
tar -C "$output" -czf "$tarball" "$stage_name"
echo "$tarball"
