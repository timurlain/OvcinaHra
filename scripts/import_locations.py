#!/usr/bin/env python3
"""Parse Ovčina LARP location MD files and optionally generate SQL migration.

Usage:
    python scripts/import_locations.py --parse-only <source_directory>
    python scripts/import_locations.py --generate-sql <source_directory>
    python scripts/import_locations.py --match-report <source_directory>
    python scripts/import_locations.py --generate-merge-sql <source_directory> [--confirmed-matches matches.json]
"""

from __future__ import annotations

import argparse
import difflib
import json
import re
import subprocess
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
# SQL helpers
# ---------------------------------------------------------------------------

# Offset applied to existing IDs to avoid collisions during migration
_ID_OFFSET = 100

# All tables that reference Locations.Id via a LocationId FK
_FK_TABLES = [
    "GameLocations",
    "SecretStashes",
    "Buildings",
    "QuestLocationLinks",
    "TreasureQuests",
    "CraftingRecipes",
]

# Docker exec command prefix for local DB access
_DOCKER_PSQL = [
    "docker", "exec", "-i", "ovcinahra-postgres",
    "psql", "-U", "ovcinahra", "-d", "ovcinahra", "-t", "-A",
]


def _sql_escape(value: str | None) -> str:
    """Escape a string value for PostgreSQL SQL literal, or return NULL."""
    if value is None:
        return "NULL"
    escaped = value.replace("'", "''")
    return f"'{escaped}'"


def _generate_sql(locations: list[dict]) -> str:
    """Generate a complete SQL migration script for inserting MD locations."""
    lines: list[str] = []
    lines.append("-- Auto-generated location migration script")
    lines.append("-- Shifts existing IDs by +{} and inserts MD-parsed locations".format(_ID_OFFSET))
    lines.append("")
    lines.append("BEGIN;")
    lines.append("")
    lines.append("-- Disable FK triggers for bulk update")
    lines.append("SET session_replication_role = 'replica';")
    lines.append("")

    # Shift existing location IDs
    lines.append("-- Shift existing location IDs by +{}".format(_ID_OFFSET))
    lines.append('-- First, clear ParentLocationId to avoid FK issues during shift')
    lines.append('CREATE TEMP TABLE _parent_map AS SELECT "Id", "ParentLocationId" FROM "Locations" WHERE "ParentLocationId" IS NOT NULL;')
    lines.append('UPDATE "Locations" SET "ParentLocationId" = NULL WHERE "ParentLocationId" IS NOT NULL;')
    lines.append("")

    # Shift FKs in child tables first (before changing Locations.Id)
    for table in _FK_TABLES:
        lines.append(
            f'UPDATE "{table}" SET "LocationId" = "LocationId" + {_ID_OFFSET} WHERE "LocationId" IS NOT NULL;'
        )
    lines.append("")

    # Shift Locations.Id itself
    lines.append(f'UPDATE "Locations" SET "Id" = "Id" + {_ID_OFFSET};')
    lines.append("")

    # Restore ParentLocationId with offset
    lines.append('UPDATE "Locations" l SET "ParentLocationId" = pm."ParentLocationId" + {} FROM _parent_map pm WHERE l."Id" = pm."Id" + {};'.format(_ID_OFFSET, _ID_OFFSET))
    lines.append('DROP TABLE _parent_map;')
    lines.append("")

    # Temporarily drop unique name index (old shifted records still have same names)
    lines.append('DROP INDEX IF EXISTS "IX_Locations_Name";')
    lines.append("")

    # Insert new locations from MD files
    lines.append("-- Insert MD-parsed locations with explicit IDs")
    for loc in locations:
        cols = '"Id", "Name", "LocationKind", "Region", "Description", "Details", "GamePotential", "SetupNotes"'
        vals = ", ".join([
            str(loc["id"]),
            _sql_escape(loc["name"]),
            _sql_escape(loc["locationKind"]),
            _sql_escape(loc.get("region")),
            _sql_escape(loc.get("description")),
            _sql_escape(loc.get("details")),
            _sql_escape(loc.get("gamePotential")),
            _sql_escape(loc.get("setupNotes")),
        ])
        lines.append(f'INSERT INTO "Locations" ({cols}) VALUES ({vals});')

    lines.append("")
    lines.append("-- Re-enable FK triggers")
    lines.append("SET session_replication_role = 'origin';")
    lines.append("")
    lines.append("-- Reset sequence to max ID")
    lines.append("""SELECT setval(pg_get_serial_sequence('"Locations"', 'Id'), (SELECT MAX("Id") FROM "Locations"));""")
    lines.append("")
    lines.append("COMMIT;")

    return "\n".join(lines)


