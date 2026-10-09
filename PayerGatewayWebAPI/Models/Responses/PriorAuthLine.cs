namespace PayerGatewayWebAPI.Models.Responses;

/// <summary>Decision for one claim line (one order).</summary>
public class PriorAuthLine
{
    /// <summary>Claim.item.sequence. Stays the same for an order across updates.</summary>
    /// <example>1</example>
    public int Sequence { get; set; }

    /// <example>ord-1-hd</example>
    public string? OrderId { get; set; }

    /// <example>90935</example>
    public string? Code { get; set; }

    /// <example>http://www.ama-assn.org/go/cpt</example>
    public string? CodeSystem { get; set; }

    /// <summary>approved | partially-approved | denied | pended | not-required | cancelled | contact-payer | modified</summary>
    /// <example>pended</example>
    public string Decision { get; set; } = "pended";

    /// <summary>X12 306 code from the payer: A1, A2, A3, A4, A6, C, CT, NA.</summary>
    /// <example>A4</example>
    public string? X12ActionCode { get; set; }

    /// <summary>Units asked for on this line.</summary>
    public decimal? RequestedQuantity { get; set; }
    /// <summary>Units the payer approved, when it said so.</summary>
    public decimal? ApprovedQuantity { get; set; }
    /// <summary>The payer's reason or note for this line.</summary>
    public string? Reason { get; set; }
    /// <summary>First day the line is authorized (yyyy-MM-dd).</summary>
    public string? ValidFrom { get; set; }
    /// <summary>Last day the line is authorized (yyyy-MM-dd).</summary>
    public string? ValidTo { get; set; }
}
