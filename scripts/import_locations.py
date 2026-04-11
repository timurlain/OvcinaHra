#!/usr/bin/env python3
"""Parse Ovčina LARP location MD files and output JSON.

Usage:
    python scripts/import_locations.py --parse-only <source_directory>
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path


# ---------------------------------------------------------------------------
# ID helpers
# ---------------------------------------------------------------------------

_RE_MAIN = re.compile(r"^(\d+)-(.+)\.md$")
_RE_MORIA = re.compile(r"^m(\d+)-(.+)\.md$")


def _file_id(filename: str) -> int | None:
    """Return numeric ID for a location file, or None if it should be skipped."""
    m = _RE_MAIN.match(filename)
    if m:
        return int(m.group(1))
    m = _RE_MORIA.match(filename)
    if m:
        return 81 + int(m.group(1))
    return None


# ---------------------------------------------------------------------------
# LocationKind mapping
# ---------------------------------------------------------------------------

def _location_kind(typ: str | None) -> str:
    """Map the Czech 'Typ' value to a LocationKind enum string."""
    if typ is None:
        return "Dungeon"  # Moria locations have no Typ line

    t = typ.lower()
    if "město" in t or "království" in t:
        return "Town"
    if "vesnice" in t:
        return "Village"
    if "magická" in t:
        return "Magical"
    if "hobití" in t:
        return "Hobbit"
    if "dungeon" in t or "temná pevnost" in t or "dračí" in t:
        return "Dungeon"
    return "PointOfInterest"


# ---------------------------------------------------------------------------
# MD parser
# ---------------------------------------------------------------------------

_RE_HEADER = re.compile(r"^###\s+(?:M?\d+)\.\s+(.+)$")
_RE_TYP_OBLAST = re.compile(
    r"^-\s+\*\*Typ:\*\*\s*(.+?)\s*\|\s*\*\*Oblast:\*\*\s*(.+)$"
)


def _extract_field(line: str, label: str) -> str | None:
    """Extract the value after '- **Label:** ' from a single line."""
    prefix = f"- **{label}:**"
    if line.startswith(prefix):
        return line[len(prefix):].strip()
    return None


def parse_location(path: Path) -> dict | None:
    """Parse a single location MD file into a dict."""
    filename = path.name

    loc_id = _file_id(filename)
    if loc_id is None:
        return None

    text = path.read_text(encoding="utf-8")
    lines = text.splitlines()

    name: str | None = None
    typ: str | None = None
    region: str | None = None
    description: str | None = None
    details: str | None = None
    game_potential: str | None = None
    mj_prompt: str | None = None

    i = 0
    while i < len(lines):
        line = lines[i]

        # Header: ### N. Name
        m = _RE_HEADER.match(line)
        if m:
            name = m.group(1).strip()
            i += 1
            continue

        # Typ / Oblast line
        m = _RE_TYP_OBLAST.match(line)
        if m:
            typ = m.group(1).strip()
            region = m.group(2).strip()
            i += 1
            continue

        # Simple fields
        val = _extract_field(line, "Popis")
        if val is not None:
            description = val
            i += 1
            continue

        val = _extract_field(line, "Podrobnosti")
        if val is not None:
            details = val
            i += 1
            continue

        val = _extract_field(line, "Herní potenciál")
        if val is not None:
            game_potential = val
            i += 1
            continue

        # Midjourney prompt block
        if line.strip().startswith("- **Midjourney prompt:**"):
            # Next line should be ```
            prompt_lines: list[str] = []
            i += 1
            # Skip the opening ```
            if i < len(lines) and lines[i].strip().startswith("```"):
                i += 1
            # Collect until closing ```
            while i < len(lines) and not lines[i].strip().startswith("```"):
                prompt_lines.append(lines[i])
                i += 1
            mj_prompt = "\n".join(prompt_lines).strip()
            i += 1
            continue

        i += 1

    if name is None:
        return None

    setup_notes: str | None = None
    if mj_prompt:
        setup_notes = f"Midjourney prompt:\n{mj_prompt}"

    return {
        "id": loc_id,
        "name": name,
        "locationKind": _location_kind(typ),
        "region": region,
        "description": description,
        "details": details,
        "gamePotential": game_potential,
        "setupNotes": setup_notes,
        "sourceFile": filename,
    }


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------

def main() -> None:
    parser = argparse.ArgumentParser(
        description="Parse Ovčina location MD files into JSON."
    )
    parser.add_argument(
        "source_dir",
        type=Path,
        help="Directory containing location .md files",
    )
    parser.add_argument(
        "--parse-only",
        action="store_true",
        help="Parse files and print JSON to stdout (no DB import)",
    )

    args = parser.parse_args()

    if not args.source_dir.is_dir():
        print(f"Error: {args.source_dir} is not a directory", file=sys.stderr)
        sys.exit(1)

    md_files = sorted(args.source_dir.glob("*.md"))

    locations: list[dict] = []
    for md_file in md_files:
        if md_file.name == "index.md":
            continue
        loc = parse_location(md_file)
        if loc is not None:
            locations.append(loc)

    # Sort by ID for stable output
    locations.sort(key=lambda x: x["id"])

    if args.parse_only:
        # Force UTF-8 on Windows where stdout defaults to cp1252
        out = sys.stdout
        if hasattr(out, "reconfigure"):
            out.reconfigure(encoding="utf-8")
        json.dump(locations, out, ensure_ascii=False, indent=2)
        out.write("\n")
    else:
        print(
            "DB import not yet implemented. Use --parse-only to verify parsing.",
            file=sys.stderr,
        )
        sys.exit(1)


if __name__ == "__main__":
    main()
