using Microsoft.EntityFrameworkCore;
using ProdvisorAi.Demo.Data;
using ProdvisorAi.Demo.Services;

namespace ProdvisorAi.Demo.Endpoints;

public static class PromptEndpoints
{
    public sealed record PromptPair(string System, string Technology);

    /// <summary>Language = which prompt set is used for generation; Templates = texts per language.</summary>
    public sealed record PromptsDto(string Language, Dictionary<string, PromptPair> Templates);

    static readonly string[] Languages = ["en", "pl"];

    public static void MapPromptEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/prompts");

        group.MapGet("/", async (AppDbContext db, SettingsService settings) => await LoadAsync(db, settings));

        group.MapPut("/", async (PromptsDto dto, AppDbContext db, SettingsService settings) =>
        {
            if (!Languages.Contains(dto.Language)) return Results.BadRequest(new { error = "Unsupported language." });

            foreach (var (lang, pair) in dto.Templates.Where(t => Languages.Contains(t.Key)))
            {
                await UpsertAsync(db, $"system.{lang}", pair.System);
                await UpsertAsync(db, $"technology.{lang}", pair.Technology);
            }
            await db.SaveChangesAsync();
            await settings.UpdateAsync(promptLanguage: dto.Language);
            return Results.Ok(await LoadAsync(db, settings));
        });

        // Restore the seeded default texts for one language.
        group.MapPost("/reset/{lang}", async (string lang, AppDbContext db, SettingsService settings) =>
        {
            if (!Languages.Contains(lang)) return Results.BadRequest(new { error = "Unsupported language." });
            foreach (var kind in new[] { "system", "technology" })
                await UpsertAsync(db, $"{kind}.{lang}", SeedData.DefaultPrompts[$"{kind}.{lang}"]);
            await db.SaveChangesAsync();
            return Results.Ok(await LoadAsync(db, settings));
        });
    }

    static async Task<PromptsDto> LoadAsync(AppDbContext db, SettingsService settings)
    {
        var all = await db.Prompts.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Text);
        var templates = Languages.ToDictionary(
            lang => lang,
            lang => new PromptPair(all.GetValueOrDefault($"system.{lang}", ""), all.GetValueOrDefault($"technology.{lang}", "")));
        return new PromptsDto((await settings.GetAsync()).PromptLanguage, templates);
    }

    static async Task UpsertAsync(AppDbContext db, string id, string text)
    {
        var row = await db.Prompts.FindAsync(id);
        if (row is null) db.Prompts.Add(new PromptTemplate { Id = id, Text = text });
        else { row.Text = text; row.UpdatedAt = DateTime.UtcNow; }
    }
}
