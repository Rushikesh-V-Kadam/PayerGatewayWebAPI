namespace PayerGatewayWebAPI.Models.Fhir;

/// <summary>A processing error on the request or on one item (ClaimResponse.error).</summary>
public class ClaimResponseError
{
    /// <summary>PAS error extensions: errorElement (X12 element), errorPath (place in the request), errorFollowupAction.</summary>
    public List<Extension> Extension { get; set; } = new();

    /// <summary>The Claim.item the error is about; empty = the whole request.</summary>
    public int? ItemSequence { get; set; }

    /// <summary>The error code (X12 reject reason), often without any text.</summary>
    public CodeableConcept Code { get; set; } = new();
}
