using ProdvisorAi.Demo.Data;
using ProdvisorAi.Demo.Models;

namespace ProdvisorAi.Demo.Services;

/// <summary>
/// Post-validation of the model's answer. Structured Outputs guarantee the SHAPE; this checks the CONTENT.
/// Findings are returned to the client as warnings - nothing is rejected.
/// </summary>
public static class TechnologyValidator
{
    const double MaxSetupMin = 480;   // one shift
    const double MaxUnitMin = 600;

    static readonly string[] InspectionKeywords = ["kontrol", "pomiar", "inspect", "qc"];

    public static List<ValidationWarning> Validate(TechnologyResult tech, IReadOnlyList<Machine> allMachines, IReadOnlyList<ProdvisorMaterial> materials, bool konstruktor = false)
    {
        var w = new List<ValidationWarning>();
        if (konstruktor)
        {
            if (tech.Material?.Grade is not { Length: > 0 })
                w.Add(new("material_missing", null, "Nie odczytano materiału z rysunku."));
            if (tech.Quantity is null)
                w.Add(new("quantity_missing", null, "Ilość sztuk nie jest podana na rysunku."));
            if (tech.Dimensions?.Part is not { } p || (p.LengthMm, p.DiameterMm, p.WidthMm, p.HeightMm) == (null, null, null, null))
                w.Add(new("dimensions_missing", null, "Nie odczytano wymiarów elementu."));
            AddMaterialWarning(tech, materials, w);
            return w;
        }
        var ops = tech.Operations;
        if (ops.Count == 0)
        {
            w.Add(new("no_operations", null, "Technologia nie zawiera operacji."));
            return w;
        }

        foreach (var dup in ops.GroupBy(o => o.No).Where(g => g.Count() > 1))
            w.Add(new("duplicate_number", dup.Key, $"Numer operacji {dup.Key} występuje więcej niż raz."));
        if (ops.Where((o, i) => o.No != i + 1).Any())
            w.Add(new("numbering_not_sequential", null, "Operacje nie są numerowane kolejno 1..n."));

        var machines = allMachines.ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var op in ops)
        {
            if (!machines.TryGetValue(op.MachineId ?? "", out var machine))
                w.Add(new("machine_unknown", op.No, $"Op. {op.No}: maszyna '{op.MachineId}' nie istnieje."));
            else if (!machine.Active)
                w.Add(new("machine_inactive", op.No, $"Op. {op.No}: maszyna '{op.MachineId}' jest nieaktywna."));

            foreach (var alt in op.AlternativeMachineIds ?? [])
            {
                if (string.Equals(alt, op.MachineId, StringComparison.OrdinalIgnoreCase))
                    w.Add(new("alternative_duplicate", op.No, $"Op. {op.No}: {alt} jest jednocześnie maszyną główną i alternatywną."));
                else if (!machines.TryGetValue(alt, out var am) || !am.Active)
                    w.Add(new("alternative_inactive", op.No, $"Op. {op.No}: maszyna alternatywna {alt} nie istnieje lub jest nieaktywna."));
            }

            // External cooperation: handling times, 0 is fine
            if (machine?.IsVirtual == true) continue;
            if (op.SetupTimeMin <= 0) w.Add(new("setup_time_invalid", op.No, $"Op. {op.No}: TPZ powinno być większe od 0."));
            else if (op.SetupTimeMin > MaxSetupMin) w.Add(new("setup_time_high", op.No, $"Op. {op.No}: TPZ {op.SetupTimeMin} min wygląda na zawyżone (> {MaxSetupMin})."));
            if (op.UnitTimeMin <= 0) w.Add(new("unit_time_invalid", op.No, $"Op. {op.No}: TJ powinno być większe od 0."));
            else if (op.UnitTimeMin > MaxUnitMin) w.Add(new("unit_time_high", op.No, $"Op. {op.No}: TJ {op.UnitTimeMin} min wygląda na zawyżone (> {MaxUnitMin})."));
        }

        var hasInspection = ops.Any(op =>
            (machines.TryGetValue(op.MachineId ?? "", out var m) && ContainsAny(m.Type, InspectionKeywords)) || ContainsAny(op.Name, InspectionKeywords));
        if (!hasInspection)
            w.Add(new("no_final_inspection", null, "Brak operacji kontroli końcowej."));

        AddMaterialWarning(tech, materials, w);
        return w;
    }

    static void AddMaterialWarning(TechnologyResult tech, IReadOnlyList<ProdvisorMaterial> materials, List<ValidationWarning> w)
    {
        if (tech.Material is { } mat && !string.IsNullOrWhiteSpace(mat.Grade)
            && (mat.SystemMaterialId is not { } id || !materials.Any(x => x.Id == id)))
            w.Add(new("material_not_in_system", null, $"Materiał {mat.Grade} nie został dopasowany do kartoteki materiałów - wybierz ręcznie."));
    }

    static bool ContainsAny(string? text, string[] keywords) =>
        text is not null && keywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));
}
