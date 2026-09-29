using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using ProdvisorAi.Demo.Data;
using ProdvisorAi.Demo.Models;
using ProdvisorAi.Demo.Prodvisor;
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
            JobQueue queue, StoragePaths paths, IConfiguration config, DxfDrawingConverter dxf) =>
        {
            var (upload, error) = await ReadDrawingAsync(request, config, dxf);
            if (upload is null) return Results.BadRequest(new { error });

            var job = new GenerationJob
            {
                PartNumber = Clean(upload.Form["partNumber"]),
                Material = Clean(upload.Form["material"]),
                BatchSize = int.TryParse(upload.Form["batchSize"], out var bs) && bs > 0 ? bs : null,
                DrawingFileName = upload.FileName,
                DrawingContentType = upload.ContentType,
                DrawingText = upload.Dxf?.ExtractedText,
                PromptLanguage = (await settings.GetAsync()).PromptLanguage,
            };
            job.DrawingPath = Path.Combine("uploads", job.Id + upload.Extension);
            await File.WriteAllBytesAsync(paths.Resolve(job.DrawingPath), upload.Bytes);
            if (upload.Dxf is not null)
            {
                // Keep the original CAD file next to the rendered PNG.
                job.SourceDrawingPath = Path.Combine("uploads", job.Id + ".dxf");
                await File.WriteAllBytesAsync(paths.Resolve(job.SourceDrawingPath), upload.SourceBytes!);
            }

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
                SourceDrawingPath = source.SourceDrawingPath,
                DrawingText = source.DrawingText,
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
                // Ready for import into PRODVISOR: operations with MASZYNY.ID, material with MaterialsKind.ID
                prodvisor = new
                {
                    material_kind_id = result.Material?.SystemMaterialId,
                    operations = result.Operations.Select(o => new
                    {
                        o.No, o.Name, machine_code = o.MachineId,
                        maszyny_id = machines.TryGetValue(o.MachineId, out var m) ? m.ProdvisorId : null,
                        cooperation = machines.TryGetValue(o.MachineId, out var m2) && m2.IsVirtual,
                        tpz_min = o.SetupTimeMin, tj_min = o.UnitTimeMin,
                    }),
                },
            };

            var name = $"technology_{(job.PartNumber ?? job.Id[..8]).Replace(' ', '_')}.json";
            var bytes = JsonSerializer.SerializeToUtf8Bytes(export, new JsonSerializerOptions { WriteIndented = true });
            return Results.File(bytes, "application/json", name);
        });

        // The original CAD file (DXF) of a converted drawing
        group.MapGet("/{id}/source", async (string id, AppDbContext db, StoragePaths paths) =>
        {
            var job = await db.Jobs.FindAsync(id);
            if (job?.SourceDrawingPath is null || !File.Exists(paths.Resolve(job.SourceDrawingPath))) return Results.NotFound();
            return Results.File(paths.Resolve(job.SourceDrawingPath), "application/dxf", job.DrawingFileName);
        });

        // Preview before generating: browsers cannot display DXF, so the page asks the server to render it.
        // Returns the PNG as a data URL plus what was extracted. Nothing is stored.
        app.MapPost("/api/drawings/preview", async (HttpRequest request, IConfiguration config, DxfDrawingConverter dxf) =>
        {
            var (upload, error) = await ReadDrawingAsync(request, config, dxf);
            if (upload is null) return Results.BadRequest(new { error });
            if (upload.Dxf is null) return Results.BadRequest(new { error = "Preview is only needed for DXF files." });
            return Results.Ok(new
            {
                image = $"data:image/png;base64,{Convert.ToBase64String(upload.Bytes)}",
                dimensionCount = upload.Dxf.DimensionCount,
                textCount = upload.Dxf.TextCount,
                extractedText = upload.Dxf.ExtractedText,
                warnings = upload.Dxf.Warnings,
            });
        });

        // The bundled sample drawings, so a demo can start with one click
        app.MapGet("/api/samples/{kind}", (string kind) =>
        {
            var (file, type) = kind switch
            {
                "drawing" => ("sample-drawing-shaft.png", "image/png"),
                "dxf" => ("sample-drawing-shaft.dxf", "application/dxf"),
                _ => ("", ""),
            };
            var path = Path.Combine(AppContext.BaseDirectory, "samples", file);
            return file != "" && File.Exists(path) ? Results.File(path, type, file) : Results.NotFound();
        });
    }

    static async Task<object> ToViewAsync(GenerationJob job, AppDbContext db)
    {
        var result = ReadResult(job);
        List<ValidationWarning>? warnings = null;
        object? materialMatch = null;
        if (result is not null)
        {
            // Validated on every read against the CURRENT machine list (a machine may have been deactivated since).
            var machines = await db.Machines.AsNoTracking().ToListAsync();
            warnings = TechnologyValidator.Validate(result, machines, job.Material);

            // Material from the drawing vs. the PRODVISOR material list (MaterialsKind)
            if (result.Material is { } mat)
            {
                var materials = await db.Materials.AsNoTracking().ToListAsync();
                var match = MaterialMatcher.Match(mat.Grade, mat.StockForm, mat.StockSizeMm, materials);
                // The model chooses (system_material_id); the rule-based matcher is the cross-check and the source of candidates.
                var selected = mat.SystemMaterialId is { } sid ? materials.FirstOrDefault(m => m.Id == sid) : null;
                var rulesBest = match.Status == "matched" ? match.Candidates[0] : null;
                materialMatch = new
                {
                    match.Status, match.Grade, match.GradeGroup, match.Candidates, selected,
                    by = mat.SystemMaterialBy, reason = mat.SystemMaterialReason,
                };

                if (selected is null && match.Status == "not_found")
                    warnings.Add(new("material_not_in_system", "warning", null, "material", TechnologyValidator.Args(("grade", mat.Grade ?? "")),
                        $"Material {mat.Grade} is not in the PRODVISOR material list."));
                else if (selected is null)
                    warnings.Add(new("material_not_selected", "warning", null, "material", TechnologyValidator.Args(("grade", mat.Grade ?? "")),
                        $"No PRODVISOR material selected for {mat.Grade} - candidates exist, please choose."));
                else if (rulesBest is not null && rulesBest.Id != selected.Id && mat.SystemMaterialBy == "ai")
                    warnings.Add(new("material_ai_differs", "info", null, "material",
                        TechnologyValidator.Args(("ai", selected.Name), ("rules", rulesBest.Name)),
                        $"AI chose {selected.Name}; rule-based matching suggests {rulesBest.Name}."));
            }
        }

        return new
        {
            materialMatch,
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
            sourceUrl = job.SourceDrawingPath is null ? null : $"/api/jobs/{job.Id}/source",
            drawingText = job.DrawingText,
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
        if (IsDxf(b)) return ("application/dxf", ".dxf");
        return (null, "");
    }

    /// <summary>ASCII DXF starts with group code 0 + SECTION; binary DXF has a fixed sentinel.</summary>
    static bool IsDxf(byte[] b)
    {
        var head = System.Text.Encoding.ASCII.GetString(b, 0, Math.Min(b.Length, 256));
        return head.StartsWith("AutoCAD Binary DXF", StringComparison.Ordinal)
               || System.Text.RegularExpressions.Regex.IsMatch(head, @"^\s*0\s*\r?\n\s*SECTION", System.Text.RegularExpressions.RegexOptions.None)
               || System.Text.RegularExpressions.Regex.IsMatch(head, @"^\s*999\s*\r?\n");   // comment line before the header
    }

    sealed record DrawingUpload(
        IFormCollection Form, string FileName, string ContentType, string Extension,
        byte[] Bytes, byte[]? SourceBytes, DxfConversion? Dxf);

    /// <summary>
    /// Reads and validates the uploaded drawing. PNG / JPG / PDF are used as-is;
    /// a DXF is rendered to PNG and its exact dimensions / texts are extracted.
    /// </summary>
    static async Task<(DrawingUpload? Upload, string? Error)> ReadDrawingAsync(HttpRequest request, IConfiguration config, DxfDrawingConverter dxf)
    {
        if (!request.HasFormContentType) return (null, "Expected multipart/form-data.");
        var form = await request.ReadFormAsync();
        var file = form.Files.GetFile("drawing");
        if (file is null || file.Length == 0) return (null, "Please upload a drawing (PNG, JPG, PDF or DXF).");

        var maxMb = config.GetValue("Upload:MaxSizeMb", 20);
        if (file.Length > maxMb * 1024L * 1024L) return (null, $"The drawing is larger than {maxMb} MB.");

        await using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var bytes = ms.ToArray();
        var fileName = Path.GetFileName(file.FileName);

        // Trust the file content, not the extension or browser-provided content type.
        var (contentType, ext) = DetectType(bytes);
        if (contentType is null) return (null, "Unsupported file type. Use PNG, JPG, PDF or DXF.");

        if (contentType != "application/dxf")
            return (new DrawingUpload(form, fileName, contentType, ext, bytes, null, null), null);

        try
        {
            var conversion = dxf.Convert(bytes);
            return (new DrawingUpload(form, fileName, "image/png", ".png", conversion.Png, bytes, conversion), null);
        }
        catch (InvalidDataException ex)
        {
            return (null, $"The DXF file could not be processed: {ex.Message}.");
        }
    }
}
