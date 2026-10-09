using PayerGatewayWebAPI.Constants;
using PayerGatewayWebAPI.Models.Responses;

namespace PayerGatewayWebAPI.Models.Entities;

/// <summary>One prior authorization request and its latest decision (table PA_PRIOR_AUTH).</summary>
public class PriorAuthRecord
{
    /// <summary>The EHR's request id (key).</summary>
    public string RequestId { get; set; } = string.Empty;
    /// <summary>Payer the request was sent to.</summary>
    public string PayerName { get; set; } = string.Empty;
    /// <summary>EHR patient id.</summary>
    public string PatientId { get; set; } = string.Empty;
    /// <summary>Insurance used for the request.</summary>
    public string CoverageId { get; set; } = string.Empty;
    /// <summary>Member id on that insurance, as it was sent to the payer.</summary>
    public string? MemberId { get; set; }
    /// <summary>Provider organization that asked for the authorization.</summary>
    public string OrganizationId { get; set; } = string.Empty;
    /// <summary>NPI of that organization.</summary>
    public string? OrganizationNpi { get; set; }
    /// <summary>Requesting clinician.</summary>
    public string? PractitionerId { get; set; }
    /// <summary>NPI of that clinician.</summary>
    public string? PractitionerNpi { get; set; }
    /// <summary>routine | urgent</summary>
    public string Priority { get; set; } = "routine";

    /// <summary>Overall status (copy of Decision.OverallStatus, kept as a column for queries).</summary>
    public string Status { get; set; } = DecisionStatus.Pended;
    /// <summary>Authorization number; filled when the payer approved.</summary>
    public string? AuthorizationNumber { get; set; }

    /// <summary>1 = first submission, +1 for every update / cancel.</summary>
    public int Version { get; set; }

    /// <summary>Orders currently in the request.</summary>
    public List<string> OrderIds { get; set; } = new();

    /// <summary>orderId -> Claim.item.sequence. Kept so an order keeps its line number on updates.</summary>
    public Dictionary<string, int> LineNumbers { get; set; } = new();

    /// <summary>Latest decision (returned to the EHR and stored in column DECISION_JSON, where the EHR reads it).</summary>
    public PriorAuthDecision Decision { get; set; } = new();

    /// <summary>Last PAS Bundle sent (needed to build $inquire).</summary>
    public string? LastRequestBundleJson { get; set; }

    /// <summary>Last payer response Bundle (for support).</summary>
    public string? LastResponseBundleJson { get; set; }

    /// <summary>When the request was first sent (UTC).</summary>
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    /// <summary>When the row was last saved (UTC). Saved only when something changed.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>True while the payer has not decided yet.</summary>
    public bool IsOpen()
    {
        return Status == DecisionStatus.Pended;
    }
}
