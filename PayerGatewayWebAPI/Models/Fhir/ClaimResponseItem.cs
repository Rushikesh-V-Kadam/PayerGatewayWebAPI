namespace PayerGatewayWebAPI.Models.Fhir;

/// <summary>
/// Decision for one Claim.item. PAS 2.0.1: the decision itself (reviewAction) is an extension of
/// item.adjudication; the authorized period and the authorized detail are extensions of the item.
/// </summary>
public class ClaimResponseItem
{
    /// <summary>Item extensions: itemPreAuthPeriod, itemAuthorizedDetail ...</summary>
    public List<Extension> Extension { get; set; } = new();

    /// <summary>Number of the Claim.item this decision is for.</summary>
    public int ItemSequence { get; set; }

    /// <summary>Numbers of the payer notes (ClaimResponse.processNote) about this line.</summary>
    public List<int> NoteNumber { get; set; } = new();

    /// <summary>Adjudications of the line; the PAS reviewAction extension is on one of them.</summary>
    public List<ClaimResponseAdjudication> Adjudication { get; set; } = new();
}
