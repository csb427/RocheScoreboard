#!/usr/bin/env python3
"""Bundle the official VLC runtime matching the target Mac architecture."""
from __future__ import annotations
import argparse
import hashlib
import os
from pathlib import Path
import platform
import shutil
import struct
import subprocess
import tempfile
import urllib.request

VERSION = "3.0.24"
PACKAGES = {
    "osx-arm64": ("arm64", "64a89d93cdd30b0e97131743e246373db82d6826ea882d265d46d74b136da2b7", 0x0100000c),
    "osx-x64": ("intel64", "1ef6c903e2dc026d4e58ddf7b2a9ed0eb6f8caf80a9a9f46f96490340b962707", 0x01000007),
}

def copy_runtime(source: Path, destination: Path, cpu: int) -> None:
    if struct.unpack("<II", (source / "lib/libvlc.dylib").read_bytes()[:8]) != (0xfeedfacf, cpu):
        raise ValueError("VLC must match the selected Mac architecture.")
    if len(list((source / "plugins").glob("*.dylib"))) < 100:
        raise ValueError("The VLC plugin set is incomplete.")
    for directory in ("lib", "plugins", "share"):
        # Dereference aliases for the updater; omit extracted HFS metadata.
        shutil.copytree(source / directory, destination / directory,
                        ignore=lambda _, names: [n for n in names if ":" in n or n == "plugins.dat"])

def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("rid", choices=PACKAGES)
    parser.add_argument("app", type=Path)
    args = parser.parse_args()
    arch, digest, cpu = PACKAGES[args.rid]
    base = f"https://download.videolan.org/pub/videolan/vlc/{VERSION}"
    url = f"{base}/macosx/vlc-{VERSION}-{arch}.dmg"
    cache = Path(os.environ.get("ROCHE_VLC_CACHE", Path.home() / ".cache/roche-vlc"))
    cache.mkdir(parents=True, exist_ok=True)
    dmg = cache / f"vlc-{VERSION}-{arch}.dmg"
    if not dmg.exists():
        partial = dmg.with_suffix(".part")
        print(f"Downloading official VLC {VERSION} for {arch}…", flush=True)
        with urllib.request.urlopen(url, timeout=120) as response, partial.open("wb") as output:
            shutil.copyfileobj(response, output)
        partial.replace(dmg)
    if hashlib.sha256(dmg.read_bytes()).hexdigest() != digest:
        raise ValueError(f"VLC SHA-256 mismatch: remove {dmg} and download again.")
    destination = args.app / "Contents/Frameworks/vlc"
    with tempfile.TemporaryDirectory(prefix="roche-vlc-") as temporary:
        root = Path(temporary)
        if platform.system() == "Darwin":
            mount = root / "mounted"
            mount.mkdir()
            subprocess.run(["hdiutil", "attach", "-readonly", "-nobrowse", "-mountpoint", str(mount), str(dmg)], check=True)
            try:
                copy_runtime(mount / "VLC.app/Contents/MacOS", destination, cpu)
            finally:
                subprocess.run(["hdiutil", "detach", str(mount)], check=True)
        else:
            seven = os.environ.get("SEVEN_ZIP") or shutil.which("7zz") or shutil.which("7z")
            if not seven:
                raise RuntimeError("Linux cross-packaging needs 7zz (7-Zip), or set SEVEN_ZIP.")
            subprocess.run([seven, "x", "-y", "-o" + str(root), str(dmg)], check=True, stdout=subprocess.DEVNULL)
            sources = list(root.glob("**/VLC.app/Contents/MacOS"))
            if len(sources) != 1:
                raise ValueError("The VLC image must contain exactly one VLC.app.")
            copy_runtime(sources[0], destination, cpu)
    notices = args.app / "Contents/Resources/ThirdParty/VLC"
    notices.mkdir(parents=True, exist_ok=True)
    for name in ("COPYING", "COPYING.LIB"):
        with urllib.request.urlopen(f"https://raw.githubusercontent.com/videolan/vlc/{VERSION}/{name}", timeout=30) as response:
            (notices / name).write_bytes(response.read())
    (notices / "UPSTREAM.txt").write_text(
        f"VideoLAN VLC {VERSION}. Libraries, plugins and data from the official Mac distribution.\n"
        f"Binary: {url}\nSHA-256: {digest}\n"
        f"Corresponding source: {base}/vlc-{VERSION}.tar.xz\n"
        f"Build/contrib sources: https://code.videolan.org/videolan/vlc/-/tree/{VERSION}\n"
        "Components have individual licences; see COPYING and COPYING.LIB.\n")
    print(f"Bundled VLC {VERSION} ({arch}), including plugins and licence notices.")

if __name__ == "__main__":
    main()
