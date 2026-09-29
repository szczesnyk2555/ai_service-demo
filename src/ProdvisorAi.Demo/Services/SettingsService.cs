using Microsoft.EntityFrameworkCore;
using ProdvisorAi.Demo.Data;

namespace ProdvisorAi.Demo.Services;

/// <summary>Effective AI settings for this installation. Contains the decrypted key - server-side use only.</summary>
public sealed record AiSettings(
    string? ApiKey,
    string Model,
    string? Organization,
    string? Project,
    bool MockMode,
    string PromptLanguage)
{
    public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>Mock is used when explicitly enabled OR when no key is configured, so the demo always works.</summary>
    public bool UseMock => MockMode || !HasApiKey;
}

/// <summary>Reads and writes settings from the key/value table, encrypting the API key.</summary>
public sealed class SettingsService(AppDbContext db, SecretProtector protector, IConfiguration config, ILogger<SettingsService> log)
{
    const string KeyApiKey = "openai.api_key";
    const string KeyModel = "openai.model";
    const string KeyOrganization = "openai.organization";
    const string KeyProject = "openai.project";
    const string KeyMockMode = "demo.mock_mode";
    const string KeyPromptLanguage = "prompts.language";

    public async Task<AiSettings> GetAsync(CancellationToken ct = default)
    {
        var all = await db.Settings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, ct);
        string? Get(string key) => all.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

        string? apiKey = null;
        if (Get(KeyApiKey) is { } encrypted)
        {
            try { apiKey = protector.Unprotect(encrypted); }
            catch (Exception ex)
            {
                // e.g. database copied from another machine, or DPAPI scope changed. Treat as "no key".
                log.LogWarning("Stored API key could not be decrypted ({Error}). Please enter the key again.", ex.GetType().Name);
            }
        }

        return new AiSettings(
            ApiKey: apiKey,
            Model: Get(KeyModel) ?? config["OpenAI:DefaultModel"] ?? "gpt-6-astra",
            Organization: Get(KeyOrganization),
            Project: Get(KeyProject),
            MockMode: Get(KeyMockMode) is { } m ? bool.Parse(m) : config.GetValue("Demo:MockModeDefault", true),
            PromptLanguage: Get(KeyPromptLanguage) ?? "pl");   // Polish by default: generated technologies are in Polish
    }

    /// <summary>Only non-null arguments are changed. Pass clearApiKey=true to remove the stored key.</summary>
    public async Task UpdateAsync(
        string? apiKey = null, bool clearApiKey = false, string? model = null, string? organization = null,
        string? project = null, bool? mockMode = null, string? promptLanguage = null, CancellationToken ct = default)
    {
        if (clearApiKey) await SetAsync(KeyApiKey, null, ct);
        else if (!string.IsNullOrWhiteSpace(apiKey)) await SetAsync(KeyApiKey, protector.Protect(apiKey.Trim()), ct);

        if (model is not null) await SetAsync(KeyModel, model.Trim(), ct);
        if (organization is not null) await SetAsync(KeyOrganization, organization.Trim(), ct);
        if (project is not null) await SetAsync(KeyProject, project.Trim(), ct);
        if (mockMode is not null) await SetAsync(KeyMockMode, mockMode.Value.ToString(), ct);
        if (promptLanguage is not null) await SetAsync(KeyPromptLanguage, promptLanguage, ct);

        await db.SaveChangesAsync(ct);
    }

    async Task SetAsync(string key, string? value, CancellationToken ct)
    {
        var row = await db.Settings.FindAsync([key], ct);
        if (row is null) db.Settings.Add(new AppSetting { Key = key, Value = value });
        else row.Value = value;
    }
}
