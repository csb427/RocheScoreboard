#!/usr/bin/env bash
set -euo pipefail

if [[ "$(uname -s)" != Darwin ]]; then
  echo 'Local application signing must run on a Mac.' >&2
  exit 2
fi
app_dir="${1:?Usage: sign-mac.sh '/path/to/Roche Scoreboard.app'}"
if [[ ! -f "$app_dir/Contents/Info.plist" || ! -f "$app_dir/Contents/MacOS/RocheScoreboard.Mac" ]]; then
  echo 'The selected path is not a Roche Scoreboard application.' >&2
  exit 2
fi
entitlements="$(mktemp "${TMPDIR:-/tmp}/roche-entitlements.XXXXXX")"
trap 'rm -f -- "$entitlements"' EXIT
cat > "$entitlements" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>com.apple.security.cs.allow-jit</key><true/>
</dict></plist>
PLIST

# Sign native plugins first. Managed .NET files in Contents/MacOS also need
# signatures, as described by Avalonia's macOS deployment guide. Signing the
# main executable before its neighbouring files makes codesign reject the
# incomplete bundle. --deep is used only to verify, never to sign.
while IFS= read -r -d '' binary; do
  case "$(file -b -- "$binary")" in
    *Mach-O*) codesign --force --sign - --entitlements "$entitlements" "$binary" ;;
  esac
done < <(find "$app_dir/Contents/Frameworks" -type f -print0)
main_executable="$app_dir/Contents/MacOS/RocheScoreboard.Mac"
while IFS= read -r -d '' binary; do
  if [[ "$binary" != "$main_executable" ]]; then
    codesign --force --sign - --entitlements "$entitlements" "$binary"
  fi
done < <(find "$app_dir/Contents/MacOS" -type f -print0)
codesign --force --sign - --entitlements "$entitlements" "$main_executable"
codesign --force --sign - --entitlements "$entitlements" "$app_dir"
codesign --verify --deep --strict "$app_dir"
