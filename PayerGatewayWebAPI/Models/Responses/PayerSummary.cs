namespace PayerGatewayWebAPI.Models.Responses;

/// <summary>The configuration of one payer, as shown by GET /payers/{piInsurancePlanId}.</summary>
public class PayerSummary
{
    /// <summary>The EHR's insurance plan id of this payer (PA_PAYER.PI_INSURANCE_PLAN_ID); the value the EHR sends as piInsurancePlanId.</summary>
    /// <example>1001</example>
    public int PiInsurancePlanId { get; set; }

    /// <summary>Name of the payer (shown in messages).</summary>
    /// <example>MockPayer</example>
    public string PayerName { get; set; } = string.Empty;

    /// <example>true</example>
    public bool Enabled { get; set; }

    /// <example>http://localhost:5080</example>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Forms = the EHR shows the questionnaire (POST /dtr/forms); SmartApp = open the link of the CRD message,
    /// which starts the payer's own SMART DTR app.
    /// </summary>
    /// <example>Forms</example>
    public string DtrMode { get; set; } = "Forms";
}