def _query_db(sql: str) -> str:
    """Execute a SQL query via docker exec psql and return stdout."""
    result = subprocess.run(
        _DOCKER_PSQL,
        input=sql,
        capture_output=True,
        text=True,
        encoding="utf-8",
    )
    if result.returncode != 0:
        print(f"psql error: {result.stderr}", file=sys.stderr)
        sys.exit(1)
    return result.stdout.strip()


def _get_db_locations() -> list[dict]:
    """Fetch all existing locations from the DB via docker exec."""
    sql = 'SELECT "Id", "Name", latitude, longitude, "ImagePath", "PlacementPhotoPath", "NpcInfo" FROM "Locations" ORDER BY "Id";'
    raw = _query_db(sql)
    if not raw:
        return []

    locations = []
    for line in raw.splitlines():
        parts = line.split("|")
        if len(parts) < 7:
            continue
        locations.append({
            "id": int(parts[0].strip()),
            "name": parts[1].strip(),
            "latitude": parts[2].strip() or None,
            "longitude": parts[3].strip() or None,
            "imagePath": parts[4].strip() or None,
            "placementPhotoPath": parts[5].strip() or None,
            "npcInfo": parts[6].strip() or None,
        })
    return locations


def _match_report(md_locations: list[dict]) -> dict:
    """Generate a name-matching report between MD locations and DB locations.

    Returns a dict with keys: exact, fuzzy, new, leftover, db_locations.
    """
    db_locations = _get_db_locations()
    db_by_name: dict[str, dict] = {loc["name"]: loc for loc in db_locations}
    db_names = list(db_by_name.keys())

    exact_matches: list[dict] = []     # {"md": ..., "db": ...}
    fuzzy_matches: list[dict] = []     # {"md": ..., "db": ..., "ratio": float}
    new_locations: list[dict] = []     # MD locations with no DB counterpart
    matched_db_ids: set[int] = set()

    for md_loc in md_locations:
        md_name = md_loc["name"]

        # Try exact match
        if md_name in db_by_name:
            db_loc = db_by_name[md_name]
            exact_matches.append({"md": md_loc, "db": db_loc})
            matched_db_ids.add(db_loc["id"])
            continue

        # Try fuzzy match
        best_ratio = 0.0
        best_match: dict | None = None
        for db_name in db_names:
            ratio = difflib.SequenceMatcher(None, md_name.lower(), db_name.lower()).ratio()
            if ratio > best_ratio:
                best_ratio = ratio
                best_match = db_by_name[db_name]

        if best_ratio > 0.7 and best_match is not None and best_match["id"] not in matched_db_ids:
            fuzzy_matches.append({
                "md": md_loc,
                "db": best_match,
                "ratio": best_ratio,
            })
            matched_db_ids.add(best_match["id"])
        else:
            new_locations.append(md_loc)

    leftover = [loc for loc in db_locations if loc["id"] not in matched_db_ids]

    return {
        "exact": exact_matches,
        "fuzzy": fuzzy_matches,
        "new": new_locations,
        "leftover": leftover,
        "db_locations": db_locations,
    }


