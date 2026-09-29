namespace ProdvisorAi.Demo.Data;

/// <summary>A machine / workstation available at the customer's shop floor.</summary>
public class Machine
{
    public string Id { get; set; } = "";          // machine code, e.g. "0401" (NR_MASZYNY) or "K38" for cooperation - returned by the model as machine_id
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";        // MASZYNY_TYP.TYP_MASZYNY, e.g. "TOKARKA CNC"; "KOOPERACJA" for virtual machines
    public string Description { get; set; } = ""; // local note about capabilities (not in PRODVISOR)
    public double HourlyRate { get; set; }        // local (not in PRODVISOR)
    public bool Active { get; set; } = true;      // local: offered to the model or not

    // Link to PRODVISOR (dbo.MASZYNY). Filled by the importer; the local fields above are kept on re-sync.
    public int? ProdvisorId { get; set; }         // MASZYNY.ID - used when the approved routing is sent to PRODVISOR
    public int? TypeId { get; set; }              // MASZYNY.ID_TYP_MASZYNY
    public int? Kind { get; set; }                // MASZYNY.ID_RODZAJ_MASZYNY
    public bool IsVirtual { get; set; }           // MASZYNY.VirtualMachine = 1: external cooperation (heat treatment, coatings, ...)
    public string? ParametersJson { get; set; }   // MASZYNY_PARAMETRY as [{"name","value"}], e.g. "Długość stołu roboczego" = "1200 mm"

    // dbo.STANOWISKO (workplace of this machine)
    public int? WorkplaceId { get; set; }         // STANOWISKO.ID - PRODVISOR routings reference workplaces
    public string? GroupCode { get; set; }        // STANOWISKO.AuxiliaryNo (auxiliary number, informational)
    public int? GroupId { get; set; }             // STANOWISKO.ID_NAZWA_STANOWISKA - machine GROUP (same workplace name)
    public string? GroupName { get; set; }        // STANOWISKO_NAZWA.NAZWA_STANOWISKA, e.g. "Frezarki CNC małe"
    public int? Hall { get; set; }                // STANOWISKO.NR_HALI
    public bool MultiOperation { get; set; }      // STANOWISKO.MultiOperation
    public bool Multiperson { get; set; }         // STANOWISKO.Multiperson
    public int? WorkplacesGroup { get; set; }     // STANOWISKO.WorkplacesGroup (raw value)
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
    public string DrawingPath { get; set; } = "";   // relative to the data directory (for DXF: the rendered PNG)
    public string? SourceDrawingPath { get; set; }  // original CAD file (DXF), if the drawing was converted
    public string? DrawingText { get; set; }        // exact dimensions/texts extracted from the DXF, sent to the model
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

/// <summary>Material kind from PRODVISOR (dbo.MaterialsKind), cached locally for matching.</summary>
public class ProdvisorMaterial
{
    public int Id { get; set; }                  // MaterialsKind.ID
    public string Name { get; set; } = "";       // e.g. "Pręt 50 gat. C45"
    public int? AlloyId { get; set; }
    public bool Active { get; set; } = true;     // NoActive != 1 and not a pseudo-material (USŁUGA, ZŁOŻENIE, test rows, ...)
}
