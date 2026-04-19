using System.Text.Json.Serialization;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Extensions;

namespace OvcinaHra.Shared.Dtos;

public record TagDto(int Id, string Name, TagKind Kind)
{
    [JsonIgnore]
    public string KindDisplay => Kind.GetDisplayName();
}

public record CreateTagDto(string Name, TagKind Kind);

public record UpdateTagDto(string Name);
