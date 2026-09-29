using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ProdvisorAi.Demo.Data;

namespace ProdvisorAi.Demo.Prodvisor;

public sealed record MachineParameter(string Name, string Value);

public sealed record ImportResult(string Source, int MachinesAdded, int MachinesUpdated, int MachinesDeactivated, int Materials);

/// <summary>
/// Copies PRODVISOR master data into the local database:
///  - machines: upsert by MASZYNY.ID; PRODVISOR fields (name, type, parameters, virtual flag) are refreshed,
///    local fields (hourly rate, active flag, description) are kept; machines removed in PRODVISOR are deactivated;
///  - materials: dbo.MaterialsKind is replaced as a whole (read-only reference list for matching).
/// </summary>
public sealed partial class ProdvisorImporter(AppDbContext db, IConfiguration config, ILogger<ProdvisorImporter> log)
{
    public const string CooperationType = "KOOPERACJA";

    // Types that are people/offices/duplicate records, not production resources -> imported as inactive.
    static readonly HashSet<string> NonProductionTypes = new(StringComparer.OrdinalIgnoreCase)
        { "BRAK", "MISTRZ", "TECHNOLOG", "KONSTRUKTOR", "UNIWERSALNE", "401_ PRODUKCJA", "402_PRODUKCJA", "PAKOWANIE" };

