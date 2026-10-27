#!/usr/bin/env python3
"""
scaffold.py — create / update DESIGN.md, and scan a project for design hints.

Pure stdlib. Produces lint-clean DESIGN.md with canonical section order and
correctly-quoted YAML front matter. Works with dmd.py for validation.

Subcommands:
  new    --from-json spec.json [--out DESIGN.md]
         Build a fresh DESIGN.md from a token spec (see SPEC SHAPE below).

  update DESIGN.md --from-json spec.json [--out DESIGN.md]
         Merge new/changed tokens into an existing file, reorder sections to
         canonical order, and preserve existing prose. Token values in the
         spec override; sections/prose not mentioned are kept as-is.

  scan   PROJECT_DIR [--json] [--max-files N]
         Read-only walk of a project to extract color/font/radius/spacing
         hints from CSS/SCSS/Tailwind/JS. Emits a suggested spec JSON you can
         review, edit, and feed to `new`.

SPEC SHAPE (all keys optional except name):
{
  "name": "Acme",
  "description": "Friendly fintech",
  "version": "alpha",
  "colors":     {"primary": "#1A1C1E", "on-primary": "#ffffff"},
  "typography": {"body": {"fontFamily": "Inter", "fontSize": "1rem",
                          "fontWeight": 400, "lineHeight": 1.5}},
  "rounded":    {"sm": "4px", "md": "8px"},
  "spacing":    {"sm": "8px", "md": "16px"},
  "components": {"button-primary": {"backgroundColor": "{colors.primary}",
                                    "textColor": "{colors.on-primary}",
                                    "rounded": "{rounded.sm}"}},
  "prose": {"Overview": "Free markdown...", "Colors": "..."}
}

SECURITY: scan is read-only, skips binaries, vendored/build dirs, large files,
and never follows symlinks outside the target directory.
"""
from __future__ import annotations

import argparse
import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import dmd  # noqa: E402

CANONICAL_ORDER = dmd.CANONICAL_ORDER
TOKEN_GROUPS = ["colors", "typography", "rounded", "spacing", "components"]
NEEDS_QUOTE = re.compile(r'^[\s#&*!|>%@`\"\'\[\]{},]|[:#]|^\s*$|^[0-9]')


# ---------------------------------------------------------------------------
# YAML emitter (restricted: matches what dmd.parse_yaml / upstream accept)
# ---------------------------------------------------------------------------
def _scalar(v):
    if isinstance(v, bool):
        return "true" if v else "false"
    if isinstance(v, (int, float)):
        return dmd._numfmt(v) if isinstance(v, float) else str(v)
    s = str(v)
    # Always quote color hexes, references, and anything risky; quote defensively.
    if s == "" or s.startswith("#") or s.startswith("{") or NEEDS_QUOTE.search(s) or ":" in s:
        return '"' + s.replace("\\", "\\\\").replace('"', '\\"') + '"'
    return s


def _emit_key(k):
    ks = str(k)
    if NEEDS_QUOTE.search(ks) or ":" in ks or ks.startswith("#"):
        return '"' + ks.replace('"', '\\"') + '"'
    return ks


def emit_yaml(data, indent=0):
    lines = []
    pad = "  " * indent
    for k, v in data.items():
        if isinstance(v, dict):
            lines.append(f"{pad}{_emit_key(k)}:")
            lines.append(emit_yaml(v, indent + 1))
        else:
            lines.append(f"{pad}{_emit_key(k)}: {_scalar(v)}")
    return "\n".join(l for l in lines if l != "")


# ---------------------------------------------------------------------------
# Build DESIGN.md from spec
# ---------------------------------------------------------------------------
def _front_matter(spec):
    fm = {}
    if spec.get("version"):
        fm["version"] = spec["version"]
    fm["name"] = spec.get("name", "Untitled")
    if spec.get("description"):
        fm["description"] = spec["description"]
    for g in TOKEN_GROUPS:
        if spec.get(g):
            fm[g] = spec[g]
    return fm


def _default_prose(section, spec):
    colors = spec.get("colors") or {}
    typo = spec.get("typography") or {}
    if section == "Overview":
        return spec.get("description") or (
            f"{spec.get('name', 'This system')} design language. Describe the overall "
            "mood, references, and the feeling the UI should evoke.")
    if section == "Colors" and colors:
        rows = "\n".join(f"- **{n} ({v}):** describe role and usage."
                         for n, v in colors.items() if isinstance(v, str))
        return "The palette and how each role is applied.\n\n" + rows
    if section == "Typography" and typo:
        rows = "\n".join(f"- **{n}:** {t.get('fontFamily', 'font')} "
                         f"{t.get('fontSize', '')}.".strip()
                         for n, t in typo.items() if isinstance(t, dict))
        return "Type scale and intended use.\n\n" + rows
    if section == "Layout":
        return "Spacing scale and layout rhythm. Explain the base unit and how spacing steps compose."
    if section == "Elevation & Depth":
        return "Shadow / depth strategy. When surfaces lift, and how."
    if section == "Shapes":
        return "Corner rounding language and any signature shapes."
    if section == "Components":
        return "Key components and the token bindings that define them."
    if section == "Do's and Don'ts":
        return "- **Do** keep contrast accessible (WCAG AA).\n- **Don't** introduce ad-hoc colors outside the palette."
    return f"Describe {section.lower()} here."


