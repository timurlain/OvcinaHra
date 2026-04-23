namespace OvcinaHra.Client.Components;

/// <summary>
/// Shared CSS-class mapping for the Items class-pip cells (Vál/Luč/Mág/Zlo).
/// Used by both the inline grid CellTemplates in <c>ItemList.razor</c> and the
/// 4-pip <c>ItemClassPips</c> component, so the lit/intensity ramp stays in
/// one place. Extracted per Copilot review on PR #88.
/// </summary>
public static class ItemClassPipsHelpers
{
    /// <summary>
    /// Returns the CSS class suffix for a class-requirement cell:
    /// empty when not required, lit when req &gt;= 1 with intensity ramp r2-r5.
    /// </summary>
    public static string PipClass(int req) => req switch
    {
        <= 0 => "",
        1 => "oh-it-pip--lit",
        2 => "oh-it-pip--lit oh-it-pip--lit-r2",
        3 => "oh-it-pip--lit oh-it-pip--lit-r3",
        4 => "oh-it-pip--lit oh-it-pip--lit-r4",
        _ => "oh-it-pip--lit oh-it-pip--lit-r5",
    };
}
