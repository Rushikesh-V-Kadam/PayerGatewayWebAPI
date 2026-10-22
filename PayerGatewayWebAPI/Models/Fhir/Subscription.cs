using System.Text.Json.Serialization;

namespace PayerGatewayWebAPI.Models.Fhir;

// FHIR R4 cardinality of each property: Subscription, https://hl7.org/fhir/R4/subscription.html
// 1..1 / 1..* = required by FHIR; 0..1 / 0..* = optional (left out when there is no data).
/// <summary>
/// FHIR R4 Subscription (Subscriptions R5 Backport, PAS profile-subscription): asks the payer to notify the gateway
/// when a pended decision changes.
/// </summary>
public class Subscription
{
    public string ResourceType { get; set; } = "Subscription";
    // R4: Subscription.id 0..1 - optional
    public string? Id { get; set; }
    // R4: Subscription.meta 0..1 - optional
    public Meta? Meta { get; set; }
    // R4: Subscription.status 1..1 - required
    public string Status { get; set; } = "requested";
    // R4: Subscription.end 0..1 - optional
    public string? End { get; set; }
    // R4: Subscription.reason 1..1 - required
    public string Reason { get; set; } = string.Empty;
    // R4: Subscription.criteria 1..1 - required
    /// <summary>The PAS SubscriptionTopic canonical.</summary>
    public string Criteria { get; set; } = string.Empty;

    // R4: Subscription._criteria (extensions on criteria) 0..* - optional
    /// <summary>Holds backport-filter-criteria (which organization's ClaimResponses).</summary>
    [JsonPropertyName("_criteria")]
    public PrimitiveExtensions? CriteriaExtensions { get; set; }

    // R4: Subscription.channel 1..1 - required
    public SubscriptionChannel Channel { get; set; } = new();
}
