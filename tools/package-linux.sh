#!/usr/bin/env bash
# Builds redistributable Linux packages of the Avalonia shell, for machines that
# have no .NET installed:
#
#   dist/TheEye-<version>-x86_64.AppImage    one file: chmod +x, double-click
#   dist/TheEye-<version>-linux-x64.tar.gz   folder with install.sh / uninstall.sh
#
#   tools/package-linux.sh                 build both
#   tools/package-linux.sh --no-appimage   only the tarball (no download needed)
#
# Unlike install-linux.sh (framework-dependent, for this machine), the payload is
# self-contained: the .NET 10 runtime ships inside, so it is ~100 MB unpacked.
# The native bits (SkiaSharp, HarfBuzz, SQLite, apphost) come prebuilt from
# NuGet against an old glibc, so building on a rolling distro does not narrow
# which distros can run it. The AppImage needs appimagetool; it is downloaded
# once into ~/.cache/the-eye-build (override with APPIMAGETOOL=/path/to/tool).
set -euo pipefail

repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dist_dir="$repo_dir/dist"
work_dir="$dist_dir/.linux-build"
cache_dir="${XDG_CACHE_HOME:-$HOME/.cache}/the-eye-build"

build_appimage=true
[[ "${1:-}" == "--no-appimage" ]] && build_appimage=false

command -v magick >/dev/null || { echo "error: ImageMagick (magick) not found" >&2; exit 1; }

resolve_dotnet() {
    if [[ -n "${DOTNET:-}" ]]; then
        printf '%s\n' "$DOTNET"
    elif [[ -x "$HOME/.dotnet/dotnet" ]]; then
        printf '%s\n' "$HOME/.dotnet/dotnet"
    else
        command -v dotnet
    fi
}
dotnet="$(resolve_dotnet || true)"
[[ -n "$dotnet" ]] || { echo "error: no dotnet found; install the SDK or set DOTNET" >&2; exit 1; }
"$dotnet" --list-sdks 2>/dev/null | grep -q . \
    || { echo "error: $dotnet has no SDK (it is runtime-only); set DOTNET" >&2; exit 1; }

# Date plus commit, so friends can tell builds apart; "-dirty" flags local edits.
version="$(date +%Y.%m.%d)-$(git -C "$repo_dir" rev-parse --short HEAD)"
git -C "$repo_dir" diff --quiet HEAD -- 2>/dev/null || version="$version-dirty"

rm -rf "$work_dir"
mkdir -p "$work_dir"
payload="$work_dir/payload"

echo "==> publishing self-contained Release build ($version)"
"$dotnet" publish "$repo_dir/src/TheEye.Desktop/TheEye.Desktop.csproj" \
    -c Release -r linux-x64 --self-contained true \
    -p:DebugType=none -p:DebugSymbols=false \
    -o "$payload"
chmod +x "$payload/TheEye.Desktop"

echo "==> rendering icons"
icons="$work_dir/icons"
for size in 16 24 32 48 64 128 256 512; do
    mkdir -p "$icons/${size}x${size}/apps"
    magick "$repo_dir/TheEye.ico[0]" -resize "${size}x${size}" -strip \
        "$icons/${size}x${size}/apps/the-eye.png"
done

desktop_entry() {   # $1 = Exec line
    cat <<DESKTOP
[Desktop Entry]
Type=Application
Version=1.5
Name=The Eye of IVAO
GenericName=IVAO Traffic Map
Comment=Live IVAO network overview: pilots, controllers and sectors on one map
Exec=$1
Icon=the-eye
Terminal=false
Categories=Network;
Keywords=IVAO;ATC;Pilot;Traffic;Radar;Flight;Map;
StartupNotify=true
StartupWMClass=TheEye.Desktop
DESKTOP
}

# .NET aborts at start-up when libicu is missing (some minimal installs). Fall
# back to invariant globalization then rather than refusing to launch.
icu_guard='if ! ldconfig -p 2>/dev/null | grep -q "libicuuc\.so"; then
    export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
fi'

# --------------------------------------------------------------------------
# Tarball
# --------------------------------------------------------------------------
echo "==> assembling tarball"
tar_name="TheEye-$version-linux-x64"
tar_root="$work_dir/$tar_name"
mkdir -p "$tar_root"
cp -a "$payload" "$tar_root/app"
cp -a "$icons" "$tar_root/icons"

cat > "$tar_root/the-eye" <<LAUNCHER
#!/usr/bin/env bash
# Runs The Eye straight from this folder, no install needed.
set -euo pipefail
here="\$(cd "\$(dirname "\$(readlink -f "\${BASH_SOURCE[0]}")")" && pwd)"
$icu_guard
exec "\$here/app/TheEye.Desktop" "\$@"
LAUNCHER
chmod +x "$tar_root/the-eye"

cat > "$tar_root/install.sh" <<'INSTALL'
#!/usr/bin/env bash
# Installs The Eye of IVAO for the current user (no root needed):
#   ~/.local/opt/the-eye                  the application
#   ~/.local/bin/the-eye                  command line launcher
#   ~/.local/share/applications/the-eye.desktop   menu entry
#   ~/.local/share/icons/hicolor/*/apps/the-eye.png
set -euo pipefail
src="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
app_dir="$HOME/.local/opt/the-eye"
bin_dir="$HOME/.local/bin"
icon_theme="$HOME/.local/share/icons/hicolor"
desktop_dir="$HOME/.local/share/applications"

