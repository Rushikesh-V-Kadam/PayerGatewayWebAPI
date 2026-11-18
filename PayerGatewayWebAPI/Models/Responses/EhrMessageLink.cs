namespace PayerGatewayWebAPI.Models.Responses;

/// <summary>
/// A link on a message, exactly as the payer sent it on its CDS Hooks card (the gateway does not change it).
/// type "smart" = the payer's SMART DTR app; type "absolute" = a normal web page.
/// TODO: how the EHR launches a "smart" link (DTR flow) is not decided yet.
/// </summary>
public class EhrMessageLink
{
    /// <summary>Text to show for the link.</summary>
    public string Label { get; set; } = string.Empty;
    /// <summary>Where the link goes: the payer's URL (for a "smart" link, the payer's SMART DTR app launch URL).</summary>
    public string Url { get; set; } = string.Empty;
    /// <summary>absolute | smart (as sent by the payer)</summary>
    public string Type { get; set; } = "absolute";
    /// <summary>Optional. For a "smart" link: the payer's appContext, to be handed to the SMART DTR app at launch. Null when the payer sent none.</summary>
    public string? AppContext { get; set; }
}
