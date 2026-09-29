using System.Data.Common;
using System.Reflection;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace ProdvisorAi.Demo.Prodvisor;

// Raw rows as they are stored in the PRODVISOR (DANSOFT) database. Column names in comments.
public sealed record MachineRow(int Id, string? Number, int? TypeId, string Name, int? Kind, bool IsVirtual);   // dbo.MASZYNY
public sealed record ParameterKindRow(int Id, string Name, int ValueType, int? UnitId);                         // dbo.MASZYNY_RODZAJ_PARAMETRU
public sealed record ParameterRow(int MachineId, int KindId, double? Value, string? Text);                       // dbo.MASZYNY_PARAMETRY
public sealed record MaterialRow(int Id, string Name, int? AlloyId, bool NoActive);                              // dbo.MaterialsKind

/// <summary>dbo.STANOWISKO - the workplace a machine belongs to. AuxiliaryNo is the workplace GROUP code
/// (e.g. "04" = all CNC milling centres 0401-0407), which lets machines be combined into groups.</summary>
public sealed record WorkplaceRow(int Id, string? Number, int MachineId, double? HourlyCost, int? Hall,
    bool MultiOperation, bool IsVirtual, string? GroupCode, bool Multiperson, int? WorkplacesGroup);

public sealed record ProdvisorData(
    IReadOnlyDictionary<int, string> MachineTypes,              // dbo.MASZYNY_TYP: ID -> TYP_MASZYNY
    IReadOnlyList<MachineRow> Machines,
    IReadOnlyDictionary<int, ParameterKindRow> ParameterKinds,
    IReadOnlyList<ParameterRow> Parameters,
    IReadOnlyList<MaterialRow> Materials,
    IReadOnlyList<WorkplaceRow> Workplaces,
    string SourceDescription);

/// <summary>
/// Where machine and material master data comes from:
///  - "Snapshot"  : JSON export of the PRODVISOR tables embedded in the app (demo, no database needed)
///  - "SqlServer" : the customer's PRODVISOR database (production)
///  - "PostgreSql": a PostgreSQL copy of the same tables (used for local integration tests)
/// The database is only READ (SELECTs); nothing is written to PRODVISOR.
/// </summary>
public sealed class ProdvisorSource(IConfiguration config, ILogger<ProdvisorSource> log)
{
    public string Provider => config["Prodvisor:Provider"] is { Length: > 0 } p ? p : "Snapshot";
    string ConnectionString => config["Prodvisor:ConnectionString"] ?? "";
    string Schema => config["Prodvisor:Schema"] is { Length: > 0 } s ? s : "dbo";

    public bool IsDatabase => Provider is "SqlServer" or "PostgreSql" && ConnectionString.Length > 0;

    public Task<ProdvisorData> LoadAsync(CancellationToken ct = default) =>
        IsDatabase ? LoadFromDatabaseAsync(ct) : Task.FromResult(LoadSnapshot());

    // ---- Snapshot (embedded JSON) --------------------------------------------------------------
    public static ProdvisorData LoadSnapshot()
    {
        using var machinesDoc = JsonDocument.Parse(ReadResource("ProdvisorSnapshot.json"));
        using var materialsDoc = JsonDocument.Parse(ReadResource("ProdvisorSnapshotMaterials.json"));
        var m = machinesDoc.RootElement;

        static int? IntOrNull(JsonElement e) => e.ValueKind == JsonValueKind.Number ? e.GetInt32() : null;
        static string? StrOrNull(JsonElement e) => e.ValueKind == JsonValueKind.String ? e.GetString() : null;

        return new ProdvisorData(
            m.GetProperty("maszyny_typ").EnumerateArray().ToDictionary(r => r[0].GetInt32(), r => r[1].GetString()!.Trim()),
            m.GetProperty("maszyny").EnumerateArray()
                .Select(r => new MachineRow(r[0].GetInt32(), StrOrNull(r[1]), IntOrNull(r[2]), r[3].GetString()!.Trim(), IntOrNull(r[4]), r[5].GetInt32() == 1))
                .ToList(),
            m.GetProperty("maszyny_rodzaj_parametru").EnumerateArray()
                .Select(r => new ParameterKindRow(r[0].GetInt32(), r[1].GetString()!.Trim(), r[2].GetInt32(), IntOrNull(r[3])))
                .ToDictionary(k => k.Id),
            m.GetProperty("maszyny_parametry").EnumerateArray()
                .Select(r => new ParameterRow(r[0].GetInt32(), r[1].GetInt32(), r[2].ValueKind == JsonValueKind.Number ? r[2].GetDouble() : null, StrOrNull(r[3])))
                .ToList(),
            materialsDoc.RootElement.GetProperty("materials_kind").EnumerateArray()
                .Select(r => new MaterialRow(r[0].GetInt32(), r[1].GetString()!.Trim(), IntOrNull(r[2]), IntOrNull(r[3]) == 1))
                .ToList(),
            m.TryGetProperty("stanowisko", out var ws)
                ? ws.EnumerateArray().Select(r => new WorkplaceRow(
                    r[0].GetInt32(), StrOrNull(r[1])?.Trim(), r[2].GetInt32(), r[3].ValueKind == JsonValueKind.Number ? r[3].GetDouble() : null,
                    IntOrNull(r[4]), IntOrNull(r[5]) == 1, IntOrNull(r[6]) == 1, StrOrNull(r[7])?.Trim(), IntOrNull(r[8]) == 1, IntOrNull(r[9]))).ToList()
                : [],
            "snapshot (embedded)");
    }

