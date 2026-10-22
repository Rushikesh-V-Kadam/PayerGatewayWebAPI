namespace PayerGatewayWebAPI.Models.Fhir;

// FHIR R4 cardinality of each property: DocumentReference.content (each entry), https://hl7.org/fhir/R4/documentreference.html
// 1..1 / 1..* = required by FHIR; 0..1 / 0..* = optional (left out when there is no data).
/// <summary>The file of a DocumentReference.</summary>
public class DocumentReferenceContent
{
    // R4: DocumentReference.content.attachment 1..1 - required
    public Attachment Attachment { get; set; } = new();
}