def _print_match_report(report: dict) -> None:
    """Print a human-readable match report."""
    out = sys.stdout
    if hasattr(out, "reconfigure"):
        out.reconfigure(encoding="utf-8")

    exact = report["exact"]
    fuzzy = report["fuzzy"]
    new = report["new"]
    leftover = report["leftover"]

    print(f"=== Location Match Report ===")
    print(f"DB locations: {len(report['db_locations'])}")
    print(f"MD locations: {len(exact) + len(fuzzy) + len(new)}")
    print()

    print(f"--- Exact matches: {len(exact)} ---")
    for m in exact:
        has_data = []
        if m["db"].get("latitude"):
            has_data.append("GPS")
        if m["db"].get("imagePath"):
            has_data.append("IMG")
        if m["db"].get("placementPhotoPath"):
            has_data.append("PHOTO")
        if m["db"].get("npcInfo"):
            has_data.append("NPC")
        data_str = f" [{', '.join(has_data)}]" if has_data else ""
        print(f"  MD #{m['md']['id']:3d} '{m['md']['name']}' == DB #{m['db']['id']:3d}{data_str}")
    print()

    print(f"--- Fuzzy matches: {len(fuzzy)} (need manual confirmation) ---")
    for m in fuzzy:
        print(f"  MD #{m['md']['id']:3d} '{m['md']['name']}' ~~ DB #{m['db']['id']:3d} '{m['db']['name']}' (ratio={m['ratio']:.2f})")
    print()

    print(f"--- New locations (no DB match): {len(new)} ---")
    for loc in new:
        print(f"  MD #{loc['id']:3d} '{loc['name']}'")
    print()

    print(f"--- Leftover DB locations (no MD counterpart): {len(leftover)} ---")
    for loc in leftover:
        has_data = []
        if loc.get("latitude"):
            has_data.append("GPS")
        if loc.get("imagePath"):
            has_data.append("IMG")
        if loc.get("placementPhotoPath"):
            has_data.append("PHOTO")
        if loc.get("npcInfo"):
            has_data.append("NPC")
        data_str = f" [{', '.join(has_data)}]" if has_data else ""
        print(f"  DB #{loc['id']:3d} '{loc['name']}'{data_str}")


def _generate_merge_sql(
    md_locations: list[dict],
    report: dict,
    confirmed_matches: dict[str, int] | None = None,
) -> str:
    """Generate SQL that inserts MD locations AND merges data from matched old ones.

    The merge copies GPS, ImagePath, PlacementPhotoPath, NpcInfo from old (shifted)
    records to new records, then deletes the old matched records.
    """
    # Start with the base SQL (shift + insert)
    base_sql = _generate_sql(md_locations)

    # Build merge UPDATE + DELETE statements
    merge_lines: list[str] = []
    merge_lines.append("")
    merge_lines.append("-- =============================================")
    merge_lines.append("-- Merge data from matched old locations")
    merge_lines.append("-- =============================================")
    merge_lines.append("BEGIN;")
    merge_lines.append("SET session_replication_role = 'replica';")
    merge_lines.append("")

    # Collect all matches: exact + confirmed fuzzy
    matches: list[tuple[int, int]] = []  # (new_id, old_shifted_id)

    for m in report["exact"]:
        new_id = m["md"]["id"]
        old_shifted_id = m["db"]["id"] + _ID_OFFSET
        matches.append((new_id, old_shifted_id))

    # Add confirmed fuzzy matches
    if confirmed_matches:
        for old_name, new_id in confirmed_matches.items():
            # Find the old DB record by name
            for db_loc in report["db_locations"]:
                if db_loc["name"] == old_name:
                    old_shifted_id = db_loc["id"] + _ID_OFFSET
                    matches.append((new_id, old_shifted_id))
                    break

    # Generate UPDATE statements to copy data from old to new
    for new_id, old_shifted_id in matches:
        merge_lines.append(f"-- Merge old #{old_shifted_id - _ID_OFFSET} -> new #{new_id}")
        merge_lines.append(
            f'UPDATE "Locations" new_loc SET '
            f'latitude = COALESCE(new_loc.latitude, old_loc.latitude), '
            f'longitude = COALESCE(new_loc.longitude, old_loc.longitude), '
            f'"ImagePath" = COALESCE(new_loc."ImagePath", old_loc."ImagePath"), '
            f'"PlacementPhotoPath" = COALESCE(new_loc."PlacementPhotoPath", old_loc."PlacementPhotoPath"), '
            f'"NpcInfo" = COALESCE(new_loc."NpcInfo", old_loc."NpcInfo") '
            f'FROM "Locations" old_loc '
            f'WHERE new_loc."Id" = {new_id} AND old_loc."Id" = {old_shifted_id};'
        )

    merge_lines.append("")

    # Delete old matched records (shifted IDs)
    if matches:
        old_ids = ", ".join(str(old_id) for _, old_id in matches)
        # First update FK references from old to new where possible
        merge_lines.append("-- Re-point FK references from old matched IDs to new IDs")
        for new_id, old_shifted_id in matches:
            for table in _FK_TABLES:
                merge_lines.append(
                    f'UPDATE "{table}" SET "LocationId" = {new_id} WHERE "LocationId" = {old_shifted_id};'
                )
        merge_lines.append("")

        # Re-point ParentLocationId references
        merge_lines.append("-- Re-point ParentLocationId from old matched to new")
        for new_id, old_shifted_id in matches:
            merge_lines.append(
                f'UPDATE "Locations" SET "ParentLocationId" = {new_id} WHERE "ParentLocationId" = {old_shifted_id};'
            )
        merge_lines.append("")

        merge_lines.append("-- Delete old matched records")
        merge_lines.append(f'DELETE FROM "Locations" WHERE "Id" IN ({old_ids});')

    merge_lines.append("")
    merge_lines.append("-- Recreate unique name index (safe now that old matched records are deleted)")
    merge_lines.append("-- Note: leftover old locations may still cause conflicts if they share names")
    merge_lines.append("-- with new ones. Rename them first.")
    # Rename leftover old locations that conflict with new names
    new_names = {loc["name"] for loc in md_locations}
    merge_lines.append("-- Rename conflicting leftover old locations")
    merge_lines.append(f'UPDATE "Locations" SET "Name" = "Name" || \' (archiv)\' WHERE "Id" > 100 AND "Name" IN ({", ".join(_sql_escape(n) for n in sorted(new_names))});')
    merge_lines.append('CREATE UNIQUE INDEX "IX_Locations_Name" ON "Locations" ("Name");')
    merge_lines.append("")
    merge_lines.append("SET session_replication_role = 'origin';")
    merge_lines.append("")
    merge_lines.append("-- Reset sequence")
    merge_lines.append("""SELECT setval(pg_get_serial_sequence('"Locations"', 'Id'), (SELECT MAX("Id") FROM "Locations"));""")
    merge_lines.append("")
    merge_lines.append("COMMIT;")

    return base_sql + "\n" + "\n".join(merge_lines)


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------

