using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ProdvisorAi.Demo.Models;

namespace ProdvisorAi.Demo.Services;

public sealed record TokenUsage(int? InputTokens, int? OutputTokens, int? TotalTokens);

/// <summary>
/// Extracts the structured result from a Responses API response object:
///   { status, error, incomplete_details: { reason }, output: [ { type: "message", content: [ {type:"output_text", text} | {type:"refusal", refusal} ] } ], usage }
/// </summary>
public static class ResponseParser
{
    public static TokenUsage ReadUsage(JsonNode response)
    {
        var usage = response["usage"];
        return new TokenUsage(
            usage?["input_tokens"]?.GetValue<int>(),
            usage?["output_tokens"]?.GetValue<int>(),
            usage?["total_tokens"]?.GetValue<int>());
    }

    public static TechnologyResult ParseTechnology(JsonNode response)
    {
        var status = response["status"]?.GetValue<string>();

        if (status == "failed")
            throw new AiServiceException("failed", $"OpenAI reported a failure: {response["error"]?["message"]?.GetValue<string>() ?? "unknown error"}");

        if (status == "incomplete")
        {
            var reason = response["incomplete_details"]?["reason"]?.GetValue<string>() ?? "unknown";
            throw new AiServiceException("incomplete", reason == "max_output_tokens"
                ? "The answer was cut off (max_output_tokens reached). Increase OpenAI:MaxOutputTokens or simplify the prompt."
                : $"The answer is incomplete (reason: {reason}).");
        }

        // Collect the text / refusal from all message items (reasoning items etc. are skipped).
        var text = new StringBuilder();
        string? refusal = null;
        foreach (var item in response["output"]?.AsArray() ?? [])
        {
            if (item?["type"]?.GetValue<string>() != "message") continue;
            foreach (var part in item["content"]?.AsArray() ?? [])
            {
                switch (part?["type"]?.GetValue<string>())
                {
                    case "output_text": text.Append(part["text"]?.GetValue<string>()); break;
                    case "refusal": refusal = part["refusal"]?.GetValue<string>(); break;
                }
            }
        }

        if (refusal is not null)
            throw new AiServiceException("refusal", $"The model refused to answer: {refusal}");
        if (text.Length == 0)
            throw new AiServiceException("bad_response", "The response contained no output text.");

        return Deserialize(text.ToString());
    }

    public static TechnologyResult Deserialize(string json)
    {
        try
        {
            var result = JsonSerializer.Deserialize<TechnologyResult>(json);
            if (result is null)
                throw new AiServiceException("bad_response", "The response JSON is empty.");
            // "konstruktor" mode has no operations
            return result with { Operations = result.Operations ?? [], GeneralNotes = result.GeneralNotes ?? "" };
        }
        catch (JsonException ex)
        {
            throw new AiServiceException("bad_response", $"The response is not valid technology JSON: {ex.Message}");
        }
    }
}
