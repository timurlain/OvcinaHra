using Microsoft.EntityFrameworkCore;
using OvcinaHra.Api.Data;
using OvcinaHra.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddDbContext<WorldDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("WorldDb")));
builder.Services.AddHealthChecks()
    .AddDbContextCheck<WorldDbContext>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.MapHealthChecks("/health");
app.MapGameEndpoints();
app.MapTagEndpoints();
app.MapLocationEndpoints();
app.MapItemEndpoints();
app.MapSecretStashEndpoints();
app.MapMonsterEndpoints();
app.MapQuestEndpoints();
app.MapBuildingEndpoints();
app.MapCraftingEndpoints();
app.MapTreasureQuestEndpoints();
app.MapTimelineEndpoints();

app.Run();

public partial class Program;