    // Virtual machines that describe a PROCESS (usable by the model as external cooperation).
    // Named subcontractors ("X - wykonanie na gotowo"), software, material suppliers and test rows stay inactive.
    static readonly HashSet<string> CooperationProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "AZOTOWANIE", "HARTOWANIE", "HARTOWANIE - PRÓŻNIA", "NAWĘGLANIE i HARTOWANIE", "CIĘCIE WODĄ", "CIĘCIE LASEREM",
        "CIĘCIE PLAZMĄ", "CIĘCIE DRUTEM", "SPAWANIE MIG / MAG", "SPAWANIE TIG", "MALOWANIE PROSZKOWE", "POMIARY 3D",
        "NIKLOWANIE", "CZERNIENIE", "CYNKOWANIE", "CHROMOWANIE", "ANODOWANIE", "FOSFORANOWANIE", "DŁUTOWANIE",
        "ELEKTRODRĄŻENIE", "BADANIA RADIOGRAFICZNE", "USŁUGA POWLEKANIA NA GOTOWO",
    };

    // Material "kinds" that are not raw materials.
    [GeneratedRegex(@"^(USŁUGA|ZŁOŻENIE|REGENERACJA|MODYFIKACJA|PRODUKT GOTOWY|Z ELEMENTU ZAKUPOWEGO|POWIERZONY DETAL KLIENTA|ŚRUBA|FILC|n|nc|pa|Próg|Rama \d+|Skrz\..*|Szpros.*|test.*|testowymaterial\d*)$", RegexOptions.IgnoreCase)]
    private static partial Regex PseudoMaterial();

    [GeneratedRegex("^[A-Za-z0-9_-]{1,20}$")]
    private static partial Regex ValidCode();

    public async Task<ImportResult> ImportAsync(ProdvisorData data, CancellationToken ct = default)
    {
        var existing = await db.Machines.ToListAsync(ct);
        var byProdvisorId = existing.Where(m => m.ProdvisorId is not null).ToDictionary(m => m.ProdvisorId!.Value);
        var byCode = existing.ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
        var seenIds = new HashSet<int>();
        var workplaceByMachine = data.Workplaces.GroupBy(w => w.MachineId).ToDictionary(g => g.Key, g => g.First());
        int added = 0, updated = 0;

        foreach (var row in data.Machines)
        {
            seenIds.Add(row.Id);
            var code = MachineCode(row);
            var type = row.IsVirtual ? CooperationType : data.MachineTypes.GetValueOrDefault(row.TypeId ?? -1, "BRAK");
            var parameters = FormatParameters(row.Id, data);

            if (!byProdvisorId.TryGetValue(row.Id, out var machine) && byCode.TryGetValue(code, out var sameCode)
                && sameCode.ProdvisorId is null)
                machine = sameCode;                                   // machine seeded by an older version: link it

            if (machine is null)
            {
                var defaults = SeedData.DefaultMachines.FirstOrDefault(m => m.Id == code);
                machine = new Machine
                {
                    Id = code,
                    HourlyRate = defaults?.HourlyRate ?? DefaultRate(type, row.IsVirtual),
                    Description = defaults?.Description ?? (row.IsVirtual ? "Kooperacja – usługa zewnętrzna." : ""),
                    Active = DefaultActive(row, type),
                };
                db.Machines.Add(machine);
                byCode[code] = machine;
                added++;
            }
            else updated++;

            machine.ProdvisorId = row.Id;
            machine.Name = row.Name;
            machine.Type = type;
            machine.TypeId = row.TypeId;
            machine.Kind = row.Kind;
            machine.IsVirtual = row.IsVirtual;
            machine.ParametersJson = parameters.Count > 0 ? JsonSerializer.Serialize(parameters) : null;

            // STANOWISKO: workplace, group, hall; the hourly cost from PRODVISOR replaces the local rate when it is set
            if (workplaceByMachine.TryGetValue(row.Id, out var wp))
            {
                machine.WorkplaceId = wp.Id;
                machine.GroupCode = string.IsNullOrWhiteSpace(wp.GroupCode) ? null : wp.GroupCode.Trim();
                machine.GroupId = wp.NameId;
                machine.GroupName = wp.NameId is { } nid && data.WorkplaceNames.TryGetValue(nid, out var gn) ? gn : null;
                machine.Hall = wp.Hall;
                machine.MultiOperation = wp.MultiOperation;
                machine.Multiperson = wp.Multiperson;
                machine.WorkplacesGroup = wp.WorkplacesGroup;
                if (wp.HourlyCost is > 0) machine.HourlyRate = wp.HourlyCost.Value;
            }
        }

        // Removed in PRODVISOR -> no longer offered to the model (kept locally for old jobs).
        var deactivated = 0;
        foreach (var gone in existing.Where(m => m.ProdvisorId is { } id && !seenIds.Contains(id) && m.Active))
        {
            gone.Active = false;
            deactivated++;
        }

        // Materials: full refresh
        await db.Materials.ExecuteDeleteAsync(ct);
        db.Materials.AddRange(data.Materials.Select(m => new ProdvisorMaterial
        {
            Id = m.Id, Name = m.Name, AlloyId = m.AlloyId, Active = !m.NoActive && !PseudoMaterial().IsMatch(m.Name.Trim()),
        }));

        await db.SaveChangesAsync(ct);
        var result = new ImportResult(data.SourceDescription, added, updated, deactivated, data.Materials.Count);
        log.LogInformation("PRODVISOR import from {Source}: machines +{Added} ~{Updated} -{Deactivated}, materials {Materials}",
            result.Source, added, updated, deactivated, result.Materials);
        return result;
    }

    /// <summary>NR_MASZYNY for real machines; "K{ID}" for virtual/cooperation machines (their numbers are random codes).</summary>
    static string MachineCode(MachineRow row) =>
        !row.IsVirtual && row.Number is { Length: > 0 } nr && ValidCode().IsMatch(nr.Trim()) ? nr.Trim() : $"K{row.Id}";

    static bool DefaultActive(MachineRow row, string type) =>
        row.IsVirtual ? CooperationProcesses.Contains(row.Name.Trim()) : !NonProductionTypes.Contains(type) && row.Name != "tedst";

    static double DefaultRate(string type, bool isVirtual) => isVirtual ? 0 : type.ToUpperInvariant() switch
    {
        var t when t.Contains("5 OSI") => 320,
        var t when t.Contains("CNC") => 200,
        var t when t.Contains("LASER") || t.Contains("ROBOT") => 230,
        _ => 110,
    };

    /// <summary>MASZYNY_PARAMETRY -> readable "name: value unit" pairs. Numeric zeros mean "not filled in" and are skipped.</summary>
    List<MachineParameter> FormatParameters(int machineId, ProdvisorData data)
    {
        var result = new List<MachineParameter>();
        foreach (var p in data.Parameters.Where(p => p.MachineId == machineId))
        {
            if (!data.ParameterKinds.TryGetValue(p.KindId, out var kind)) continue;
            var unit = config[$"Prodvisor:Units:{kind.UnitId}"] ?? "";
            string? value = kind.ValueType switch
            {
                1 => p.Value is > 0 ? "tak" : "nie",                                        // bool
                0 => p.Text is { Length: > 0 } t ? t : null,                                // text
                _ => p.Value is { } v && v != 0
                    ? $"{v.ToString("0.###", CultureInfo.InvariantCulture)} {unit}".Trim()  // number
                    : p.Text is { Length: > 0 } t2 ? t2 : null,
            };
            if (value is not null) result.Add(new MachineParameter(kind.Name, value));
        }
        return result;
    }
}
