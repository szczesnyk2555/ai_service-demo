using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ProdvisorAi.Demo.Data;
using ProdvisorAi.Demo.Prodvisor;

namespace ProdvisorAi.Demo.Endpoints;

public static partial class MachineEndpoints
{
    public sealed record MachineDto(string Id, string Name, string Type, string? Description, double HourlyRate, bool Active);

    // Machine IDs end up in the JSON Schema enum, so keep them simple.
    [GeneratedRegex("^[A-Za-z0-9_-]{1,20}$")]
    private static partial Regex IdPattern();

    public static void MapMachineEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/machines");

        // Own machines first (by code), then cooperation. Parameters are returned parsed.
        group.MapGet("/", async (AppDbContext db) =>
            (await db.Machines.AsNoTracking().ToListAsync())
                .OrderBy(m => m.IsVirtual).ThenBy(m => m.IsVirtual ? m.Name : m.Id)
                .Select(m => new
                {
                    m.Id, m.Name, m.Type, m.Description, m.HourlyRate, m.Active,
                    m.ProdvisorId, m.IsVirtual, m.Kind,
                    Parameters = m.ParametersJson is null ? [] : System.Text.Json.JsonSerializer.Deserialize<List<MachineParameter>>(m.ParametersJson)!,
                }));

        // Re-read machines + materials from PRODVISOR (database if configured, otherwise the embedded snapshot).
        group.MapPost("/sync", async (ProdvisorSource source, ProdvisorImporter importer, CancellationToken ct) =>
        {
            try { return Results.Ok(await importer.ImportAsync(await source.LoadAsync(ct), ct)); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Results.BadRequest(new { error = $"Cannot read PRODVISOR data ({source.Provider}): {ex.Message}" });
            }
        });

        // PRODVISOR material list (dbo.MaterialsKind) + matching test endpoint
        app.MapGet("/api/materials", async (AppDbContext db, string? q) =>
        {
            var all = await db.Materials.AsNoTracking().OrderBy(m => m.Name).ToListAsync();
            if (string.IsNullOrWhiteSpace(q)) return Results.Ok(all);
            var needle = MaterialMatcher.Normalize(q);
            return Results.Ok(all.Where(m => MaterialMatcher.Normalize(m.Name).Contains(needle)));
        });
        app.MapGet("/api/materials/match", async (AppDbContext db, string grade, string? form, double? size) =>
            MaterialMatcher.Match(grade, form, size, await db.Materials.AsNoTracking().ToListAsync()));
        app.MapGet("/api/prodvisor/info", (ProdvisorSource source) => new { provider = source.Provider, database = source.IsDatabase });

        group.MapPost("/", async (MachineDto dto, AppDbContext db) =>
        {
            if (Validate(dto) is { } error) return Results.BadRequest(new { error });
            if (await db.Machines.AnyAsync(m => m.Id == dto.Id))
                return Results.Conflict(new { error = $"Machine '{dto.Id}' already exists." });

            var machine = new Machine();
            Apply(machine, dto);
            machine.Id = dto.Id.Trim();
            db.Machines.Add(machine);
            await db.SaveChangesAsync();
            return Results.Created($"/api/machines/{machine.Id}", machine);
        });

        group.MapPut("/{id}", async (string id, MachineDto dto, AppDbContext db) =>
        {
            var machine = await db.Machines.FindAsync(id);
            if (machine is null) return Results.NotFound();
            if (Validate(dto with { Id = id }) is { } error) return Results.BadRequest(new { error });

            Apply(machine, dto);
            await db.SaveChangesAsync();
            return Results.Ok(machine);
        });

        group.MapDelete("/{id}", async (string id, AppDbContext db) =>
        {
            var machine = await db.Machines.FindAsync(id);
            if (machine is null) return Results.NotFound();
            db.Machines.Remove(machine);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }

    static string? Validate(MachineDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Id) || !IdPattern().IsMatch(dto.Id.Trim()))
            return "ID is required (letters, digits, - or _, max 20 characters).";
        if (string.IsNullOrWhiteSpace(dto.Name)) return "Name is required.";
        if (string.IsNullOrWhiteSpace(dto.Type)) return "Type is required.";
        if (dto.HourlyRate < 0) return "Hourly rate must not be negative.";
        return null;
    }

    static void Apply(Machine m, MachineDto dto)
    {
        m.Name = dto.Name.Trim();
        m.Type = dto.Type.Trim();
        m.Description = dto.Description?.Trim() ?? "";
        m.HourlyRate = dto.HourlyRate;
        m.Active = dto.Active;
    }
}
