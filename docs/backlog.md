# OvčinaHra — Backlog & Future Opportunities

## Authentication — Registrace as Identity Provider

**Priority:** Before deployment

Reuse registrace-ovčina as the shared identity provider for all Ovčina apps instead of setting up Entra External ID separately.

### Why
- Registrace already authenticates users via Google, Seznam, and Microsoft OAuth
- Regular Entra ID only handles Microsoft accounts — that's why registrace had to build the other two providers separately
- Entra External ID (formerly B2C) supports custom OIDC providers but adds complexity and cost for ~40 organizers
- Building our own lightweight identity gateway is simpler and reusable

### Plan
1. Add a JWT token endpoint to registrace-ovčina (issues signed JWTs after successful OAuth login)
2. OvčinaHra validates those JWTs with standard `AddAuthentication().AddJwtBearer()`
3. Add `[Authorize]` to all API endpoints
4. Blazor WASM client gets MSAL-like auth state provider that redirects to registrace for login

### Extends to
- Any future Ovčina apps can reuse the same identity provider
- Single user pool across all apps, single sign-on possible later

---

## Game Sync — Registrace as Source of Truth (Option B)

**Priority:** When registrace integration begins

OvčinaHra games link to registrace-ovčina games via `ExternalGameId`. Registrace is the source of truth for game editions (players register *for* a game there).

### What's done
- `ExternalGameId` (nullable int) added to Game entity
- `POST /api/games/{id}/link` — sets ExternalGameId
- `DELETE /api/games/{id}/link` — clears it
- ExternalGameId visible in list and detail DTOs

### What's next (when registrace API is ready)
1. **Registrace side:** Add `GET /api/games` endpoint listing available games (id, name, edition, dates)
2. **OvčinaHra UI:** "Link s registrací" button on game edit page:
   - Calls registrace API to list games
   - Organizer picks the matching game
   - Sets `ExternalGameId`
3. OvčinaHra can still create games independently (draft/planning before registration opens)
4. Future: auto-sync game name/dates changes from registrace

### Flow
1. Organizer creates game world in OvčinaHra (planning phase, no ExternalGameId)
2. When registration opens, organizer clicks "Link s registrací" and picks the game
3. Both apps now share the same game identity via ExternalGameId
4. Auth tokens from registrace can include the game ID for context

---

## Map Bounding Box — World Boundaries per Game

**Priority:** Phase 6 (Map UI)

Each game edition plays on a real-world location (Czech countryside). The MapLibre map needs to center and zoom exactly on the game area, not show the whole world.

### What
- A **bounding box** (SW corner + NE corner, 4 coordinates) defines the game's physical play area
- Stored per Game entity — potentially different per edition, though currently the same
- Map UI uses the bounding box as initial view (center + zoom) and optionally constrains panning

### Data Model Change
Add to `Game` entity:
- `BoundsSouthWestLat`, `BoundsSouthWestLon` (decimal?)
- `BoundsNorthEastLat`, `BoundsNorthEastLon` (decimal?)
- Or a `MapBounds` value object with SW + NE corners

### UI
- Game settings page: organizer draws/adjusts bounding box on map
- All map views for that game auto-center and zoom to bounds
- Consider max zoom constraint to prevent zooming out beyond play area

---

## GPS Data Import — Old Ovčina Databases

**Priority:** Nice to have (Phase 13 — Excel Import)

Legacy Ovčina data lives in:
- `OneDrive/Databases/Ovcina/Ovcina.mdb` (Access, 9.8MB)
- `OneDrive/Databases/Ovcina/Ovcina1.mdf` (SQL Server, 144MB)

May contain real-world GPS coordinates for locations from previous game editions. Worth checking before manually entering all coordinates in the new app.