    static string ReadResource(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded resource {name} not found.");
        return new StreamReader(stream).ReadToEnd();
    }

    // ---- Database (SQL Server or PostgreSQL) ----------------------------------------------------
    async Task<ProdvisorData> LoadFromDatabaseAsync(CancellationToken ct)
    {
        var pg = Provider == "PostgreSql";
        // Identifier quoting differs; table/column names are identical (the PostgreSQL copy mirrors SQL Server).
        string T(string table) => pg ? $"\"{Schema}\".\"{table}\"" : $"[{Schema}].[{table}]";
        string C(string column) => pg ? $"\"{column}\"" : $"[{column}]";

        await using DbConnection conn = pg ? new NpgsqlConnection(ConnectionString) : new SqlConnection(ConnectionString);
        await conn.OpenAsync(ct);
        log.LogInformation("Reading PRODVISOR master data from {Provider}", Provider);

        async Task<List<T>> Query<T>(string sql, Func<DbDataReader, T> map)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            await using var r = await cmd.ExecuteReaderAsync(ct);
            var list = new List<T>();
            while (await r.ReadAsync(ct)) list.Add(map(r));
            return list;
        }

        var types = await Query($"SELECT {C("ID")}, {C("TYP_MASZYNY")} FROM {T("MASZYNY_TYP")}",
            r => (Id: Int(r, 0)!.Value, Name: Str(r, 1) ?? ""));
        var machines = await Query(
            $"SELECT {C("ID")}, {C("NR_MASZYNY")}, {C("ID_TYP_MASZYNY")}, {C("NAZWA_MASZYNY")}, {C("ID_RODZAJ_MASZYNY")}, {C("VirtualMachine")} FROM {T("MASZYNY")}",
            r => new MachineRow(Int(r, 0)!.Value, Str(r, 1), Int(r, 2), Str(r, 3) ?? "", Int(r, 4), Int(r, 5) == 1));
        var kinds = await Query(
            $"SELECT {C("ID")}, {C("RODZAJ_PARAMETRU")}, {C("TYP_PARAMETRU")}, {C("JEDNOSTKA")} FROM {T("MASZYNY_RODZAJ_PARAMETRU")}",
            r => new ParameterKindRow(Int(r, 0)!.Value, Str(r, 1) ?? "", Int(r, 2) ?? 2, Int(r, 3)));
        var parameters = await Query(
            $"SELECT {C("ID_MASZYNY")}, {C("ID_RODZAJ_PARAMETRU")}, {C("WARTOSC")}, {C("TEXT")} FROM {T("MASZYNY_PARAMETRY")}",
            r => new ParameterRow(Int(r, 0)!.Value, Int(r, 1)!.Value, Dbl(r, 2), Str(r, 3)));
        var materials = await Query(
            $"SELECT {C("ID")}, {C("Name")}, {C("AlloyID")}, {C("NoActive")} FROM {T("MaterialsKind")}",
            r => new MaterialRow(Int(r, 0)!.Value, Str(r, 1) ?? "", Int(r, 2), Int(r, 3) == 1));
        var workplaces = await Query(
            $"SELECT {C("ID")}, {C("NR_STANOWISKA")}, {C("ID_MASZYNY")}, {C("KOSZT_ROBOCZOGODZINY")}, {C("NR_HALI")}, {C("MultiOperation")}, " +
            $"{C("VirtualWorkplace")}, {C("AuxiliaryNo")}, {C("Multiperson")}, {C("WorkplacesGroup")} FROM {T("STANOWISKO")} WHERE {C("ID_MASZYNY")} IS NOT NULL",
            r => new WorkplaceRow(Int(r, 0)!.Value, Str(r, 1), Int(r, 2)!.Value, Dbl(r, 3), Int(r, 4), Int(r, 5) == 1, Int(r, 6) == 1,
                Str(r, 7), Int(r, 8) == 1, Int(r, 9)));

        return new ProdvisorData(
            types.ToDictionary(t => t.Id, t => t.Name), machines, kinds.ToDictionary(k => k.Id), parameters, materials, workplaces,
            $"{Provider} database");
    }

    // Tolerant readers: PRODVISOR columns are a mix of int / smallint / bit / decimal / char(n).
    static int? Int(DbDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToInt32(r.GetValue(i));
    static double? Dbl(DbDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToDouble(r.GetValue(i));
    static string? Str(DbDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToString(r.GetValue(i))?.Trim();
}
