namespace PayerGatewayWebAPI.Models.Responses;

/// <summary>A configured payer, as shown by GET /api/v1/payers.</summary>
public class PayerSummary
{
    /// <summary>The value the EHR sends as payerName.</summary>
    /// <example>MockPayer</example>
    public string PayerName { get; set; } = string.Empty;

    /// <example>Mock Da Vinci Payer</example>
    public string DisplayName { get; set; } = string.Empty;

    /// <example>true</example>
    public bool Enabled { get; set; }

    /// <example>http://localhost:5080</example>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Forms = the EHR shows the questionnaire (POST /api/v1/dtr/forms); SmartApp = open the link of the CRD message,
    /// which starts the payer's own SMART DTR app.
    /// </summary>
    /// <example>Forms</example>
    public string DtrMode { get; set; } = "Forms";
}
