using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using DevExpress.Blazor.Localization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using OvcinaHra.Client;
using OvcinaHra.Client.Auth;
using OvcinaHra.Client.Services;

var culture = new CultureInfo("cs-CZ");
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;
CultureInfo.CurrentCulture = culture;
CultureInfo.CurrentUICulture = culture;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? builder.HostEnvironment.BaseAddress;

builder.Services.AddScoped<UnauthorizedRedirectHandler>();
builder.Services.AddScoped(sp =>
{
    var handler = sp.GetRequiredService<UnauthorizedRedirectHandler>();
    handler.InnerHandler = new HttpClientHandler();
    return new HttpClient(handler) { BaseAddress = new Uri(apiBaseUrl) };
});
builder.Services.AddScoped<ApiClient>();
builder.Services.AddScoped<JwtAuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<JwtAuthStateProvider>());
builder.Services.AddScoped<TokenRefreshService>();
builder.Services.AddScoped<GameContextService>();
builder.Services.AddScoped<IDashboardEventStream, PollingDashboardEventStream>();
builder.Services.AddScoped<SkillService>();
builder.Services.AddScoped<PersonalQuestService>();
builder.Services.AddScoped<VersionDriftService>();
builder.Services.AddAuthorizationCore();
builder.Services.AddLocalization();
builder.Services.AddSingleton(typeof(IDxLocalizationService), typeof(LocalizationService));
builder.Services.AddDevExpressBlazor();

await builder.Build().RunAsync();
