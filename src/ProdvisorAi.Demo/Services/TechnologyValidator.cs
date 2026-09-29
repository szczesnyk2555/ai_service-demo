using ProdvisorAi.Demo.Data;
using ProdvisorAi.Demo.Models;

namespace ProdvisorAi.Demo.Services;

/// <summary>
/// Server-side post-validation of a (generated or edited) technology.
/// Structured Outputs guarantee the SHAPE of the answer; this checks the CONTENT.
/// Findings are flagged for the technologist - nothing is rejected.
/// </summary>
public static class TechnologyValidator
{
    // Sanity bounds for a typical machining job shop (minutes).
    const double MaxSetupMin = 480;   // one shift
    const double MaxUnitMin = 600;

    static readonly string[] InspectionKeywords = ["inspect", "qc", "quality", "control", "kontrol", "pomiar", "measure"];

    public static List<ValidationWarning> Validate(TechnologyResult tech, IReadOnlyList<Machine> allMachines, string? userMaterial = null)
    {
        var w = new List<ValidationWarning>();

        // Material: must be known; if the user typed one, it should match what was read from the drawing.
        if (tech.Material is { } mat)
        {
            if (string.IsNullOrWhiteSpace(mat.Grade))
                w.Add(new("material_missing", "warning", null, "material", null, "Material grade is not specified."));
            else if (!string.IsNullOrWhiteSpace(userMaterial) && !Normalize(mat.Grade).Contains(Normalize(userMaterial)))
                w.Add(new("material_mismatch", "warning", null, "material", Args(("drawing", mat.Grade), ("user", userMaterial)),
                    $"Material on the drawing ({mat.Grade}) differs from the part data ({userMaterial})."));
        }
        var ops = tech.Operations;

        if (ops.Count == 0)
        {
            w.Add(new("no_operations", "error", null, null, null, "The technology contains no operations."));
            return w;
        }

        // Operation numbers: unique and sequential 1..n
        foreach (var dup in ops.GroupBy(o => o.No).Where(g => g.Count() > 1))
            w.Add(new("duplicate_number", "error", dup.Key, "no", Args(("no", dup.Key)), $"Operation number {dup.Key} is used more than once."));

        for (var i = 0; i < ops.Count; i++)
        {
            if (ops[i].No != i + 1)
            {
                w.Add(new("numbering_not_sequential", "warning", ops[i].No, "no", Args(("expected", i + 1), ("actual", ops[i].No)),
                    $"Operations are not numbered sequentially (position {i + 1} has number {ops[i].No})."));
                break; // one hint is enough; the UI offers "Renumber"
            }
        }

        var machines = allMachines.ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var op in ops)
        {
            // Machine exists and is active
            if (!machines.TryGetValue(op.MachineId ?? "", out var machine))
                w.Add(new("machine_unknown", "error", op.No, "machine_id", Args(("machine", op.MachineId)), $"Op {op.No}: machine '{op.MachineId}' does not exist."));
            else if (!machine.Active)
                w.Add(new("machine_inactive", "error", op.No, "machine_id", Args(("machine", op.MachineId)), $"Op {op.No}: machine '{op.MachineId}' is not active."));

            // Machine group (alternatives): must exist and be active, must not repeat machine_id
            foreach (var alt in op.AlternativeMachineIds ?? [])
            {
                if (string.Equals(alt, op.MachineId, StringComparison.OrdinalIgnoreCase))
                    w.Add(new("alternative_duplicate", "info", op.No, "machine_id", Args(("machine", alt)), $"Op {op.No}: {alt} is both the machine and an alternative."));
                else if (!machines.TryGetValue(alt, out var am) || !am.Active)
                    w.Add(new("alternative_inactive", "warning", op.No, "machine_id", Args(("machine", alt)), $"Op {op.No}: alternative machine {alt} does not exist or is inactive."));
            }

            // Times > 0 and within sane bounds (not for external cooperation: those are handling times, 0 is fine)
            if (machine?.IsVirtual == true) { }
            else if (op.SetupTimeMin <= 0)
                w.Add(new("setup_time_invalid", "warning", op.No, "setup_time_min", null, $"Op {op.No}: setup time should be greater than 0."));
            else if (op.SetupTimeMin > MaxSetupMin)
                w.Add(new("setup_time_high", "warning", op.No, "setup_time_min", Args(("value", op.SetupTimeMin), ("max", MaxSetupMin)),
                    $"Op {op.No}: setup time {op.SetupTimeMin} min looks unusually high (> {MaxSetupMin})."));

            if (machine?.IsVirtual == true) { }
            else if (op.UnitTimeMin <= 0)
                w.Add(new("unit_time_invalid", "warning", op.No, "unit_time_min", null, $"Op {op.No}: unit time should be greater than 0."));
            else if (op.UnitTimeMin > MaxUnitMin)
                w.Add(new("unit_time_high", "warning", op.No, "unit_time_min", Args(("value", op.UnitTimeMin), ("max", MaxUnitMin)),
                    $"Op {op.No}: unit time {op.UnitTimeMin} min looks unusually high (> {MaxUnitMin})."));

            if (op.Confidence == "low")
                w.Add(new("low_confidence", "info", op.No, "confidence", null, $"Op {op.No}: the model has low confidence - please review."));
        }

        // Final inspection present? (QC machine type, or an inspection-like operation name)
        var hasInspection = ops.Any(op =>
            (machines.TryGetValue(op.MachineId ?? "", out var m) && ContainsAny(m.Type, InspectionKeywords))
            || ContainsAny(op.Name, InspectionKeywords));
        if (!hasInspection)
            w.Add(new("no_final_inspection", "warning", null, null, null, "No final inspection operation found."));

        return w;
    }

    static string Normalize(string s) => new string(s.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    static bool ContainsAny(string? text, string[] keywords) =>
        text is not null && keywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));

    internal static Dictionary<string, string> Args(params (string Key, object? Value)[] args) =>
        args.ToDictionary(a => a.Key, a => Convert.ToString(a.Value, System.Globalization.CultureInfo.InvariantCulture) ?? "");
}