def build_markdown(spec, existing_prose=None):
    existing_prose = existing_prose or {}
    fm = _front_matter(spec)
    out = ["---", emit_yaml(fm), "---", ""]
    # which sections to include: those with prose (existing or provided) or
    # implied by tokens, in canonical order.
    provided = spec.get("prose") or {}
    include = set(provided) | set(existing_prose)
    if spec.get("colors"):
        include |= {"Overview", "Colors"}
    if spec.get("typography"):
        include.add("Typography")
    if spec.get("spacing"):
        include.add("Layout")
    if spec.get("rounded"):
        include.add("Shapes")
    if spec.get("components"):
        include.add("Components")
    include.add("Overview")
    ordered = [s for s in CANONICAL_ORDER if s in include]
    for s in ordered:
        out.append(f"## {s}")
        body = provided.get(s) or existing_prose.get(s) or _default_prose(s, spec)
        out.append("")
        out.append(body.rstrip())
        out.append("")
    return "\n".join(out).rstrip() + "\n"


# ---------------------------------------------------------------------------
# Parse existing prose out of a DESIGN.md (to preserve on update)
# ---------------------------------------------------------------------------
def extract_prose(text):
    prose = {}
    lines = text.splitlines()
    # skip front matter
    i = 0
    if lines and lines[0].strip() == "---":
        i = 1
        while i < len(lines) and lines[i].strip() != "---":
            i += 1
        i += 1
    cur = None
    buf = []
    for line in lines[i:]:
        m = dmd.H2_RE.match(line.rstrip())
        if m:
            if cur is not None:
                prose[cur] = "\n".join(buf).strip()
            cur = dmd._resolve_alias(m.group(1).strip())
            buf = []
        elif cur is not None:
            buf.append(line)
    if cur is not None:
        prose[cur] = "\n".join(buf).strip()
    return prose


def extract_tokens(text):
    pr = dmd.parse_document(text)
    return {g: pr.tokens.get(g, {}) for g in ["version", "name", "description"] + TOKEN_GROUPS
            if pr.tokens.get(g) is not None}


# ---------------------------------------------------------------------------
# Project scan
# ---------------------------------------------------------------------------
SKIP_DIRS = {"node_modules", ".git", "dist", "build", ".next", "out", "vendor",
             "coverage", ".cache", "__pycache__", ".venv", "venv", "target"}
SCAN_EXT = {".css", ".scss", ".sass", ".less", ".js", ".jsx", ".ts", ".tsx",
            ".vue", ".svelte", ".html", ".json", ".cjs", ".mjs"}
MAX_FILE_BYTES = 512 * 1024

HEX = re.compile(r"#(?:[0-9a-fA-F]{3,4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})\b")
FONT_FAMILY = re.compile(r"font-family\s*:\s*([^;{}\n]+)", re.I)
RADIUS = re.compile(r"border-radius\s*:\s*([0-9.]+(?:px|rem|em))", re.I)
CSS_VAR = re.compile(r"--([a-z0-9-]+)\s*:\s*(#(?:[0-9a-fA-F]{3,8}))", re.I)


