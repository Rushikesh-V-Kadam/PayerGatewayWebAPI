using System.Text.Json.Nodes;

namespace PayerGatewayWebAPI.Models.Fhir;

// FHIR R4 cardinality of each property: QuestionnaireResponse, https://hl7.org/fhir/R4/questionnaireresponse.html
// 1..1 / 1..* = required by FHIR; 0..1 / 0..* = optional (left out when there is no data).
/// <summary>
/// FHIR QuestionnaireResponse (DTR profile): the filled-in payer form.
/// Built by the gateway from the EHR's plain answers, saved at the FHIR API, and attached to PAS / CDex.
/// </summary>
public class QuestionnaireResponse
{
    public string ResourceType { get; set; } = "QuestionnaireResponse";
    // R4: QuestionnaireResponse.id 0..1 - optional
    public string? Id { get; set; }
    // R4: QuestionnaireResponse.meta 0..1 - optional
    public Meta? Meta { get; set; }
    // R4: QuestionnaireResponse.contained 0..* - optional
    /// <summary>Adaptive forms: the payer Questionnaire (questions asked so far) travels inside the response.</summary>
    public List<JsonObject> Contained { get; set; } = new();
    // R4: QuestionnaireResponse.extension 0..* - optional
    /// <summary>qr-context extensions: which order / coverage the form is for.</summary>
    public List<Extension> Extension { get; set; } = new();
    // R4: QuestionnaireResponse.questionnaire 0..1 - optional
    /// <summary>Canonical URL (and version) of the payer Questionnaire.</summary>
    public string? Questionnaire { get; set; }
    // R4: QuestionnaireResponse.status 1..1 - required
    /// <summary>in-progress | completed | amended</summary>
    public string? Status { get; set; }
    // R4: QuestionnaireResponse.subject 0..1 - optional
    public ResourceReference? Subject { get; set; }
    // R4: QuestionnaireResponse.authored 0..1 - optional
    public string? Authored { get; set; }
    // R4: QuestionnaireResponse.author 0..1 - optional
    public ResourceReference? Author { get; set; }
    // R4: QuestionnaireResponse.item 0..* - optional
    public List<QuestionnaireResponseItem> Item { get; set; } = new();
}
