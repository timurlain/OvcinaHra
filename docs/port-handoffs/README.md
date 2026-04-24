# Port handoffs — tinkerer session starters

Each file here is a self-contained prompt for a fresh `hra-ovcina-tinkerer` session to port one Claude Design mockup into production Blazor on its own branch.

**Why this folder, not `.claude/restart-prompts/`** — that directory gets cleaned between sessions. These port specs need to survive across agents + branches, so they live under `docs/` alongside their design briefs.

## How to use

1. Open a new Claude Code session.
2. Tell the agent: *"Load the hra-ovcina-tinkerer skill and read `docs/port-handoffs/hra-ovcina-{feature}-port.md` for the full handoff."*
3. The agent branches, implements, tests, opens PR.

## Index

| File | What ports |
|---|---|
| `hra-ovcina-secret-stash-list-port.md` | `/secret-stashes` gallery (5:7 MTG tiles replace DxGrid) |
| `hra-ovcina-monster-list-port.md` | `/monsters` card-row bestiary (drops 13-col DxGrid) |
| `hra-ovcina-monster-detail-port.md` | `/monsters/{id}` with 5 tabs incl. Bojovka presenter |
| `hra-ovcina-sidebar-port.md` | NavMenu redesign — `Tato hra / Katalog` tab switch |
| `hra-ovcina-item-list-port.md` | `/items` grid with Foto · Typ-dot · 4 class pips · Flags box |
| `hra-ovcina-item-detail-port.md` | `/items/{id}` with 5 tabs (Karta MTG 5:7, Tvorba recipe graph, Výskyt, Obchod) |
| `hra-ovcina-treasures-planning-port.md` | `/treasures` dashboard with pie-wedge pin map + drag-drop + stage palette |

## Companion folder

Design briefs (the input to Claude Design) live in `docs/design/dialogs/`. Each port handoff here references its sibling brief.
