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

        // Default GPS = game area center. User will drag to correct positions.
        var center = new OvcinaHra.Shared.Domain.ValueObjects.GpsCoordinates(49.4532m, 18.0203m);

        // From legacy DB Lokace table (51 locations) + lore docs
        var locations = new List<Location>
        {
            new() { Name = "Esgaroth", LocationKind = LocationKind.Town, Coordinates = center },
            new() { Name = "Arathrind", LocationKind = LocationKind.Town, Coordinates = center },
            new() { Name = "Azanulinbar-Dum", LocationKind = LocationKind.Town, Coordinates = center },
            new() { Name = "Caras Amarth", LocationKind = LocationKind.Town, Coordinates = center },
            new() { Name = "Bílý kámen", LocationKind = LocationKind.Magical, Coordinates = center },
            new() { Name = "Knihovna", LocationKind = LocationKind.Magical, Coordinates = center },
            new() { Name = "Ledová jeskyně", LocationKind = LocationKind.Magical, Coordinates = center },
            new() { Name = "Vílí palouček", LocationKind = LocationKind.Magical, Coordinates = center },
            new() { Name = "Věčný plamen", LocationKind = LocationKind.Magical, Coordinates = center },
            new() { Name = "Kamenný kruh", LocationKind = LocationKind.Magical, Coordinates = center },
            new() { Name = "Elfí chrám", LocationKind = LocationKind.Magical, Coordinates = center },
            new() { Name = "Proměnlivý strom", LocationKind = LocationKind.Magical, Coordinates = center },
            new() { Name = "Menhir", LocationKind = LocationKind.Magical, Coordinates = center },
            new() { Name = "Dol Guldur", LocationKind = LocationKind.Dungeon, Coordinates = center },
            new() { Name = "Opuštěný důl", LocationKind = LocationKind.Dungeon, Coordinates = center },
            new() { Name = "Ruiny staré pevnosti", LocationKind = LocationKind.Dungeon, Coordinates = center },
            new() { Name = "Kobka starého krále", LocationKind = LocationKind.Dungeon, Coordinates = center },
            new() { Name = "Hnízdiště nestvůr", LocationKind = LocationKind.Dungeon, Coordinates = center },
            new() { Name = "Temné ruiny", LocationKind = LocationKind.Dungeon, Coordinates = center },
            new() { Name = "Jeskyně", LocationKind = LocationKind.Dungeon, Coordinates = center },
            new() { Name = "Starý lom", LocationKind = LocationKind.Dungeon, Coordinates = center },
            new() { Name = "Spálená vesnice", LocationKind = LocationKind.Village, Coordinates = center },
            new() { Name = "Rhosgobel", LocationKind = LocationKind.Village, Coordinates = center },
            new() { Name = "Sarn Gowiring", LocationKind = LocationKind.Village, Coordinates = center },
            new() { Name = "Skalbal", LocationKind = LocationKind.Village, Coordinates = center },
            new() { Name = "Golem", LocationKind = LocationKind.Village, Coordinates = center },
            new() { Name = "Ruiny města Dol", LocationKind = LocationKind.Village, Coordinates = center },
            new() { Name = "Zaniklé město", LocationKind = LocationKind.Village, Coordinates = center },
            new() { Name = "Rozcestí", LocationKind = LocationKind.PointOfInterest, Coordinates = center },
            new() { Name = "Dcera řeky", LocationKind = LocationKind.PointOfInterest, Coordinates = center },
            new() { Name = "Srdce hvozdu", LocationKind = LocationKind.PointOfInterest, Coordinates = center },
            new() { Name = "Kráter", LocationKind = LocationKind.PointOfInterest, Coordinates = center },
            new() { Name = "Močál", LocationKind = LocationKind.PointOfInterest, Coordinates = center },
            new() { Name = "Mokřad", LocationKind = LocationKind.PointOfInterest, Coordinates = center },
            new() { Name = "Polom", LocationKind = LocationKind.PointOfInterest, Coordinates = center },
            new() { Name = "Harpyjí hnízdo", LocationKind = LocationKind.Wilderness, Coordinates = center },
            new() { Name = "Ceber Fanuin", LocationKind = LocationKind.Wilderness, Coordinates = center },
            new() { Name = "Spálený strom", LocationKind = LocationKind.Wilderness, Coordinates = center },
            new() { Name = "Kamenná věž", LocationKind = LocationKind.Wilderness, Coordinates = center },
            new() { Name = "Kamenná poušť", LocationKind = LocationKind.Wilderness, Coordinates = center },
            new() { Name = "Smradlavé díry", LocationKind = LocationKind.Wilderness, Coordinates = center },
            new() { Name = "Vlčí shromaždiště", LocationKind = LocationKind.Wilderness, Coordinates = center },
            new() { Name = "Lesní oltář", LocationKind = LocationKind.Wilderness, Coordinates = center },
            new() { Name = "Trouchnivý most", LocationKind = LocationKind.Wilderness, Coordinates = center },
            new() { Name = "Pavoučí palouček", LocationKind = LocationKind.Wilderness, Coordinates = center },
            new() { Name = "Skalní římsa", LocationKind = LocationKind.Wilderness, Coordinates = center },
            new() { Name = "Ostrov na jezeře", LocationKind = LocationKind.Wilderness, Coordinates = center },
            new() { Name = "Jezerní písčina", LocationKind = LocationKind.Wilderness, Coordinates = center },
            new() { Name = "Ledový průsmyk", LocationKind = LocationKind.Wilderness, Coordinates = center },
            new() { Name = "Osaměla hora", LocationKind = LocationKind.Wilderness, Coordinates = center },
            new() { Name = "Hlavní brána Ereboru", LocationKind = LocationKind.Wilderness, Coordinates = center },
            new() { Name = "Příbytky skřítků", LocationKind = LocationKind.Hobbit, Coordinates = center },
            new() { Name = "Kupecká stezka", LocationKind = LocationKind.PointOfInterest, Coordinates = center },
            new() { Name = "Stará elfí cesta", LocationKind = LocationKind.PointOfInterest, Coordinates = center },
        };

        db.Locations.AddRange(locations);
        await db.SaveChangesAsync();
        return TypedResults.Ok(new SeedResult(locations.Count, $"Importováno {locations.Count} lokací s výchozí GPS. Uprav pozice na mapě."));
    }
}

public record SeedResult(int Count, string Message);
