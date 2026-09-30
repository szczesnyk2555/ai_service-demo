using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ProdvisorAi.Demo.Data;

namespace ProdvisorAi.Demo.Services;

/// <summary>Element data read from PRODVISOR by element_id.</summary>
public sealed record ElementData(string? ElementId, string? Number, string? Name, string? Material, int? Quantity, string DrawingFileName, string? DrawingText);

public sealed record DrawingInput(byte[] Bytes, string ContentType, string FileName);

/// <summary>
/// Builds the body for POST /v1/responses:
///   developer message = system prompt (built into the service)
///   user message      = input_text (technology prompt + element + machines + groups + materials) + drawing
///   text.format       = JSON Schema, strict: machine ids are an enum of the active machines
/// </summary>
public static class TechnologyRequestBuilder
{
    public const string SchemaName = "production_technology";

    static readonly JsonSerializerOptions Compact = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static JsonObject Build(
        string model, string systemPrompt, string technologyPrompt, ElementData element,
        IReadOnlyList<Machine> machines, IReadOnlyList<ProdvisorMaterial> materials, DrawingInput drawing,
        int maxOutputTokens, string? reasoningEffort)
    {
        var body = new JsonObject
        {
            ["model"] = model,
            ["input"] = new JsonArray
            {
                new JsonObject { ["role"] = "developer", ["content"] = systemPrompt },
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = new JsonArray
                    {
                        new JsonObject { ["type"] = "input_text", ["text"] = BuildUserText(technologyPrompt, element, machines, materials) },
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
                    ["schema"] = BuildSchema(machines.Select(m => m.Id)),
                },
            },
            ["max_output_tokens"] = maxOutputTokens,
            ["store"] = false,   // drawings are customer IP - nothing is kept on the OpenAI side
        };
        if (!string.IsNullOrWhiteSpace(reasoningEffort))
            body["reasoning"] = new JsonObject { ["effort"] = reasoningEffort };
        return body;
    }

