using System.Diagnostics;
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
        group.MapGet("/{gameId:int}/exports/cenik.pdf", DownloadCenikPdf);
        group.MapGet("/{gameId:int}/exports/organizer-map.pdf", DownloadOrganizerMapPdf);
        group.MapGet("/{gameId:int}/exports/kingdom-map.pdf", DownloadKingdomMapPdf);

        return group;
    }

    private static Task<IResult> DownloadExplorerMapPdf(
        int gameId,
        string? style,
        IExplorerMapExportService exporter,
        HttpContext http,
        ILoggerFactory loggerFactory,
        CancellationToken ct) =>
        DownloadMapPdf(
            gameId,
            style,
            MapExportKind.Explorer,
            MapExportPageFormat.A4Portrait,
            organizerOnly: false,
            exporter,
            http,
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
        HttpContext http,
        ILoggerFactory loggerFactory,
        CancellationToken ct) =>
        DownloadMapPdf(
            gameId,
            style,
            MapExportKind.Kingdom,
            MapExportPageFormat.A3Portrait,
            organizerOnly: false,
            exporter,
            http,
            loggerFactory,
            ct);

    private static async Task<IResult> DownloadMapPdf(
        int gameId,
        string? style,
        MapExportKind kind,
        MapExportPageFormat pageFormat,
        bool organizerOnly,
        IExplorerMapExportService exporter,
        HttpContext http,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("OvcinaHra.Api.Endpoints.ExportEndpoints");
        var timer = Stopwatch.StartNew();
        logger.LogInformation(
            "[export-server] entry method={Method} path={Path} gameId={GameId} kind={Kind} pageFormat={PageFormat} styleRaw={StyleRaw}",
            http.Request.Method,
            http.Request.Path,
            gameId,
            kind,
            pageFormat,
            style);
        logger.LogInformation(
            "[map-export-pr3] export endpoint entry gameId={GameId} kind={Kind} pageFormat={PageFormat} styleRaw={StyleRaw}",
            gameId,
            kind,
            pageFormat,
            style);

        if (!TryParseStyle(style, out var parsedStyle))
        {
            timer.Stop();
            logger.LogInformation(
                "[export-server] exit method={Method} path={Path} gameId={GameId} kind={Kind} status=bad-request elapsedMs={ElapsedMs} detail={Detail}",
                http.Request.Method,
                http.Request.Path,
                gameId,
                kind,
                timer.ElapsedMilliseconds,
                "unknown-style");
            return TypedResults.BadRequest(ValidationProblem("Neznámý podklad mapy."));
        }

        if (organizerOnly && !IsOrganizer(http.User))
        {
            timer.Stop();
            logger.LogInformation(
                "[export-server] exit method={Method} path={Path} gameId={GameId} kind={Kind} status=forbidden elapsedMs={ElapsedMs}",
                http.Request.Method,
                http.Request.Path,
                gameId,
                kind,
                timer.ElapsedMilliseconds);
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
            timer.Stop();
            logger.LogInformation(
                "[export-server] exit method={Method} path={Path} gameId={GameId} kind={Kind} status=file fileName={FileName} bytes={ByteCount} elapsedMs={ElapsedMs}",
                http.Request.Method,
                http.Request.Path,
                gameId,
                kind,
                pdf.FileName,
                pdf.Bytes.Length,
                timer.ElapsedMilliseconds);
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
            timer.Stop();
            logger.LogInformation(
                "[export-server] exit method={Method} path={Path} gameId={GameId} kind={Kind} status=not-found elapsedMs={ElapsedMs}",
                http.Request.Method,
                http.Request.Path,
                gameId,
                kind,
                timer.ElapsedMilliseconds);
            logger.LogInformation(
                "[map-export-pr3] export endpoint exit gameId={GameId} kind={Kind} status=not-found",
                gameId,
                kind);
            return TypedResults.NotFound();
        }
        catch (MapExportProblemException ex)
        {
            timer.Stop();
            logger.LogInformation(
                "[export-server] exit method={Method} path={Path} gameId={GameId} kind={Kind} status=bad-request elapsedMs={ElapsedMs} detail={Detail}",
                http.Request.Method,
                http.Request.Path,
                gameId,
                kind,
                timer.ElapsedMilliseconds,
                ex.Detail);
            return TypedResults.BadRequest(ValidationProblem(ex.Title, ex.Detail));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            timer.Stop();
            logger.LogError(
                ex,
                "[export-server] exception method={Method} path={Path} gameId={GameId} kind={Kind} elapsedMs={ElapsedMs} detail={Detail}",
                http.Request.Method,
                http.Request.Path,
                gameId,
                kind,
                timer.ElapsedMilliseconds,
                ex.Message);
            throw;
        }
    }

    private static async Task<Results<FileContentHttpResult, NotFound, BadRequest<ProblemDetails>>> DownloadMagicBookPdf(
        int gameId,
        IMagicBookExportService exporter,
        HttpContext http,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("OvcinaHra.Api.Endpoints.ExportEndpoints");
        var timer = Stopwatch.StartNew();
        logger.LogInformation(
            "[export-server] entry method={Method} path={Path} gameId={GameId} kind={Kind}",
            http.Request.Method,
            http.Request.Path,
            gameId,
            "MagicBook");
        try
        {
            var pdf = await exporter.RenderMagicBookAsync(gameId, ct);
            timer.Stop();
            logger.LogInformation(
                "[export-server] exit method={Method} path={Path} gameId={GameId} kind={Kind} status=file fileName={FileName} bytes={ByteCount} elapsedMs={ElapsedMs}",
                http.Request.Method,
                http.Request.Path,
                gameId,
                "MagicBook",
                pdf.FileName,
                pdf.Bytes.Length,
                timer.ElapsedMilliseconds);
            return TypedResults.File(
                pdf.Bytes,
                contentType: "application/pdf",
                fileDownloadName: pdf.FileName);
        }
        catch (KeyNotFoundException)
        {
            timer.Stop();
            logger.LogInformation(
                "[export-server] exit method={Method} path={Path} gameId={GameId} kind={Kind} status=not-found elapsedMs={ElapsedMs}",
                http.Request.Method,
                http.Request.Path,
                gameId,
                "MagicBook",
                timer.ElapsedMilliseconds);
            return TypedResults.NotFound();
        }
        catch (MagicBookExportProblemException ex)
        {
            timer.Stop();
            logger.LogInformation(
                "[export-server] exit method={Method} path={Path} gameId={GameId} kind={Kind} status=bad-request elapsedMs={ElapsedMs} detail={Detail}",
                http.Request.Method,
                http.Request.Path,
                gameId,
                "MagicBook",
                timer.ElapsedMilliseconds,
                ex.Detail);
            return TypedResults.BadRequest(ValidationProblem(ex.Title, ex.Detail));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            timer.Stop();
            logger.LogError(
                ex,
                "[export-server] exception method={Method} path={Path} gameId={GameId} kind={Kind} elapsedMs={ElapsedMs} detail={Detail}",
                http.Request.Method,
                http.Request.Path,
                gameId,
                "MagicBook",
                timer.ElapsedMilliseconds,
                ex.Message);
            throw;
        }
    }

    private static async Task<Results<FileContentHttpResult, NotFound, BadRequest<ProblemDetails>>> DownloadCenikPdf(
        int gameId,
        ICenikExportService exporter,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("OvcinaHra.Api.Endpoints.ExportEndpoints");
        logger.LogInformation("[export-server] cenik.entry gameId={GameId}", gameId);
        try
        {
            var pdf = await exporter.RenderCenikAsync(gameId, ct);
            return TypedResults.File(
                pdf.Bytes,
                contentType: "application/pdf",
                fileDownloadName: pdf.FileName);
        }
        catch (KeyNotFoundException)
        {
            logger.LogInformation("[export-server] cenik.exit status=404 gameId={GameId}", gameId);
            return TypedResults.NotFound();
        }
        catch (CenikExportProblemException ex)
        {
            logger.LogInformation(
                "[export-server] cenik.exit status=400 gameId={GameId} detail={Detail}",
                gameId,
                ex.Detail);
            return TypedResults.BadRequest(ValidationProblem(ex.Title, ex.Detail));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[export-server] cenik.exception detail={Detail}", ex.Message);
            throw;
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
