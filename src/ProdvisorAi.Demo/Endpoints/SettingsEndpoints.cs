using ProdvisorAi.Demo.Services;

namespace ProdvisorAi.Demo.Endpoints;

public static class SettingsEndpoints
{
    /// <summary>PUT body. ApiKey is write-only: null/empty = keep the current key.</summary>
    public sealed record SettingsUpdate(
        string? ApiKey, bool ClearApiKey, string? Model, string? Organization, string? Project, bool? MockMode);

    public static void MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/settings");

        group.MapGet("/", async (SettingsService svc, IConfiguration config) => ToView(await svc.GetAsync(), config));

        group.MapPut("/", async (SettingsUpdate body, SettingsService svc, IConfiguration config) =>
        {
            if (body.Model is not null && string.IsNullOrWhiteSpace(body.Model))
                return Results.BadRequest(new { error = "Model name must not be empty." });

            await svc.UpdateAsync(body.ApiKey, body.ClearApiKey, body.Model, body.Organization, body.Project, body.MockMode);
            return Results.Ok(ToView(await svc.GetAsync(), config));
        });

        group.MapPost("/test", async (SettingsService svc, OpenAiResponsesClient client, CancellationToken ct) =>
        {
            var s = await svc.GetAsync(ct);
            if (!s.HasApiKey)
                return Results.Ok(new { ok = false, kind = "no_key", message = "No API key configured." });
            try
            {
                var message = await client.TestAsync(new OpenAiCredentials(s.ApiKey!, s.Organization, s.Project), s.Model, ct);
                return Results.Ok(new { ok = true, kind = "ok", message });
            }
            catch (AiServiceException ex)
            {
                return Results.Ok(new { ok = false, kind = ex.Kind, message = ex.Message });
            }
        });

        // Static app info for the UI (currency, upload limit, version)
        app.MapGet("/api/info", (IConfiguration config) => new
        {
            version = typeof(SettingsEndpoints).Assembly.GetName().Version?.ToString(3),
            currency = config["Demo:Currency"] ?? "PLN",
            maxUploadMb = config.GetValue("Upload:MaxSizeMb", 20),
        });
    }

    // The decrypted key is NEVER returned - only whether one exists and a masked hint.
    static object ToView(AiSettings s, IConfiguration config) => new
    {
        hasApiKey = s.HasApiKey,
        apiKeyMasked = s.HasApiKey ? SecretProtector.Mask(s.ApiKey!) : null,
        model = s.Model,
        organization = s.Organization,
        project = s.Project,
        mockMode = s.MockMode,
        effectiveMock = s.UseMock,
        promptLanguage = s.PromptLanguage,
        suggestedModels = config.GetSection("OpenAI:SuggestedModels").Get<string[]>() ?? [],
    };
}
