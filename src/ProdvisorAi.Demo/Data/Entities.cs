namespace ProdvisorAi.Demo.Data;

// ============================================================================================
// Service data
// ============================================================================================

/// <summary>A machine / workstation from PRODVISOR, as used by the service (local copy, see ProdvisorImporter).</summary>
public class Machine
{
    public string Id { get; set; } = "";          // machine code, e.g. "0401" (NR_MASZYNY) or "K38" for cooperation - returned by the model as machine_id
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";        // MASZYNY_TYP.TYP_MASZYNY; "KOOPERACJA" for virtual machines
    public string Description { get; set; } = ""; // local note (not in PRODVISOR)
    public double HourlyRate { get; set; }        // STANOWISKO.KOSZT_ROBOCZOGODZINY (not sent to the model)
    public bool Active { get; set; } = true;      // offered to the model or not

    public int? ProdvisorId { get; set; }         // MASZYNY.ID
    public int? TypeId { get; set; }              // MASZYNY.ID_TYP_MASZYNY
    public int? Kind { get; set; }                // MASZYNY.ID_RODZAJ_MASZYNY
    public bool IsVirtual { get; set; }           // MASZYNY.VirtualMachine = 1: external cooperation
    public string? ParametersJson { get; set; }   // MASZYNY_PARAMETRY as [{"name","value"}]

    public int? WorkplaceId { get; set; }         // STANOWISKO.ID
    public string? GroupCode { get; set; }        // STANOWISKO.AuxiliaryNo (informational)
    public int? GroupId { get; set; }             // STANOWISKO.ID_NAZWA_STANOWISKA - machine GROUP
    public string? GroupName { get; set; }        // STANOWISKO_NAZWA.NAZWA_STANOWISKA, e.g. "Frezarki CNC małe"
    public int? Hall { get; set; }                // STANOWISKO.NR_HALI
    public bool MultiOperation { get; set; }
    public bool Multiperson { get; set; }
    public int? WorkplacesGroup { get; set; }
}

/// <summary>Material kind from PRODVISOR (dbo.MaterialsKind).</summary>
public class ProdvisorMaterial
{
    public int Id { get; set; }                  // MaterialsKind.ID
    public string Name { get; set; } = "";       // e.g. "Pręt 50 gat. C45"
    public int? AlloyId { get; set; }
    public bool Active { get; set; } = true;     // NoActive != 1 and not a pseudo-material
}

/// <summary>System prompt profile - built into the service, selectable by the client per request.</summary>
public class SystemPrompt
{
    public string Id { get; set; } = "";         // e.g. "cnc"
    public string Name { get; set; } = "";       // shown to the technologist, e.g. "Obróbka skrawaniem CNC"
    public string Text { get; set; } = "";
    public bool IsDefault { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Key/value settings. Secrets are stored encrypted (see SecretProtector).</summary>
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

/// <summary>One "generate technology" request from the client and its result.</summary>
public class GenerationJob
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = JobStatus.Pending;

    // Request (from the desktop application)
    public string? ElementId { get; set; }
    public string? TechnologyPrompt { get; set; }
    public string? SystemPromptId { get; set; }
    public string DrawingFileName { get; set; } = "";
    public string DrawingContentType { get; set; } = "";
    public string DrawingPath { get; set; } = "";   // relative to the data directory (for DXF: the rendered PNG)
    public string? SourceDrawingPath { get; set; }  // original DXF, if converted
    public string? DrawingText { get; set; }        // exact dimensions/texts extracted from the DXF

    // Element data read from PRODVISOR by ElementId (snapshot at the time of the request)
    public string? PartNumber { get; set; }
    public string? PartName { get; set; }
    public string? Material { get; set; }
    public int? BatchSize { get; set; }

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
    public string? ResultJson { get; set; }         // model output (production_technology schema)

    // Debug (request without base64; the API key is never part of the body)
    public string? DebugRequestJson { get; set; }
    public string? DebugResponseJson { get; set; }

    // Columns of earlier demo versions, kept so existing databases stay compatible
    public string PromptLanguage { get; set; } = "pl";
    public bool Edited { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? RegeneratedFromId { get; set; }
}

// ============================================================================================
// Simulated PRODVISOR side (stands in for the customer's database / desktop application)
// ============================================================================================

/// <summary>An element (part) in PRODVISOR with the drawing uploaded by the designer. Looked up by element_id.</summary>
public class ProdvisorElement
{
    public string Id { get; set; } = "";         // element_id
    public string Number { get; set; } = "";     // drawing / part number
    public string Name { get; set; } = "";
    public string? Material { get; set; }
    public int? Quantity { get; set; }
    public string DrawingFile { get; set; } = ""; // file name in the samples folder
}

/// <summary>Technology approved in the desktop application and saved to PRODVISOR (simulation).</summary>
public class SavedTechnology
{
    public string ElementId { get; set; } = "";
    public string JobId { get; set; } = "";
    public DateTime SavedAt { get; set; } = DateTime.UtcNow;
    public string Json { get; set; } = "";       // the technology card as saved by the desktop application
}
