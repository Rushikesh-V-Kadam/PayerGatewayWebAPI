namespace PayerGatewayWebAPI.Models.Fhir;

// FHIR R4 cardinality of each property: DocumentReference (only the elements the gateway reads), https://hl7.org/fhir/R4/documentreference.html
// 1..1 / 1..* = required by FHIR; 0..1 / 0..* = optional (left out when there is no data).
/// <summary>Typed view of a DocumentReference (type and file location).</summary>
public class DocumentReference
{
    public string ResourceType { get; set; } = "DocumentReference";
    // R4: DocumentReference.id 0..1 - optional
    public string? Id { get; set; }
    // R4: DocumentReference.type 0..1 - optional
    public CodeableConcept? Type { get; set; }
    // R4: DocumentReference.date 0..1 - optional
    public string? Date { get; set; }
    // R4: DocumentReference.content 1..* - required
    public List<DocumentReferenceContent> Content { get; set; } = new();
}
