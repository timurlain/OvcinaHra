using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddDbContext<WorldDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("WorldDb")));
builder.Services.AddHealthChecks()
    .AddDbContextCheck<WorldDbContext>();

// JWT Authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
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
    });
builder.Services.AddAuthorization();

// CORS for Blazor WASM client
builder.Services.AddCors(options =>
{
    options.AddPolicy("BlazorClient", policy => policy
        .WithOrigins(
            builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
            ?? ["https://localhost:5190", "http://localhost:5190"])
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseCors("BlazorClient");
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health").AllowAnonymous();

// Auth — dev token only in Development, refresh always available
app.MapAuthEndpoints(builder.Configuration, app.Environment.IsDevelopment());

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

app.Run();

public partial class Program;
