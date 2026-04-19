using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Shared.Extensions;

/// <summary>
/// UI helpers for <see cref="PlayerClass"/>. The <c>GetDisplayName</c> lookup
/// is provided by <see cref="EnumExtensions.GetDisplayName(System.Enum)"/> and
/// reused here to keep a single source of truth.
/// </summary>
public static class PlayerClassExtensions
{
    /// <summary>
    /// Label used in the Hrdina UI and elsewhere when showing a class restriction:
    /// the localized display name for a concrete class, or "Dobrodruh" when no
    /// class is required (null means "available to all adventurers").
    /// </summary>
    public static string GetClassRestrictionLabel(this PlayerClass? pc)
        => pc.HasValue ? pc.Value.GetDisplayName() : "Dobrodruh";
}
