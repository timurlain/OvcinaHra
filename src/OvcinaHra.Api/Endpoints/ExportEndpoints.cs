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

        return group;
    }

    private static async Task<Results<FileContentHttpResult, NotFound, BadRequest<ProblemDetails>>> DownloadExplorerMapPdf(
        int gameId,
        string? style,
        IExplorerMapExportService exporter,
        CancellationToken ct)
    {
        if (!TryParseStyle(style, out var parsedStyle))
            return TypedResults.BadRequest(ValidationProblem("Neznámý podklad mapy."));

        try
        {
            var pdf = await exporter.RenderExplorerMapAsync(gameId, parsedStyle, ct);
            return TypedResults.File(
                pdf.Bytes,
                contentType: "application/pdf",
                fileDownloadName: pdf.FileName);
        }
        catch (KeyNotFoundException)
        {
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
