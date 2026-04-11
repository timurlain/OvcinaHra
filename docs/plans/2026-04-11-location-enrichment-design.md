# Location Enrichment — Design

> Date: 2026-04-11 | Author: Tasha + Tomáš

## Goal

Enrich the Location domain entity with lore fields from the Balinova pozvánka game location documents (83 MD files). Remap location IDs so the file index (1-81, m1-m7) becomes the database ID, making sort order implicit.

## Data Source

`OneDrive/Bridge/Ovčina/games/2026 05 01 Balinova pozvánka/lokace/*.md`

Each MD file contains:
- Index + Name (e.g. "1. Aradhrynd")
- Typ / Oblast (type / region)
- Popis (description — player-facing)
- Podrobnosti (details — player-facing lore)
- Herní potenciál (game potential — organizer-only)
- Midjourney prompt (append to SetupNotes)

## Domain Model Changes

Add 3 nullable string fields to `Location`:

| Field | Source | Visibility |
|-------|--------|------------|
| `Details` | Podrobnosti | Player-facing |
| `GamePotential` | Herní potenciál | Organizer-only |
| `Region` | Oblast | Player-facing |

No `SortIndex` — the `Id` is the sort order.

## ID Remapping Strategy

1. Shift all existing location IDs to 101+ (cascade to all FK references)
2. Insert MD locations with explicit IDs: 1-81 (main), 82-88 (Moria m1-m7), 89-91 reserved
3. Match existing locations by name — merge GPS, ImagePath, PlacementPhotoPath, NpcInfo from old record
4. Fuzzy matches require manual confirmation
5. Leftover old locations (101+ with no match) stay as-is — previous game content

FK tables to cascade: GameLocations, SecretStashes, Buildings, QuestLocationLinks, TreasureQuests, Locations (ParentLocationId).

## LocationKind Mapping

| MD Typ | Enum |
|--------|------|
| Město / hráčské království | Town |
| Vesnice | Village |
| Magická | Magical |
| Hobití | Hobbit |
| Dungeon | Dungeon |
| Temná pevnost, Dračí doupě | Dungeon |
| Standardní, NPC město, Obchodní, Posvátné místo | PointOfInterest |
| Moria | Dungeon |

## DTO Changes

- `LocationListDto`: add `Region`
- `LocationDetailDto`: add `Details`, `GamePotential`, `Region`
- `CreateLocationDto`: add `Details`, `GamePotential`, `Region`
- `UpdateLocationDto`: add `Details`, `GamePotential`, `Region`

## Full-Text Search

Update tsvector computed column to include `Details` and `Region` alongside existing `Name`, `Description`, `NpcInfo`, `SetupNotes`.

## UI Changes

### Location Popup
- Width: 900px (wider for lore text)
- Two tabs via `DxTabs`:
  - **Základní** (Basic): Name, LocationKind, Region, GPS, ImagePath, PlacementPhotoPath, NpcInfo, SetupNotes, ParentLocationId
  - **Lore**: Description (read-only), Details (read-only), GamePotential (editable)
- "Upravit texty" button toggles Description + Details to editable (client-side `bool isLoreEditable`)

### Location Grid
- Add `Region` as visible column
- No grid columns for Details/GamePotential (too long)

## Import Script

One-time Python script in `/scripts/import_locations.py`:
1. Connects to production DB
2. Shifts existing IDs to 101+
3. Parses MD files
4. Matches by name (exact → auto-merge, fuzzy → ask user)
5. Inserts with explicit IDs
6. Resets PG sequence

## Not In Scope

- No new enums or LocationKind changes
- No API endpoint for import
- No changes to map, variants, game assignment, or other features
