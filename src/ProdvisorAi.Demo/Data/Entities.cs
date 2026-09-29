namespace ProdvisorAi.Demo.Data;

/// <summary>A machine / workstation available at the customer's shop floor.</summary>
public class Machine
{
    public string Id { get; set; } = "";          // e.g. "M01" - this is what the model must return in machine_id
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";        // e.g. "CNC lathe", "Band saw", "QC"
    public string Description { get; set; } = ""; // capabilities: working range, axes, max diameter, ...
    public double HourlyRate { get; set; }
    public bool Active { get; set; } = true;
}

/// <summary>Editable prompt text. Id is "{kind}.{language}", e.g. "system.en" or "technology.pl".</summary>
public class PromptTemplate
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Simple key/value settings store. Secrets are stored encrypted (see SecretProtector).</summary>
public class AppSetting
{
    public string Key { get; set; } = "";
    public string? Value { get; set; }
}

public static class JobStatus
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Done = "done";
    public const string Failed = "failed";
}

/// <summary>One "generate technology" request and its (draft) result.</summary>
public class GenerationJob
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = JobStatus.Pending;

    // Input
    public string? PartNumber { get; set; }
    public string? Material { get; set; }
    public int? BatchSize { get; set; }
    public string DrawingFileName { get; set; } = "";
    public string DrawingContentType { get; set; } = "";
    public string DrawingPath { get; set; } = "";   // relative to the data directory
    public string PromptLanguage { get; set; } = "pl";
    public string? RegeneratedFromId { get; set; }

    // Execution
    public string? Model { get; set; }
    public bool Mock { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public long? DurationMs { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public int? TotalTokens { get; set; }
    public string? Error { get; set; }
    public string? ErrorKind { get; set; }

    // Result (draft technology as JSON matching the production_technology schema)
    public string? ResultJson { get; set; }
    public bool Edited { get; set; }
    public DateTime? ApprovedAt { get; set; }

    // Debug (request is stored WITHOUT image/PDF base64; the API key is never part of the body)
    public string? DebugRequestJson { get; set; }
    public string? DebugResponseJson { get; set; }
}
