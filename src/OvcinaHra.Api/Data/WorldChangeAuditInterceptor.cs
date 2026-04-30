using System.Diagnostics;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OvcinaHra.Shared.Domain.Entities;
using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Api.Data;

public class WorldChangeAuditInterceptor(
    IHttpContextAccessor httpContextAccessor,
    ILogger<WorldChangeAuditInterceptor> logger) : SaveChangesInterceptor
{
    private static readonly HashSet<Type> AuditedEntityTypes =
    [
        typeof(Location),
        typeof(OrganizerRoleAssignment),
        typeof(TreasureQuest),
        typeof(Item),
        typeof(Building),
        typeof(CraftingRecipe),
        typeof(BuildingRecipe),
        typeof(Game),
        typeof(GameTimeSlot),
        typeof(Monster),
        typeof(PersonalQuest),
        typeof(Quest),
        typeof(Npc),
        typeof(Character)
    ];

    private static readonly string[] DisplayNameProperties =
    [
        "Name",
        "Title",
        "PersonName",
        "DisplayName"
    ];

    private List<PendingWorldChange>? _pending;
    private bool _savingAuditRows;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (_savingAuditRows || eventData.Context is not WorldDbContext db)
            return base.SavingChangesAsync(eventData, result, cancellationToken);

        try
        {
            _pending = db.ChangeTracker
                .Entries()
                .Select(CreatePending)
                .Where(p => p is not null)
                .Select(p => p!)
                .ToList();

            logger.LogInformation(
                "[world-change-audit] interceptor.entry entityCount={EntityCount}",
                _pending.Count);
        }
        catch (Exception ex)
        {
            _pending = null;
            logger.LogWarning(ex,
                "[world-change-audit] interceptor.capture-failed");
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (_savingAuditRows || eventData.Context is not WorldDbContext db)
            return await base.SavedChangesAsync(eventData, result, cancellationToken);

        var pending = _pending;
        _pending = null;

        if (pending is null || pending.Count == 0)
            return await base.SavedChangesAsync(eventData, result, cancellationToken);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var rows = pending
                .Select(BuildWorldChange)
                .Where(c => c is not null)
                .Select(c => c!)
                .ToList();

            foreach (var row in rows)
            {
                logger.LogInformation(
                    "[world-change-audit] interceptor.captured entityType={EntityType} operation={Operation} entityId={EntityId} actorUserId={ActorUserId}",
                    row.EntityType,
                    row.Operation,
                    row.EntityId,
                    row.ActorUserId);
            }

            if (rows.Count > 0)
            {
                _savingAuditRows = true;
                db.WorldChanges.AddRange(rows);
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            DetachPendingAuditRows(db);
            logger.LogWarning(ex,
                "[world-change-audit] interceptor.persist-failed pendingCount={PendingCount}",
                pending.Count);
        }
        finally
        {
            _savingAuditRows = false;
            stopwatch.Stop();
            logger.LogInformation(
                "[world-change-audit] interceptor.exit elapsedMs={ElapsedMs}",
                stopwatch.ElapsedMilliseconds);
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        _pending = null;
        _savingAuditRows = false;
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    private PendingWorldChange? CreatePending(EntityEntry entry)
    {
        if (!AuditedEntityTypes.Contains(entry.Metadata.ClrType))
            return null;

        var operation = entry.State switch
        {
            EntityState.Added => WorldChangeOperation.Created,
            EntityState.Modified when HasMeaningfulModification(entry) => WorldChangeOperation.Updated,
            EntityState.Deleted => WorldChangeOperation.Deleted,
            _ => (WorldChangeOperation?)null
        };

        if (operation is null)
            return null;

        var useOriginal = operation == WorldChangeOperation.Deleted;
        var actor = CurrentActor();
        var entityId = operation == WorldChangeOperation.Created
            ? null
            : ReadIntProperty(entry, "Id", useOriginal);

        return new PendingWorldChange(
            entry,
            entry.Metadata.ClrType.Name,
            operation.Value,
            entityId,
            ReadGameId(entry, useOriginal),
            ReadDisplayName(entry, useOriginal),
            actor.UserId,
            actor.DisplayName);
    }

    private static bool HasMeaningfulModification(EntityEntry entry)
        => entry.Properties.Any(p => p.IsModified && !p.Metadata.IsPrimaryKey());

    private WorldChange? BuildWorldChange(PendingWorldChange pending)
    {
        var entityId = pending.EntityId ?? ReadIntProperty(pending.Entry, "Id", useOriginal: false);
        if (entityId is null or <= 0)
            return null;

        var capturedGameId = pending.GameId is > 0 ? pending.GameId : null;
        var gameId = capturedGameId
            ?? (pending.EntityType == nameof(Game) ? entityId : ReadGameId(pending.Entry, useOriginal: false));

        var entityName = pending.EntityName
            ?? ReadDisplayName(pending.Entry, useOriginal: false)
            ?? $"{pending.EntityType} #{entityId.Value}";

        return new WorldChange
        {
            GameId = gameId,
            EntityType = pending.EntityType,
            EntityId = entityId.Value,
            EntityName = TrimToMax(entityName, 300),
            Operation = pending.Operation,
            ActorUserId = TrimToMax(pending.ActorUserId, 200),
            ActorDisplayName = TrimToMax(pending.ActorDisplayName, 200),
            ChangedAtUtc = DateTime.UtcNow
        };
    }

    private (string UserId, string DisplayName) CurrentActor()
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
            return ("system", "System");

        var userId = user.FindFirstValue("sub")
            ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? "system";
        var displayName = user.FindFirstValue("name")
            ?? user.FindFirstValue(ClaimTypes.Name)
            ?? user.Identity.Name
            ?? userId;

        return (userId, displayName);
    }

    private static int? ReadGameId(EntityEntry entry, bool useOriginal)
    {
        if (entry.Metadata.ClrType == typeof(Game))
            return ReadIntProperty(entry, "Id", useOriginal);

        return ReadIntProperty(entry, "GameId", useOriginal);
    }

    private static string? ReadDisplayName(EntityEntry entry, bool useOriginal)
    {
        foreach (var propertyName in DisplayNameProperties)
        {
            var value = ReadStringProperty(entry, propertyName, useOriginal);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        var startTime = ReadDateTimeProperty(entry, "StartTime", useOriginal);
        return startTime is null ? null : $"GameTimeSlot {startTime.Value:yyyy-MM-dd HH:mm}";
    }

    private static int? ReadIntProperty(EntityEntry entry, string propertyName, bool useOriginal)
    {
        if (entry.Metadata.FindProperty(propertyName) is null)
            return null;

        var value = useOriginal
            ? entry.Property(propertyName).OriginalValue
            : entry.Property(propertyName).CurrentValue;

        return value switch
        {
            int i => i,
            _ => null
        };
    }

    private static string? ReadStringProperty(EntityEntry entry, string propertyName, bool useOriginal)
    {
        if (entry.Metadata.FindProperty(propertyName) is null)
            return null;

        var value = useOriginal
            ? entry.Property(propertyName).OriginalValue
            : entry.Property(propertyName).CurrentValue;

        return value as string;
    }

    private static DateTime? ReadDateTimeProperty(EntityEntry entry, string propertyName, bool useOriginal)
    {
        if (entry.Metadata.FindProperty(propertyName) is null)
            return null;

        var value = useOriginal
            ? entry.Property(propertyName).OriginalValue
            : entry.Property(propertyName).CurrentValue;

        return value switch
        {
            DateTime dt => dt,
            _ => null
        };
    }

    private static string TrimToMax(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

    private static void DetachPendingAuditRows(WorldDbContext db)
    {
        foreach (var entry in db.ChangeTracker.Entries<WorldChange>()
                     .Where(e => e.State is EntityState.Added or EntityState.Modified)
                     .ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    private sealed record PendingWorldChange(
        EntityEntry Entry,
        string EntityType,
        WorldChangeOperation Operation,
        int? EntityId,
        int? GameId,
        string? EntityName,
        string ActorUserId,
        string ActorDisplayName);
}
