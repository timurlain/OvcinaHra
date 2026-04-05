using Microsoft.Playwright;

namespace OvcinaHra.E2E.PageObjects;

public class GameListPage
{
    private readonly IPage _page;

    public GameListPage(IPage page) => _page = page;

    public async Task NavigateAsync() => await _page.GotoAsync("/hry");

    public ILocator GameRows => _page.Locator("[data-testid='game-row']");
    public ILocator CreateButton => _page.Locator("[data-testid='create-game']");
    public ILocator PageTitle => _page.Locator("h1");

    public async Task<int> GetGameCountAsync() => await GameRows.CountAsync();

    public async Task ClickCreateAsync() => await CreateButton.ClickAsync();
}
