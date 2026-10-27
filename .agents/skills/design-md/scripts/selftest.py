#!/usr/bin/env python3
"""Self-test for dmd.py — runs offline, no Node, no network.

Verifies parsing, the 9 lint rules, WCAG contrast, token-reference resolution,
diff regression detection, and all three export formats against known-good
expectations. Run:  python dmd.py selftest   (or)  python selftest.py
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import dmd  # noqa: E402

PASS = 0
FAIL = 0
FAILURES = []


def check(name, cond, detail=""):
    global PASS, FAIL
    if cond:
        PASS += 1
    else:
        FAIL += 1
        FAILURES.append(f"{name}: {detail}")


CLEAN = """---
name: Test
colors:
  primary: "#1A1C1E"
  on-primary: "#ffffff"
typography:
  body:
    fontFamily: Public Sans
    fontSize: 1rem
    fontWeight: 400
    lineHeight: 1.5
rounded:
  sm: 4px
spacing:
  md: 16px
components:
  button-primary:
    backgroundColor: "{colors.primary}"
    textColor: "{colors.on-primary}"
    rounded: "{rounded.sm}"
---

## Overview
A test system.

## Colors
Primary ink.

## Typography
Body copy.
"""

LOWCONTRAST = """---
name: Bad
colors:
  primary: "#777777"
  fg: "#888888"
components:
  card:
    backgroundColor: "{colors.primary}"
    textColor: "{colors.fg}"
---
## Colors
x
"""

BROKEN = """---
name: Broken
colors:
  primary: "#000000"
components:
  btn:
    backgroundColor: "{colors.nope}"
---
## Colors
x
"""


def run():
    # --- color parsing ---
    c = dmd.parse_css_color("#1A1C1E")
    check("hex-parse", c and c["hex"] == "#1a1c1e", str(c))
    check("hex-short", dmd.parse_css_color("#fff")["hex"] == "#ffffff")
    check("rgb-parse", dmd.parse_css_color("rgb(255 0 0)")["hex"] == "#ff0000")
    check("hsl-parse", dmd.parse_css_color("hsl(0 100% 50%)")["hex"] == "#ff0000")
    check("named-parse", dmd.parse_css_color("rebeccapurple")["hex"] == "#663399")
    ok = dmd.parse_css_color("oklch(0.7 0.15 250)")
    check("oklch-parse", ok is not None and ok["hex"].startswith("#"), str(ok))
    check("bad-color", dmd.parse_css_color("not-a-color") is None)

    # --- contrast (white on black ~21:1) ---
    black = dmd.parse_css_color("#000000")
    white = dmd.parse_css_color("#ffffff")
    r = dmd.contrast_ratio(black, white)
    check("contrast-21", abs(r - 21.0) < 0.05, f"got {r}")

    # --- clean doc lints with no errors/warnings ---
    res = dmd.lint(CLEAN)
    check("clean-no-errors", res["summary"]["errors"] == 0, str(res["summary"]))
    check("clean-no-warnings", res["summary"]["warnings"] == 0, str(res["summary"]))
    msgs = [f["message"] for f in res["findings"]]
    check("clean-token-summary", any("Design system defines" in m for m in msgs), str(msgs))

    # --- broken reference -> error ---
    res = dmd.lint(BROKEN)
    check("broken-ref-error", res["summary"]["errors"] == 1, str(res["summary"]))
    check("broken-ref-msg",
          any("does not resolve" in f["message"] for f in res["findings"]))

    # --- low contrast -> warning ---
    res = dmd.lint(LOWCONTRAST)
    check("low-contrast-warn",
          any("below WCAG AA" in f["message"] for f in res["findings"]),
          str([f["message"] for f in res["findings"]]))

    # --- missing primary ---
    res = dmd.lint('---\nname: X\ncolors:\n  accent: "#f00"\n---\n## Colors\nx\n')
    check("missing-primary",
          any("No 'primary' color" in f["message"] for f in res["findings"]))

    # --- section order ---
    res = dmd.lint('---\nname: X\ncolors:\n  primary: "#000"\n---\n'
                   '## Typography\nx\n## Colors\ny\n')
    check("section-order",
          any("out of order" in f["message"] for f in res["findings"]))

    # --- unknown key typo ---
    res = dmd.lint('---\nname: X\ncolours:\n  primary: "#000"\n---\n## Colors\nx\n')
    check("unknown-key",
          any('did you mean "colors"' in f["message"] for f in res["findings"]))

    # --- duplicate key (recoverable warning) ---
    dup = '---\nname: A\ncolors:\n  primary: "#000"\n---\n```yaml\ncolors:\n  x: "#fff"\n```\n'
    res = dmd.lint(dup)
    check("dup-key-warn",
          res["summary"]["errors"] == 0 and any("defined in both" in f["message"] for f in res["findings"]),
          str(res["summary"]))

    # --- diff regression ---
    d = dmd.diff(CLEAN, BROKEN)
    check("diff-regression", d["regression"] is True, str(d["findings"]))
    d2 = dmd.diff(CLEAN, CLEAN)
    check("diff-no-regression", d2["regression"] is False)
    check("diff-no-token-change", d2["tokens"]["colors"]["modified"] == [])

    # --- exports ---
    m = dmd.lint(CLEAN)["_model"]
    tw = dmd.export_json_tailwind(m)
    check("export-tailwind-color", tw["theme"]["extend"]["colors"].get("primary") == "#1a1c1e",
          str(tw["theme"]["extend"]["colors"]))
    css = dmd.export_css_tailwind(m)
    check("export-css-var", "--color-primary: #1a1c1e;" in css, css)
    dt = dmd.export_dtcg(m, "Test")
    check("export-dtcg-schema", dt["$schema"].endswith("format.json"))
    check("export-dtcg-color", "primary" in dt["color"])

    # --- bundled official examples (if present) lint cleanly (0 errors) ---
    ex_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "assets", "examples")
    if os.path.isdir(ex_dir):
        for fn in sorted(os.listdir(ex_dir)):
            if fn.endswith(".md"):
                with open(os.path.join(ex_dir, fn), encoding="utf-8") as f:
                    r = dmd.lint(f.read())
                check(f"example-{fn}-no-errors", r["summary"]["errors"] == 0, str(r["summary"]))

    print(f"\nselftest: {PASS} passed, {FAIL} failed")
    for fl in FAILURES:
        print("  FAIL:", fl)
    return 0 if FAIL == 0 else 1


if __name__ == "__main__":
    sys.exit(run())
