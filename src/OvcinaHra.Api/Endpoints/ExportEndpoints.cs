using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using OvcinaHra.Api.Services;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Endpoints;

public static class ExportEndpoints
{
    public static RouteGroupBuilder MapExportEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/games").WithTags("Exports");

        group.MapGet("/{gameId:int}/exports/explorer-map.pdf", DownloadExplorerMapPdf);
        group.MapGet("/{gameId:int}/exports/magic-book.pdf", DownloadMagicBookPdf);
        group.MapGet("/{gameId:int}/exports/organizer-map.pdf", DownloadOrganizerMapPdf);
        group.MapGet("/{gameId:int}/exports/kingdom-map.pdf", DownloadKingdomMapPdf);

        return group;
    }

    private static Task<IResult> DownloadExplorerMapPdf(
        int gameId,
        string? style,
        IExplorerMapExportService exporter,
        ILoggerFactory loggerFactory,
        CancellationToken ct) =>
        DownloadMapPdf(
            gameId,
            style,
            MapExportKind.Explorer,
            MapExportPageFormat.A4Portrait,
            organizerOnly: false,
            exporter,
            null,
            loggerFactory,
            ct);

    private static Task<IResult> DownloadOrganizerMapPdf(
        int gameId,
        string? style,
        IExplorerMapExportService exporter,
        HttpContext http,
        ILoggerFactory loggerFactory,
        CancellationToken ct) =>
        DownloadMapPdf(
            gameId,
            style,
            MapExportKind.Organizer,
            MapExportPageFormat.A4Portrait,
            organizerOnly: true,
            exporter,
            http,
            loggerFactory,
            ct);

    private static Task<IResult> DownloadKingdomMapPdf(
        int gameId,
        string? style,
        IExplorerMapExportService exporter,
        ILoggerFactory loggerFactory,
        CancellationToken ct) =>
        DownloadMapPdf(
            gameId,
            style,
            MapExportKind.Kingdom,
            MapExportPageFormat.A3Portrait,
            organizerOnly: false,
            exporter,
            null,
            loggerFactory,
            ct);

    private static async Task<IResult> DownloadMapPdf(
        int gameId,
        string? style,
        MapExportKind kind,
        MapExportPageFormat pageFormat,
        bool organizerOnly,
        IExplorerMapExportService exporter,
        HttpContext? http,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("OvcinaHra.Api.Endpoints.ExportEndpoints");
        logger.LogInformation(
            "[map-export-pr3] export endpoint entry gameId={GameId} kind={Kind} pageFormat={PageFormat} styleRaw={StyleRaw}",
            gameId,
            kind,
            pageFormat,
            style);

        if (!TryParseStyle(style, out var parsedStyle))
            return TypedResults.BadRequest(ValidationProblem("Neznámý podklad mapy."));

        if (organizerOnly && (http?.User is null || !IsOrganizer(http.User)))
        {
            logger.LogWarning(
                "[map-export-pr3] export auth-gate denial gameId={GameId} kind={Kind}",
                gameId,
                kind);
            return TypedResults.Problem(
                title: "Přístup odepřen",
                detail: "Pouze organizátoři mohou stáhnout tuto mapu.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        try
        {
            var pdf = await exporter.RenderMapAsync(gameId, parsedStyle, kind, pageFormat, ct);
            logger.LogInformation(
                "[map-export-pr3] export endpoint exit gameId={GameId} kind={Kind} status=file fileName={FileName} bytes={ByteCount}",
                gameId,
                kind,
                pdf.FileName,
                pdf.Bytes.Length);
            return TypedResults.File(
                pdf.Bytes,
                contentType: "application/pdf",
                fileDownloadName: pdf.FileName);
        }
        catch (KeyNotFoundException)
        {
            logger.LogInformation(
                "[map-export-pr3] export endpoint exit gameId={GameId} kind={Kind} status=not-found",
                gameId,
                kind);
            return TypedResults.NotFound();
        }
        catch (MapExportProblemException ex)
        {
            return TypedResults.BadRequest(ValidationProblem(ex.Title, ex.Detail));
        }
    }

    private static async Task<Results<FileContentHttpResult, NotFound, BadRequest<ProblemDetails>>> DownloadMagicBookPdf(
        int gameId,
        IMagicBookExportService exporter,
        CancellationToken ct)
    {
        try
        {
            var pdf = await exporter.RenderMagicBookAsync(gameId, ct);
            return TypedResults.File(
                pdf.Bytes,
                contentType: "application/pdf",
                fileDownloadName: pdf.FileName);
        }
        catch (KeyNotFoundException)
        {
            return TypedResults.NotFound();
        }
        catch (MagicBookExportProblemException ex)
        {
            return TypedResults.BadRequest(ValidationProblem(ex.Title, ex.Detail));
        }
    }

    private static bool IsOrganizer(ClaimsPrincipal user)
    {
        var roles = user.FindAll(ClaimTypes.Role)
            .Concat(user.FindAll("role"))
            .Select(c => c.Value);

        return roles.Any(role => role is "Organizer" or "Organizator" or "Organizátor" or "Admin");
    }

    private static bool TryParseStyle(string? value, out MapExportBasemapStyle style)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            style = MapExportBasemapStyle.Tourist;
            return true;
        }

        return Enum.TryParse(value, ignoreCase: true, out style)
            && Enum.IsDefined(style);
    }

    private static ProblemDetails ValidationProblem(string detail) =>
        ValidationProblem("Export mapy se nepodařil", detail);

    private static ProblemDetails ValidationProblem(string title, string detail) => new()
    {
        Title = title,
        Detail = detail,
        Status = StatusCodes.Status400BadRequest
    };
}