def _parse_locations(source_dir: Path) -> list[dict]:
    """Parse all MD files in source_dir and return sorted location dicts."""
    md_files = sorted(source_dir.glob("*.md"))

    locations: list[dict] = []
    for md_file in md_files:
        if md_file.name == "index.md":
            continue
        loc = parse_location(md_file)
        if loc is not None:
            locations.append(loc)

    locations.sort(key=lambda x: x["id"])
    return locations


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Parse Ovčina location MD files into JSON or generate SQL migration."
    )
    parser.add_argument(
        "source_dir",
        type=Path,
        help="Directory containing location .md files",
    )

    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument(
        "--parse-only",
        action="store_true",
        help="Parse files and print JSON to stdout (no DB import)",
    )
    mode.add_argument(
        "--generate-sql",
        action="store_true",
        help="Generate SQL migration script to stdout",
    )
    mode.add_argument(
        "--match-report",
        action="store_true",
        help="Compare MD locations with DB and print a match report",
    )
    mode.add_argument(
        "--generate-merge-sql",
        action="store_true",
        help="Generate SQL that inserts + merges data from matched old locations",
    )

    parser.add_argument(
        "--confirmed-matches",
        type=Path,
        default=None,
        help="JSON file mapping old DB name -> new MD ID for confirmed fuzzy matches (for --generate-merge-sql)",
    )

    args = parser.parse_args()

    if not args.source_dir.is_dir():
        print(f"Error: {args.source_dir} is not a directory", file=sys.stderr)
        sys.exit(1)

    locations = _parse_locations(args.source_dir)

    if args.parse_only:
        # Force UTF-8 on Windows where stdout defaults to cp1252
        out = sys.stdout
        if hasattr(out, "reconfigure"):
            out.reconfigure(encoding="utf-8")
        json.dump(locations, out, ensure_ascii=False, indent=2)
        out.write("\n")

    elif args.generate_sql:
        out = sys.stdout
        if hasattr(out, "reconfigure"):
            out.reconfigure(encoding="utf-8")
        print(_generate_sql(locations))

    elif args.match_report:
        report = _match_report(locations)
        _print_match_report(report)

    elif args.generate_merge_sql:
        report = _match_report(locations)

        confirmed: dict[str, int] | None = None
        if args.confirmed_matches:
            if not args.confirmed_matches.is_file():
                print(f"Error: {args.confirmed_matches} not found", file=sys.stderr)
                sys.exit(1)
            confirmed = json.loads(args.confirmed_matches.read_text(encoding="utf-8"))

        out = sys.stdout
        if hasattr(out, "reconfigure"):
            out.reconfigure(encoding="utf-8")
        print(_generate_merge_sql(locations, report, confirmed))


if __name__ == "__main__":
    main()
