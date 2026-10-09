using System.Text.Json.Nodes;
using PayerGatewayWebAPI.Configuration;
using PayerGatewayWebAPI.Models.Fhir;

namespace PayerGatewayWebAPI.Services.Pas;

/// <summary>Everything PasBundleBuilder needs for one PAS request Bundle (all read by PasService beforehand).</summary>
public class PasBuildInput
{
    /// <summary>The EHR's request id (becomes Claim.identifier).</summary>
    public string RequestId { get; set; } = string.Empty;
    /// <summary>1 = first submission; higher = update / cancel (uses the Claim Update profile).</summary>
    public int Version { get; set; } = 1;
    /// <summary>routine | urgent</summary>
    public string Priority { get; set; } = "routine";
    /// <summary>The payer the Bundle is for.</summary>
    public PayerSettings Payer { get; set; } = new PayerSettings();

    /// <summary>The patient (raw FHIR Patient from the FHIR API).</summary>
    public JsonObject Patient { get; set; } = new JsonObject();
    /// <summary>The patient's insurance (FHIR Coverage from the FHIR API); the member id and payer id come from it.</summary>
    public Coverage Coverage { get; set; } = new Coverage();
    /// <summary>Requesting provider organization (Claim.provider).</summary>
    public JsonObject Organization { get; set; } = new JsonObject();
    /// <summary>Requesting clinician (raw FHIR Practitioner), when known.</summary>
    public JsonObject? Practitioner { get; set; }

    /// <summary>Orders requested now (active lines).</summary>
    public List<JsonObject> Orders { get; set; } = new List<JsonObject>();
    /// <summary>Orders removed from the request (lines sent as cancelled).</summary>
    public List<JsonObject> CancelledOrders { get; set; } = new List<JsonObject>();
    /// <summary>Order ids of the previous version (new orders are marked "added").</summary>
    public List<string> PreviousOrderIds { get; set; } = new List<string>();
    /// <summary>orderId -> claim line number.</summary>
    public Dictionary<string, int> LineNumbers { get; set; } = new Dictionary<string, int>();

    /// <summary>DTR forms (raw QuestionnaireResponse JSON).</summary>
    public List<JsonObject> Forms { get; set; } = new List<JsonObject>();
    /// <summary>Documents with their file embedded (raw DocumentReference JSON).</summary>
    public List<JsonObject> Documents { get; set; } = new List<JsonObject>();
    /// <summary>Free-text note for the reviewer.</summary>
    public string? Notes { get; set; }

    /// <summary>new | renewal</summary>
    public string RequestType { get; set; } = "new";
    /// <summary>Renewal: the authorization number being renewed.</summary>
    public string? PreviousAuthorizationNumber { get; set; }
    /// <summary>Renewal: first date of service of the previous authorization (yyyy-MM-dd).</summary>
    public string? PreviousServiceStart { get; set; }
    /// <summary>Renewal: last date of service of the previous authorization (yyyy-MM-dd).</summary>
    public string? PreviousServiceEnd { get; set; }
    /// <summary>Renewal: number of treatments given under the previous authorization.</summary>
    public int? PreviousTreatments { get; set; }
    /// <summary>permanent | transient</summary>
    public string PatientType { get; set; } = "permanent";
    /// <summary>Referring physician (Claim.careTeam sequence 2), read from the FHIR API.</summary>
    public JsonObject? ReferringPractitioner { get; set; }
}
