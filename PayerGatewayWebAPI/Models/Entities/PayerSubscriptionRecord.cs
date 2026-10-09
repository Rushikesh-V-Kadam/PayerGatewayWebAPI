namespace PayerGatewayWebAPI.Models.Entities;

/// <summary>The PAS Subscription created at a payer for one provider organization (table PA_PAYER_SUBSCRIPTION).</summary>
public class PayerSubscriptionRecord
{
    /// <summary>Payer the Subscription was created at (key, part 1).</summary>
    public string PayerName { get; set; } = string.Empty;
    /// <summary>NPI of the provider organization (key, part 2).</summary>
    public string OrganizationNpi { get; set; } = string.Empty;
    /// <summary>The payer's id of the Subscription.</summary>
    public string? SubscriptionId { get; set; }
    /// <summary>active | error</summary>
    public string Status { get; set; } = "active";
    /// <summary>When the Subscription was created (UTC).</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
