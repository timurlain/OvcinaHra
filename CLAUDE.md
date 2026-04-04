# OvcinaHra — World App

## What is this?
Organizer-only CRUD web app for managing the Ovčina LARP game world. Locations, items, monsters, quests, treasures — all in one place with an interactive MapLibre map.

## Architecture
- **API:** ASP.NET Core Web API (`src/OvcinaHra.Api/`)
- **Client:** Blazor WASM PWA (`src/OvcinaHra.Client/`)
- **Shared:** DTOs, enums, value objects (`src/OvcinaHra.Shared/`)
- **Import:** One-time Excel migration CLI (`src/OvcinaHra.Import/`)
- **Tests:** API integration tests (`tests/OvcinaHra.Api.Tests/`), E2E Playwright (`tests/OvcinaHra.E2E/`)

## Tech Stack
- .NET 10, EF Core + Npgsql (PostgreSQL), Azure Blob Storage
- Auth: Entra External ID (MS/Google/Seznam OAuth + magic-link)
- Map: MapLibre GL JS via JS interop
- Search: PostgreSQL FTS with Czech dictionary
- CI/CD: GitHub Actions
- Deploy: API on Azure Web App, Client on Azure Static Web Apps
- URL: hra.ovcina.cz

## Key commands
- Build: `dotnet build OvcinaHra.slnx`
- Test: `dotnet test OvcinaHra.slnx`
- Run API: `dotnet run --project src/OvcinaHra.Api`
- Run Client: `dotnet run --project src/OvcinaHra.Client`
- Add migration: `dotnet ef migrations add <Name> --project src/OvcinaHra.Api`
- Update DB: `dotnet ef database update --project src/OvcinaHra.Api`

## Conventions
- Code in English, UI in Czech
- Blob storage: store path keys (e.g. `"locations/14/fantasy.jpg"`), resolve URLs in API
- Use composite PKs on join tables, no surrogate IDs
- Minimal API endpoints with MapGroup
- Value objects as EF Core owned types: GpsCoordinates, ClassRequirements, CombatStats
- PostgreSQL FTS with tsvector generated columns + GIN indexes
- Domain model: see `docs/plans/2026-04-04-world-app-domain-model.md` (in the Ovčina project, not this repo)
