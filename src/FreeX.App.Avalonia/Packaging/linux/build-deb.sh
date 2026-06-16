#!/usr/bin/env bash
# Build a Debian package (.deb) for FreeX from a self-contained .NET publish directory.
# This is the distro-native install option alongside the relocatable tarball and AppImage
# (comparable to the Windows MSIX). Requires dpkg-deb (Linux).
#
# Usage:
#   build-deb.sh --runtime linux-x64 --published <dir> --version 0.1.0 --output <dir>
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
  echo "Usage: build-deb.sh --runtime <rid> --published <dir> --version <v> --output <dir>" >&2
  exit 2
fi

if [[ ! -x "$published/FreeX" ]]; then
  echo "Published directory '$published' does not contain an executable FreeX apphost." >&2
  exit 1
fi

case "$runtime" in
  linux-x64) deb_arch="amd64";;
  linux-arm64) deb_arch="arm64";;
  *) echo "Unsupported runtime for .deb: $runtime" >&2; exit 1;;
esac

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
app_id="io.github.tony-xmelon.freex"
pkg_root="$output/freex_${version}_${deb_arch}"

rm -rf "$pkg_root"
mkdir -p "$pkg_root/DEBIAN"
mkdir -p "$pkg_root/usr/lib/freex"
mkdir -p "$pkg_root/usr/bin"
mkdir -p "$pkg_root/usr/share/applications"
mkdir -p "$pkg_root/usr/share/icons/hicolor/scalable/apps"
mkdir -p "$pkg_root/usr/share/mime/packages"
mkdir -p "$pkg_root/usr/share/metainfo"

cp -a "$published/." "$pkg_root/usr/lib/freex/"
chmod +x "$pkg_root/usr/lib/freex/FreeX"

cat > "$pkg_root/usr/bin/freex" <<'LAUNCH'
#!/usr/bin/env bash
set -euo pipefail
exec /usr/lib/freex/FreeX "$@"
LAUNCH
chmod +x "$pkg_root/usr/bin/freex"

cp "$script_dir/$app_id.desktop" "$pkg_root/usr/share/applications/$app_id.desktop"
cp "$script_dir/$app_id.svg" "$pkg_root/usr/share/icons/hicolor/scalable/apps/$app_id.svg"
cp "$script_dir/$app_id.xml" "$pkg_root/usr/share/mime/packages/$app_id.xml"
cp "$script_dir/$app_id.metainfo.xml" "$pkg_root/usr/share/metainfo/$app_id.metainfo.xml"

installed_size_kb="$(du -sk "$pkg_root/usr" | cut -f1)"

cat > "$pkg_root/DEBIAN/control" <<CONTROL
Package: freex
Version: $version
Section: office
Priority: optional
Architecture: $deb_arch
Maintainer: FreeX <noreply@github.com>
Installed-Size: $installed_size_kb
Depends: libc6, libstdc++6, libfontconfig1, libice6, libsm6
Description: FreeX spreadsheet
 FreeX is a spreadsheet application. This package contains the self-contained
 Linux build of the FreeX Avalonia app; no system .NET runtime is required.
CONTROL

cat > "$pkg_root/DEBIAN/postinst" <<'POSTINST'
#!/bin/sh
set -e
update-desktop-database /usr/share/applications >/dev/null 2>&1 || true
update-mime-database /usr/share/mime >/dev/null 2>&1 || true
gtk-update-icon-cache -f -t /usr/share/icons/hicolor >/dev/null 2>&1 || true
exit 0
POSTINST
chmod 0755 "$pkg_root/DEBIAN/postinst"

cat > "$pkg_root/DEBIAN/postrm" <<'POSTRM'
#!/bin/sh
set -e
update-desktop-database /usr/share/applications >/dev/null 2>&1 || true
update-mime-database /usr/share/mime >/dev/null 2>&1 || true
gtk-update-icon-cache -f -t /usr/share/icons/hicolor >/dev/null 2>&1 || true
exit 0
POSTRM
chmod 0755 "$pkg_root/DEBIAN/postrm"

deb_path="$output/freex_${version}_${deb_arch}.deb"
rm -f "$deb_path"
dpkg-deb --root-owner-group --build "$pkg_root" "$deb_path" >/dev/null
echo "$deb_path"
