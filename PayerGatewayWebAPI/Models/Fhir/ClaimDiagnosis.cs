namespace PayerGatewayWebAPI.Models.Fhir;

// FHIR R4 cardinality of each property: Claim.diagnosis (each entry), https://hl7.org/fhir/R4/claim.html
// 1..1 / 1..* = required by FHIR; 0..1 / 0..* = optional (left out when there is no data).
/// <summary>Claim.diagnosis: an ICD-10 code taken from the orders.</summary>
public class ClaimDiagnosis
{
    // R4: Claim.diagnosis.sequence 1..1 - required
    public int Sequence { get; set; }
    // R4: Claim.diagnosis.diagnosis[x] 1..1 - required
    public CodeableConcept? DiagnosisCodeableConcept { get; set; }
}