    /// <summary>Answer format. machine ids are an enum of the active machines, so the model cannot return others.</summary>
    public static JsonObject BuildSchema(IEnumerable<string> machineIds)
    {
        var ids = new JsonArray();
        foreach (var id in machineIds) ids.Add(id);

        static JsonObject T(params string[] types) => types.Length == 1
            ? new JsonObject { ["type"] = types[0] }
            : new JsonObject { ["type"] = new JsonArray(types.Select(t => (JsonNode)t).ToArray()) };
        static JsonObject Obj(JsonObject props) => new()
        {
            ["type"] = "object",
            ["properties"] = props,
            ["required"] = new JsonArray(props.Select(p => (JsonNode)p.Key).ToArray()),
            ["additionalProperties"] = false,
        };

        return Obj(new JsonObject
        {
            ["operations"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = Obj(new JsonObject
                {
                    ["no"] = T("integer"),
                    ["name"] = T("string"),
                    ["machine_id"] = new JsonObject { ["type"] = "string", ["enum"] = ids },
                    ["alternative_machine_ids"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string", ["enum"] = ids.DeepClone() } },
                    ["setup_time_min"] = T("number"),
                    ["unit_time_min"] = T("number"),
                    ["description"] = T("string"),
                    ["comment"] = T("string", "null"),
                }),
            },
            ["material"] = Obj(new JsonObject
            {
                ["grade"] = T("string", "null"),
                ["stock"] = T("string", "null"),
                ["system_material_id"] = T("integer", "null"),
            }),
            ["dimensions"] = Obj(new JsonObject
            {
                ["part"] = Obj(new JsonObject
                {
                    ["length_mm"] = T("number", "null"), ["diameter_mm"] = T("number", "null"),
                    ["width_mm"] = T("number", "null"), ["height_mm"] = T("number", "null"),
                }),
                ["stock"] = Obj(new JsonObject
                {
                    ["form"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("round_bar", "hex_bar", "square_bar", "flat_bar", "plate", "tube", "profile", "other") },
                    ["size_mm"] = T("number", "null"),
                    ["length_mm"] = T("number", "null"),
                }),
            }),
            ["general_notes"] = T("string"),
        });
    }

    static string BuildUserText(string technologyPrompt, ElementData element, IReadOnlyList<Machine> machines, IReadOnlyList<ProdvisorMaterial> materials)
    {
        static string Or(string? v) => string.IsNullOrWhiteSpace(v) ? "brak danych (odczytaj z rysunku, jeśli widoczne)" : v;
        var sb = new StringBuilder();

        sb.AppendLine("## Instrukcje technologiczne (zasady firmowe)");
        sb.AppendLine(string.IsNullOrWhiteSpace(technologyPrompt) ? "(brak)" : technologyPrompt.Trim());
        sb.AppendLine();

        sb.AppendLine("## Element");
        sb.AppendLine($"- Numer: {Or(element.Number)}");
        sb.AppendLine($"- Nazwa: {Or(element.Name)}");
        sb.AppendLine($"- Materiał: {Or(element.Material)}");
        sb.AppendLine($"- Ilość (partia): {(element.Quantity is > 0 ? $"{element.Quantity} szt." : "brak danych")}");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(element.DrawingText))
        {
            sb.AppendLine("## Dane odczytane z pliku DXF (DOKŁADNE - mają pierwszeństwo przed odczytem z obrazu)");
            sb.AppendLine(element.DrawingText.Trim());
            sb.AppendLine();
        }

        // Machines: only what the model needs to decide (no costs, no database ids).
        sb.AppendLine("## Maszyny (używaj WYŁĄCZNIE tych machine_id)");
        sb.AppendLine("cooperation = true to usługi zewnętrzne (obróbka cieplna, powłoki, cięcie...) - tylko do prac, których nie da się wykonać na miejscu;");
        sb.AppendLine("dla nich TPZ/TJ = czas obsługi wewnętrznej. Sprawdzaj parametry maszyn (stół, przesuwy, średnice, zakres cięcia).");
        sb.AppendLine(JsonSerializer.Serialize(machines.Select(m => new
        {
            machine_id = m.Id,
            name = m.Name,
            type = m.Type,
            group = m.IsVirtual ? null : m.GroupName,
            cooperation = m.IsVirtual ? true : (bool?)null,
            parameters = m.ParametersJson is null ? null
                : JsonSerializer.Deserialize<List<Prodvisor.MachineParameter>>(m.ParametersJson)!.Select(p => $"{p.Name}: {p.Value}").ToList(),
        }), Compact));
        sb.AppendLine();

        // Groups = PRODVISOR workplace names (STANOWISKO_NAZWA). The model decides whether a group can be used.
        var groups = machines.Where(m => !m.IsVirtual && m.GroupName is not null)
            .GroupBy(m => m.GroupName!).Where(g => g.Count() > 1).OrderBy(g => g.Key).ToList();
        if (groups.Count > 0)
        {
            sb.AppendLine("## Grupy maszyn (maszyny o tej samej nazwie stanowiska)");
            foreach (var g in groups) sb.AppendLine($"- {g.Key}: {string.Join(", ", g.Select(m => m.Id))}");
            sb.AppendLine("Dla każdej operacji: machine_id = najlepsza maszyna. Sprawdź każdą inną maszynę z jej grupy względem tego elementu");
            sb.AppendLine("(wymiary, przesuwy, osie, obroty, zakres, dokładność): pasują wszystkie -> wpisz je w alternative_machine_ids; pasują niektóre -> tylko te;");
            sb.AppendLine("żadna / operacja wymaga tej jednej maszyny -> pusta lista. Wykluczaj TYLKO z powodów technicznych (koszt i obłożenie to sprawa planisty).");
            sb.AppendLine("Pominięte maszyny z grupy wymień z krótkim powodem w comment operacji. Nigdy nie powtarzaj machine_id w alternative_machine_ids.");
            sb.AppendLine();
        }

        sb.AppendLine("## Materiał i wymiary");
        sb.AppendLine("material.grade = gatunek z rysunku (tabliczka / uwagi); material.stock = planowany półfabrykat (np. \"Pręt okrągły Ø45 x 184 mm\").");
        sb.AppendLine("dimensions.part = gabaryty gotowego elementu z rysunku (null, jeśli nie dotyczy); dimensions.stock = forma i wymiar półfabrykatu z naddatkiem.");
        sb.AppendLine("material.system_material_id = ID z listy poniżej dla planowanego półfabrykatu:");
        sb.AppendLine("- dopasuj GATUNEK także przez oznaczenia zamienne (EN / PN / W.Nr / AISI / nazwy handlowe, np. C45 = 1.0503 = STAL 45,");
        sb.AppendLine("  42CrMo4 = 40HM, S355J2 = 18G2A, S235JR = St3S, 1.2379 = NC11LV, EN AW-2017 = PA6, 1.4301 = 304), ignoruj literówki;");
        sb.AppendLine("- potem FORMĘ i WYMIAR: pozycja co najmniej tak duża jak półfabrykat, najmniejsza wystarczająca; pozycja bez wymiaru, jeśli brak lepszej;");
        sb.AppendLine("- pomijaj materiały klienta / powierzone / testowe; brak właściwego gatunku -> null i informacja w general_notes.");
        sb.AppendLine("Lista materiałów (ID | nazwa):");
        foreach (var m in materials.Where(m => m.Active).OrderBy(m => m.Name))
            sb.Append(m.Id).Append(" | ").AppendLine(m.Name);
        sb.AppendLine();

        sb.AppendLine("Przygotuj projekt technologii dla elementu z załączonego rysunku. Wszystkie pola tekstowe pisz po polsku.");
        return sb.ToString();
    }

    static JsonObject BuildDrawingPart(DrawingInput drawing)
    {
        var dataUrl = $"data:{drawing.ContentType};base64,{Convert.ToBase64String(drawing.Bytes)}";
        return drawing.ContentType == "application/pdf"
            ? new JsonObject { ["type"] = "input_file", ["filename"] = drawing.FileName, ["file_data"] = dataUrl }
            : new JsonObject { ["type"] = "input_image", ["image_url"] = dataUrl, ["detail"] = "high" };
    }

    /// <summary>Copy of the request for the debug log with the (large) base64 payload removed.</summary>
    public static JsonNode Redact(JsonObject body)
    {
        var copy = JsonNode.Parse(body.ToJsonString())!;
        foreach (var message in copy["input"]!.AsArray())
        {
            if (message?["content"] is not JsonArray parts) continue;
            foreach (var part in parts.OfType<JsonObject>())
                foreach (var field in new[] { "image_url", "file_data" })
                    if (part[field]?.GetValue<string>() is { } data)
                        part[field] = $"<base64 omitted, {data.Length} chars>";
        }
        return copy;
    }
}
