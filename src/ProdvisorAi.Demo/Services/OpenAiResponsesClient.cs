using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace ProdvisorAi.Demo.Services;

public sealed record OpenAiCredentials(string ApiKey, string? Organization, string? Project);

/// <summary>
/// Minimal client for the OpenAI Responses API (POST /v1/responses) using plain HttpClient.
/// The API key is only ever placed in the Authorization header of the outgoing request.
/// </summary>
public sealed class OpenAiResponsesClient(HttpClient http, ILogger<OpenAiResponsesClient> log)
{
    /// <summary>Sends a request body to /responses. One automatic retry on 5xx, network errors and timeouts.</summary>
    public async Task<JsonNode> CreateResponseAsync(JsonObject body, OpenAiCredentials creds, CancellationToken ct)
    {
        var json = body.ToJsonString();
        const int maxAttempts = 2;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var request = BuildRequest(HttpMethod.Post, "responses", creds);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                using var response = await http.SendAsync(request, ct);
                var text = await response.Content.ReadAsStringAsync(ct);

                if (response.IsSuccessStatusCode)
                    return JsonNode.Parse(text) ?? throw new AiServiceException("bad_response", "Empty response from OpenAI.");

                var error = MapError(response.StatusCode, text);
                if ((int)response.StatusCode >= 500 && attempt < maxAttempts)
                {
                    log.LogWarning("OpenAI returned {Status}, retrying (attempt {Attempt})", (int)response.StatusCode, attempt);
                    await Task.Delay(TimeSpan.FromSeconds(2), ct);
                    continue;
                }
                throw error;
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                // HttpClient.Timeout elapsed (not a shutdown).
                if (attempt < maxAttempts) { log.LogWarning("OpenAI request timed out, retrying"); continue; }
                throw new AiServiceException("timeout", $"OpenAI did not respond within {http.Timeout.TotalSeconds:0} s.");
            }
            catch (HttpRequestException ex)
            {
                if (attempt < maxAttempts) { log.LogWarning("Network error calling OpenAI ({Error}), retrying", ex.Message); continue; }
                throw new AiServiceException("network", $"Cannot reach the OpenAI API: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// "Test connection": a tiny Responses call. This checks the key, the model access AND billing/credit
    /// (a GET /models call would succeed even with no credit left). Costs a handful of tokens.
    /// </summary>
    public async Task<string> TestAsync(OpenAiCredentials creds, string model, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["model"] = model,
            ["input"] = "Reply with the single word OK.",
            ["max_output_tokens"] = 16,
            ["store"] = false,
        };
        try
        {
            using var request = BuildRequest(HttpMethod.Post, "responses", creds);
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                throw MapError(response.StatusCode, await response.Content.ReadAsStringAsync(ct));
            return $"Connection OK - model \"{model}\" is available for this key.";
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new AiServiceException("timeout", "OpenAI did not respond in time.");
        }
        catch (HttpRequestException ex)
        {
            throw new AiServiceException("network", $"Cannot reach the OpenAI API: {ex.Message}");
        }
    }

    HttpRequestMessage BuildRequest(HttpMethod method, string path, OpenAiCredentials creds)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", creds.ApiKey);
        if (!string.IsNullOrWhiteSpace(creds.Organization)) request.Headers.Add("OpenAI-Organization", creds.Organization);
        if (!string.IsNullOrWhiteSpace(creds.Project)) request.Headers.Add("OpenAI-Project", creds.Project);
        return request;
    }

    /// <summary>Turns an OpenAI error response ({"error":{"message","type","code"}}) into a user-friendly exception.</summary>
    static AiServiceException MapError(HttpStatusCode status, string body)
    {
        string? apiMessage = null, apiCode = null;
        try
        {
            var err = JsonNode.Parse(body)?["error"];
            apiMessage = err?["message"]?.GetValue<string>();
            apiCode = err?["code"]?.GetValue<string>();
        }
        catch { /* not JSON */ }

        var detail = apiMessage is null ? "" : $" OpenAI: {apiMessage}";
        var code = (int)status;
        return status switch
        {
            HttpStatusCode.Unauthorized => new("unauthorized", $"Invalid API key (401). Check the key in Settings.{detail}", code),
            HttpStatusCode.Forbidden => new("forbidden", $"Access denied (403). The key/project may not have access to this model or region.{detail}", code),
            HttpStatusCode.NotFound => new("model_not_found", $"Model not found or not available for this project (404).{detail}", code),
            HttpStatusCode.TooManyRequests when apiCode == "insufficient_quota"
                => new("quota", $"No credit / quota exceeded (429). Add billing credit to the OpenAI project.{detail}", code),
            HttpStatusCode.TooManyRequests => new("rate_limit", $"Rate limit reached (429). Wait a moment and try again.{detail}", code),
            HttpStatusCode.BadRequest => new("bad_request", $"OpenAI rejected the request (400).{detail}", code),
            _ when code >= 500 => new("server_error", $"OpenAI server error ({code}). Try again later.{detail}", code),
            _ => new("http_error", $"Unexpected response from OpenAI ({code}).{detail}", code),
        };
    }
}
