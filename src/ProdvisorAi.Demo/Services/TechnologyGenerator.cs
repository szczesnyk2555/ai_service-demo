using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using ProdvisorAi.Demo.Data;
using ProdvisorAi.Demo.Models;
using ProdvisorAi.Demo.Prodvisor;

namespace ProdvisorAi.Demo.Services;

/// <summary>
/// Runs one generation job: machines + materials (PRODVISOR tables), system prompt profile, technology prompt,
/// element data and drawing -> mock provider or OpenAI Responses API -> result stored on the job.
/// </summary>
public sealed class TechnologyGenerator(
    AppDbContext db,
    SettingsService settingsService,
    OpenAiResponsesClient openAi,
    MockTechnologyProvider mock,
    StoragePaths paths,
    IConfiguration config)
{
    static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public async Task GenerateAsync(GenerationJob job, CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        job.Model = settings.Model;
        job.Mock = settings.UseMock;

        var machines = await db.Machines.AsNoTracking().Where(m => m.Active).OrderBy(m => m.Id).ToListAsync(ct);
        var materials = await db.Materials.AsNoTracking().ToListAsync(ct);
        if (machines.Count == 0)
            throw new AiServiceException("no_machines", "Brak aktywnych maszyn.");

        var systemPrompt = await db.SystemPrompts.AsNoTracking().FirstAsync(p => p.Id == SeedData.SystemPromptId, ct);

        var drawing = new DrawingInput(
            await File.ReadAllBytesAsync(paths.Resolve(job.DrawingPath), ct), job.DrawingContentType, job.DrawingFileName);

        var request = TechnologyRequestBuilder.Build(
            settings.Model, systemPrompt.Text, job.TechnologyPrompt ?? "",
            new ElementData(job.ElementId, job.PartNumber, job.PartName, job.Material, job.BatchSize, job.DrawingFileName, job.DrawingText),
            machines, materials, drawing,
            maxOutputTokens: config.GetValue("OpenAI:MaxOutputTokens", 16000),
            reasoningEffort: config["OpenAI:ReasoningEffort"]);

        // Stored for the admin panel: no base64 payload; the key is never part of the body.
        job.DebugRequestJson = TechnologyRequestBuilder.Redact(request).ToJsonString(Indented);

        if (settings.UseMock)
        {
            var (result, raw) = await mock.GenerateAsync(machines, ct);
            job.ResultJson = JsonSerializer.Serialize(SimulateMaterialChoice(result, materials));
            job.DebugResponseJson = new JsonObject
            {
                ["mock"] = true,
                ["note"] = settings.HasApiKey ? "Tryb mock włączony w ustawieniach." : "Brak klucza API - użyto trybu mock.",
                ["output_text"] = JsonNode.Parse(raw),
            }.ToJsonString(Indented);
            return;
        }

        var response = await openAi.CreateResponseAsync(
            request, new OpenAiCredentials(settings.ApiKey!, settings.Organization, settings.Project), ct);
        job.DebugResponseJson = response.ToJsonString(Indented);

        var usage = ResponseParser.ReadUsage(response);
        job.InputTokens = usage.InputTokens;
        job.OutputTokens = usage.OutputTokens;
        job.TotalTokens = usage.TotalTokens;

        var technology = ResponseParser.ParseTechnology(response);
        job.ResultJson = JsonSerializer.Serialize(ValidateMaterialChoice(technology, materials));
    }

    /// <summary>The model may only pick an ID that exists (and is active) in MaterialsKind; anything else is dropped.</summary>
    static TechnologyResult ValidateMaterialChoice(TechnologyResult t, IReadOnlyList<ProdvisorMaterial> materials)
    {
        if (t.Material is not { SystemMaterialId: { } id } m || materials.Any(x => x.Id == id && x.Active)) return t;
        return t with
        {
            Material = m with { SystemMaterialId = null },
            GeneralNotes = $"{t.GeneralNotes}\nAI wskazało materiał ID {id}, którego nie ma na liście aktywnych materiałów - wybierz ręcznie.".Trim(),
        };
    }

    /// <summary>Mock mode: simulate the model's material choice with the rule-based matcher.</summary>
    static TechnologyResult SimulateMaterialChoice(TechnologyResult t, IReadOnlyList<ProdvisorMaterial> materials)
    {
        if (t.Material is not { } m) return t;
        var stock = t.Dimensions?.Stock;
        var match = MaterialMatcher.Match(m.Grade, stock?.Form, stock?.SizeMm, materials);
        return t with { Material = m with { SystemMaterialId = match.Status == "matched" ? match.Candidates[0].Id : null } };
    }
}
