using System.Text.Json;
using System.Text.Json.Serialization;

namespace PayerGatewayWebAPI.Models.Fhir;

// FHIR R4 cardinality of each property: CodeableConcept (data type), https://hl7.org/fhir/R4/datatypes.html#CodeableConcept
// 1..1 / 1..* = required by FHIR; 0..1 / 0..* = optional (left out when there is no data).
/// <summary>FHIR CodeableConcept: one or more codings plus optional text.</summary>
public class CodeableConcept
{
    // R4: CodeableConcept.coding 0..* - optional
    public List<Coding> Coding { get; set; } = new();
    // R4: CodeableConcept.text 0..1 - optional
    public string? Text { get; set; }

    /// <summary>
    /// Every other property of this element, kept exactly as it was read. The gateway does not use them, but they are
    /// written back when the element is sent on (to a payer or the FHIR API), so nothing the sender put in is lost.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? OtherProperties { get; set; }

    /// <summary>Empty concept (used by the JSON serializer).</summary>
    public CodeableConcept()
    {
    }

    /// <summary>Creates a concept with one coding.</summary>
    public CodeableConcept(string? system, string? code, string? display = null)
    {
        Coding.Add(new Coding(system, code, display));
    }

    /// <summary>First coding, or null.</summary>
    public Coding? FirstCoding()
    {
        if (Coding.Count == 0)
        {
            return null;
        }
        return Coding[0];
    }

    /// <summary>First code, or null.</summary>
    public string? FirstCode()
    {
        Coding? first = FirstCoding();
        if (first == null)
        {
            return null;
        }
        return first.Code;
    }

    /// <summary>First coding with this system, or null.</summary>
    public Coding? CodingWithSystem(string system)
    {
        foreach (Coding coding in Coding)
        {
            if (coding.System == system)
            {
                return coding;
            }
        }
        return null;
    }

    /// <summary>True when any coding has this code.</summary>
    public bool HasCode(string code)
    {
        foreach (Coding coding in Coding)
        {
            if (coding.Code == code)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Text, else the first display, else the first code.</summary>
    public string? BestText()
    {
        if (!string.IsNullOrEmpty(Text))
        {
            return Text;
        }
        Coding? first = FirstCoding();
        if (first == null)
        {
            return null;
        }
        if (!string.IsNullOrEmpty(first.Display))
        {
            return first.Display;
        }
        return first.Code;
    }
}
