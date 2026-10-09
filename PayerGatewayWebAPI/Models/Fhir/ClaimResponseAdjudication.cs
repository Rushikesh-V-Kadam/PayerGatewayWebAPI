namespace PayerGatewayWebAPI.Models.Fhir;

/// <summary>
/// ClaimResponse.item.adjudication. PAS 2.0.1 puts the decision of a line here, in the reviewAction extension
/// (X12 action code, authorization number, reason).
/// </summary>
public class ClaimResponseAdjudication
{
    /// <summary>Holds the PAS reviewAction extension.</summary>
    public List<Extension> Extension { get; set; } = new();

    /// <summary>Kind of adjudication; PAS uses "submitted".</summary>
    public CodeableConcept? Category { get; set; }
}
