using Microsoft.Playwright;

namespace OvcinaHra.E2E.PageObjects;

public class GameFormPage
{
    private readonly IPage _page;

    public GameFormPage(IPage page) => _page = page;

    public ILocator NameInput => _page.Locator("[data-testid='game-name']");
    public ILocator EditionInput => _page.Locator("[data-testid='game-edition']");
    public ILocator StartDateInput => _page.Locator("[data-testid='game-start-date']");
    public ILocator EndDateInput => _page.Locator("[data-testid='game-end-date']");
    public ILocator SaveButton => _page.Locator("[data-testid='save-game']");
    public ILocator DeleteButton => _page.Locator("[data-testid='delete-game']");

    public async Task FillAsync(string name, int edition, string startDate, string endDate)
    {
        await NameInput.FillAsync(name);
        await EditionInput.FillAsync(edition.ToString());
        await StartDateInput.FillAsync(startDate);
        await EndDateInput.FillAsync(endDate);
    }

    public async Task SaveAsync() => await SaveButton.ClickAsync();
    public async Task DeleteAsync() => await DeleteButton.ClickAsync();
}
