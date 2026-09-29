using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ProdvisorAi.Demo.Data;

namespace ProdvisorAi.Demo.Services;

/// <param name="DrawingText">Exact dimensions and texts extracted from a DXF (null for images / PDFs).</param>
public sealed record PartMetadata(string? PartNumber, string? Material, int? BatchSize, string DrawingFileName, string? DrawingText = null);

public sealed record DrawingInput(byte[] Bytes, string ContentType, string FileName);

/// <summary>
/// Builds the body for POST /v1/responses:
///   input[0]  developer message  = system prompt
///   input[1]  user message       = input_text (technology prompt + part data + machines as JSON)
///                                + input_image (PNG/JPG) or input_file (PDF)
///   text.format                  = JSON Schema with strict: true (machine_id enum built from the DB)
/// </summary>
public static class TechnologyRequestBuilder
{
    public const string SchemaName = "production_technology";

    public static JsonObject Build(
        string model, string systemPrompt, string technologyPrompt, PartMetadata part,
        IReadOnlyList<Machine> activeMachines, IReadOnlyList<ProdvisorMaterial> materials, DrawingInput drawing, string language,
        int maxOutputTokens, string? reasoningEffort)
    {
        var body = new JsonObject
        {
            ["model"] = model,
            ["input"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "developer",
                    ["content"] = systemPrompt,
                },
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = new JsonArray
                    {
                        new JsonObject { ["type"] = "input_text", ["text"] = BuildUserText(technologyPrompt, part, activeMachines, materials, language) },
                        BuildDrawingPart(drawing),
                    },
                },
            },
            ["text"] = new JsonObject
            {
                ["format"] = new JsonObject
                {
                    ["type"] = "json_schema",
                    ["name"] = SchemaName,
                    ["strict"] = true,
                    ["schema"] = BuildSchema(activeMachines.Select(m => m.Id)),
                },
            },
            ["max_output_tokens"] = maxOutputTokens,
            // Drawings are customer IP - do not keep the response stored on the OpenAI side.
            ["store"] = false,
        };

        if (!string.IsNullOrWhiteSpace(reasoningEffort))
            body["reasoning"] = new JsonObject { ["effort"] = reasoningEffort };

        return body;
    }

    /// <summary>
    /// The JSON Schema for the answer. The root must be an object, so the operations array is wrapped.
    /// machine_id is an enum of the customer's ACTIVE machines, regenerated for every request, so with
    /// strict mode the model physically cannot return a machine the customer does not have.
    /// </summary>
    public static JsonObject BuildSchema(IEnumerable<string> machineIds)
    {
        var ids = new JsonArray();
        foreach (var id in machineIds) ids.Add(id);

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["operations"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["no"] = new JsonObject { ["type"] = "integer" },
                            ["name"] = new JsonObject { ["type"] = "string" },
                            ["machine_id"] = new JsonObject { ["type"] = "string", ["enum"] = ids },
                            ["description"] = new JsonObject { ["type"] = "string" },
                            ["setup_time_min"] = new JsonObject { ["type"] = "number" },
                            ["unit_time_min"] = new JsonObject { ["type"] = "number" },
                            ["tools"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
                            ["confidence"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("high", "medium", "low") },
                            ["alternative_machine_ids"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string", ["enum"] = ids.DeepClone() } },
                        },
                        ["required"] = new JsonArray("no", "name", "machine_id", "description", "setup_time_min", "unit_time_min", "tools", "confidence", "alternative_machine_ids"),
                        ["additionalProperties"] = false,
                    },
                },
                ["material"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["grade"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
                        ["stock"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
                        ["heat_treatment"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
                        ["source"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("drawing", "user", "assumed", "unknown") },
                        ["stock_form"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("round_bar", "hex_bar", "square_bar", "flat_bar", "plate", "tube", "profile", "other") },
                        ["stock_size_mm"] = new JsonObject { ["type"] = new JsonArray("number", "null") },
                        ["stock_length_mm"] = new JsonObject { ["type"] = new JsonArray("number", "null") },
                        ["system_material_id"] = new JsonObject { ["type"] = new JsonArray("integer", "null") },
                        ["system_material_reason"] = new JsonObject { ["type"] = "string" },
                    },
                    ["required"] = new JsonArray("grade", "stock", "heat_treatment", "source", "stock_form", "stock_size_mm", "stock_length_mm",
                        "system_material_id", "system_material_reason"),
                    ["additionalProperties"] = false,
                },
                ["general_notes"] = new JsonObject { ["type"] = "string" },
            },
            ["required"] = new JsonArray("material", "operations", "general_notes"),
            ["additionalProperties"] = false,
        };
    }

    static string BuildUserText(string technologyPrompt, PartMetadata part, IReadOnlyList<Machine> machines,
        IReadOnlyList<ProdvisorMaterial> materials, string language)
    {
        // Parameters come from PRODVISOR (MASZYNY_PARAMETRY): working ranges, axes, spindle speed, cutting capacity...
        var machineList = machines.Select(m => new
        {
            machine_id = m.Id,
            name = m.Name,
            type = m.Type,
            external_cooperation = m.IsVirtual ? true : (bool?)null,
            workplace_id = m.WorkplaceId,
            workplace_group = m.GroupCode,
            hall = m.Hall,
            multi_operation = m.MultiOperation ? true : (bool?)null,
            capabilities = string.IsNullOrWhiteSpace(m.Description) ? null : m.Description,
            parameters = m.ParametersJson is null ? null
                : JsonSerializer.Deserialize<List<Prodvisor.MachineParameter>>(m.ParametersJson)!.Select(p => $"{p.Name}: {p.Value}").ToList(),
            hourly_rate = m.IsVirtual ? (double?)null : m.HourlyRate,
        });

        static string OrUnknown(string? v) => string.IsNullOrWhiteSpace(v) ? "not provided (read from drawing if visible)" : v;

        var sb = new StringBuilder();
        sb.AppendLine("## Technology instructions");
        sb.AppendLine(technologyPrompt.Trim());
        sb.AppendLine();
        sb.AppendLine("## Part data");
        sb.AppendLine($"- Part number: {OrUnknown(part.PartNumber)}");
        sb.AppendLine($"- Material: {OrUnknown(part.Material)}");
        sb.AppendLine($"- Batch size: {(part.BatchSize is > 0 ? $"{part.BatchSize} pcs" : "not provided")}");
        sb.AppendLine($"- Drawing file: {part.DrawingFileName}");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(part.DrawingText))
        {
            sb.AppendLine("## Data extracted from the DXF file");
            sb.AppendLine("The attached image was rendered from a DXF CAD file. The values below were read directly from the file and are EXACT -");
            sb.AppendLine("prefer them over reading numbers from the image. Use the image for geometry and context.");
            sb.AppendLine(part.DrawingText.Trim());
            sb.AppendLine();
        }
        sb.AppendLine("## Available machines (use ONLY these machine_id values)");
        sb.AppendLine("Machines with external_cooperation = true are external services (heat treatment, coatings, cutting...). Use them only for");
        sb.AppendLine("work the in-house machines cannot do; for them setup_time_min / unit_time_min = internal handling time, lead time goes to the description.");
        sb.AppendLine("Respect machine parameters (table size, travels, turning diameter/length, cutting capacity) when choosing a machine.");
        sb.AppendLine("Machine GROUPS (from PRODVISOR workplaces, table STANOWISKO): machines with the same workplace_group are workplaces of the same kind");
        sb.AppendLine("(e.g. group \"04\" = all CNC milling centres). You may combine machines into a group for an operation: put the best machine in");
        sb.AppendLine("machine_id and the other machines that can do the SAME operation with the same program / set-up in alternative_machine_ids");
        sb.AppendLine("(the planner then chooses the free one). Use the group code, type and parameters to decide - only add machines that really fit");
        sb.AppendLine("(size, axes, capacity); an empty list means the operation is bound to machine_id. Never repeat machine_id in the list.");
        sb.AppendLine(JsonSerializer.Serialize(machineList, new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));

        sb.AppendLine();
        sb.AppendLine("Fill \"material\" separately: grade = material designation as written on the drawing (title block / notes),");
        sb.AppendLine("stock = raw material form and size you plan (e.g. round bar diameter x length), heat_treatment = as on the drawing or null.");
        sb.AppendLine("source = \"drawing\" if read from the drawing, \"user\" if only given in the part data, \"assumed\" if you had to assume it, \"unknown\" otherwise.");
        sb.AppendLine("stock_form / stock_size_mm / stock_length_mm = the raw material you plan to use (e.g. round_bar, 45, 184): size = bar diameter,");
        sb.AppendLine("hex across flats or plate thickness incl. machining allowance. The stock is matched against the company material list.");
        sb.AppendLine("If the material in the part data differs from the drawing, keep the drawing value and explain the conflict in general_notes.");
        sb.AppendLine();

        // The company material list: the model picks the stock item (names in the ERP differ from drawing designations).
        sb.AppendLine("## Company material list (PRODVISOR, format: ID | name)");
        sb.AppendLine("Choose material.system_material_id from this list for the stock you plan:");
        sb.AppendLine("- Match the GRADE, also through equivalent designations (EN / PN / DIN W.Nr / AISI / trade names, e.g. C45 = 1.0503 = \"STAL 45\",");
        sb.AppendLine("  42CrMo4 = 40HM = 40HMT, S355J2 = 18G2A, S235JR = St3S, 1.2379 = NC11LV = D2, EN AW-2017 = PA6, 1.4301 = 304), ignoring typos and formatting.");
        sb.AppendLine("- Then the FORM (pręt = round bar, sześciokąt = hex, blacha = plate, płaskownik = flat bar, profil/rura...) and SIZE:");
        sb.AppendLine("  the item must be at least your stock_size_mm; prefer the smallest sufficient one. A generic item without size (e.g. \"Stal 1,2311\") is acceptable if no sized item fits.");
        sb.AppendLine("- Avoid customer-supplied / test items (\"materiał klienta\", \"powierzony\", \"test\") unless the drawing says the material is supplied by the customer.");
        sb.AppendLine("- If nothing in the list is the right grade: system_material_id = null and say in system_material_reason that the material is not in the system.");
        sb.AppendLine("- system_material_reason: one short sentence (why this item, e.g. \"C45 = 1.0503, pręt Ø50 ≥ Ø45\", or why none).");
        foreach (var m in materials.Where(m => m.Active).OrderBy(m => m.Name))
            sb.Append(m.Id).Append(" | ").AppendLine(m.Name);
        sb.AppendLine();

        sb.AppendLine("Prepare the draft production technology for the part on the attached drawing.");
        sb.AppendLine(language == "pl"
            ? "Write all text fields (name, description, tools, general_notes, material.stock, material.heat_treatment, material.system_material_reason) in Polish."
            : "Write all text fields (name, description, tools, general_notes, material.stock, material.heat_treatment, material.system_material_reason) in English.");
        return sb.ToString();
    }

    static JsonObject BuildDrawingPart(DrawingInput drawing)
    {
        var dataUrl = $"data:{drawing.ContentType};base64,{Convert.ToBase64String(drawing.Bytes)}";

        return drawing.ContentType == "application/pdf"
            ? new JsonObject { ["type"] = "input_file", ["filename"] = drawing.FileName, ["file_data"] = dataUrl }
            : new JsonObject { ["type"] = "input_image", ["image_url"] = dataUrl, ["detail"] = "high" };
    }

    /// <summary>Copy of the request for the Debug panel with the (large) base64 payload removed.</summary>
    public static JsonNode Redact(JsonObject body)
    {
        var copy = JsonNode.Parse(body.ToJsonString())!;
        foreach (var message in copy["input"]!.AsArray())
        {
            if (message?["content"] is not JsonArray parts) continue;
            foreach (var part in parts.OfType<JsonObject>())
            {
                foreach (var field in new[] { "image_url", "file_data" })
                    if (part[field]?.GetValue<string>() is { } data)
                        part[field] = $"<base64 omitted, {data.Length} chars>";
            }
        }
        return copy;
    }
}
