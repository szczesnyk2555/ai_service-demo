using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ProdvisorAi.Demo.Data;

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

        group.MapGet("/", async (AppDbContext db) =>
            await db.Machines.AsNoTracking().OrderBy(m => m.Id).ToListAsync());

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
