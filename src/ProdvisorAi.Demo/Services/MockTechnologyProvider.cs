using System.Reflection;
using ProdvisorAi.Demo.Data;
using ProdvisorAi.Demo.Models;

namespace ProdvisorAi.Demo.Services;

/// <summary>
/// Returns a realistic canned technology (samples/sample-mock-response.json, embedded in the assembly)
/// after a short delay, so the demo works without network, API key or credit.
/// </summary>
public sealed class MockTechnologyProvider(IConfiguration config)
{
    public async Task<(TechnologyResult Result, string RawJson)> GenerateAsync(IReadOnlyList<Machine> activeMachines, CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(config.GetValue("Demo:MockDelaySeconds", 3.0)), ct);

        var raw = ReadResource("MockResponse.json");
        var result = ResponseParser.Deserialize(raw);

        // The canned answer references the seeded machines. If a machine was deactivated, use another active machine
        // of the same type, or drop the operation (like the real model, which only sees active machines).
        var ops = result.Operations
            .Select(op => op with
            {
                MachineId = MapMachine(op.MachineId, activeMachines)!,
                AlternativeMachineIds = (op.AlternativeMachineIds ?? []).Where(a => activeMachines.Any(m => m.Id == a)).ToList(),
            })
            .Where(op => op.MachineId is not null)
            .Select((op, i) => op with { No = i + 1 })
            .ToList();
        return (result with { Operations = ops }, raw);
    }

    static string? MapMachine(string machineId, IReadOnlyList<Machine> active)
    {
        if (active.Any(m => m.Id == machineId)) return machineId;
        var seededType = SeedData.DefaultMachines.FirstOrDefault(m => m.Id == machineId)?.Type;
        return active.FirstOrDefault(m => string.Equals(m.Type, seededType, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    static string ReadResource(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded resource {name} not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
