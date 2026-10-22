namespace PayerGatewayWebAPI.Models.Fhir;

// FHIR R4 cardinality of each property: Attachment (data type), https://hl7.org/fhir/R4/datatypes.html#Attachment
// 1..1 / 1..* = required by FHIR; 0..1 / 0..* = optional (left out when there is no data).
/// <summary>FHIR Attachment: a file, embedded (data) or linked (url).</summary>
public class Attachment
{
    // R4: Attachment.contentType 0..1 - optional
    public string? ContentType { get; set; }
    // R4: Attachment.data 0..1 - optional
    public string? Data { get; set; }
    // R4: Attachment.url 0..1 - optional
    public string? Url { get; set; }
    // R4: Attachment.title 0..1 - optional
    public string? Title { get; set; }
}
