using System.Text.Json.Serialization;

namespace OvcinaHra.Shared.Domain.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CipherSkillKey
{
    HledaniMagie,
    Prohledavani,
    SestySmysl,
    ZnalostBytosti,
    Lezeni
}
