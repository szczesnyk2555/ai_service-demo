using System.Text.Json.Serialization;

namespace ProdvisorAi.Demo.Models;

// Model output - mirrors the "production_technology" JSON Schema sent to OpenAI (snake_case on the wire).

public sealed record TechnologyOperation(
    [property: JsonPropertyName("no")] int No,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("machine_id")] string MachineId,                                 // main machine (code)
    [property: JsonPropertyName("alternative_machine_ids")] List<string>? AlternativeMachineIds, // other machines of the group that fit
    [property: JsonPropertyName("setup_time_min")] double SetupTimeMin,                          // TPZ - per batch
    [property: JsonPropertyName("unit_time_min")] double UnitTimeMin,                            // TJ - per piece
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("comment")] string? Comment);                                     // remarks for this operation

public sealed record TechnologyMaterial(
    [property: JsonPropertyName("grade")] string? Grade,                    // as on the drawing, e.g. "C45 (1.0503)"
    [property: JsonPropertyName("stock")] string? Stock,                    // e.g. "Pręt okrągły Ø45 x 184 mm"
    [property: JsonPropertyName("system_material_id")] int? SystemMaterialId); // MaterialsKind.ID, null = not in the system

public sealed record PartDimensions(
    [property: JsonPropertyName("length_mm")] double? LengthMm,
    [property: JsonPropertyName("diameter_mm")] double? DiameterMm,
    [property: JsonPropertyName("width_mm")] double? WidthMm,
    [property: JsonPropertyName("height_mm")] double? HeightMm);

public sealed record StockDimensions(
    [property: JsonPropertyName("form")] string? Form,                      // round_bar | hex_bar | square_bar | flat_bar | plate | tube | profile | other
    [property: JsonPropertyName("size_mm")] double? SizeMm,                 // bar diameter / across flats / plate thickness
    [property: JsonPropertyName("length_mm")] double? LengthMm);            // cut length per piece

public sealed record TechnologyDimensions(
    [property: JsonPropertyName("part")] PartDimensions? Part,
    [property: JsonPropertyName("stock")] StockDimensions? Stock);

public sealed record TechnologyResult(
    [property: JsonPropertyName("operations")] List<TechnologyOperation> Operations,
    [property: JsonPropertyName("material")] TechnologyMaterial? Material,
    [property: JsonPropertyName("dimensions")] TechnologyDimensions? Dimensions,
    [property: JsonPropertyName("general_notes")] string GeneralNotes);

/// <summary>A post-validation finding. Reported to the client, never blocks the result.</summary>
public sealed record ValidationWarning(string Code, int? OperationNo, string Message);
