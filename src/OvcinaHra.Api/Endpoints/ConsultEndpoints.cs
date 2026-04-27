using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using OvcinaHra.Api.Services;
using OvcinaHra.Shared.Dtos;

namespace OvcinaHra.Api.Endpoints;

public static class ConsultEndpoints
{
    public static RouteGroupBuilder MapConsultEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/consult")
            .WithTags("Consult")
            .RequireAuthorization();

        group.MapGet("/available", GetAvailability);
        group.MapPost("/{persona:regex(^(rulemaster|loremaster)$)}", AskAsync);
        group.MapPost("/{persona:regex(^(rulemaster|loremaster)$)}/reset", ResetAsync);

        return group;
    }

    private static Ok<ConsultAvailabilityDto> GetAvailability(IBotConsultClient bot)
        => TypedResults.Ok(new ConsultAvailabilityDto(bot.IsEnabled));

    private static async Task<IResult> AskAsync(
        string persona,
        ConsultRequestDto request,
        ClaimsPrincipal user,
        IBotConsultClient bot,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        if (!bot.IsEnabled)
            return UnavailableProblem();

        var message = request.Message.Trim();
        if (string.IsNullOrWhiteSpace(message))
        {
            return Results.Problem(
                title: "Prázdná otázka",
                detail: "Napiš otázku pro Drozda.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var email = GetUserEmail(user);
        if (email is null)
            return MissingEmailProblem();

        try
        {
            var answer = await bot.AskAsync(
                persona,
                message,
                email,
                GetBotRole(user),
                ct);
            return TypedResults.Ok(answer);
        }
        catch (BotConsultTimeoutException)
        {
            return Results.Problem(
                title: "Drozd neodpověděl včas",
                detail: "Konzultační služba překročila časový limit.",
                statusCode: StatusCodes.Status504GatewayTimeout);
        }
        catch (BotConsultUnavailableException)
        {
            return UnavailableProblem();
        }
        catch (BotConsultUpstreamException ex)
        {
            LogUpstreamFailure(loggerFactory, persona, ex);
            return Results.Problem(
                title: "Drozd se teď neozval",
                detail: "Drozd právě nemůže odpovědět, zkuste to prosím za chvíli.",
                statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static async Task<IResult> ResetAsync(
        string persona,
        ClaimsPrincipal user,
        IBotConsultClient bot,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        if (!bot.IsEnabled)
            return UnavailableProblem();

        var email = GetUserEmail(user);
        if (email is null)
            return MissingEmailProblem();

        try
        {
            await bot.ResetAsync(persona, email, ct);
            return TypedResults.Ok(new { ok = true });
        }
        catch (BotConsultTimeoutException)
        {
            return Results.Problem(
                title: "Drozd neodpověděl včas",
                detail: "Konzultační služba překročila časový limit.",
                statusCode: StatusCodes.Status504GatewayTimeout);
        }
        catch (BotConsultUnavailableException)
        {
            return UnavailableProblem();
        }
        catch (BotConsultUpstreamException ex)
        {
            LogUpstreamFailure(loggerFactory, persona, ex);
            return Results.Problem(
                title: "Drozd se teď neozval",
                detail: "Reset historie selhal, zkuste to prosím za chvíli.",
                statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static void LogUpstreamFailure(
        ILoggerFactory loggerFactory,
        string persona,
        BotConsultUpstreamException exception)
    {
        var logger = loggerFactory.CreateLogger("OvcinaHra.Api.Endpoints.ConsultEndpoints");
        logger.LogWarning(
            exception,
            "Bot consult upstream failure for {Persona}: {StatusCode}. UpstreamDetail={UpstreamDetail}",
            persona,
            exception.UpstreamStatusCode,
            exception.Detail);
    }

    private static string? GetUserEmail(ClaimsPrincipal user)
        => user.FindFirstValue("email") ?? user.FindFirstValue(ClaimTypes.Email);

    private static string GetBotRole(ClaimsPrincipal user)
    {
        var roles = user.FindAll("role")
            .Concat(user.FindAll(ClaimTypes.Role))
            .Select(claim => claim.Value);

        if (roles.Any(role =>
                role.Equals("Admin", StringComparison.OrdinalIgnoreCase)
                || role.Equals("Administrator", StringComparison.OrdinalIgnoreCase)))
            return "admin";

        if (roles.Any(role =>
                role.Equals("Organizer", StringComparison.OrdinalIgnoreCase)
                || role.Equals("Organizator", StringComparison.OrdinalIgnoreCase)
                || role.Equals("Organizátor", StringComparison.OrdinalIgnoreCase)))
            return "organizator";

        return "hráč";
    }

    private static IResult MissingEmailProblem()
        => Results.Problem(
            title: "Chybí e-mail",
            detail: "Přihlášený účet nemá e-mail potřebný pro konzultaci.",
            statusCode: StatusCodes.Status400BadRequest);

    private static IResult UnavailableProblem()
        => Results.Problem(
            title: "Drozd není dostupný",
            detail: "Konzultace s Drozdem není nakonfigurovaná.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
}
