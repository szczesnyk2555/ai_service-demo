using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using ProdvisorAi.Demo.Data;
using ProdvisorAi.Demo.Models;
using ProdvisorAi.Demo.Services;

namespace ProdvisorAi.Demo.Endpoints;

public static class JobEndpoints
{
    public sealed record SaveTechnologyRequest(TechnologyResult Technology, int? BatchSize);

    public static void MapJobEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/jobs");

        // Create a job from a multipart upload: drawing (file) + partNumber, material, batchSize.
        // Returns immediately with the job id; the work happens in JobWorker.
        group.MapPost("/", async (HttpRequest request, AppDbContext db, SettingsService settings,
            JobQueue queue, StoragePaths paths, IConfiguration config) =>
        {
            if (!request.HasFormContentType) return Results.BadRequest(new { error = "Expected multipart/form-data." });
            var form = await request.ReadFormAsync();
            var file = form.Files.GetFile("drawing");
            if (file is null || file.Length == 0) return Results.BadRequest(new { error = "Please upload a drawing (PNG, JPG or PDF)." });

            var maxMb = config.GetValue("Upload:MaxSizeMb", 20);
            if (file.Length > maxMb * 1024L * 1024L) return Results.BadRequest(new { error = $"The drawing is larger than {maxMb} MB." });

            await using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            var bytes = ms.ToArray();

            // Trust the file content, not the extension or browser-provided content type.
            var (contentType, ext) = DetectType(bytes);
            if (contentType is null) return Results.BadRequest(new { error = "Unsupported file type. Use PNG, JPG or PDF." });

            var job = new GenerationJob
            {
                PartNumber = Clean(form["partNumber"]),
                Material = Clean(form["material"]),
                BatchSize = int.TryParse(form["batchSize"], out var bs) && bs > 0 ? bs : null,
                DrawingFileName = Path.GetFileName(file.FileName),
                DrawingContentType = contentType,
                PromptLanguage = (await settings.GetAsync()).PromptLanguage,
            };
            job.DrawingPath = Path.Combine("uploads", job.Id + ext);
            await File.WriteAllBytesAsync(paths.Resolve(job.DrawingPath), bytes);

            db.Jobs.Add(job);
            await db.SaveChangesAsync();
            await queue.EnqueueAsync(job.Id);
            return Results.Accepted($"/api/jobs/{job.Id}", new { jobId = job.Id });
        });

        // History list (newest first)
        group.MapGet("/", async (AppDbContext db) =>
        {
            var jobs = await db.Jobs.AsNoTracking().OrderByDescending(j => j.CreatedAt).Take(200).ToListAsync();
            return jobs.Select(j => new
            {
                id = j.Id, createdAt = j.CreatedAt, status = j.Status, partNumber = j.PartNumber, material = j.Material,
                batchSize = j.BatchSize, drawingFileName = j.DrawingFileName, model = j.Model, mock = j.Mock,
                durationMs = j.DurationMs, totalTokens = j.TotalTokens, approvedAt = j.ApprovedAt, edited = j.Edited,
                operationCount = ReadResult(j)?.Operations.Count,
            });
        });

        group.MapGet("/{id}", async (string id, AppDbContext db) =>
            await db.Jobs.FindAsync(id) is { } job ? Results.Ok(await ToViewAsync(job, db)) : Results.NotFound());

        // The uploaded drawing (for the preview in job view / history)
        group.MapGet("/{id}/drawing", async (string id, AppDbContext db, StoragePaths paths) =>
        {
            var job = await db.Jobs.FindAsync(id);
            if (job is null || !File.Exists(paths.Resolve(job.DrawingPath))) return Results.NotFound();
            return Results.File(paths.Resolve(job.DrawingPath), job.DrawingContentType);
        });

        // Save the technologist's edits to the draft
        group.MapPut("/{id}/operations", async (string id, SaveTechnologyRequest body, AppDbContext db) =>
        {
            var job = await db.Jobs.FindAsync(id);
            if (job is null) return Results.NotFound();
            if (job.Status != JobStatus.Done) return Results.Conflict(new { error = "The job has no result yet." });
            if (job.ApprovedAt is not null) return Results.Conflict(new { error = "The technology is already approved." });
            if (body.Technology?.Operations is null) return Results.BadRequest(new { error = "Missing operations." });

            job.ResultJson = JsonSerializer.Serialize(body.Technology with { GeneralNotes = body.Technology.GeneralNotes ?? "" });
            job.BatchSize = body.BatchSize is > 0 ? body.BatchSize : job.BatchSize;
            job.Edited = true;
            await db.SaveChangesAsync();
            return Results.Ok(await ToViewAsync(job, db));
        });

        // Approve = a human accepted the draft. In the product this would create the routing in PRODVISOR.
        group.MapPost("/{id}/approve", async (string id, AppDbContext db, ILogger<GenerationJob> log) =>
        {
            var job = await db.Jobs.FindAsync(id);
            if (job is null) return Results.NotFound();
            if (job.Status != JobStatus.Done) return Results.Conflict(new { error = "Only finished jobs can be approved." });

            job.ApprovedAt ??= DateTime.UtcNow;
            await db.SaveChangesAsync();
            log.LogInformation("Job {JobId} approved (edited={Edited}, operations={Count})", job.Id, job.Edited, ReadResult(job)?.Operations.Count);
            return Results.Ok(await ToViewAsync(job, db));
        });

