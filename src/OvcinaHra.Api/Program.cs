using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Endpoints;
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

    // JWT Authentication + External OAuth
    var authBuilder = builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!)),
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    })
    .AddCookie("ExternalLogin", options =>
    {
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.HttpOnly = true;
    });

    // Conditionally add external OAuth providers
    var googleId = builder.Configuration["ExternalAuth:Google:ClientId"];
    var googleSecret = builder.Configuration["ExternalAuth:Google:ClientSecret"];
    if (!string.IsNullOrEmpty(googleId) && !string.IsNullOrEmpty(googleSecret))
    {
        authBuilder.AddGoogle(options =>
        {
            options.SignInScheme = "ExternalLogin";
            options.ClientId = googleId;
            options.ClientSecret = googleSecret;
        });
    }

    var msId = builder.Configuration["ExternalAuth:Microsoft:ClientId"];
    var msSecret = builder.Configuration["ExternalAuth:Microsoft:ClientSecret"];
    if (!string.IsNullOrEmpty(msId) && !string.IsNullOrEmpty(msSecret))
    {
        authBuilder.AddMicrosoftAccount(options =>
        {
            options.SignInScheme = "ExternalLogin";
            options.ClientId = msId;
            options.ClientSecret = msSecret;
        });
    }

    // Seznam (custom OAuth2, not standard OIDC)
    var seznamConfig = builder.Configuration.GetSection("ExternalAuth:Seznam");
    if (!string.IsNullOrEmpty(seznamConfig["ClientId"]) && !string.IsNullOrEmpty(seznamConfig["ClientSecret"]))
    {
        authBuilder.AddOAuth("Seznam", "Seznam", options =>
        {
            options.SignInScheme = "ExternalLogin";
            options.ClientId = seznamConfig["ClientId"]!;
            options.ClientSecret = seznamConfig["ClientSecret"]!;
            options.AuthorizationEndpoint = "https://login.szn.cz/api/v1/oauth/auth";
            options.TokenEndpoint = "https://login.szn.cz/api/v1/oauth/token";
            options.UserInformationEndpoint = "https://login.szn.cz/api/v1/user";
            options.CallbackPath = "/signin-seznam";
            options.Scope.Add("identity");
            options.SaveTokens = false;
            options.ClaimActions.MapJsonKey(System.Security.Claims.ClaimTypes.NameIdentifier, "oauth_user_id");
            options.ClaimActions.MapJsonKey(System.Security.Claims.ClaimTypes.Email, "email");
            options.ClaimActions.MapJsonKey(System.Security.Claims.ClaimTypes.Name, "firstname");
            options.Events.OnCreatingTicket = async context =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, context.Options.UserInformationEndpoint);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", context.AccessToken);
                using var response = await context.Backchannel.SendAsync(request);
                response.EnsureSuccessStatusCode();
                var user = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
                context.RunClaimActions(user);
            };
        });
    }

    builder.Services.AddAuthorization();
    builder.Services.AddProblemDetails();
    builder.Services.AddSingleton<IBlobStorageService, BlobStorageService>();

    builder.Services.AddHttpClient<RegistraceClient>(client =>
    {
        var baseUrl = builder.Configuration["Registrace:BaseUrl"];
        if (!string.IsNullOrEmpty(baseUrl))
            client.BaseAddress = new Uri(baseUrl);
        client.DefaultRequestHeaders.Add("X-Api-Key",
            builder.Configuration["Registrace:ApiKey"] ?? "");
    });

    // CORS for Blazor WASM client
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("BlazorClient", policy => policy
            .WithOrigins(
                builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
                ?? ["https://localhost:5290", "http://localhost:5290"])
            .AllowAnyHeader()
            .AllowAnyMethod());
    });

    var app = builder.Build();

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
                httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous");
        };
    });

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }

    app.UseForwardedHeaders(new ForwardedHeadersOptions
    {
        ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
            | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
    });
    app.UseHttpsRedirection();
    app.UseCors("BlazorClient");
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapHealthChecks("/health").AllowAnonymous();

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
    app.MapMonsterEndpoints().RequireAuthorization();
    app.MapQuestEndpoints().RequireAuthorization();
    app.MapBuildingEndpoints().RequireAuthorization();
    app.MapCraftingEndpoints().RequireAuthorization();
    app.MapTreasureQuestEndpoints().RequireAuthorization();
    app.MapTimelineEndpoints().RequireAuthorization();
    app.MapSearchEndpoints().RequireAuthorization();
    app.MapImageEndpoints().RequireAuthorization();

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
