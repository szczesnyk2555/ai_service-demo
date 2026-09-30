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
    public sealed record PromptBody(string Name, string Text);

    const string ElementsFolder = "elements";

    static string DrawingPath(ProdvisorElement e, StoragePaths paths) =>
        e.DrawingFile.StartsWith(ElementsFolder + "/")
            ? paths.Resolve(e.DrawingFile)
            : Path.Combine(AppContext.BaseDirectory, "samples", e.DrawingFile);

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

        // New element with its drawing (multipart: number, name, material, quantity, drawing)
        sim.MapPost("/elements", async (HttpRequest request, AppDbContext db, StoragePaths paths, IConfiguration config) =>
        {
            if (!request.HasFormContentType) return Results.BadRequest(new { error = "Oczekiwano multipart/form-data." });
            var form = await request.ReadFormAsync();
            string? Field(string key) => form[key].ToString().Trim() is { Length: > 0 } v ? v : null;

            var number = Field("number");
            var name = Field("name");
            if (number is null || name is null) return Results.BadRequest(new { error = "Numer i nazwa są wymagane." });
            int? quantity = null;
            if (Field("quantity") is { } q)
            {
                if (!int.TryParse(q, out var qv) || qv <= 0) return Results.BadRequest(new { error = "Ilość musi być liczbą większą od 0." });
                quantity = qv;
            }

            var file = form.Files.GetFile("drawing");
            if (file is null || file.Length == 0) return Results.BadRequest(new { error = "Dodaj rysunek (PNG, JPG, PDF lub DXF)." });
            var maxMb = config.GetValue("Upload:MaxSizeMb", 20);
            if (file.Length > maxMb * 1024L * 1024L) return Results.BadRequest(new { error = $"Rysunek jest większy niż {maxMb} MB." });
            await using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            var bytes = ms.ToArray();
            var (type, ext) = DrawingFiles.DetectType(bytes);
            if (type is null) return Results.BadRequest(new { error = "Nieobsługiwany typ pliku. Dozwolone: PNG, JPG, PDF, DXF." });

            // element_id: next number (PRODVISOR assigns it in the real application)
            var ids = await db.Elements.Select(e => e.Id).ToListAsync();
            var id = (ids.Select(i => long.TryParse(i, out var n) ? n : 0).DefaultIfEmpty(100230).Max() + 1).ToString();

            // Uploaded drawings live in the data directory; the seeded ones come from the samples folder.
            var baseName = string.Concat(Path.GetFileNameWithoutExtension(file.FileName).Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
            var drawingFile = $"{ElementsFolder}/{id}_{(baseName == "" ? "rysunek" : baseName)}{ext}";
            Directory.CreateDirectory(paths.Resolve(ElementsFolder));
            await File.WriteAllBytesAsync(paths.Resolve(drawingFile), bytes);

            var element = new ProdvisorElement { Id = id, Number = number, Name = name, Material = Field("material"), Quantity = quantity, DrawingFile = drawingFile };
            db.Elements.Add(element);
            await db.SaveChangesAsync();
            return Results.Ok(element);
        });

        sim.MapDelete("/elements/{id}", async (string id, AppDbContext db, StoragePaths paths) =>
        {
            var element = await db.Elements.FindAsync(id);
            if (element is null) return Results.NotFound();
            if (await db.SavedTechnologies.FindAsync(id) is { } saved) db.SavedTechnologies.Remove(saved);
            db.Elements.Remove(element);
            await db.SaveChangesAsync();
            if (element.DrawingFile.StartsWith(ElementsFolder + "/") && File.Exists(paths.Resolve(element.DrawingFile)))
                File.Delete(paths.Resolve(element.DrawingFile));
            return Results.NoContent();
        });

        // The drawing attached to the element
        sim.MapGet("/elements/{id}/drawing", async (string id, AppDbContext db, StoragePaths paths) =>
        {
            var element = await db.Elements.FindAsync(id);
            var path = element is null ? null : DrawingPath(element, paths);
            if (path is null || !File.Exists(path)) return Results.NotFound();
            var type = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".pdf" => "application/pdf", _ => "application/dxf",
            };
            return Results.File(path, type, Path.GetFileName(element!.DrawingFile));
        });

        // Preview for the screen (browsers cannot display DXF - rendered to PNG here)
        sim.MapGet("/elements/{id}/preview", async (string id, AppDbContext db, StoragePaths paths, DxfDrawingConverter dxf) =>
        {
            var element = await db.Elements.FindAsync(id);
            var path = element is null ? null : DrawingPath(element, paths);
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

        // Technology prompts = the technologist's company rules, kept in the desktop application; one is chosen per generation
        sim.MapGet("/technology-prompts", async (AppDbContext db) =>
            await db.TechnologyPrompts.AsNoTracking().OrderBy(p => p.Name).ToListAsync());
        sim.MapPost("/technology-prompts", async (PromptBody body, AppDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(body.Name)) return Results.BadRequest(new { error = "Nazwa jest wymagana." });
            var prompt = new TechnologyPrompt { Id = Guid.NewGuid().ToString("N")[..8], Name = body.Name.Trim(), Text = body.Text ?? "" };
            db.TechnologyPrompts.Add(prompt);
            await db.SaveChangesAsync();
            return Results.Ok(prompt);
        });
        sim.MapPut("/technology-prompts/{id}", async (string id, PromptBody body, AppDbContext db) =>
        {
            var prompt = await db.TechnologyPrompts.FindAsync(id);
            if (prompt is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(body.Name)) return Results.BadRequest(new { error = "Nazwa jest wymagana." });
            prompt.Name = body.Name.Trim();
            prompt.Text = body.Text ?? "";
            await db.SaveChangesAsync();
            return Results.Ok(prompt);
        });
        sim.MapDelete("/technology-prompts/{id}", async (string id, AppDbContext db) =>
        {
            var prompt = await db.TechnologyPrompts.FindAsync(id);
            if (prompt is null) return Results.NotFound();
            db.TechnologyPrompts.Remove(prompt);
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