echo "==> installing to $app_dir"
rm -rf "$app_dir"
mkdir -p "$app_dir" "$bin_dir" "$desktop_dir" "$icon_theme"
cp -a "$src/app/." "$app_dir/"
cp -a "$src/the-eye" "$app_dir/the-eye"
sed -i 's|\$here/app/TheEye.Desktop|$here/TheEye.Desktop|' "$app_dir/the-eye"
ln -sf "$app_dir/the-eye" "$bin_dir/the-eye"

echo "==> installing icons and menu entry"
cp -a "$src/icons/." "$icon_theme/"
cp "$src/the-eye.desktop" "$desktop_dir/the-eye.desktop"
sed -i "s|^Exec=.*|Exec=$app_dir/the-eye|" "$desktop_dir/the-eye.desktop"
chmod +x "$desktop_dir/the-eye.desktop"
update-desktop-database "$desktop_dir" >/dev/null 2>&1 || true
gtk-update-icon-cache -q "$icon_theme" >/dev/null 2>&1 || true

echo "==> done. Launch \"The Eye of IVAO\" from your menu, or run: $bin_dir/the-eye"
INSTALL
chmod +x "$tar_root/install.sh"

cat > "$tar_root/uninstall.sh" <<'UNINSTALL'
#!/usr/bin/env bash
# Removes what install.sh put in place. Settings in ~/.config/TheEyeIVAO stay.
set -euo pipefail
rm -rf "$HOME/.local/opt/the-eye"
rm -f "$HOME/.local/bin/the-eye" "$HOME/.local/share/applications/the-eye.desktop"
find "$HOME/.local/share/icons/hicolor" -name the-eye.png -delete 2>/dev/null || true
update-desktop-database "$HOME/.local/share/applications" >/dev/null 2>&1 || true
echo "The Eye of IVAO removed (settings kept in ~/.config/TheEyeIVAO)."
UNINSTALL
chmod +x "$tar_root/uninstall.sh"

desktop_entry "the-eye" > "$tar_root/the-eye.desktop"

cat > "$tar_root/README.txt" <<README
The Eye of IVAO ($version) - Linux x86_64
=========================================

No .NET or other dependencies needed; everything is bundled.

  Try it without installing:   ./the-eye
  Install into your menu:      ./install.sh
  Remove it again:             ./uninstall.sh

Settings live in ~/.config/TheEyeIVAO (crash.log too, if something goes wrong).
README

mkdir -p "$dist_dir"
tarball="$dist_dir/$tar_name.tar.gz"
tar -C "$work_dir" -czf "$tarball" "$tar_name"
echo "    $tarball"

# --------------------------------------------------------------------------
# AppImage
# --------------------------------------------------------------------------
if $build_appimage; then
    appimagetool="${APPIMAGETOOL:-}"
    if [[ -z "$appimagetool" ]]; then
        appimagetool="$(command -v appimagetool || true)"
    fi
    if [[ -z "$appimagetool" ]]; then
        appimagetool="$cache_dir/appimagetool-x86_64.AppImage"
        if [[ ! -x "$appimagetool" ]]; then
            echo "==> downloading appimagetool into $cache_dir"
            mkdir -p "$cache_dir"
            curl -fL --retry 3 -o "$appimagetool.part" \
                https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage
            chmod +x "$appimagetool.part"
            mv "$appimagetool.part" "$appimagetool"
        fi
    fi

    echo "==> assembling AppDir"
    appdir="$work_dir/TheEye.AppDir"
    mkdir -p "$appdir/usr/lib" "$appdir/usr/share/applications" "$appdir/usr/share/icons"
    cp -a "$payload" "$appdir/usr/lib/the-eye"
    cp -a "$icons" "$appdir/usr/share/icons/hicolor"
    desktop_entry "the-eye" > "$appdir/the-eye.desktop"
    cp "$appdir/the-eye.desktop" "$appdir/usr/share/applications/the-eye.desktop"
    cp "$icons/256x256/apps/the-eye.png" "$appdir/the-eye.png"
    ln -s the-eye.png "$appdir/.DirIcon"

    cat > "$appdir/AppRun" <<APPRUN
#!/usr/bin/env bash
set -euo pipefail
here="\${APPDIR:-\$(cd "\$(dirname "\$(readlink -f "\${BASH_SOURCE[0]}")")" && pwd)}"
$icu_guard
exec "\$here/usr/lib/the-eye/TheEye.Desktop" "\$@"
APPRUN
    chmod +x "$appdir/AppRun"

    echo "==> building AppImage"
    appimage="$dist_dir/TheEye-$version-x86_64.AppImage"
    # APPIMAGE_EXTRACT_AND_RUN lets appimagetool itself run where FUSE is absent.
    ARCH=x86_64 APPIMAGE_EXTRACT_AND_RUN=1 "$appimagetool" --no-appstream "$appdir" "$appimage"
    chmod +x "$appimage"
    echo "    $appimage"
fi

rm -rf "$work_dir"
echo "==> done"
ls -lh "$dist_dir"/TheEye-"$version"*
