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
        IReadOnlyList<Machine> activeMachines, DrawingInput drawing, string language,
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
                        new JsonObject { ["type"] = "input_text", ["text"] = BuildUserText(technologyPrompt, part, activeMachines, language) },
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
                        },
                        ["required"] = new JsonArray("no", "name", "machine_id", "description", "setup_time_min", "unit_time_min", "tools", "confidence"),
                        ["additionalProperties"] = false,
                    },
                },
                ["general_notes"] = new JsonObject { ["type"] = "string" },
            },
            ["required"] = new JsonArray("operations", "general_notes"),
            ["additionalProperties"] = false,
        };
    }

    static string BuildUserText(string technologyPrompt, PartMetadata part, IReadOnlyList<Machine> machines, string language)
    {
        var machineList = machines.Select(m => new
        {
            machine_id = m.Id,
            name = m.Name,
            type = m.Type,
            capabilities = m.Description,
            hourly_rate = m.HourlyRate,
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
        sb.AppendLine(JsonSerializer.Serialize(machineList, new JsonSerializerOptions { WriteIndented = true }));
        sb.AppendLine();
        sb.AppendLine("Prepare the draft production technology for the part on the attached drawing.");
        sb.AppendLine(language == "pl"
            ? "Write all text fields (name, description, tools, general_notes) in Polish."
            : "Write all text fields (name, description, tools, general_notes) in English.");
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
