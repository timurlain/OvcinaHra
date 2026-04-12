using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Shared.Domain.ValueObjects;

public record ClassRequirements(int Warrior, int Archer, int Mage, int Thief)
{
    public int GetRequirement(PlayerClass playerClass) => playerClass switch
    {
        PlayerClass.Warrior => Warrior,
        PlayerClass.Archer => Archer,
        PlayerClass.Mage => Mage,
        PlayerClass.Thief => Thief,
        _ => 0
    };

    public bool IsUnrestricted => Warrior == 0 && Archer == 0 && Mage == 0 && Thief == 0;
}
