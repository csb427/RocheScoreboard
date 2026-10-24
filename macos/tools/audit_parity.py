#!/usr/bin/env python3
"""Inventory the real app and reject packaging an incomplete replacement."""
from __future__ import annotations

import argparse
import hashlib
import json
import pathlib
import re
import sys
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parents[2]
STATE = ROOT / "macos/port-state.json"
X = "http://schemas.microsoft.com/winfx/2006/xaml"
EVENTS = {
    "Click", "Checked", "Unchecked", "SelectionChanged", "TextChanged", "ValueChanged",
    "MouseLeftButtonUp", "MouseLeftButtonDown", "MouseRightButtonUp", "MouseEnter", "MouseLeave",
    "PreviewMouseWheel", "PreviewMouseLeftButtonUp", "PreviewKeyDown", "KeyDown", "KeyUp",
    "SizeChanged", "Loaded", "Unloaded", "Thumb.DragStarted", "Thumb.DragCompleted",
}


def inventory() -> dict:
    views = []
    for path in sorted((ROOT / "Roche_Scoreboard").rglob("*.xaml")):
        data = path.read_bytes()
        tree = ET.fromstring(data)
        handlers = []
        for element in tree.iter():
            for key, value in element.attrib.items():
                if key in EVENTS:
                    handlers.append({"element": element.get(f"{{{X}}}Name"), "event": key, "handler": value})
        views.append({
            "source": str(path.relative_to(ROOT)),
            "class": tree.get(f"{{{X}}}Class"),
            "sha256": hashlib.sha256(data).hexdigest(),
            "named_elements": [e.get(f"{{{X}}}Name") for e in tree.iter() if e.get(f"{{{X}}}Name")],
            "handlers": handlers,
            "fonts": sorted({e.get("FontFamily") for e in tree.iter() if e.get("FontFamily")}),
        })
    code = []
    for path in sorted((ROOT / "Roche_Scoreboard").rglob("*.cs")):
        if {"obj", "bin"}.intersection(path.parts):
            continue
        data = path.read_bytes()
        text = data.decode("utf-8-sig")
        code.append({"source": str(path.relative_to(ROOT)), "sha256": hashlib.sha256(data).hexdigest(),
                     "lines": len(text.splitlines()),
                     "native_libraries": sorted(set(re.findall(r'\[DllImport\("([^\"]+)"', text)))})
    return {"reference": "Roche_Scoreboard", "xaml": views, "code": code}


def check_package(contract: dict, state: dict) -> list[str]:
    required_screens = {item["source"] for item in contract["xaml"]}
    missing_screens = required_screens - set(state["completed_screens"])
    missing_features = set(state["required_features"]) - set(state["completed_features"])
    errors = []
    if state.get("ready_to_package") is not True:
        errors.append("The Mac port is not ready for verified release packaging.")
    if missing_screens:
        errors.append(f"{len(missing_screens)} production XAML surfaces still require parity verification.")
    if missing_features:
        errors.append(f"{len(missing_features)} feature groups still require parity verification.")
    if state.get("mac_acceptance_verified") is not True:
        errors.append("Native Mac acceptance and visual comparison are outstanding.")
    return errors


def check_translation(contract: dict) -> list[str]:
    """Check source coverage and freshness, independently of native acceptance."""
    generated = ROOT / 'macos/RocheScoreboard.Mac/Native/Generated'
    manifests = {m['source']: (p, m) for p in generated.rglob('*.migration.json')
                 for m in [json.loads(p.read_text())]}
    errors = []
    for translated in generated.rglob('*.cs'):
        header = translated.read_text()[:500]
        source = re.search(r'// Generated from ([^;\n]+);', header)
        digest = re.search(r'// Source SHA-256: ([a-f0-9]{64})', header)
        if not source or not digest:
            errors.append('Missing controller provenance: ' + str(translated.relative_to(ROOT)))
            continue
        original = ROOT / 'Roche_Scoreboard' / source[1]
        if not original.is_file() or hashlib.sha256(original.read_bytes()).hexdigest() != digest[1]:
            errors.append('Stale generated controller: ' + str(translated.relative_to(ROOT)))
    for source in contract['xaml']:
        if source['source'] == 'Roche_Scoreboard/App.xaml':
            # Native Application initialization loads the translated resource
            # dictionaries and opens the original production MainWindow.
            app = (ROOT / 'macos/RocheScoreboard.Mac/App.cs').read_text()
            if 'new Roche_Scoreboard.MainWindow()' not in app:
                errors.append('The default entry point must open the production MainWindow.')
            continue
        found = manifests.get(source['source'])
        if found is None:
            errors.append('Missing production translation: ' + source['source'])
            continue
        path, migration = found
        if migration['source_sha256'] != source['sha256']:
            errors.append('Stale production translation: ' + source['source'])
        missing = set(source['named_elements']) - {n['name'] for n in migration['names']}
        if missing:
            errors.append(f"Lost source identifiers in {source['source']}: {sorted(missing)}")
        original_code = ROOT / (source['source'] + '.cs')
        if original_code.exists() and migration.get('code_source_sha256') != hashlib.sha256(original_code.read_bytes()).hexdigest():
            errors.append('Stale production controller: ' + str(original_code.relative_to(ROOT)))
        if not path.with_name(path.name.replace('.migration.json', '.axaml')).exists():
            errors.append('Missing translated visual tree: ' + str(path))
    return errors


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--write-contract", action="store_true")
    parser.add_argument("--check-package", action="store_true")
    parser.add_argument("--check-translation", action="store_true")
    args = parser.parse_args()
    contract = inventory()
    if args.write_contract:
        path = ROOT / "macos/production-source-contract.json"
        path.write_text(json.dumps(contract, indent=2, ensure_ascii=False) + "\n")
    print(f"Production reference: {len(contract['xaml'])} XAML surfaces, {len(contract['code'])} C# files, "
          f"{sum(len(item['handlers']) for item in contract['xaml'])} declared UI event bindings.")
    if args.check_package or args.check_translation:
        errors = check_translation(contract)
        if args.check_package:
            errors += check_package(contract, json.loads(STATE.read_text()))
        if errors:
            print("\n".join(errors), file=sys.stderr)
            print("Use explicit preview packaging for native review; a preview does not establish Mac acceptance.", file=sys.stderr)
            return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
