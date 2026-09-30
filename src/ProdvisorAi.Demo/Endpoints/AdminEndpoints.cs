using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using ProdvisorAi.Demo.Data;
using ProdvisorAi.Demo.Prodvisor;
using ProdvisorAi.Demo.Services;

namespace ProdvisorAi.Demo.Endpoints;

/// <summary>Service administration: OpenAI key + model, system prompt, machines, request log.</summary>
public static class AdminEndpoints
{
    /// <summary>ApiKey is write-only: null/empty = keep the current key.</summary>
    public sealed record SettingsUpdate(string? ApiKey, bool ClearApiKey, string? Model, bool? MockMode);
    public sealed record TextBody(string Text);

    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin");

        // ---- Settings (the decrypted key is NEVER returned) ----
        admin.MapGet("/settings", async (SettingsService svc, IConfiguration config) => SettingsView(await svc.GetAsync(), config));
        admin.MapPut("/settings", async (SettingsUpdate body, SettingsService svc, IConfiguration config) =>
        {
            if (body.Model is not null && string.IsNullOrWhiteSpace(body.Model))
                return Results.BadRequest(new { error = "Nazwa modelu nie może być pusta." });
            await svc.UpdateAsync(body.ApiKey, body.ClearApiKey, body.Model, mockMode: body.MockMode);
            return Results.Ok(SettingsView(await svc.GetAsync(), config));
        });
        admin.MapPost("/settings/test", async (SettingsService svc, OpenAiResponsesClient client, CancellationToken ct) =>
        {
            var s = await svc.GetAsync(ct);
            if (!s.HasApiKey) return Results.Ok(new { ok = false, message = "Brak klucza API." });
            try { return Results.Ok(new { ok = true, message = await client.TestAsync(new OpenAiCredentials(s.ApiKey!, s.Organization, s.Project), s.Model, ct) }); }
            catch (AiServiceException ex) { return Results.Ok(new { ok = false, message = ex.Message }); }
        });

        // ---- System prompt (one, used for every request) ----
        admin.MapGet("/system-prompt", async (AppDbContext db) =>
            await db.SystemPrompts.AsNoTracking().FirstAsync(p => p.Id == SeedData.SystemPromptId));
        admin.MapPut("/system-prompt", async (TextBody body, AppDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(body.Text)) return Results.BadRequest(new { error = "Treść jest wymagana." });
            var prompt = await db.SystemPrompts.FirstAsync(p => p.Id == SeedData.SystemPromptId);
            prompt.Text = body.Text;
            prompt.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(prompt);
        });
        admin.MapPost("/system-prompt/reset", async (AppDbContext db) =>
        {
            var prompt = await db.SystemPrompts.FirstAsync(p => p.Id == SeedData.SystemPromptId);
            prompt.Text = SeedData.DefaultSystemPrompt;
            prompt.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(prompt);
        });

        // ---- Machines + materials (read from PRODVISOR; only "active" is local) ----
        admin.MapGet("/machines", async (AppDbContext db) =>
            (await db.Machines.AsNoTracking().ToListAsync())
                .OrderBy(m => m.IsVirtual).ThenBy(m => m.IsVirtual ? m.Name : m.Id)
                .Select(m => new
                {
                    m.Id, m.Name, m.Type, m.Active, m.ProdvisorId, m.WorkplaceId, m.GroupName, m.IsVirtual,
                    Parameters = m.ParametersJson is null ? [] : JsonSerializer.Deserialize<List<MachineParameter>>(m.ParametersJson)!,
                }));
        admin.MapPut("/machines/{id}/active", async (string id, bool active, AppDbContext db) =>
        {
            var machine = await db.Machines.FindAsync(id);
            if (machine is null) return Results.NotFound();
            machine.Active = active;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
        admin.MapGet("/materials", async (AppDbContext db) => await db.Materials.AsNoTracking().OrderBy(m => m.Name).ToListAsync());
        admin.MapPost("/sync", async (ProdvisorSource source, ProdvisorImporter importer, CancellationToken ct) =>
        {
            try { return Results.Ok(await importer.ImportAsync(await source.LoadAsync(ct), ct)); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Results.BadRequest(new { error = $"Nie można odczytać danych PRODVISOR ({source.Provider}): {ex.Message}" });
            }
        });

        // ---- Request log ----
        admin.MapGet("/jobs", async (AppDbContext db) =>
            (await db.Jobs.AsNoTracking().OrderByDescending(j => j.CreatedAt).Take(200).ToListAsync())
                .Select(j => new
                {
                    j.Id, j.CreatedAt, j.Status, j.ElementId, j.PartNumber, j.TechnologyPromptName, j.Model, j.Mock,
                    j.DurationMs, j.TotalTokens, j.ErrorKind,
                }));
        admin.MapGet("/jobs/{id}", async (string id, AppDbContext db) =>
        {
            var job = await db.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == id);
            if (job is null) return Results.NotFound();
            return Results.Ok(new
            {
                api = await TechnologyApiEndpoints.ToApiViewAsync(job, db),
                drawingUrl = $"/api/admin/jobs/{job.Id}/drawing",
                drawingText = job.DrawingText,
                technologyPrompt = job.TechnologyPrompt,
                debug = new { request = Parse(job.DebugRequestJson), response = Parse(job.DebugResponseJson) },
            });
        });
        admin.MapGet("/jobs/{id}/drawing", async (string id, AppDbContext db, StoragePaths paths) =>
        {
            var job = await db.Jobs.FindAsync(id);
            if (job is null || !File.Exists(paths.Resolve(job.DrawingPath))) return Results.NotFound();
            return Results.File(paths.Resolve(job.DrawingPath), job.DrawingContentType);
        });

        admin.MapGet("/info", (IConfiguration config, ProdvisorSource source) => new
        {
            version = typeof(AdminEndpoints).Assembly.GetName().Version?.ToString(3),
            prodvisorSource = source.Provider,
            maxUploadMb = config.GetValue("Upload:MaxSizeMb", 20),
        });
    }

    static object SettingsView(AiSettings s, IConfiguration config) => new
    {
        hasApiKey = s.HasApiKey,
        apiKeyMasked = s.HasApiKey ? SecretProtector.Mask(s.ApiKey!) : null,
        model = s.Model,
        mockMode = s.MockMode,
        effectiveMock = s.UseMock,
        suggestedModels = config.GetSection("OpenAI:SuggestedModels").Get<string[]>() ?? [],
    };

    static JsonNode? Parse(string? json) => json is null ? null : JsonNode.Parse(json);
}
