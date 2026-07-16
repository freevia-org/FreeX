#!/usr/bin/env bash
# Shared Linux packaging implementation for FreeX and FreeW.
# Product-specific values are read from a data-only config; config text is never executed.
set -euo pipefail

usage() {
  echo "Usage: package-linux.sh --operation <tarball|appimage|deb> --config <file> --asset-dir <dir> --runtime <rid> --published <dir> --version <v> --output <dir> [--appimagetool <path>] [--dry-run]" >&2
}

die_usage() {
  echo "$1" >&2
  usage
  exit 2
}

die_config() {
  echo "Invalid packaging config: $1" >&2
  exit 1
}

operation=""
config=""
asset_dir=""
runtime=""
published=""
version="0.1.0"
output=""
appimagetool=""
dry_run=false

while [[ $# -gt 0 ]]; do
  case "$1" in
    --operation)
      [[ $# -ge 2 ]] || die_usage "Missing value for --operation."
      operation="$2"
      shift 2
      ;;
    --config)
      [[ $# -ge 2 ]] || die_usage "Missing value for --config."
      config="$2"
      shift 2
      ;;
    --asset-dir)
      [[ $# -ge 2 ]] || die_usage "Missing value for --asset-dir."
      asset_dir="$2"
      shift 2
      ;;
    --runtime)
      [[ $# -ge 2 ]] || die_usage "Missing value for --runtime."
      runtime="$2"
      shift 2
      ;;
    --published)
      [[ $# -ge 2 ]] || die_usage "Missing value for --published."
      published="$2"
      shift 2
      ;;
    --version)
      [[ $# -ge 2 ]] || die_usage "Missing value for --version."
      version="$2"
      shift 2
      ;;
    --output)
      [[ $# -ge 2 ]] || die_usage "Missing value for --output."
      output="$2"
      shift 2
      ;;
    --appimagetool)
      [[ $# -ge 2 ]] || die_usage "Missing value for --appimagetool."
      appimagetool="$2"
      shift 2
      ;;
    --dry-run)
      dry_run=true
      shift
      ;;
    *)
      die_usage "Unknown argument: $1"
      ;;
  esac
done

[[ "$operation" == "tarball" || "$operation" == "appimage" || "$operation" == "deb" ]] || die_usage "Unsupported packaging operation: $operation"
[[ -n "$config" && -n "$asset_dir" && -n "$runtime" && -n "$published" && -n "$output" ]] || die_usage "Missing required packaging argument."
if [[ "$operation" == "appimage" && -z "$appimagetool" ]]; then
  die_usage "Missing required argument: --appimagetool"
fi

validate_path_argument() {
  local value="$1"
  local label="$2"
  [[ ! "$value" =~ [[:cntrl:]] ]] || die_usage "$label must not contain control characters."
}

validate_component_argument() {
  local value="$1"
  local label="$2"
  [[ "$value" =~ ^[A-Za-z0-9][A-Za-z0-9._-]*$ ]] || die_usage "$label must be a single safe path component."
}

# Validate user-controlled path arguments and output-name components before any config or filesystem use.
validate_path_argument "$config" "--config"
validate_path_argument "$asset_dir" "--asset-dir"
validate_path_argument "$published" "--published"
validate_path_argument "$output" "--output"
[[ -z "$appimagetool" ]] || validate_path_argument "$appimagetool" "--appimagetool"
validate_component_argument "$runtime" "--runtime"
validate_component_argument "$version" "--version"

declare -A config_values=()

load_config() {
  local line key value

  [[ -f "$config" ]] || die_config "config file not found: $config"
  while IFS= read -r line || [[ -n "$line" ]]; do
    [[ -z "$line" || "$line" == \#* ]] && continue
    [[ "$line" == *=* ]] || die_config "expected key=value: $line"
    key="${line%%=*}"
    value="${line#*=}"
    [[ "$key" =~ ^[a-z][a-z0-9_]*$ ]] || die_config "invalid key: $key"
    [[ ! "$value" =~ [[:cntrl:]] ]] || die_config "control characters are not allowed in $key"
    case "$key" in
      product_key|display_name|binary_name|launcher_name|library_dir|app_id|appimage_prefix|stage_prefix|package_name|maintainer|description|description_long_1|description_long_2|mime_asset|cache_mime)
        config_values["$key"]="$value"
        ;;
      *)
        die_config "unknown key: $key"
        ;;
    esac
  done < "$config"
}

validate_config() {
  local key value
  local required_keys=(product_key display_name binary_name launcher_name library_dir app_id appimage_prefix stage_prefix package_name maintainer description description_long_1 description_long_2 cache_mime)

  for key in "${required_keys[@]}"; do
    [[ -n "${config_values[$key]-}" ]] || die_config "missing value for $key"
  done

  product_key="${config_values[product_key]}"
  display_name="${config_values[display_name]}"
  binary_name="${config_values[binary_name]}"
  launcher_name="${config_values[launcher_name]}"
  library_dir="${config_values[library_dir]}"
  app_id="${config_values[app_id]}"
  appimage_prefix="${config_values[appimage_prefix]}"
  stage_prefix="${config_values[stage_prefix]}"
  package_name="${config_values[package_name]}"
  maintainer="${config_values[maintainer]}"
  description="${config_values[description]}"
  description_long_1="${config_values[description_long_1]}"
  description_long_2="${config_values[description_long_2]}"
  mime_asset="${config_values[mime_asset]-}"
  cache_mime="${config_values[cache_mime]}"

  for key in product_key binary_name launcher_name library_dir app_id appimage_prefix stage_prefix package_name; do
    value="${config_values[$key]}"
    [[ "$value" =~ ^[A-Za-z0-9][A-Za-z0-9._-]*$ ]] || die_config "$key must be a single safe path component"
  done
  if [[ -n "$mime_asset" && ! "$mime_asset" =~ ^[A-Za-z0-9][A-Za-z0-9._-]*$ ]]; then
    die_config "mime_asset must be a single safe filename"
  fi
  [[ "$cache_mime" == "true" || "$cache_mime" == "false" ]] || die_config "cache_mime must be true or false"
}

check_published() {
  if [[ ! -x "$published/$binary_name" ]]; then
    echo "Published directory '$published' does not contain an executable $binary_name apphost." >&2
    exit 1
  fi
}

make_share_dirs() {
  local root="$1"
  mkdir -p "$root/applications" "$root/icons/hicolor/scalable/apps" "$root/metainfo"
  if [[ -n "$mime_asset" ]]; then
    mkdir -p "$root/mime/packages"
  fi
}

copy_share_assets() {
  local share_root="$1"
  cp "$asset_dir/$app_id.desktop" "$share_root/applications/$app_id.desktop"
  cp "$asset_dir/$app_id.svg" "$share_root/icons/hicolor/scalable/apps/$app_id.svg"
  if [[ -n "$mime_asset" ]]; then
    cp "$asset_dir/$mime_asset" "$share_root/mime/packages/$mime_asset"
  fi
  cp "$asset_dir/$app_id.metainfo.xml" "$share_root/metainfo/$app_id.metainfo.xml"
}

write_relocatable_launcher() {
  local path="$1"
  printf '%s\n' \
    '#!/usr/bin/env bash' \
    'set -euo pipefail' \
    'here="$(cd "$(dirname "$(readlink -f "${BASH_SOURCE[0]}")")" && pwd)"' \
    "exec \"\$here/../lib/$library_dir/$binary_name\" \"\$@\"" > "$path"
  chmod +x "$path"
}

write_absolute_launcher() {
  local path="$1"
  printf '%s\n' \
    '#!/usr/bin/env bash' \
    'set -euo pipefail' \
    "exec /usr/lib/$library_dir/$binary_name \"\$@\"" > "$path"
  chmod +x "$path"
}

write_install_script() {
  local path="$1"
  {
    printf '%s\n' '#!/usr/bin/env bash' 'set -euo pipefail'
    printf '%s\n' 'here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"' 'prefix="${1:-$HOME/.local}"'
    printf 'app_id="%s"\n' "$app_id"
    printf 'mkdir -p "$prefix/lib/%s" "$prefix/bin" \\\n' "$library_dir"
    printf '%s\n' '  "$prefix/share/applications" \'
    printf '%s\n' '  "$prefix/share/icons/hicolor/scalable/apps" \'
    if [[ -n "$mime_asset" ]]; then
      printf '%s\n' '  "$prefix/share/mime/packages" \'
    fi
    printf '%s\n' '  "$prefix/share/metainfo"'
    printf 'cp -a "$here/lib/%s/." "$prefix/lib/%s/"\n' "$library_dir" "$library_dir"
    printf 'chmod +x "$prefix/lib/%s/%s"\n' "$library_dir" "$binary_name"
    printf 'ln -sf "$prefix/lib/%s/%s" "$prefix/bin/%s"\n' "$library_dir" "$binary_name" "$launcher_name"
    printf 'cp "$here/share/applications/$app_id.desktop" "$prefix/share/applications/$app_id.desktop"\n'
    printf 'cp "$here/share/icons/hicolor/scalable/apps/$app_id.svg" "$prefix/share/icons/hicolor/scalable/apps/$app_id.svg"\n'
    if [[ -n "$mime_asset" ]]; then
      printf 'mime_asset="%s"\n' "$mime_asset"
      printf '%s\n' 'cp "$here/share/mime/packages/$mime_asset" "$prefix/share/mime/packages/$mime_asset"'
    fi
    printf 'cp "$here/share/metainfo/$app_id.metainfo.xml" "$prefix/share/metainfo/$app_id.metainfo.xml"\n'
    printf '%s\n' 'update-desktop-database "$prefix/share/applications" >/dev/null 2>&1 || true'
    if [[ "$cache_mime" == "true" ]]; then
      printf '%s\n' 'update-mime-database "$prefix/share/mime" >/dev/null 2>&1 || true'
    fi
    printf '%s\n' 'gtk-update-icon-cache -f -t "$prefix/share/icons/hicolor" >/dev/null 2>&1 || true'
    printf 'echo "%s installed to $prefix. Ensure $prefix/bin is on PATH, then launch %s."\n' "$display_name" "'$launcher_name'"
  } > "$path"
  chmod +x "$path"
}

write_uninstall_script() {
  local path="$1"
  {
    printf '%s\n' '#!/usr/bin/env bash' 'set -euo pipefail' 'prefix="${1:-$HOME/.local}"'
    printf 'app_id="%s"\n' "$app_id"
    printf 'rm -rf "$prefix/lib/%s"\n' "$library_dir"
    printf 'rm -f "$prefix/bin/%s"\n' "$launcher_name"
    printf '%s\n' 'rm -f "$prefix/share/applications/$app_id.desktop"'
    printf '%s\n' 'rm -f "$prefix/share/icons/hicolor/scalable/apps/$app_id.svg"'
    if [[ -n "$mime_asset" ]]; then
      printf 'mime_asset="%s"\n' "$mime_asset"
      printf '%s\n' 'rm -f "$prefix/share/mime/packages/$mime_asset"'
    fi
    printf '%s\n' 'rm -f "$prefix/share/metainfo/$app_id.metainfo.xml"'
    printf '%s\n' 'update-desktop-database "$prefix/share/applications" >/dev/null 2>&1 || true'
    if [[ "$cache_mime" == "true" ]]; then
      printf '%s\n' 'update-mime-database "$prefix/share/mime" >/dev/null 2>&1 || true'
    fi
    printf '%s\n' 'gtk-update-icon-cache -f -t "$prefix/share/icons/hicolor" >/dev/null 2>&1 || true'
    printf 'echo "%s removed from $prefix."\n' "$display_name"
  } > "$path"
  chmod +x "$path"
}

write_cache_script() {
  local path="$1"
  {
    printf '%s\n' '#!/bin/sh' 'set -e' 'update-desktop-database /usr/share/applications >/dev/null 2>&1 || true'
    if [[ "$cache_mime" == "true" ]]; then
      printf '%s\n' 'update-mime-database /usr/share/mime >/dev/null 2>&1 || true'
    fi
    printf '%s\n' 'gtk-update-icon-cache -f -t /usr/share/icons/hicolor >/dev/null 2>&1 || true' 'exit 0'
  } > "$path"
  chmod 0755 "$path"
}

build_tarball() {
  local stage_name="$stage_prefix-$version-$runtime"
  local stage="$output/$stage_name"
  rm -rf -- "$stage"
  mkdir -p "$stage/lib/$library_dir" "$stage/bin"
  make_share_dirs "$stage/share"
  cp -a "$published/." "$stage/lib/$library_dir/"
  chmod +x "$stage/lib/$library_dir/$binary_name"
  write_relocatable_launcher "$stage/bin/$launcher_name"
  copy_share_assets "$stage/share"
  write_install_script "$stage/install.sh"
  write_uninstall_script "$stage/uninstall.sh"
  cp "$asset_dir/README.md" "$stage/README.md" 2>/dev/null || true
  local tarball="$output/$stage_name.tar.gz"
  rm -f -- "$tarball"
  tar -C "$output" -czf "$tarball" "$stage_name"
  echo "$tarball"
}

build_appimage() {
  local arch
  case "$runtime" in
    linux-x64) arch="x86_64" ;;
    linux-arm64) arch="aarch64" ;;
    *) echo "Unsupported runtime for AppImage: $runtime" >&2; exit 1 ;;
  esac

  local appdir="$output/$appimage_prefix-$runtime.AppDir"
  rm -rf -- "$appdir"
  mkdir -p "$appdir/usr/lib/$library_dir" "$appdir/usr/bin" "$appdir/usr/share/applications" \
    "$appdir/usr/share/icons/hicolor/scalable/apps" "$appdir/usr/share/metainfo"
  if [[ -n "$mime_asset" ]]; then
    mkdir -p "$appdir/usr/share/mime/packages"
  fi
  cp -a "$published/." "$appdir/usr/lib/$library_dir/"
  chmod +x "$appdir/usr/lib/$library_dir/$binary_name"
  write_relocatable_launcher "$appdir/usr/bin/$launcher_name"
  cp "$asset_dir/$app_id.desktop" "$appdir/usr/share/applications/$app_id.desktop"
  cp "$asset_dir/$app_id.desktop" "$appdir/$app_id.desktop"
  cp "$asset_dir/$app_id.svg" "$appdir/usr/share/icons/hicolor/scalable/apps/$app_id.svg"
  cp "$asset_dir/$app_id.svg" "$appdir/$app_id.svg"
  ln -sf "$app_id.svg" "$appdir/.DirIcon"
  if [[ -n "$mime_asset" ]]; then
    cp "$asset_dir/$mime_asset" "$appdir/usr/share/mime/packages/$mime_asset"
  fi
  cp "$asset_dir/$app_id.metainfo.xml" "$appdir/usr/share/metainfo/$app_id.metainfo.xml"
  printf '%s\n' \
    '#!/usr/bin/env bash' \
    'set -euo pipefail' \
    'here="$(cd "$(dirname "$(readlink -f "${BASH_SOURCE[0]}")")" && pwd)"' \
    "exec \"\$here/usr/lib/$library_dir/$binary_name\" \"\$@\"" > "$appdir/AppRun"
  chmod +x "$appdir/AppRun"
  mkdir -p "$output"
  local appimage="$output/$appimage_prefix-$version-$arch.AppImage"
  rm -f -- "$appimage"
  ARCH="$arch" "$appimagetool" "$appdir" "$appimage" 1>&2
  echo "$appimage"
}

build_deb() {
  local deb_arch
  case "$runtime" in
    linux-x64) deb_arch="amd64" ;;
    linux-arm64) deb_arch="arm64" ;;
    *) echo "Unsupported runtime for .deb: $runtime" >&2; exit 1 ;;
  esac

  local pkg_root="$output/${package_name}_${version}_${deb_arch}"
  rm -rf -- "$pkg_root"
  mkdir -p "$pkg_root/DEBIAN" "$pkg_root/usr/lib/$library_dir" "$pkg_root/usr/bin"
  make_share_dirs "$pkg_root/usr/share"
  cp -a "$published/." "$pkg_root/usr/lib/$library_dir/"
  chmod +x "$pkg_root/usr/lib/$library_dir/$binary_name"
  write_absolute_launcher "$pkg_root/usr/bin/$launcher_name"
  copy_share_assets "$pkg_root/usr/share"
  local installed_size_kb
  installed_size_kb="$(du -sk "$pkg_root/usr" | cut -f1)"
  {
    printf 'Package: %s\n' "$package_name"
    printf 'Version: %s\n' "$version"
    printf '%s\n' 'Section: office' 'Priority: optional'
    printf 'Architecture: %s\n' "$deb_arch"
    printf 'Maintainer: %s\n' "$maintainer"
    printf 'Installed-Size: %s\n' "$installed_size_kb"
    printf '%s\n' 'Depends: libc6, libstdc++6, libfontconfig1, libice6, libsm6'
    printf 'Description: %s\n' "$description"
    printf ' %s\n' "$description_long_1" "$description_long_2"
  } > "$pkg_root/DEBIAN/control"
  write_cache_script "$pkg_root/DEBIAN/postinst"
  write_cache_script "$pkg_root/DEBIAN/postrm"
  local deb_path="$output/${package_name}_${version}_${deb_arch}.deb"
  rm -f -- "$deb_path"
  dpkg-deb --root-owner-group --build "$pkg_root" "$deb_path" >/dev/null
  echo "$deb_path"
}

load_config
validate_config

validate_operation_runtime() {
  case "$operation:$runtime" in
    appimage:linux-x64|appimage:linux-arm64|deb:linux-x64|deb:linux-arm64|tarball:*) ;;
    appimage:*|deb:*) echo "Unsupported runtime for $operation: $runtime" >&2; exit 1 ;;
  esac
}

