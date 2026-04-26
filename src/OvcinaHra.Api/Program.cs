using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Endpoints;
using OvcinaHra.Api.Logging;
using OvcinaHra.Api.Services;
using Serilog;

// Two-stage bootstrap: early logger catches startup errors
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Serilog
    builder.Host.UseSerilog((context, loggerConfig) =>
    {
        loggerConfig
            .ReadFrom.Configuration(context.Configuration)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "OvcinaHra.Api")
            .WriteTo.Console(outputTemplate:
                "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
            .WriteTo.File(
                Path.Combine(context.HostingEnvironment.ContentRootPath, "..", "..", "logs", "api-.log"),
                rollingInterval: RollingInterval.Day,
                outputTemplate: "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}");
    });

    builder.Services.AddOpenApi();
    builder.Services.AddDbContext<WorldDbContext>(options =>
        options.UseNpgsql(builder.Configuration.GetConnectionString("WorldDb")));
    builder.Services.AddHealthChecks()
        .AddDbContextCheck<WorldDbContext>();

    // Authentication: JWT Bearer with dual validation
    // Accepts both OIDC tokens (from registrace) and self-issued tokens (dev + service)
    var oidcAuthority = builder.Configuration["Oidc:Authority"];
    var jwtKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!));

    builder.Services.AddHttpClient<RegistraceImportService>();
    builder.Services.AddHttpClient<RegistraceGameService>();

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        if (!string.IsNullOrEmpty(oidcAuthority))
        {
            // Production: validate against registrace OIDC discovery endpoint
            options.Authority = oidcAuthority;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = oidcAuthority.TrimEnd('/') + "/",
                ValidateAudience = false,
                ValidateLifetime = true,
                NameClaimType = "name",
                RoleClaimType = "role",
                ClockSkew = TimeSpan.FromSeconds(30),
                // Also accept self-issued tokens (service tokens, dev tokens)
                IssuerSigningKeys = [jwtKey],
                ValidIssuers = [oidcAuthority.TrimEnd('/') + "/", builder.Configuration["Jwt:Issuer"]!]
            };
        }
        else
        {
            // Development: self-issued tokens only
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = builder.Configuration["Jwt:Issuer"],
                ValidAudience = builder.Configuration["Jwt:Audience"],
                IssuerSigningKey = jwtKey,
                ClockSkew = TimeSpan.FromSeconds(30)
            };
        }
    });

    builder.Services.AddAuthorization();
    builder.Services.AddProblemDetails();
    builder.Services.AddHttpClient();
    builder.Services.AddSingleton<IBlobStorageService, BlobStorageService>();
    builder.Services.AddSingleton<IThumbnailService, ThumbnailService>();
    // Walks every image-bearing entity on startup and pre-generates all
    // thumbnail presets so list pages never pay the cold-resize cost at
    // runtime. Runs in the background — does not block startup.
    builder.Services.AddHostedService<ThumbnailBackfillHostedService>();

    // In-memory log ring buffer for production debugging
    var logBuffer = new LogRingBuffer();
    builder.Services.AddSingleton(logBuffer);
    builder.Logging.AddProvider(new RingBufferLoggerProvider(logBuffer));

    // CORS for Blazor WASM client. Configured origins (prod hostname) plus
    // any localhost / 127.0.0.1 origin so a developer can run the Client
    // locally with appsettings.LocalAgainstProd.json pointing here. Browsers
    // can't fake the Origin header across machines, so the localhost branch
    // is safe — it can only be exercised from the user's own machine.
    builder.Services.AddCors(options =>
    {
        var configuredOrigins =
            builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
            ?? ["https://localhost:5290", "http://localhost:5290"];
        options.AddPolicy("BlazorClient", policy => policy
            .SetIsOriginAllowed(origin =>
                configuredOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase)
                || IsLocalhostOrigin(origin))
            .AllowAnyHeader()
            .AllowAnyMethod());
    });

    static bool IsLocalhostOrigin(string origin)
    {
        // Use Uri.IsLoopback rather than StartsWith — covers IPv6 ([::1]),
        // missing-port forms, and any future loopback aliases without
        // brittle string prefix checks.
        if (string.IsNullOrWhiteSpace(origin)) return false;
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
        return (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && uri.IsLoopback;
    }

    var app = builder.Build();

    // Auto-apply pending migrations on startup
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
        await db.Database.MigrateAsync();
    }

    // Global exception handler — logs and returns ProblemDetails
    app.UseExceptionHandler(error =>
    {
        error.Run(async context =>
        {
            var ex = context.Features.Get<IExceptionHandlerFeature>()?.Error;
            if (ex is not null)
            {
                Log.Error(ex, "Unhandled exception on {Method} {Path}",
                    context.Request.Method, context.Request.Path);
            }

            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://tools.ietf.org/html/rfc9110#section-15.6.1",
                title = "Interní chyba serveru",
                status = 500,
                detail = app.Environment.IsDevelopment() ? ex?.Message : null,
                traceId = context.TraceIdentifier
            });
        });
    });

    app.UseSerilogRequestLogging(options =>
    {
        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            diagnosticContext.Set("UserId",
                httpContext.User.FindFirstValue("sub")
                ?? httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? "anonymous");
        };
    });

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }

    var forwardedHeadersOptions = new ForwardedHeadersOptions
    {
        ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
            | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
    };
    forwardedHeadersOptions.KnownIPNetworks.Clear();
    forwardedHeadersOptions.KnownProxies.Clear();
    app.UseForwardedHeaders(forwardedHeadersOptions);
    app.UseHttpsRedirection();
    app.UseCors("BlazorClient");
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapHealthChecks("/health").AllowAnonymous();

    // Deployment diagnostic — returns the commit SHA and container start time so
    // operators can confirm the deployed image matches the expected build. In CI
    // the SHA is injected via Dockerfile ARG GIT_SHA → ENV; locally it reports
    // "unknown".
    var apiStartedUtc = DateTimeOffset.UtcNow;
    app.MapGet("/api/version", () => Results.Ok(new
    {
        commit = Environment.GetEnvironmentVariable("GIT_SHA") ?? "unknown",
        startedUtc = apiStartedUtc
    })).AllowAnonymous();

    // Auth — dev token only in Development, refresh always available
    app.MapAuthEndpoints(builder.Configuration, app.Environment.IsDevelopment());

    // Seed endpoints — dev only
    if (app.Environment.IsDevelopment())
    {
        app.MapSeedEndpoints().RequireAuthorization();
    }

    // All CRUD endpoints require authorization
    app.MapGameEndpoints().RequireAuthorization();
    app.MapTagEndpoints().RequireAuthorization();
    app.MapLocationEndpoints().RequireAuthorization();
    app.MapItemEndpoints().RequireAuthorization();
    app.MapSecretStashEndpoints().RequireAuthorization();
    app.MapCharacterEndpoints().RequireAuthorization();
    app.MapKingdomEndpoints().RequireAuthorization();
    app.MapMonsterEndpoints().RequireAuthorization();
    app.MapNpcEndpoints().RequireAuthorization();
    app.MapQuestEndpoints().RequireAuthorization();
    app.MapBuildingEndpoints().RequireAuthorization();
    app.MapSkillEndpoints().RequireAuthorization();
    app.MapSpellEndpoints().RequireAuthorization();
    app.MapCraftingEndpoints().RequireAuthorization();
    app.MapRecipeEndpoints().RequireAuthorization();
    app.MapBuildingRecipeEndpoints().RequireAuthorization();
    app.MapTreasureQuestEndpoints().RequireAuthorization();
    app.MapPersonalQuestEndpoints().RequireAuthorization();
    app.MapTreasurePlanningEndpoints().RequireAuthorization();
    app.MapTimelineEndpoints().RequireAuthorization();
    app.MapDashboardEndpoints().RequireAuthorization();
    app.MapMapEndpoints().RequireAuthorization();
    app.MapGameEventEndpoints();
    app.MapSearchEndpoints().RequireAuthorization();
    app.MapImageEndpoints().RequireAuthorization();
    app.MapScanEndpoints();
    app.MapLogEndpoints(); // No auth — needed for debugging login issues

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program;
