#!/usr/bin/env bash
# Installs the Avalonia (Linux) shell into the user profile so it shows up in the
# launcher:
#
#   ~/.local/opt/the-eye                      published payload (framework-dependent)
#   ~/.local/share/icons/hicolor/*/apps/the-eye.png   artwork in eight sizes
#   ~/.local/bin/the-eye                      wrapper the .desktop entry calls
#   ~/.local/share/applications/the-eye.desktop       launcher entry
#
#   tools/install-linux.sh              publish, then (re)install everything
#   tools/install-linux.sh --no-build   only rewrite the launcher files
#
# It stays framework-dependent on purpose: the .NET 10 runtime already lives in
# /usr/share/dotnet, so the payload is ~32 MB instead of ~250 MB. Point the
# DOTNET variable at a different SDK, or THE_EYE_DIR at another install prefix,
# if needed. ImageMagick (`magick`) turns the multi-resolution TheEye.ico into
# the PNGs the freedesktop icon theme expects (.ico is Windows-only).
set -euo pipefail

repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
app_dir="${THE_EYE_DIR:-$HOME/.local/opt/the-eye}"
bin_dir="$HOME/.local/bin"
icon_theme="$HOME/.local/share/icons/hicolor"
desktop_dir="$HOME/.local/share/applications"
command -v magick >/dev/null || { echo "error: ImageMagick (magick) not found" >&2; exit 1; }

# The distro ships a runtime-only /usr/bin/dotnet (that is enough to *run* the
# app); the SDK that can publish lives in ~/.dotnet. Prefer an explicit $DOTNET,
# then the user-local SDK, then whatever PATH offers.
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

if [[ "${1:-}" != "--no-build" ]]; then
    [[ -n "$dotnet" ]] || { echo "error: no dotnet found; install the SDK or set DOTNET" >&2; exit 1; }
    if ! "$dotnet" --list-sdks 2>/dev/null | grep -q .; then
        echo "error: $dotnet has no SDK (it is runtime-only); set DOTNET to an SDK's dotnet" >&2
        exit 1
    fi

    echo "==> publishing Release build to $app_dir"
    "$dotnet" publish "$repo_dir/src/TheEye.Desktop/TheEye.Desktop.csproj" \
        -c Release -r linux-x64 --self-contained false -p:DebugType=none -o "$app_dir"
fi

echo "==> installing the icon"
for size in 16 24 32 48 64 128 256 512; do
    mkdir -p "$icon_theme/${size}x${size}/apps"
    magick "$repo_dir/TheEye.ico[0]" -resize "${size}x${size}" -strip \
        "$icon_theme/${size}x${size}/apps/the-eye.png"
done

# gtk-update-icon-cache refuses to work without the theme index; GTK falls back
# to scanning the directories when no cache exists, so this is belt and braces.
if [[ ! -f "$icon_theme/index.theme" ]]; then
    cp /usr/share/icons/hicolor/index.theme "$icon_theme/index.theme"
fi
rm -f "$icon_theme/.icon-theme.cache"

echo "==> installing the command line launcher"
mkdir -p "$bin_dir"
cat > "$bin_dir/the-eye" <<'WRAPPER'
#!/usr/bin/env bash
# The Eye of IVAO — terminal launcher for the Avalonia (Linux) build.
# The bits live in ~/.local/opt/the-eye (sources:
# /home/namco/Documents/Projects/Personal/TheEye). This script exists so the
# .desktop entry and the shell share one command; THE_EYE_DIR can point it at
# another install prefix.
set -euo pipefail

app_dir="${THE_EYE_DIR:-$HOME/.local/opt/the-eye}"
exec "$app_dir/TheEye.Desktop" "$@"
WRAPPER
chmod +x "$bin_dir/the-eye"

echo "==> installing the desktop entry"
mkdir -p "$desktop_dir"
cat > "$desktop_dir/the-eye.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Version=1.5
Name=The Eye of IVAO
GenericName=IVAO Traffic Map
Comment=Live IVAO network overview: pilots, controllers and sectors on one map
Exec=$bin_dir/the-eye
TryExec=$bin_dir/the-eye
Icon=the-eye
Terminal=false
Categories=Network;
Keywords=IVAO;ATC;Pilot;Traffic;Radar;Flight;Map;
StartupNotify=true
StartupWMClass=TheEye.Desktop
DESKTOP
chmod +x "$desktop_dir/the-eye.desktop"

update-desktop-database "$desktop_dir" >/dev/null 2>&1 || true
desktop-file-validate "$desktop_dir/the-eye.desktop"

echo "==> done. Launch \"The Eye of IVAO\" from the menu, or run: the-eye"
