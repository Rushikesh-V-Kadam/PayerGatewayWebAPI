using System.Text.Json.Serialization;

namespace PayerGatewayWebAPI.Models.Fhir;

// FHIR R4 cardinality of each property: Subscription.channel, https://hl7.org/fhir/R4/subscription.html
// 1..1 / 1..* = required by FHIR; 0..1 / 0..* = optional (left out when there is no data).
/// <summary>Where and how the payer sends notifications.</summary>
public class SubscriptionChannel
{
    // R4: Subscription.channel.type 1..1 - required
    public string Type { get; set; } = "rest-hook";
    // R4: Subscription.channel.endpoint 0..1 - optional
    public string? Endpoint { get; set; }
    // R4: Subscription.channel.payload 0..1 - optional
    public string? Payload { get; set; }

    // R4: Subscription.channel._payload (extensions on payload) 0..* - optional
    /// <summary>Holds backport-payload-content (id-only).</summary>
    [JsonPropertyName("_payload")]
    public PrimitiveExtensions? PayloadExtensions { get; set; }

    // R4: Subscription.channel.header 0..* - optional
    /// <summary>HTTP headers the payer adds to each notification (e.g. our shared secret).</summary>
    public List<string> Header { get; set; } = new();
}
