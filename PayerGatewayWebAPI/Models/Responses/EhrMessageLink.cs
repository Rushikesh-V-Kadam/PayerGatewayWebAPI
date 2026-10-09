namespace PayerGatewayWebAPI.Models.Responses;

/// <summary>
/// A link on a message. type "smart" = a payer DTR app: a plain gateway URL (/api/v1/dtr/launch/{linkId}) the EHR
/// opens in a browser; the gateway then starts the SMART app. type "absolute" = a normal web page.
/// </summary>
public class EhrMessageLink
{
    /// <summary>Text to show for the link.</summary>
    public string Label { get; set; } = string.Empty;
    /// <summary>Where the link goes. For a DTR launch this is a gateway URL: /api/v1/dtr/launch/{linkId}.</summary>
    public string Url { get; set; } = string.Empty;
    /// <summary>absolute | smart</summary>
    public string Type { get; set; } = "absolute";

    /// <summary>
    /// type "smart" only. true = the payer works with its own SMART DTR app (PA_PAYER.DTR_MODE = SmartApp): open Url in
    /// a browser window and the payer's app starts. false = the payer's questionnaire is filled in through the gateway
    /// forms (POST /api/v1/dtr/forms); Url still opens the app when the payer's card names one.
    /// </summary>
    /// <example>false</example>
    public bool OpensPayerApp { get; set; }
}
