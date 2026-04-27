namespace OvcinaHra.Api.Tests.ClientSmoke;

public class BotConsultDrawerSmokeTests
{
    [Fact]
    public void Drawer_IsAuthenticatedConfigGated_AndPersonaAware()
    {
        var root = FindRepoRoot();
        var layout = File.ReadAllText(Path.Combine(
            root,
            "src",
            "OvcinaHra.Client",
            "Layout",
            "MainLayout.razor"));
        var drawer = File.ReadAllText(Path.Combine(
            root,
            "src",
            "OvcinaHra.Client",
            "Components",
            "BotConsultDrawer.razor"));
        var css = File.ReadAllText(Path.Combine(
            root,
            "src",
            "OvcinaHra.Client",
            "Components",
            "BotConsultDrawer.razor.css"));

        Assert.Contains("<AuthorizeView>", layout);
        Assert.Contains("<BotConsultDrawer />", layout);
        Assert.Contains("/api/consult/available", drawer);
        Assert.Contains("oh-bot-last-persona", drawer);
        Assert.Contains("rulemaster", drawer);
        Assert.Contains("loremaster", drawer);
        Assert.Contains("Zeptat se Drozda", drawer);
        Assert.Contains("Drozd přemýšlí…", drawer);
        Assert.Contains("Zeptej se Drozda…", drawer);
        Assert.Contains("Pravidla", drawer);
        Assert.Contains("Lore", drawer);
        Assert.Contains("Odeslat", drawer);
        Assert.Contains("Zavřít", drawer);
        Assert.Contains("--oh-color-forest-deep", css);
        Assert.Contains("--oh-color-azanulinbar", css);
        Assert.Contains("@media (max-width: 767.98px)", css);
        Assert.Contains("height: 100dvh;", css);
    }

    [Fact]
    public void MarkdownRenderer_UsesAdvancedExtensions_AndDisablesHtml()
    {
        var renderer = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "src",
            "OvcinaHra.Client",
            "Components",
            "MarkdownRenderer.razor"));

        Assert.Contains(".UseAdvancedExtensions()", renderer);
        Assert.Contains(".DisableHtml()", renderer);
        Assert.Contains("Markdown.ToHtml", renderer);
    }

    [Fact]
    public void Program_ConfiguresBotConsultRetry()
    {
        var program = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "src",
            "OvcinaHra.Api",
            "Program.cs"));

        Assert.Contains("AddHttpClient<IBotConsultClient, BotConsultClient>", program);
        Assert.Contains("HttpPolicyExtensions", program);
        Assert.Contains("WaitAndRetryAsync(1", program);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OvcinaHra.slnx")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new InvalidOperationException("Could not find repository root.");
    }
}
