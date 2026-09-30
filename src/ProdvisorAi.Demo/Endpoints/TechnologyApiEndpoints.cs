using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProdvisorAi.Demo.Data;
using ProdvisorAi.Demo.Models;
using ProdvisorAi.Demo.Services;

namespace ProdvisorAi.Demo.Endpoints;

/// <summary>
/// The API used by the PRODVISOR desktop application:
///   POST /api/v1/technology-jobs         multipart: drawing + element_id [+ mode technolog|konstruktor] + technology_prompt [+ technology_prompt_name] -> 202 { job_id }
///   GET  /api/v1/technology-jobs/{id}    status; when done: result mapped to PRODVISOR ids (MASZYNY.ID, STANOWISKO.ID, MaterialsKind.ID)
/// </summary>
public static class TechnologyApiEndpoints
{
    public static void MapTechnologyApiEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1");

        api.MapPost("/technology-jobs", async (HttpRequest request, AppDbContext db, JobQueue queue, StoragePaths paths,
            IConfiguration config, DxfDrawingConverter dxf) =>
        {
            if (!request.HasFormContentType) return Error("Oczekiwano multipart/form-data.");
            var form = await request.ReadFormAsync();

            var elementId = form["element_id"].ToString().Trim();
            if (elementId == "") return Error("Brak element_id.");
            // Element data (number, name, material, quantity) is read from PRODVISOR by element_id.
            var element = await db.Elements.AsNoTracking().FirstOrDefaultAsync(e => e.Id == elementId);
            if (element is null) return Results.NotFound(new { error = $"Nie znaleziono elementu {elementId}." });


            var mode = form["mode"].ToString().Trim().ToLowerInvariant() is { Length: > 0 } md ? md : "technolog";
            if (mode is not ("technolog" or "konstruktor")) return Error("mode: technolog lub konstruktor.");

            var file = form.Files.GetFile("drawing");
            if (file is null || file.Length == 0) return Error("Brak rysunku (pole drawing: PNG, JPG, PDF lub DXF).");
            var maxMb = config.GetValue("Upload:MaxSizeMb", 20);
            if (file.Length > maxMb * 1024L * 1024L) return Error($"Rysunek jest większy niż {maxMb} MB.");

            await using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            var (drawing, error) = DrawingFiles.Prepare(ms.ToArray(), Path.GetFileName(file.FileName), dxf);
            if (drawing is null) return Error(error!);

            var job = new GenerationJob
            {
                ElementId = element.Id,
                PartNumber = element.Number,
                PartName = element.Name,
                Material = element.Material,
                BatchSize = element.Quantity,
                Mode = mode,
                TechnologyPrompt = form["technology_prompt"].ToString(),
                TechnologyPromptName = form["technology_prompt_name"].ToString().Trim() is { Length: > 0 } n ? n : null,
                DrawingFileName = drawing.FileName,
                DrawingContentType = drawing.ContentType,
                DrawingText = drawing.Dxf?.ExtractedText,
            };
            job.DrawingPath = Path.Combine("uploads", job.Id + drawing.Extension);
            await File.WriteAllBytesAsync(paths.Resolve(job.DrawingPath), drawing.Bytes);
            if (drawing.SourceDxf is not null)
            {
                job.SourceDrawingPath = Path.Combine("uploads", job.Id + ".dxf");
                await File.WriteAllBytesAsync(paths.Resolve(job.SourceDrawingPath), drawing.SourceDxf);
            }

            db.Jobs.Add(job);
            await db.SaveChangesAsync();
            await queue.EnqueueAsync(job.Id);
            return Results.Accepted($"/api/v1/technology-jobs/{job.Id}", new { job_id = job.Id, status = job.Status });
        }).DisableAntiforgery();

        api.MapGet("/technology-jobs/{id}", async (string id, AppDbContext db) =>
            await db.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == id) is { } job
                ? Results.Ok(await ToApiViewAsync(job, db))
                : Results.NotFound(new { error = "Nie znaleziono zadania." }));
    }

    static IResult Error(string message) => Results.BadRequest(new { error = message });

    /// <summary>Job status + the result mapped to PRODVISOR ids, ready to fill the technology card.</summary>
    public static async Task<object> ToApiViewAsync(GenerationJob job, AppDbContext db)
    {
        object? result = null;
        List<ValidationWarning>? warnings = null;
        if (job.ResultJson is not null)
        {
            var tech = JsonSerializer.Deserialize<TechnologyResult>(job.ResultJson)!;
            var machineList = await db.Machines.AsNoTracking().ToListAsync();
            var materialList = await db.Materials.AsNoTracking().ToListAsync();
            var machines = machineList.ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
            warnings = TechnologyValidator.Validate(tech, machineList, materialList, job.Mode == "konstruktor");

            object MachineRef(string code) => machines.TryGetValue(code, out var m)
                ? new { machine_code = m.Id, maszyny_id = m.ProdvisorId, stanowisko_id = m.WorkplaceId, name = m.Name, group = m.GroupName, cooperation = m.IsVirtual }
                : new { machine_code = code, maszyny_id = (int?)null, stanowisko_id = (int?)null, name = "?", group = (string?)null, cooperation = false };

            var kind = tech.Material?.SystemMaterialId is { } kid ? materialList.FirstOrDefault(m => m.Id == kid) : null;
            result = new
            {
                operations = tech.Operations.Select(o => new
                {
                    no = o.No,
                    name = o.Name,
                    machine = MachineRef(o.MachineId),
                    alternatives = (o.AlternativeMachineIds ?? []).Select(MachineRef),
                    tpz_min = o.SetupTimeMin,
                    tj_min = o.UnitTimeMin,
                    description = o.Description,
                    comment = o.Comment,
                }),
                material = new
                {
                    grade = tech.Material?.Grade,
                    stock = tech.Material?.Stock,
                    material_kind_id = kind?.Id,
                    material_kind_name = kind?.Name,
                },
                dimensions = tech.Dimensions,
                quantity = tech.Quantity,
                notes = tech.GeneralNotes,
            };
        }

        return new
        {
            job_id = job.Id,
            status = job.Status,
            mode = job.Mode,
            element_id = job.ElementId,
            technology_prompt_name = job.TechnologyPromptName,
            created_at = job.CreatedAt,
            completed_at = job.CompletedAt,
            duration_ms = job.DurationMs,
            model = job.Model,
            mock = job.Mock,
            usage = job.TotalTokens is null ? null : new { input_tokens = job.InputTokens, output_tokens = job.OutputTokens, total_tokens = job.TotalTokens },
            error = job.Error is null ? null : new { kind = job.ErrorKind, message = job.Error },
            result,
            warnings = warnings?.Select(w => new { code = w.Code, operation_no = w.OperationNo, message = w.Message }),
        };
    }
}
