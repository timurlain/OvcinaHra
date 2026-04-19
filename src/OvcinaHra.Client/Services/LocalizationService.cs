using System.Globalization;
using DevExpress.Blazor.Localization;

namespace OvcinaHra.Client.Services;

/// <summary>
/// Routes DevExpress string lookups through our Czech resx when the UI culture is cs-CZ,
/// falling back to DevExpress's built-in English when no Czech entry exists.
/// Drop the Czech resx file obtained from https://localization.devexpress.com/ into
/// <c>Resources/LocalizationRes.cs.resx</c> and its strings will take effect automatically.
/// </summary>
public class LocalizationService : DxLocalizationService, IDxLocalizationService
{
    string? IDxLocalizationService.GetString(string key)
    {
        if (CultureInfo.CurrentUICulture.Name == "cs-CZ")
        {
            var czech = Resources.LocalizationRes.ResourceManager.GetString(key);
            if (!string.IsNullOrEmpty(czech))
                return czech;
        }
        return base.GetString(key);
    }
}
