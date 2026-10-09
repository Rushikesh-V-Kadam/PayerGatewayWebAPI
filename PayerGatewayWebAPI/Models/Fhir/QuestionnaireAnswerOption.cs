using System.Text.Json;
using System.Text.Json.Serialization;

namespace PayerGatewayWebAPI.Models.Fhir;

/// <summary>One allowed answer of a choice question.</summary>
public class QuestionnaireAnswerOption
{
    public Coding? ValueCoding { get; set; }
    public string? ValueString { get; set; }
    public int? ValueInteger { get; set; }
    public string? ValueDate { get; set; }
    public bool? InitialSelected { get; set; }

    /// <summary>
    /// Every other property of this element, kept exactly as it was read. The gateway does not use them, but they are
    /// written back when the element is sent on (to a payer or the FHIR API), so nothing the sender put in is lost.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? OtherProperties { get; set; }
}
