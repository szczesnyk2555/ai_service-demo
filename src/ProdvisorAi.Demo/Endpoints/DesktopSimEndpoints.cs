using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using ProdvisorAi.Demo.Data;
using ProdvisorAi.Demo.Services;

namespace ProdvisorAi.Demo.Endpoints;

/// <summary>
/// Stand-in for the PRODVISOR desktop application's own data (NOT part of the service API):
/// elements with drawings, the technologist's technology prompt, dictionaries and saving an approved technology.
/// The simulated desktop page (/desktop/) talks to the service only through /api/v1.
/// </summary>
public static class DesktopSimEndpoints
{
    const string TechnologyPromptKey = "desktop.technology_prompt";

    public sealed record PromptBody(string Text);

    public static void MapDesktopSimEndpoints(this IEndpointRouteBuilder app)
    {
        var sim = app.MapGroup("/sim/api");

        sim.MapGet("/elements", async (AppDbContext db) =>
        {
            var saved = await db.SavedTechnologies.AsNoTracking().ToDictionaryAsync(t => t.ElementId, t => t.SavedAt);
            return (await db.Elements.AsNoTracking().OrderBy(e => e.Number).ToListAsync()).Select(e => new
            {
                e.Id, e.Number, e.Name, e.Material, e.Quantity, e.DrawingFile,
                savedAt = saved.TryGetValue(e.Id, out var at) ? at : (DateTime?)null,
            });
        });

        // The drawing attached to the element (in the demo: the bundled sample files)
        sim.MapGet("/elements/{id}/drawing", async (string id, AppDbContext db) =>
        {
            var element = await db.Elements.FindAsync(id);
            var path = element is null ? null : Path.Combine(AppContext.BaseDirectory, "samples", element.DrawingFile);
            if (path is null || !File.Exists(path)) return Results.NotFound();
            var type = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".pdf" => "application/pdf", _ => "application/dxf",
            };
            return Results.File(path, type, element!.DrawingFile);
        });

        // Preview for the screen (browsers cannot display DXF - rendered to PNG here)
        sim.MapGet("/elements/{id}/preview", async (string id, AppDbContext db, DxfDrawingConverter dxf) =>
        {
            var element = await db.Elements.FindAsync(id);
            var path = element is null ? null : Path.Combine(AppContext.BaseDirectory, "samples", element.DrawingFile);
            if (path is null || !File.Exists(path)) return Results.NotFound();
            if (!path.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase)) return Results.Redirect($"/sim/api/elements/{id}/drawing");
            return Results.File(dxf.Convert(await File.ReadAllBytesAsync(path)).Png, "image/png");
        });

        // Dictionaries the desktop already has (machines with PRODVISOR ids, materials) - for editing the result
        sim.MapGet("/lookup", async (AppDbContext db) => new
        {
            machines = (await db.Machines.AsNoTracking().Where(m => m.Active).ToListAsync())
                .OrderBy(m => m.IsVirtual).ThenBy(m => m.Id)
                .Select(m => new { code = m.Id, m.Name, maszynyId = m.ProdvisorId, stanowiskoId = m.WorkplaceId, group = m.GroupName }),
            materials = await db.Materials.AsNoTracking().Where(m => m.Active).OrderBy(m => m.Name).Select(m => new { m.Id, m.Name }).ToListAsync(),
        });

        // Technology prompt = the technologist's company rules, kept in the desktop application
        sim.MapGet("/technology-prompt", async (AppDbContext db) =>
            new { text = (await db.Settings.FindAsync(TechnologyPromptKey))?.Value ?? SeedData.DefaultTechnologyPrompt });
        sim.MapPut("/technology-prompt", async (PromptBody body, AppDbContext db) =>
        {
            var row = await db.Settings.FindAsync(TechnologyPromptKey);
            if (row is null) db.Settings.Add(new AppSetting { Key = TechnologyPromptKey, Value = body.Text });
            else row.Value = body.Text;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // Approved technology saved to PRODVISOR (simulation: stored as JSON per element)
        sim.MapGet("/elements/{id}/technology", async (string id, AppDbContext db) =>
            await db.SavedTechnologies.FindAsync(id) is { } t
                ? Results.Ok(new { t.JobId, t.SavedAt, technology = JsonNode.Parse(t.Json) })
                : Results.NotFound());
        sim.MapPut("/elements/{id}/technology", async (string id, HttpRequest request, AppDbContext db) =>
        {
            if (await db.Elements.FindAsync(id) is null) return Results.NotFound();
            var body = await JsonNode.ParseAsync(request.Body) as JsonObject;
            if (body?["technology"] is not JsonObject tech) return Results.BadRequest(new { error = "Brak technologii." });

            var row = await db.SavedTechnologies.FindAsync(id);
            if (row is null) db.SavedTechnologies.Add(row = new SavedTechnology { ElementId = id });
            row.JobId = body["jobId"]?.GetValue<string>() ?? "";
            row.SavedAt = DateTime.UtcNow;
            row.Json = tech.ToJsonString();
            await db.SaveChangesAsync();
            return Results.Ok(new { row.SavedAt });
        });
    }
}
