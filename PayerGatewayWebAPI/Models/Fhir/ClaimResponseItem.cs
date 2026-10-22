namespace PayerGatewayWebAPI.Models.Fhir;

// FHIR R4 cardinality of each property: ClaimResponse.item (each line), https://hl7.org/fhir/R4/claimresponse.html
// 1..1 / 1..* = required by FHIR; 0..1 / 0..* = optional (left out when there is no data).
/// <summary>
/// Decision for one Claim.item. PAS 2.0.1: the decision itself (reviewAction) is an extension of
/// item.adjudication; the authorized period and the authorized detail are extensions of the item.
/// </summary>
public class ClaimResponseItem
{
    // R4: ClaimResponse.item.extension 0..* - optional
    /// <summary>Item extensions: itemPreAuthPeriod, itemAuthorizedDetail ...</summary>
    public List<Extension> Extension { get; set; } = new();

    // R4: ClaimResponse.item.itemSequence 1..1 - required
    /// <summary>Number of the Claim.item this decision is for.</summary>
    public int ItemSequence { get; set; }

    // R4: ClaimResponse.item.noteNumber 0..* - optional
    /// <summary>Numbers of the payer notes (ClaimResponse.processNote) about this line.</summary>
    public List<int> NoteNumber { get; set; } = new();

    // R4: ClaimResponse.item.adjudication 1..* - required
    /// <summary>Adjudications of the line; the PAS reviewAction extension is on one of them.</summary>
    public List<ClaimResponseAdjudication> Adjudication { get; set; } = new();
}
