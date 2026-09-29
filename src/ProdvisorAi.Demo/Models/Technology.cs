using System.Text.Json.Serialization;

namespace ProdvisorAi.Demo.Models;

// These records mirror the "production_technology" JSON Schema sent to OpenAI (snake_case on the wire).
// The same shape is used for the UI, for saving edits and for the JSON export.

public sealed record TechnologyOperation(
    [property: JsonPropertyName("no")] int No,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("machine_id")] string MachineId,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("setup_time_min")] double SetupTimeMin,
    [property: JsonPropertyName("unit_time_min")] double UnitTimeMin,
    [property: JsonPropertyName("tools")] string? Tools,
    [property: JsonPropertyName("confidence")] string Confidence);

/// <summary>Material as read from the drawing (title block / notes), kept separate from the operations.</summary>
public sealed record TechnologyMaterial(
    [property: JsonPropertyName("grade")] string? Grade,                    // e.g. "C45 (1.0503)"
    [property: JsonPropertyName("stock")] string? Stock,                    // e.g. "round bar Ø45 x 184 mm"
    [property: JsonPropertyName("heat_treatment")] string? HeatTreatment,   // e.g. "hardened 45-50 HRC", null if none
    [property: JsonPropertyName("source")] string Source);                  // "drawing" | "user" | "assumed" | "unknown"

public sealed record TechnologyResult(
    [property: JsonPropertyName("material")] TechnologyMaterial? Material,  // null for jobs created before this field existed
    [property: JsonPropertyName("operations")] List<TechnologyOperation> Operations,
    [property: JsonPropertyName("general_notes")] string GeneralNotes);

/// <summary>A post-validation finding. Flagged in the UI, never blocks the result.</summary>
public sealed record ValidationWarning(
    string Code,                               // stable code, translated in the UI (e.g. "machine_inactive")
    string Severity,                           // "error" | "warning" | "info"
    int? OperationNo,                          // null = applies to the whole technology
    string? Field,                             // e.g. "machine_id", "unit_time_min"
    Dictionary<string, string>? Args,          // values for the UI message template
    string Message);                           // English fallback text
