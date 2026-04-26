using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace OvcinaHra.Shared.Domain.Enums;

/// <summary>
/// Recipe taxonomy from the cookbook design (issue #218). Drives the
/// catalog filter chips and the category-coloured ribbon on each formula
/// card. Existing rows backfill to <see cref="Ostatni"/> via Migration B
/// so organizers can re-categorise after the migration.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RecipeCategory
{
    [Display(Name = "Budova")]   Budova,
    [Display(Name = "Lektvar")]  Lektvar,
    [Display(Name = "Artefakt")] Artefakt,
    [Display(Name = "Ostatní")]  Ostatni
}
