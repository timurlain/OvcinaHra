using Microsoft.EntityFrameworkCore;
using OvcinaHra.Shared.Domain.Entities;

namespace OvcinaHra.Api.Data;

public class WorldDbContext(DbContextOptions<WorldDbContext> options) : DbContext(options)
{
    public DbSet<Game> Games => Set<Game>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<GameLocation> GameLocations => Set<GameLocation>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<GameItem> GameItems => Set<GameItem>();
    public DbSet<Building> Buildings => Set<Building>();
    public DbSet<GameBuilding> GameBuildings => Set<GameBuilding>();
    public DbSet<CraftingRecipe> CraftingRecipes => Set<CraftingRecipe>();
    public DbSet<CraftingIngredient> CraftingIngredients => Set<CraftingIngredient>();
    public DbSet<CraftingBuildingRequirement> CraftingBuildingRequirements => Set<CraftingBuildingRequirement>();
    public DbSet<Monster> Monsters => Set<Monster>();
    public DbSet<Npc> Npcs => Set<Npc>();
    public DbSet<GameNpc> GameNpcs => Set<GameNpc>();
    public DbSet<MonsterTagLink> MonsterTagLinks => Set<MonsterTagLink>();
    public DbSet<MonsterLoot> MonsterLoots => Set<MonsterLoot>();
    public DbSet<GameMonster> GameMonsters => Set<GameMonster>();
    public DbSet<Quest> Quests => Set<Quest>();
    public DbSet<QuestTagLink> QuestTagLinks => Set<QuestTagLink>();
    public DbSet<QuestLocationLink> QuestLocationLinks => Set<QuestLocationLink>();
    public DbSet<QuestEncounter> QuestEncounters => Set<QuestEncounter>();
    public DbSet<QuestReward> QuestRewards => Set<QuestReward>();
    public DbSet<SecretStash> SecretStashes => Set<SecretStash>();
    public DbSet<GameSecretStash> GameSecretStashes => Set<GameSecretStash>();
    public DbSet<TreasureQuest> TreasureQuests => Set<TreasureQuest>();
    public DbSet<TreasureItem> TreasureItems => Set<TreasureItem>();
    public DbSet<GameTimeSlot> GameTimeSlots => Set<GameTimeSlot>();
    public DbSet<BattlefieldBonus> BattlefieldBonuses => Set<BattlefieldBonus>();
    public DbSet<LocalUser> LocalUsers => Set<LocalUser>();
    public DbSet<Character> Characters => Set<Character>();
    public DbSet<CharacterAssignment> CharacterAssignments => Set<CharacterAssignment>();
    public DbSet<CharacterEvent> CharacterEvents => Set<CharacterEvent>();
    public DbSet<GameEvent> GameEvents => Set<GameEvent>();
    public DbSet<GameEventTimeSlot> GameEventTimeSlots => Set<GameEventTimeSlot>();
    public DbSet<GameEventLocation> GameEventLocations => Set<GameEventLocation>();
    public DbSet<GameEventQuest> GameEventQuests => Set<GameEventQuest>();
    public DbSet<GameEventNpc> GameEventNpcs => Set<GameEventNpc>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WorldDbContext).Assembly);
    }
}
