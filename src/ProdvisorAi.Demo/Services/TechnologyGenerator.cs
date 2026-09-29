using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using ProdvisorAi.Demo.Data;

namespace ProdvisorAi.Demo.Services;

/// <summary>
/// Runs one generation job: loads settings, machines, prompts and the drawing, then calls either
/// the mock provider or the OpenAI Responses API, and stores the result on the job.
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
        if (machines.Count == 0)
            throw new AiServiceException("no_machines", "There are no active machines. Add or activate machines first.");

        var lang = job.PromptLanguage;
        var systemPrompt = (await db.Prompts.FindAsync([$"system.{lang}"], ct))?.Text ?? "";
        var technologyPrompt = (await db.Prompts.FindAsync([$"technology.{lang}"], ct))?.Text ?? "";

        var drawing = new DrawingInput(
            await File.ReadAllBytesAsync(paths.Resolve(job.DrawingPath), ct), job.DrawingContentType, job.DrawingFileName);

        var request = TechnologyRequestBuilder.Build(
            settings.Model, systemPrompt, technologyPrompt,
            new PartMetadata(job.PartNumber, job.Material, job.BatchSize, job.DrawingFileName, job.DrawingText),
            machines, drawing, lang,
            maxOutputTokens: config.GetValue("OpenAI:MaxOutputTokens", 16000),
            reasoningEffort: config["OpenAI:ReasoningEffort"]);

        // Stored for the Debug panel: no base64 payload, and the key is never part of the body anyway.
        job.DebugRequestJson = TechnologyRequestBuilder.Redact(request).ToJsonString(Indented);

        if (settings.UseMock)
        {
            var (result, raw) = await mock.GenerateAsync(machines, lang, ct);
            job.ResultJson = JsonSerializer.Serialize(result);
            job.DebugResponseJson = new JsonObject
            {
                ["mock"] = true,
                ["note"] = settings.HasApiKey ? "Mock mode is enabled in Settings." : "No API key configured - mock mode used automatically.",
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
        job.ResultJson = JsonSerializer.Serialize(technology);
    }
}