        // Regenerate = new job with the same drawing and part data (uses the current prompts/settings)
        group.MapPost("/{id}/regenerate", async (string id, AppDbContext db, SettingsService settings, JobQueue queue) =>
        {
            var source = await db.Jobs.FindAsync(id);
            if (source is null) return Results.NotFound();

            var job = new GenerationJob
            {
                PartNumber = source.PartNumber,
                Material = source.Material,
                BatchSize = source.BatchSize,
                DrawingFileName = source.DrawingFileName,
                DrawingContentType = source.DrawingContentType,
                DrawingPath = source.DrawingPath,
                PromptLanguage = (await settings.GetAsync()).PromptLanguage,
                RegeneratedFromId = source.Id,
            };
            db.Jobs.Add(job);
            await db.SaveChangesAsync();
            await queue.EnqueueAsync(job.Id);
            return Results.Accepted($"/api/jobs/{job.Id}", new { jobId = job.Id });
        });

        // Export the (edited) technology as a JSON file
        group.MapGet("/{id}/export", async (string id, AppDbContext db, IConfiguration config) =>
        {
            var job = await db.Jobs.FindAsync(id);
            var result = job is null ? null : ReadResult(job);
            if (job is null || result is null) return Results.NotFound();

            var machines = await db.Machines.AsNoTracking().ToDictionaryAsync(m => m.Id);
            var batch = job.BatchSize ?? 1;
            var export = new
            {
                format = "prodvisor-technology-draft/v1",
                exported_at = DateTime.UtcNow,
                status = job.ApprovedAt is null ? "draft" : "approved",
                approved_at = job.ApprovedAt,
                part = new { part_number = job.PartNumber, material = job.Material, batch_size = batch, drawing = job.DrawingFileName },
                generated_by = new { model = job.Model, mock = job.Mock, job_id = job.Id, edited_by_user = job.Edited },
                summary = Summarize(result, machines, batch, config["Demo:Currency"] ?? "PLN"),
                technology = result,
            };

            var name = $"technology_{(job.PartNumber ?? job.Id[..8]).Replace(' ', '_')}.json";
            var bytes = JsonSerializer.SerializeToUtf8Bytes(export, new JsonSerializerOptions { WriteIndented = true });
            return Results.File(bytes, "application/json", name);
        });

        // The bundled sample drawing, so a demo can start with one click
        app.MapGet("/api/samples/drawing", () =>
        {
            var path = Path.Combine(AppContext.BaseDirectory, "samples", "sample-drawing-shaft.png");
            return File.Exists(path) ? Results.File(path, "image/png", "sample-drawing-shaft.png") : Results.NotFound();
        });
    }

    static async Task<object> ToViewAsync(GenerationJob job, AppDbContext db)
    {
        var result = ReadResult(job);
        List<ValidationWarning>? warnings = null;
        if (result is not null)
        {
            // Validated on every read against the CURRENT machine list (a machine may have been deactivated since).
            var machines = await db.Machines.AsNoTracking().ToListAsync();
            warnings = TechnologyValidator.Validate(result, machines);
        }

        return new
        {
            id = job.Id,
            status = job.Status,
            createdAt = job.CreatedAt,
            startedAt = job.StartedAt,
            completedAt = job.CompletedAt,
            partNumber = job.PartNumber,
            material = job.Material,
            batchSize = job.BatchSize,
            drawingFileName = job.DrawingFileName,
            drawingContentType = job.DrawingContentType,
            drawingUrl = $"/api/jobs/{job.Id}/drawing",
            promptLanguage = job.PromptLanguage,
            model = job.Model,
            mock = job.Mock,
            durationMs = job.DurationMs,
            usage = new { inputTokens = job.InputTokens, outputTokens = job.OutputTokens, totalTokens = job.TotalTokens },
            error = job.Error,
            errorKind = job.ErrorKind,
            edited = job.Edited,
            approvedAt = job.ApprovedAt,
            regeneratedFromId = job.RegeneratedFromId,
            result,
            warnings,
            debug = new { request = ParseOrNull(job.DebugRequestJson), response = ParseOrNull(job.DebugResponseJson) },
        };
    }

    static object Summarize(TechnologyResult t, Dictionary<string, Machine> machines, int batch, string currency)
    {
        var setup = t.Operations.Sum(o => o.SetupTimeMin);
        var unit = t.Operations.Sum(o => o.UnitTimeMin);
        var cost = t.Operations.Sum(o =>
            (o.SetupTimeMin + o.UnitTimeMin * batch) / 60.0 * (machines.TryGetValue(o.MachineId, out var m) ? m.HourlyRate : 0));
        return new
        {
            total_setup_time_min = setup,
            total_unit_time_min = unit,
            batch_time_min = setup + unit * batch,
            estimated_cost = Math.Round(cost, 2),
            currency,
        };
    }

    static TechnologyResult? ReadResult(GenerationJob job) =>
        job.ResultJson is null ? null : JsonSerializer.Deserialize<TechnologyResult>(job.ResultJson);

    static JsonNode? ParseOrNull(string? json) => json is null ? null : JsonNode.Parse(json);

    static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    static (string? ContentType, string Ext) DetectType(byte[] b)
    {
        if (b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return ("image/png", ".png");
        if (b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return ("image/jpeg", ".jpg");
        if (b.Length > 4 && b[0] == '%' && b[1] == 'P' && b[2] == 'D' && b[3] == 'F') return ("application/pdf", ".pdf");
        return (null, "");
    }
}
