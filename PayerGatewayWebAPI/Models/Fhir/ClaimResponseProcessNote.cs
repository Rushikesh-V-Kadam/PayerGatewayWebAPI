namespace PayerGatewayWebAPI.Models.Fhir;

// FHIR R4 cardinality of each property: ClaimResponse.processNote (each entry), https://hl7.org/fhir/R4/claimresponse.html
// 1..1 / 1..* = required by FHIR; 0..1 / 0..* = optional (left out when there is no data).
/// <summary>A free-text note from the payer.</summary>
public class ClaimResponseProcessNote
{
    // R4: ClaimResponse.processNote.number 0..1 - optional
    public int? Number { get; set; }
    // R4: ClaimResponse.processNote.text 1..1 - required
    public string? Text { get; set; }
}
