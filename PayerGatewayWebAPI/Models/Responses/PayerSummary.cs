namespace PayerGatewayWebAPI.Models.Responses;

/// <summary>The configuration of one payer, as shown by GET /payers/{insurancePlanId}.</summary>
public class PayerSummary
{
    /// <summary>The EHR's insurance plan id of this payer (PA_PAYER.INSURANCE_PLAN_ID); the value the EHR sends as insurancePlanId.</summary>
    /// <example>1001</example>
    public int InsurancePlanId { get; set; }

    /// <summary>Name of the payer (shown in messages).</summary>
    /// <example>MockPayer</example>
    public string PayerName { get; set; } = string.Empty;

    /// <example>true</example>
    public bool Enabled { get; set; }

    /// <example>http://localhost:5080</example>
    public string BaseUrl { get; set; } = string.Empty;
}