def scan_project(root, max_files=4000):
    root = os.path.realpath(root)
    color_counts = {}
    fonts = {}
    radii = {}
    named_vars = {}
    seen = 0
    for dirpath, dirnames, filenames in os.walk(root, followlinks=False):
        dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS and not d.startswith(".")]
        for fn in filenames:
            ext = os.path.splitext(fn)[1].lower()
            if ext not in SCAN_EXT:
                continue
            path = os.path.join(dirpath, fn)
            if os.path.islink(path):
                continue
            try:
                if os.path.getsize(path) > MAX_FILE_BYTES:
                    continue
                with open(path, "r", encoding="utf-8", errors="ignore") as f:
                    text = f.read()
            except (OSError, UnicodeError):
                continue
            seen += 1
            if seen > max_files:
                break
            for hx in HEX.findall(text):
                k = hx.lower()
                color_counts[k] = color_counts.get(k, 0) + 1
            for fam in FONT_FAMILY.findall(text):
                first = fam.split(",")[0].strip().strip('"\'')
                if first and not first.startswith("var(") and len(first) < 40:
                    fonts[first] = fonts.get(first, 0) + 1
            for rad in RADIUS.findall(text):
                radii[rad] = radii.get(rad, 0) + 1
            for name, val in CSS_VAR.findall(text):
                named_vars.setdefault(name, val.lower())
        if seen > max_files:
            break

    top_colors = sorted(color_counts.items(), key=lambda kv: -kv[1])[:12]
    top_fonts = sorted(fonts.items(), key=lambda kv: -kv[1])[:4]
    top_radii = sorted(radii.items(), key=lambda kv: -kv[1])[:6]

    colors = {}
    # prefer semantically-named CSS vars when present
    for name, val in list(named_vars.items())[:12]:
        safe = re.sub(r"[^a-z0-9-]", "-", name.lower())
        colors[safe] = val
    if not colors:
        for i, (hx, _c) in enumerate(top_colors):
            colors[f"color-{i+1}" if i else "primary"] = hx

    typography = {}
    scale_names = ["h1", "body", "label"]
    for i, (fam, _c) in enumerate(top_fonts[:3]):
        typography[scale_names[i] if i < len(scale_names) else f"font-{i}"] = {
            "fontFamily": fam,
            "fontSize": "2rem" if i == 0 else ("1rem" if i == 1 else "0.875rem"),
        }

    rounded = {}
    labels = ["sm", "md", "lg", "xl", "xxl", "full"]
    for i, (val, _c) in enumerate(top_radii):
        rounded[labels[i] if i < len(labels) else f"r{i}"] = val

    spec = {
        "name": os.path.basename(root) or "Scanned",
        "description": "Draft scaffolded from project scan — review before use.",
        "version": "alpha",
        "colors": colors,
    }
    if typography:
        spec["typography"] = typography
    if rounded:
        spec["rounded"] = rounded
    spec["_scan_stats"] = {
        "files_scanned": seen,
        "distinct_colors": len(color_counts),
        "top_colors": top_colors,
        "top_fonts": top_fonts,
        "top_radii": top_radii,
    }
    return spec


# ---------------------------------------------------------------------------
# Commands
# ---------------------------------------------------------------------------
def _load_spec(path):
    with open(path, "r", encoding="utf-8") as f:
        spec = json.load(f)
    spec.pop("_scan_stats", None)
    return spec


def _write(out, text):
    if out == "-" or out is None:
        sys.stdout.write(text)
    else:
        with open(out, "w", encoding="utf-8") as f:
            f.write(text)
        sys.stderr.write(f"Wrote {out}\n")


def _validate(text):
    res = dmd.lint(text)
    s = res["summary"]
    sys.stderr.write(f"Lint: {s['errors']} error(s), {s['warnings']} warning(s), "
                     f"{s['infos']} info.\n")
    for f in res["findings"]:
        if f["severity"] in ("error", "warning"):
            sys.stderr.write(f"  [{f['severity']}] {f.get('path','')}: {f['message']}\n")
    return s["errors"] == 0


def cmd_new(args):
    spec = _load_spec(args.from_json)
    text = build_markdown(spec)
    _write(args.out, text)
    return 0 if _validate(text) else 1


def cmd_update(args):
    with open(args.file, "r", encoding="utf-8") as f:
        existing = f.read()
    existing_prose = extract_prose(existing)
    existing_tokens = extract_tokens(existing)
    spec = _load_spec(args.from_json) if args.from_json else {}
    # merge: existing tokens as base, spec overrides per group key
    merged = {"version": existing_tokens.get("version") or spec.get("version"),
              "name": spec.get("name") or existing_tokens.get("name"),
              "description": spec.get("description") or existing_tokens.get("description")}
    for g in TOKEN_GROUPS:
        base = dict(existing_tokens.get(g) or {})
        base.update(spec.get(g) or {})
        if base:
            merged[g] = base
    merged["prose"] = spec.get("prose") or {}
    text = build_markdown(merged, existing_prose=existing_prose)
    _write(args.out or args.file, text)
    return 0 if _validate(text) else 1


def cmd_scan(args):
    spec = scan_project(args.dir, max_files=args.max_files)
    if args.json:
        print(json.dumps(spec, indent=2))
    else:
        stats = spec.pop("_scan_stats", {})
        print(json.dumps(spec, indent=2))
        sys.stderr.write("\nScan stats: " + json.dumps(stats, indent=2) + "\n")
    return 0


def main(argv=None):
    p = argparse.ArgumentParser(prog="scaffold", description="Create/update/scan DESIGN.md")
    sub = p.add_subparsers(dest="cmd", required=True)
    sp = sub.add_parser("new")
    sp.add_argument("--from-json", required=True)
    sp.add_argument("--out", default="DESIGN.md")
    sp.set_defaults(func=cmd_new)
    sp = sub.add_parser("update")
    sp.add_argument("file")
    sp.add_argument("--from-json")
    sp.add_argument("--out")
    sp.set_defaults(func=cmd_update)
    sp = sub.add_parser("scan")
    sp.add_argument("dir")
    sp.add_argument("--json", action="store_true")
    sp.add_argument("--max-files", type=int, default=4000)
    sp.set_defaults(func=cmd_scan)
    args = p.parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    sys.exit(main())
