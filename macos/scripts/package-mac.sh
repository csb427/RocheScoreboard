#!/usr/bin/env bash
set -euo pipefail
script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_dir="$(cd -- "$script_dir/../.." && pwd)"
rid="${1:-}"
if [[ -z "$rid" ]]; then
  case "$(uname -m)" in arm64|aarch64) rid=osx-arm64 ;; *) rid=osx-x64 ;; esac
fi
case "$rid" in osx-arm64|osx-x64) ;; *) echo "Usage: $0 [osx-arm64|osx-x64] [output directory] [--preview]" >&2; exit 2 ;; esac
channel="${3:-}"
case "$channel" in
  '') python3 "$repo_dir/macos/tools/audit_parity.py" --check-package; archive_name="Roche-Scoreboard-$rid.zip" ;;
  --preview) python3 "$repo_dir/macos/tools/audit_parity.py" --check-translation; archive_name="Roche-Scoreboard-Mac-Preview-$rid.zip" ;;
  *) echo 'The third argument must be --preview, or omitted for a verified release.' >&2; exit 2 ;;
esac
output_dir="${2:-$repo_dir/artifacts/macos}"
mkdir -p -- "$output_dir"
output_dir="$(cd -- "$output_dir" && pwd)"
stage_dir="$(mktemp -d "$output_dir/.package-$rid.XXXXXX")"
trap 'rm -rf -- "$stage_dir"' EXIT
payload="$stage_dir/payload"
app_dir="$payload/Roche Scoreboard.app"
mkdir -p -- "$app_dir/Contents/MacOS" "$app_dir/Contents/Resources"
dotnet publish "$repo_dir/macos/RocheScoreboard.Mac/RocheScoreboard.Mac.csproj" \
  -c Release -r "$rid" --self-contained true \
  -p:PublishSingleFile=false -p:PublishTrimmed=false -p:UseSharedCompilation=false \
  -o "$app_dir/Contents/MacOS"
chmod +x "$app_dir/Contents/MacOS/RocheScoreboard.Mac"
python3 "$repo_dir/macos/tools/bundle_vlc.py" "$rid" "$app_dir"
python3 - "$repo_dir" "$app_dir" "$channel" <<'PY'
import pathlib, plistlib, struct, sys, xml.etree.ElementTree as ET
repo, app, channel = pathlib.Path(sys.argv[1]), pathlib.Path(sys.argv[2]), sys.argv[3]
version = ET.parse(repo / 'macos/RocheScoreboard.Mac/RocheScoreboard.Mac.csproj').findtext('.//Version')
info = {
    'CFBundleName': 'Roche Scoreboard', 'CFBundleDisplayName': 'Roche Scoreboard',
    'CFBundleIdentifier': 'com.rochescoreboard.mac', 'CFBundleVersion': version,
    'CFBundleShortVersionString': version, 'CFBundleExecutable': 'RocheScoreboard.Mac',
    'CFBundlePackageType': 'APPL', 'LSMinimumSystemVersion': '15.0',
    'NSPrincipalClass': 'NSApplication', 'NSHighResolutionCapable': True,
    'CFBundleIconFile': 'RocheScoreboard.icns',
    'RocheBuildChannel': 'preview' if channel else 'release',
    'NSHumanReadableCopyright': 'Roche Scoreboard. Third-party licences in Contents/Resources/ThirdParty.',
}
(app / 'Contents/Info.plist').write_bytes(plistlib.dumps(info))
# Apple's PNG-backed 1024x1024 icon slot retains the original artwork.
png = (repo / 'Roche_Scoreboard/app.png').read_bytes()
entry = b'ic10' + struct.pack('>I', len(png) + 8) + png
(app / 'Contents/Resources/RocheScoreboard.icns').write_bytes(b'icns' + struct.pack('>I', len(entry) + 8) + entry)
PY
cp -- "$script_dir/sign-mac.sh" "$payload/sign-mac.sh"
cat > "$payload/Prepare Roche Scoreboard.command" <<'COMMAND'
#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "$0")"
bash ./sign-mac.sh './Roche Scoreboard.app'
open './Roche Scoreboard.app'
COMMAND
chmod +x "$payload/Prepare Roche Scoreboard.command" "$payload/sign-mac.sh"
cp -- "$repo_dir/macos/MAC_ACCEPTANCE.md" "$payload/Mac acceptance.md"
cat > "$payload/Read me.txt" <<'NOTICE'
Roche Scoreboard for macOS 15 or later

Extract the complete ZIP. On a Mac, run "Prepare Roche Scoreboard.command"
once to apply a local signature and open the application. Keep the helper
beside the app for this first launch. The .NET runtime and VLC are included.
You can then move Roche Scoreboard.app to Applications.
If macOS blocks this unnotarised local build, use System Settings > Privacy &
Security > Open Anyway after attempting to open the app you built/downloaded.

This build carries the original production screens and application logic.
Preview archives have not completed native Mac acceptance or a Windows/Mac
visual comparison. See "Mac acceptance.md" for the remaining acceptance work.
The package uses local ad-hoc signing, without Apple notarisation.
NOTICE
if [[ "$(uname -s)" == Darwin ]]; then
  bash "$script_dir/sign-mac.sh" "$app_dir"
  ditto -c -k --sequesterRsrc "$payload" "$stage_dir/$archive_name"
else
  (cd -- "$payload" && zip -q -r "$stage_dir/$archive_name" .)
fi
mv -f -- "$stage_dir/$archive_name" "$output_dir/$archive_name"
python3 - "$output_dir/$archive_name" <<'PY'
import hashlib, pathlib, sys
p = pathlib.Path(sys.argv[1])
with p.open('rb') as stream:
    digest = hashlib.file_digest(stream, 'sha256').hexdigest()
p.with_suffix('.zip.sha256').write_text(digest + '  ' + p.name + '\n')
print('Created ' + str(p))
PY
