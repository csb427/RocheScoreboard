#!/usr/bin/env python3
"""
catalog.py — browse the bundled catalog of reference DESIGN.md analyses (offline).

This script does NOT touch the network. It reads assets/catalog-index.json and
prints matching entries with their raw_url. To actually retrieve a chosen
DESIGN.md, fetch the raw_url with the web_fetch tool (per the skill workflow),
then adapt it.

Usage:
  python catalog.py list                 # all entries
  python catalog.py search <query...>    # match name/slug/description
  python catalog.py url <slug>           # print the raw_url for a slug
"""
from __future__ import annotations

import json
import os
import sys

INDEX = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "assets", "catalog-index.json")


def load():
    with open(INDEX, encoding="utf-8") as f:
        return json.load(f)


def main(argv=None):
    argv = argv if argv is not None else sys.argv[1:]
    if not argv:
        argv = ["list"]
    cmd, rest = argv[0], argv[1:]
    cat = load()
    designs = cat["designs"]
    if cmd == "list":
        for d in designs:
            print(f"{d['slug']:18s} {d['name']:16s} {d['description']}")
        print(f"\n{len(designs)} designs · source: {cat['source']}")
    elif cmd == "search":
        q = " ".join(rest).lower()
        hits = [d for d in designs if q in d["slug"].lower()
                or q in d["name"].lower() or q in d["description"].lower()]
        for d in hits:
            print(f"{d['slug']:18s} {d['name']:16s} {d['description']}")
            print(f"  fetch: {d['raw_url']}")
        if not hits:
            print(f"No matches for {q!r}. Try `python catalog.py list`.")
    elif cmd == "url":
        slug = rest[0] if rest else ""
        for d in designs:
            if d["slug"] == slug:
                print(d["raw_url"])
                return 0
        sys.stderr.write(f"Unknown slug {slug!r}. Run `python catalog.py list`.\n")
        return 1
    else:
        sys.stderr.write(__doc__)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
