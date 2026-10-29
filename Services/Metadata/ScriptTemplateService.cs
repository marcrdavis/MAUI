using Microsoft.EntityFrameworkCore;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Resources.Scripts;

namespace MoveItAutomationUI.Services.Metadata;

public sealed record ScriptTemplate(string Id, string Name, string Source);
public sealed record ScriptTemplateSave(string Name = "", string Source = "", bool OverwriteExisting = false, string TemplateId = "", string Language = "PS1");

// Reuses the provider-neutral, environment-scoped metadata store. No schema change.
// Serialize name checks, first-use seeding and mutations across requests in this app.
public sealed class ScriptTemplateService(MetadataDbContext db, IMoveItSessionService sessions)
{
    private const string Type = "ScriptTemplate", SeedType = "ScriptTemplateSeed";
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private (string Environment, string User) Context()
    {
        var session = sessions.Get() ?? throw new MoveItAuthenticationException("MOVEit session expired. Please sign in again.");
        return (MoveItEnvironmentKey.Normalize(session.GetEnvironmentKey()), session.Username);
    }
    public async Task<IReadOnlyList<ScriptTemplate>> GetAsync(CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        try { var context = Context(); await SeedAsync(context.Environment, context.User, ct); return await ListAsync(context.Environment, ct); }
        finally { Gate.Release(); }
    }
    private async Task<List<ScriptTemplate>> ListAsync(string environment, CancellationToken ct)
    {
        var values = await db.MetadataValues.AsNoTracking().Where(x => x.EnvironmentKey == environment && x.ObjectType == Type).ToListAsync(ct);
        return values.GroupBy(x => x.ObjectId).Select(group => new ScriptTemplate(group.Key,
            group.FirstOrDefault(x => x.FieldName == "Name")?.Value ?? "", group.FirstOrDefault(x => x.FieldName == "Source")?.Value ?? ""))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
    private void Add(string environment, string user, string id, string name, string source, string type = Type)
    {
        var now = DateTimeOffset.UtcNow;
        db.MetadataObjects.Add(new() { EnvironmentKey = environment, ObjectType = type, ObjectId = id,
            MetadataCreatedUtc = now, MetadataCreatedBy = user, MetadataModifiedUtc = now, MetadataModifiedBy = user });
        if (type != Type) return;
        foreach (var (key, value) in new[] { ("Name", name), ("Source", source) })
            db.MetadataValues.Add(new() { EnvironmentKey = environment, ObjectType = Type, ObjectId = id, FieldName = key, Value = value });
    }
    private async Task SeedAsync(string environment, string user, CancellationToken ct)
    {
        if (await db.MetadataObjects.AnyAsync(x => x.EnvironmentKey == environment && x.ObjectType == SeedType && x.ObjectId == "v1", ct)) return;
        var templates = await ListAsync(environment, ct);
        var defaults = new[] { ("Download Basic Auth", "DownloadBasicAuth.ps1"), ("Get JSON Next ID", "GetJSONNextID.ps1"),
            ("MOVEit Transfer REST API", "MOVEitTransferRESTAPI.ps1"), ("Process File Line by Line", "ProcessFileLineByLine.ps1") };
        foreach (var (name, file) in defaults)
        {
            if (templates.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
            using var stream = typeof(ScriptTemplateService).Assembly.GetManifestResourceStream("MoveItAutomationUI.Templates.Scripts." + file)
                ?? throw new InvalidOperationException("A default Script template is unavailable.");
            using var reader = new StreamReader(stream);
            Add(environment, user, Guid.NewGuid().ToString("N"), name, await reader.ReadToEndAsync(ct));
        }
        Add(environment, user, "v1", "", "", SeedType);
        await db.SaveChangesAsync(ct); // Seed marker and all defaults are one EF transaction.
    }
    public async Task<ScriptTemplate> SaveAsync(ScriptTemplateSave request, CancellationToken ct = default)
    {
        if (ScriptConfiguration.Language(request.Language) != "PS1") throw new ArgumentException("Only PowerShell Scripts can be saved as templates.");
        var name = request.Name?.Trim() ?? "";
        if (name.Length is 0 or > 100) throw new ArgumentException("Template Name is required and must contain at most 100 characters.");
        var source = request.Source ?? "";
        if (string.IsNullOrWhiteSpace(source) || source.Length > ScriptConfiguration.MaxSourceLength) throw new ArgumentException("Template Source is required and must be no larger than 2 MiB of text.");
        await Gate.WaitAsync(ct);
        try
        {
            var (environment, user) = Context(); await SeedAsync(environment, user, ct);
            var templates = await ListAsync(environment, ct);
            var selected = string.IsNullOrWhiteSpace(request.TemplateId) ? null : templates.FirstOrDefault(x => x.Id == request.TemplateId)
                ?? throw new InvalidOperationException("The selected Script template no longer exists.");
            var named = templates.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (named is not null && named.Id != selected?.Id && (!request.OverwriteExisting || selected is not null))
                throw new InvalidOperationException($"A Script template named '{name}' already exists. Choose a unique name or select Overwrite existing template if present.");
            var target = selected ?? (request.OverwriteExisting ? named : null);
            var id = target?.Id ?? Guid.NewGuid().ToString("N");
            if (target is null) Add(environment, user, id, name, source);
            else
            {
                var values = await db.MetadataValues.Where(x => x.EnvironmentKey == environment && x.ObjectType == Type && x.ObjectId == id).ToListAsync(ct);
                foreach (var (field, value) in new[] { ("Name", name), ("Source", source) })
                {
                    var entry = values.FirstOrDefault(x => x.FieldName == field);
                    if (entry is null) db.MetadataValues.Add(new() { EnvironmentKey = environment, ObjectType = Type, ObjectId = id, FieldName = field, Value = value });
                    else entry.Value = value;
                }
                var item = await db.MetadataObjects.SingleAsync(x => x.EnvironmentKey == environment && x.ObjectType == Type && x.ObjectId == id, ct);
                item.MetadataModifiedUtc = DateTimeOffset.UtcNow; item.MetadataModifiedBy = user;
            }
            await db.SaveChangesAsync(ct); return new(id, name, source);
        }
        finally { Gate.Release(); }
    }
    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var (environment, _) = Context();
            var item = await db.MetadataObjects.SingleOrDefaultAsync(x => x.EnvironmentKey == environment && x.ObjectType == Type && x.ObjectId == id, ct)
                ?? throw new InvalidOperationException("The selected Script template no longer exists.");
            db.MetadataValues.RemoveRange(await db.MetadataValues.Where(x => x.EnvironmentKey == environment && x.ObjectType == Type && x.ObjectId == id).ToListAsync(ct));
            db.MetadataObjects.Remove(item); await db.SaveChangesAsync(ct);
        }
        finally { Gate.Release(); }
    }
}