print_dry_run() {
  local stage_name="$stage_prefix-$version-$runtime"
  local output_name
  case "$operation" in
    tarball) output_name="$stage_name.tar.gz" ;;
    appimage)
      local arch
      case "$runtime" in
        linux-x64) arch="x86_64" ;;
        linux-arm64) arch="aarch64" ;;
      esac
      output_name="$appimage_prefix-$version-$arch.AppImage"
      ;;
    deb)
      local deb_arch
      case "$runtime" in
        linux-x64) deb_arch="amd64" ;;
        linux-arm64) deb_arch="arm64" ;;
      esac
      output_name="${package_name}_${version}_${deb_arch}.deb"
      ;;
  esac
  printf '%s\n' \
    "operation=$operation" \
    "product_key=$product_key" \
    "app_id=$app_id" \
    "binary_name=$binary_name" \
    "launcher_name=$launcher_name" \
    "mime_asset=$mime_asset" \
    "runtime=$runtime" \
    "version=$version" \
    "output_name=$output_name" \
    "desktop_asset=$app_id.desktop" \
    "icon_asset=$app_id.svg" \
    "metainfo_asset=$app_id.metainfo.xml"
}

validate_operation_runtime
if [[ "$dry_run" == true ]]; then
  print_dry_run
  exit 0
fi
check_published

case "$operation" in
  tarball) build_tarball ;;
  appimage) build_appimage ;;
  deb) build_deb ;;
esac
