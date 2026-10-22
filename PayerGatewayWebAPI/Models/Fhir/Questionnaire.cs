using System.Text.Json;
using System.Text.Json.Serialization;

namespace PayerGatewayWebAPI.Models.Fhir;

// FHIR R4 cardinality of each property: Questionnaire (read from the payer), https://hl7.org/fhir/R4/questionnaire.html
// 1..1 / 1..* = required by FHIR; 0..1 / 0..* = optional (left out when there is no data).
/// <summary>
/// FHIR Questionnaire (DTR profile) as returned by the payer's $questionnaire-package.
/// Only the parts the gateway needs to show the questions and build the QuestionnaireResponse.
/// </summary>
public class Questionnaire
{
    public string ResourceType { get; set; } = "Questionnaire";
    // R4: Questionnaire.id 0..1 - optional
    public string? Id { get; set; }
    // R4: Questionnaire.url 0..1 - optional
    /// <summary>Canonical URL, e.g. http://payer.example.org/fhir/Questionnaire/dialysis-pa.</summary>
    public string? Url { get; set; }
    // R4: Questionnaire.version 0..1 - optional
    public string? Version { get; set; }
    // R4: Questionnaire.name 0..1 - optional
    public string? Name { get; set; }
    // R4: Questionnaire.title 0..1 - optional
    public string? Title { get; set; }
    // R4: Questionnaire.status 1..1 - required
    public string? Status { get; set; }
    // R4: Questionnaire.extension 0..* - optional
    /// <summary>Holds sdc-questionnaire-adaptive when the payer sends the questions one at a time ($next-question).</summary>
    public List<Extension> Extension { get; set; } = new();
    // R4: Questionnaire.item 0..* - optional
    public List<QuestionnaireItem> Item { get; set; } = new();

    /// <summary>
    /// Every other property of this element, kept exactly as it was read. The gateway does not use them, but they are
    /// written back when the element is sent on (to a payer or the FHIR API), so nothing the sender put in is lost.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? OtherProperties { get; set; }

    /// <summary>"url|version" when a version is known, else the url.</summary>
    public string? GetCanonical()
    {
        if (string.IsNullOrEmpty(Url))
        {
            return null;
        }
        if (string.IsNullOrEmpty(Version))
        {
            return Url;
        }
        return Url + "|" + Version;
    }
}
