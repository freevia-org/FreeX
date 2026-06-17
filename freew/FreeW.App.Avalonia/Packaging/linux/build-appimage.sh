#!/usr/bin/env bash
# Build a relocatable FreeW AppImage from a self-contained .NET publish directory.
#
# Usage:
#   build-appimage.sh --runtime linux-x64 --published <dir> --version 0.1.0 \
#       --output <dir> --appimagetool <path-to-appimagetool>
#
# appimagetool is not downloaded here; CI fetches the architecture-appropriate release
# and passes its path. ARCH is derived from the runtime identifier.
set -euo pipefail

runtime=""
published=""
version="0.1.0"
output=""
appimagetool=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --runtime) runtime="$2"; shift 2;;
    --published) published="$2"; shift 2;;
    --version) version="$2"; shift 2;;
    --output) output="$2"; shift 2;;
    --appimagetool) appimagetool="$2"; shift 2;;
    *) echo "Unknown argument: $1" >&2; exit 2;;
  esac
done

if [[ -z "$runtime" || -z "$published" || -z "$output" || -z "$appimagetool" ]]; then
  echo "Usage: build-appimage.sh --runtime <rid> --published <dir> --version <v> --output <dir> --appimagetool <path>" >&2
  exit 2
fi

if [[ ! -x "$published/FreeW" ]]; then
  echo "Published directory '$published' does not contain an executable FreeW apphost." >&2
  exit 1
fi

case "$runtime" in
  linux-x64) arch="x86_64";;
  linux-arm64) arch="aarch64";;
  *) echo "Unsupported runtime for AppImage: $runtime" >&2; exit 1;;
esac

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
app_id="io.github.tony-xmelon.freew"
appdir="$output/FreeW-$runtime.AppDir"

rm -rf "$appdir"
mkdir -p "$appdir/usr/lib/freew"
mkdir -p "$appdir/usr/bin"
mkdir -p "$appdir/usr/share/applications"
mkdir -p "$appdir/usr/share/icons/hicolor/scalable/apps"
mkdir -p "$appdir/usr/share/metainfo"

cp -a "$published/." "$appdir/usr/lib/freew/"
chmod +x "$appdir/usr/lib/freew/FreeW"

cat > "$appdir/usr/bin/freew" <<'LAUNCH'
#!/usr/bin/env bash
set -euo pipefail
here="$(cd "$(dirname "$(readlink -f "${BASH_SOURCE[0]}")")" && pwd)"
exec "$here/../lib/freew/FreeW" "$@"
LAUNCH
chmod +x "$appdir/usr/bin/freew"

# AppImage requires the desktop entry and icon at the AppDir root as well.
cp "$script_dir/$app_id.desktop" "$appdir/usr/share/applications/$app_id.desktop"
cp "$script_dir/$app_id.desktop" "$appdir/$app_id.desktop"
cp "$script_dir/$app_id.svg" "$appdir/usr/share/icons/hicolor/scalable/apps/$app_id.svg"
cp "$script_dir/$app_id.svg" "$appdir/$app_id.svg"
ln -sf "$app_id.svg" "$appdir/.DirIcon"
cp "$script_dir/$app_id.metainfo.xml" "$appdir/usr/share/metainfo/$app_id.metainfo.xml"

cat > "$appdir/AppRun" <<'APPRUN'
#!/usr/bin/env bash
set -euo pipefail
here="$(cd "$(dirname "$(readlink -f "${BASH_SOURCE[0]}")")" && pwd)"
exec "$here/usr/lib/freew/FreeW" "$@"
APPRUN
chmod +x "$appdir/AppRun"

mkdir -p "$output"
appimage="$output/FreeW-$version-$arch.AppImage"
rm -f "$appimage"
# Send appimagetool's own output to stderr so this script's stdout is only the path.
ARCH="$arch" "$appimagetool" "$appdir" "$appimage" 1>&2
echo "$appimage"
