using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Api.Endpoints;

/// <summary>
/// Development-only seed endpoints for importing initial data.
/// </summary>
public static class SeedEndpoints
{
    public static RouteGroupBuilder MapSeedEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/seed").WithTags("Seed");

        group.MapPost("/games", SeedGames);
        group.MapPost("/locations", SeedLocations);
        group.MapPost("/location-descriptions", SeedLocationDescriptions);

        return group;
    }

    private static async Task<Ok<SeedResult>> SeedGames(WorldDbContext db)
    {
        if (await db.Games.AnyAsync())
            return TypedResults.Ok(new SeedResult(0, "Hry už existují. Smaž je nejdřív."));

        var games = new List<Game>
        {
            // Early era (dates unknown, using Jan 1 placeholder)
            new() { Name = "Ovčina", Edition = 1, StartDate = new DateOnly(1996, 1, 1), EndDate = new DateOnly(1996, 1, 1), Status = GameStatus.Archived },
            new() { Name = "Ovčina", Edition = 2, StartDate = new DateOnly(1996, 6, 1), EndDate = new DateOnly(1996, 6, 1), Status = GameStatus.Archived },
            new() { Name = "Ovčina", Edition = 3, StartDate = new DateOnly(1997, 1, 1), EndDate = new DateOnly(1997, 1, 1), Status = GameStatus.Archived },
            new() { Name = "Ovčina", Edition = 4, StartDate = new DateOnly(1997, 6, 1), EndDate = new DateOnly(1997, 6, 1), Status = GameStatus.Archived },
            new() { Name = "Ovčina", Edition = 5, StartDate = new DateOnly(1998, 1, 1), EndDate = new DateOnly(1998, 1, 1), Status = GameStatus.Archived },
            new() { Name = "Ovčina", Edition = 6, StartDate = new DateOnly(1998, 6, 1), EndDate = new DateOnly(1998, 6, 1), Status = GameStatus.Archived },
            new() { Name = "Ovčina", Edition = 7, StartDate = new DateOnly(1999, 1, 1), EndDate = new DateOnly(1999, 1, 1), Status = GameStatus.Archived },
            new() { Name = "Ovčina", Edition = 8, StartDate = new DateOnly(1999, 6, 1), EndDate = new DateOnly(1999, 6, 1), Status = GameStatus.Archived },
            new() { Name = "Ovčina", Edition = 9, StartDate = new DateOnly(2000, 1, 1), EndDate = new DateOnly(2000, 1, 1), Status = GameStatus.Archived },
            new() { Name = "Ovčina", Edition = 10, StartDate = new DateOnly(2000, 6, 1), EndDate = new DateOnly(2000, 6, 1), Status = GameStatus.Archived },
            new() { Name = "Ovčina", Edition = 11, StartDate = new DateOnly(2000, 9, 1), EndDate = new DateOnly(2000, 9, 1), Status = GameStatus.Archived },
            new() { Name = "Ovčina", Edition = 12, StartDate = new DateOnly(2001, 5, 1), EndDate = new DateOnly(2001, 5, 1), Status = GameStatus.Archived },
            new() { Name = "Ovčina", Edition = 13, StartDate = new DateOnly(2001, 8, 1), EndDate = new DateOnly(2001, 8, 1), Status = GameStatus.Archived },
            new() { Name = "Ovčina", Edition = 14, StartDate = new DateOnly(2001, 10, 1), EndDate = new DateOnly(2001, 10, 1), Status = GameStatus.Archived },
            // Known era (from legacy DB)
            new() { Name = "4 čarodějové", Edition = 15, StartDate = new DateOnly(2002, 6, 1), EndDate = new DateOnly(2002, 6, 1), Status = GameStatus.Archived },
            new() { Name = "Noldo a prsteny", Edition = 16, StartDate = new DateOnly(2002, 10, 11), EndDate = new DateOnly(2002, 10, 13), Status = GameStatus.Archived },
            new() { Name = "Ovčina", Edition = 17, StartDate = new DateOnly(2003, 5, 1), EndDate = new DateOnly(2003, 5, 1), Status = GameStatus.Archived },
            new() { Name = "Ovčina", Edition = 18, StartDate = new DateOnly(2003, 9, 11), EndDate = new DateOnly(2003, 9, 13), Status = GameStatus.Archived },
            new() { Name = "Úsvit", Edition = 19, StartDate = new DateOnly(2004, 6, 25), EndDate = new DateOnly(2004, 6, 28), Status = GameStatus.Archived },
            new() { Name = "Ovčina", Edition = 20, StartDate = new DateOnly(2004, 10, 1), EndDate = new DateOnly(2004, 10, 3), Status = GameStatus.Archived },
            new() { Name = "Nová země", Edition = 21, StartDate = new DateOnly(2005, 7, 1), EndDate = new DateOnly(2005, 7, 1), Status = GameStatus.Archived },
            new() { Name = "Vůně ohňů", Edition = 22, StartDate = new DateOnly(2005, 10, 28), EndDate = new DateOnly(2005, 10, 30), Status = GameStatus.Archived },
            // Pause 2006–2018
            new() { Name = "Ovčina", Edition = 23, StartDate = new DateOnly(2019, 9, 14), EndDate = new DateOnly(2019, 9, 14), Status = GameStatus.Archived },
            new() { Name = "Ovčina", Edition = 24, StartDate = new DateOnly(2020, 9, 18), EndDate = new DateOnly(2020, 9, 19), Status = GameStatus.Archived },
            new() { Name = "Velká Slavnost", Edition = 25, StartDate = new DateOnly(2021, 9, 11), EndDate = new DateOnly(2021, 9, 11), Status = GameStatus.Archived },
            new() { Name = "Dlouhá Zima", Edition = 26, StartDate = new DateOnly(2022, 9, 10), EndDate = new DateOnly(2022, 9, 10), Status = GameStatus.Archived },
            new() { Name = "Dlouhá Zima II", Edition = 27, StartDate = new DateOnly(2023, 5, 6), EndDate = new DateOnly(2023, 5, 6), Status = GameStatus.Archived },
            new() { Name = "Za pokladem ze Severní spouště", Edition = 28, StartDate = new DateOnly(2024, 5, 4), EndDate = new DateOnly(2024, 5, 5), Status = GameStatus.Archived },
            new() { Name = "S jídlem roste chuť", Edition = 29, StartDate = new DateOnly(2025, 5, 3), EndDate = new DateOnly(2025, 5, 4), Status = GameStatus.Archived },
            new() { Name = "Balinova pozvánka", Edition = 30, StartDate = new DateOnly(2026, 5, 1), EndDate = new DateOnly(2026, 5, 2), Status = GameStatus.Active },
        };

        db.Games.AddRange(games);
        await db.SaveChangesAsync();
        return TypedResults.Ok(new SeedResult(games.Count, $"Importováno {games.Count} her."));
    }

    private static async Task<Ok<SeedResult>> SeedLocations(WorldDbContext db)
    {
        if (await db.Locations.AnyAsync())
            return TypedResults.Ok(new SeedResult(0, "Lokace už existují. Smaž je nejdřív."));

        // Default GPS = game area center. User will drag to correct positions on map.
        // Each entity needs its own owned-type instance — EF Core change tracker
        // loses coordinates when the same GpsCoordinates reference is shared.
        OvcinaHra.Shared.Domain.ValueObjects.GpsCoordinates center() => new(49.4532m, 18.0203m);

        // 83 locations from OvčinaTabulky.xlsx (edition 29 "S jídlem roste chuť")
        var locations = new List<Location>
        {
            new() { Name = "Aradhryand", LocationKind = LocationKind.Town, Coordinates = center() },
            new() { Name = "Azanulinbar-dum", LocationKind = LocationKind.Town, Coordinates = center() },
            new() { Name = "Esgaroth", LocationKind = LocationKind.Town, Coordinates = center() },
            new() { Name = "Nový Arnor", LocationKind = LocationKind.Town, Coordinates = center() },
            new() { Name = "Bílý kámen - Quest", LocationKind = LocationKind.Magical, Coordinates = center() },
            new() { Name = "Ceber Fanuin", LocationKind = LocationKind.Magical, Coordinates = center() },
            new() { Name = "Elfí chrám", LocationKind = LocationKind.Magical, Coordinates = center() },
            new() { Name = "Hraniční kameny", LocationKind = LocationKind.Magical, Coordinates = center() },
            new() { Name = "Kamenná věž - QUEST", LocationKind = LocationKind.Magical, Coordinates = center() },
            new() { Name = "Kamenný kruh", LocationKind = LocationKind.Magical, Coordinates = center() },
            new() { Name = "Lesní oltář", LocationKind = LocationKind.Magical, Coordinates = center() },
            new() { Name = "Proměnlivý strom", LocationKind = LocationKind.Magical, Coordinates = center() },
            new() { Name = "Rhosgobel", LocationKind = LocationKind.Magical, Coordinates = center() },
            new() { Name = "Sarn Gowiring", LocationKind = LocationKind.Magical, Coordinates = center() },
            new() { Name = "Věčný plamen - Quest", LocationKind = LocationKind.Magical, Coordinates = center() },
            new() { Name = "Caras Amarth", LocationKind = LocationKind.Village, Coordinates = center() },
            new() { Name = "Caras Amarth - Karáskov", LocationKind = LocationKind.Village, Coordinates = center() },
            new() { Name = "Dol - Město", LocationKind = LocationKind.Village, Coordinates = center() },
            new() { Name = "Dol - Ruiny města", LocationKind = LocationKind.Village, Coordinates = center() },
            new() { Name = "Dolany - Obnovený důl", LocationKind = LocationKind.Village, Coordinates = center() },
            new() { Name = "Dolany - Opuštěný důl", LocationKind = LocationKind.Village, Coordinates = center() },
            new() { Name = "Doubravka", LocationKind = LocationKind.Village, Coordinates = center() },
            new() { Name = "Doubravka - Staleté duby", LocationKind = LocationKind.Village, Coordinates = center() },
            new() { Name = "Jezerní písčina - JEZERKA", LocationKind = LocationKind.Village, Coordinates = center() },
            new() { Name = "Jezerní písčina -Ohrožená JEZERKA", LocationKind = LocationKind.Village, Coordinates = center() },
            new() { Name = "Polom", LocationKind = LocationKind.Village, Coordinates = center() },
            new() { Name = "Polom - Obrov", LocationKind = LocationKind.Village, Coordinates = center() },
            new() { Name = "Ruiny staré pevnosti", LocationKind = LocationKind.Village, Coordinates = center() },
            new() { Name = "Ruiny staré pevnosti - TROSKOV", LocationKind = LocationKind.Village, Coordinates = center() },
            new() { Name = "Spálená vesnice", LocationKind = LocationKind.Village, Coordinates = center() },
            new() { Name = "Spálená vesnice - Spálov", LocationKind = LocationKind.Village, Coordinates = center() },
            new() { Name = "Starý lom", LocationKind = LocationKind.Village, Coordinates = center() },
            new() { Name = "Starý lom - Lomná", LocationKind = LocationKind.Village, Coordinates = center() },
            new() { Name = "Úrodné stráně", LocationKind = LocationKind.Village, Coordinates = center() },
            new() { Name = "Úrodné stráně - Skřetomlaty", LocationKind = LocationKind.Village, Coordinates = center() },
            new() { Name = "Dcera řeky", LocationKind = LocationKind.Wilderness, Coordinates = center() },
            new() { Name = "Dol Guldur", LocationKind = LocationKind.Wilderness, Coordinates = center() },
            new() { Name = "Golém", LocationKind = LocationKind.Wilderness, Coordinates = center() },
            new() { Name = "Harpyjí hnízdo", LocationKind = LocationKind.Wilderness, Coordinates = center() },
            new() { Name = "Havraní skála", LocationKind = LocationKind.Wilderness, Coordinates = center() },
            new() { Name = "Hlavní brána Ereboru", LocationKind = LocationKind.Wilderness, Coordinates = center() },
            new() { Name = "Hnízdiště nestvůr", LocationKind = LocationKind.Wilderness, Coordinates = center() },
            new() { Name = "Jeskyně - Dungeon", LocationKind = LocationKind.Wilderness, Coordinates = center() },
            new() { Name = "Jezerní písčina - Quest", LocationKind = LocationKind.Wilderness, Coordinates = center() },
            new() { Name = "Kamenná poušť", LocationKind = LocationKind.Wilderness, Coordinates = center() },
            new() { Name = "Kobka starého krále", LocationKind = LocationKind.Wilderness, Coordinates = center() },
            new() { Name = "Kobka starého krále - Quest", LocationKind = LocationKind.Wilderness, Coordinates = center() },
            new() { Name = "Kráter", LocationKind = LocationKind.Wilderness, Coordinates = center() },
            new() { Name = "Ledová jeskyně", LocationKind = LocationKind.Wilderness, Coordinates = center() },
            new() { Name = "Ledový průsmyk", LocationKind = LocationKind.Wilderness, Coordinates = center() },
            new() { Name = "Lesní knihovna", LocationKind = LocationKind.Wilderness, Coordinates = center() },
            new() { Name = "Menhir - Quest", LocationKind = LocationKind.Wilderness, Coordinates = center() },
            new() { Name = "Močál", LocationKind = LocationKind.Wilderness, Coordinates = center() },
            new() { Name = "Mokřady", LocationKind = LocationKind.Wilderness, Coordinates = center() },
            new() { Name = "Osamělá hora", LocationKind = LocationKind.Wilderness, Coordinates = center() },
            new() { Name = "Ostrov na jezeře", LocationKind = LocationKind.Wilderness, Coordinates = center() },
            new() { Name = "Ovirská kotlina", LocationKind = LocationKind.Wilderness, Coordinates = center() },
            new() { Name = "Pavoučí palouček", LocationKind = LocationKind.Wilderness, Coordinates = center() },
            new() { Name = "Příbytky skřítků", LocationKind = LocationKind.Wilderness, Coordinates = center() },
            new() { Name = "Puklina", LocationKind = LocationKind.Wilderness, Coordinates = center() },
            new() { Name = "Rozcestí - Quest", LocationKind = LocationKind.Wilderness, Coordinates = center() },
            new() { Name = "Ruiny Dor Rhúnen", LocationKind = LocationKind.Wilderness, Coordinates = center() },
            new() { Name = "Skalbal", LocationKind = LocationKind.Wilderness, Coordinates = center() },
            new() { Name = "Skalní římsa - Quest", LocationKind = LocationKind.Wilderness, Coordinates = center() },
            new() { Name = "Skřetí cesta", LocationKind = LocationKind.Wilderness, Coordinates = center() },
            new() { Name = "Smradlavé díry", LocationKind = LocationKind.Wilderness, Coordinates = center() },
            new() { Name = "Spálený strom", LocationKind = LocationKind.Wilderness, Coordinates = center() },
            new() { Name = "Srdce hvozdu - Quest", LocationKind = LocationKind.Wilderness, Coordinates = center() },
            new() { Name = "Starý brod", LocationKind = LocationKind.Wilderness, Coordinates = center() },
            new() { Name = "Temné ruiny", LocationKind = LocationKind.Wilderness, Coordinates = center() },
            new() { Name = "Trouchnivý most", LocationKind = LocationKind.Wilderness, Coordinates = center() },
            new() { Name = "Vílí palouček", LocationKind = LocationKind.Wilderness, Coordinates = center() },
            new() { Name = "Vlčí shromaždiště", LocationKind = LocationKind.Wilderness, Coordinates = center() },
            new() { Name = "Vlčí shromaždiště - quest", LocationKind = LocationKind.Wilderness, Coordinates = center() },
            new() { Name = "Zaniklé město lesních lidí", LocationKind = LocationKind.Wilderness, Coordinates = center() },
            new() { Name = "Ztracený elfí pramen", LocationKind = LocationKind.Wilderness, Coordinates = center() },
            new() { Name = "Dcera řeky - Quest pro hobity", LocationKind = LocationKind.Hobbit, Coordinates = center() },
            new() { Name = "Kráter - Quest pro Hobity", LocationKind = LocationKind.Hobbit, Coordinates = center() },
            new() { Name = "Pavoučí palouček - QUEST pro Hobity", LocationKind = LocationKind.Hobbit, Coordinates = center() },
            new() { Name = "Příbytky skřítků - quest jen pro hobity", LocationKind = LocationKind.Hobbit, Coordinates = center() },
            new() { Name = "Vílí palouček - Quest pro Hobity", LocationKind = LocationKind.Hobbit, Coordinates = center() },
            new() { Name = "Úrodná pole I", LocationKind = LocationKind.PointOfInterest, Coordinates = center() },
            new() { Name = "Úrodná pole II", LocationKind = LocationKind.PointOfInterest, Coordinates = center() },
        };

        // Pass 1: save all locations to get IDs
        db.Locations.AddRange(locations);
        await db.SaveChangesAsync();

        // Pass 2: link variants to their parent locations via ParentLocationId.
        // Variant → Parent mapping: variant name prefix matches parent name exactly.
        // For groups with no "plain" parent, the first listed entry is the primary.
        var byName = locations.ToDictionary(l => l.Name, l => l.Id);

        // (variant name, parent name) — explicit mapping for all variant relationships
        var variantLinks = new (string Variant, string Parent)[]
        {
            // Village state changes
            ("Caras Amarth - Karáskov", "Caras Amarth"),
            ("Dol - Ruiny města", "Dol - Město"),
            ("Dolany - Obnovený důl", "Dolany - Opuštěný důl"),
            ("Doubravka - Staleté duby", "Doubravka"),
            ("Jezerní písčina -Ohrožená JEZERKA", "Jezerní písčina - JEZERKA"),
            ("Polom - Obrov", "Polom"),
            ("Ruiny staré pevnosti - TROSKOV", "Ruiny staré pevnosti"),
            ("Spálená vesnice - Spálov", "Spálená vesnice"),
            ("Starý lom - Lomná", "Starý lom"),
            ("Úrodné stráně - Skřetomlaty", "Úrodné stráně"),
            // Wilderness quest variants
            ("Kobka starého krále - Quest", "Kobka starého krále"),
            ("Vlčí shromaždiště - quest", "Vlčí shromaždiště"),
            ("Jezerní písčina - Quest", "Jezerní písčina - JEZERKA"),
            // Hobbit quest variants (parent is the wilderness location)
            ("Dcera řeky - Quest pro hobity", "Dcera řeky"),
            ("Kráter - Quest pro Hobity", "Kráter"),
            ("Pavoučí palouček - QUEST pro Hobity", "Pavoučí palouček"),
            ("Příbytky skřítků - quest jen pro hobity", "Příbytky skřítků"),
            ("Vílí palouček - Quest pro Hobity", "Vílí palouček"),
        };

        var linked = 0;
        foreach (var (variant, parent) in variantLinks)
        {
            if (byName.TryGetValue(variant, out var variantId) && byName.TryGetValue(parent, out var parentId))
            {
                var loc = locations.First(l => l.Id == variantId);
                loc.ParentLocationId = parentId;
                linked++;
            }
        }

        if (linked > 0)
            await db.SaveChangesAsync();

        return TypedResults.Ok(new SeedResult(locations.Count, $"Importováno {locations.Count} lokací ({linked} variant propojeno). Uprav pozice na mapě."));
    }
    private static async Task<Ok<SeedResult>> SeedLocationDescriptions(
        WorldDbContext db, IWebHostEnvironment env)
    {
        var jsonPath = Path.Combine(env.ContentRootPath, "..", "..", "docs", "legacy-data", "location-descriptions.json");
        if (!File.Exists(jsonPath))
            return TypedResults.Ok(new SeedResult(0, $"Soubor nenalezen: {Path.GetFullPath(jsonPath)}"));

        var json = await File.ReadAllTextAsync(jsonPath);
        var descriptions = JsonSerializer.Deserialize<Dictionary<string, LocationDescriptionData>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var locations = await db.Locations.ToListAsync();
        var updated = 0;

        foreach (var loc in locations)
        {
            if (!descriptions.TryGetValue(loc.Name, out var data))
                continue;

            var changed = false;
            if (data.Desc is not null && loc.Description is null)
            {
                loc.Description = data.Desc;
                changed = true;
            }
            if (data.Npc_info is not null && loc.NpcInfo is null)
            {
                loc.NpcInfo = data.Npc_info;
                changed = true;
            }
            if (data.Setup is not null && loc.SetupNotes is null)
            {
                loc.SetupNotes = data.Setup;
                changed = true;
            }
            if (changed) updated++;
        }

        await db.SaveChangesAsync();
        return TypedResults.Ok(new SeedResult(updated, $"Aktualizováno {updated} lokací s popisky z Excel."));
    }

    private record LocationDescriptionData(string? Desc, string? Npc_info, string? Setup);
}

public record SeedResult(int Count, string Message);
