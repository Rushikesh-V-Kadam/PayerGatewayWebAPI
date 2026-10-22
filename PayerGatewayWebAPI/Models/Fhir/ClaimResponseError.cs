namespace PayerGatewayWebAPI.Models.Fhir;

// FHIR R4 cardinality of each property: ClaimResponse.error (each entry), https://hl7.org/fhir/R4/claimresponse.html
// 1..1 / 1..* = required by FHIR; 0..1 / 0..* = optional (left out when there is no data).
/// <summary>A processing error on the request or on one item (ClaimResponse.error).</summary>
public class ClaimResponseError
{
    // R4: ClaimResponse.error.extension 0..* - optional
    /// <summary>PAS error extensions: errorElement (X12 element), errorPath (place in the request), errorFollowupAction.</summary>
    public List<Extension> Extension { get; set; } = new();

    // R4: ClaimResponse.error.itemSequence 0..1 - optional
    /// <summary>The Claim.item the error is about; empty = the whole request.</summary>
    public int? ItemSequence { get; set; }

    // R4: ClaimResponse.error.code 1..1 - required
    /// <summary>The error code (X12 reject reason), often without any text.</summary>
    public CodeableConcept Code { get; set; } = new();
}
