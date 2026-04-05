using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Shared.Dtos;

public record TagDto(int Id, string Name, TagKind Kind);

public record CreateTagDto(string Name, TagKind Kind);

public record UpdateTagDto(string Name);
