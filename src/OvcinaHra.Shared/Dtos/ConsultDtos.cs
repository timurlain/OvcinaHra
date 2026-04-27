namespace OvcinaHra.Shared.Dtos;

public sealed record ConsultRequestDto(string Message);
public sealed record ConsultAnswerDto(string Answer, int TokensUsed);
public sealed record ConsultAvailabilityDto(bool Enabled);
