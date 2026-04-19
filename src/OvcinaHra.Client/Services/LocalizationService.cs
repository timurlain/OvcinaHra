using DevExpress.Blazor.Localization;

namespace OvcinaHra.Client.Services;

/// <summary>
/// Routes DevExpress string lookups through the project's resx files.
/// <see cref="System.Resources.ResourceManager"/> automatically picks the culture-specific
/// variant (<c>LocalizationRes.cs.resx</c> under cs*, else the neutral <c>LocalizationRes.resx</c>).
/// Falls back to DevExpress's built-in resources when a key isn't in either file.
/// Drop updated resx files obtained from https://localization.devexpress.com/ into
/// <c>Resources/</c> and translations take effect automatically.
/// </summary>
public class LocalizationService : DxLocalizationService, IDxLocalizationService
{
    string? IDxLocalizationService.GetString(string key)
    {
        var value = Resources.LocalizationRes.ResourceManager.GetString(key);
        return !string.IsNullOrEmpty(value) ? value : base.GetString(key);
    }
}
