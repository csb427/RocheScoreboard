#!/usr/bin/env python3
"""Translate the production scorebug markup without replacing its visual tree.

This is a migration tool, not a general WPF compatibility layer. Every changed
attribute is recorded so platform translations can be reviewed against the source.
The generated markup preserves the production names, geometry, colours and text.
"""
from __future__ import annotations

import hashlib
import json
import math
import pathlib
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parents[2]
SOURCE = ROOT / "Roche_Scoreboard/Views/ScorebugControl.xaml"
DESTINATION = ROOT / "macos/RocheScoreboard.Mac/Presentation/ProductionScorebug.axaml"
WPF = "http://schemas.microsoft.com/winfx/2006/xaml/presentation"
AVALONIA = "https://github.com/avaloniaui"
X = "http://schemas.microsoft.com/winfx/2006/xaml"
COMPAT = "clr-namespace:RocheScoreboard.Mac.Production"

ET.register_namespace("", AVALONIA)
ET.register_namespace("x", X)
ET.register_namespace("port", COMPAT)


def relative(value: str) -> str:
    return ",".join(f"{float(v) * 100:g}%" for v in value.split(","))


def translate() -> None:
    source = SOURCE.read_bytes()
    tree = ET.fromstring(source)
    tree.set(f"{{{COMPAT}}}ProductionNames.Root", "True")
    names = [e.get(f"{{{X}}}Name") for e in tree.iter() if e.get(f"{{{X}}}Name")]
    changes: list[dict[str, str]] = []
    events: list[dict[str, str]] = []

    for element in tree.iter():
        original_tag = element.tag.removeprefix(f"{{{WPF}}}")
        element.tag = f"{{{AVALONIA}}}{original_tag}"
        name = element.get(f"{{{X}}}Name", original_tag)
        if original_tag in {"LinearGradientBrush", "RadialGradientBrush", "SolidColorBrush", "GradientStop", "ColumnDefinition", "RowDefinition", "TranslateTransform", "ScaleTransform", "SkewTransform", "RotateTransform", "TransformGroup", "DropShadowEffect", "BlurEffect"} and f"{{{X}}}Name" in element.attrib:
            element.set(f"{{{COMPAT}}}ProductionNames.Id", element.attrib.pop(f"{{{X}}}Name"))
        if original_tag == "DropShadowEffect":
            depth = float(element.attrib.pop("ShadowDepth", "5"))
            direction = math.radians(float(element.attrib.pop("Direction", "315")))
            element.set("OffsetX", f"{depth * math.cos(direction):.12g}")
            element.set("OffsetY", f"{-depth * math.sin(direction):.12g}")
        if original_tag == "TextBlock.LayoutTransform":
            # Production code scales these text runs to fit; the native controller
            # manages the scale using a layout-transform host after loading.
            element.tag = f"{{{AVALONIA}}}TextBlock.RenderTransform"
        if original_tag == "Style":
            element.tag = f"{{{AVALONIA}}}ControlTheme"
            if f"{{{X}}}Key" not in element.attrib:
                element.set(f"{{{X}}}Key", "{x:Type " + element.get("TargetType", "TextBlock") + "}")

        for key, value in list(element.attrib.items()):
            local = key.rsplit("}", 1)[-1]
            if key.startswith("{") and not key.startswith((f"{{{X}}}", f"{{{COMPAT}}}")):
                del element.attrib[key]
                continue
            if local == "Class":
                element.set(key, "RocheScoreboard.Mac.Production.ProductionScorebug")
            elif local == "SizeChanged":
                events.append({"name": name, "event": local, "handler": value})
                del element.attrib[key]
            elif local == "Visibility":
                del element.attrib[key]
                element.set("IsVisible", "True" if value == "Visible" else "False")
                changes.append({"name": name, "property": local, "translation": "IsVisible"})
            elif local == "Panel.ZIndex":
                del element.attrib[key]
                element.set("ZIndex", value)
            elif local == "StrokeLineJoin":
                del element.attrib[key]
                element.set("StrokeJoin", value)
            elif local in {"StartPoint", "EndPoint", "Center", "GradientOrigin"} and "Brush" in original_tag:
                element.set(key, relative(value))
            elif local in {"RadiusX", "RadiusY"} and original_tag == "RadialGradientBrush":
                element.set(key, f"{float(value) * 100:g}%")
            elif local == "RenderTransformOrigin":
                element.set(key, relative(value))
            elif local in {"Viewport", "Viewbox"} and "Brush" in original_tag:
                del element.attrib[key]
                units = element.get(local + "Units", "RelativeToBoundingBox")
                element.set("DestinationRect" if local == "Viewport" else "SourceRect", value if units == "Absolute" else relative(value))
            elif local in {"ViewportUnits", "ViewboxUnits"}:
                element.attrib.pop(key, None)
            elif local in {"TextOptions.TextFormattingMode", "TextOptions.TextHintingMode", "TextOptions.TextRenderingMode", "SnapsToDevicePixels", "LineStackingStrategy"}:
                changes.append({"name": name, "property": local, "translation": "Native text/rasterization backend; requires visual comparison"})
                element.attrib.pop(key, None)
            elif local == "Property" and value == "TextOptions.TextFormattingMode":
                element.set(key, "UseLayoutRounding")
                element.set("Value", "True")
            elif local == "Property" and value == "StrokeLineJoin":
                element.set(key, "StrokeJoin")

    DESTINATION.parent.mkdir(parents=True, exist_ok=True)
    retained = [e.get(f"{{{X}}}Name") or e.get(f"{{{COMPAT}}}ProductionNames.Id") for e in tree.iter()]
    retained = [name for name in retained if name]
    if sorted(retained) != sorted(names):
        raise RuntimeError("Translation lost production names; refusing to write incomplete markup.")
    ET.indent(tree, space="    ")
    DESTINATION.write_text(
        "<!-- Generated from Roche_Scoreboard/Views/ScorebugControl.xaml.\n"
        "     Change the translator, not this file; preserve the production visual tree. -->\n"
        + ET.tostring(tree, encoding="unicode") + "\n",
        encoding="utf-8",
    )
    manifest = {
        "source": str(SOURCE.relative_to(ROOT)),
        "source_sha256": hashlib.sha256(source).hexdigest(),
        "named_elements": names,
        "platform_translations": changes,
        "events_requiring_native_binding": events,
        "visual_parity_verified": False,
        "behavior_parity_verified": False,
    }
    DESTINATION.with_suffix(".migration.json").write_text(json.dumps(manifest, indent=2) + "\n")
    print(f"Translated production scorebug: {len(names)} named elements retained; {len(changes)} platform translations recorded.")


if __name__ == "__main__":
    translate()
