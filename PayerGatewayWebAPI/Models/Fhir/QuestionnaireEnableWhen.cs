using System.Text.Json;
using System.Text.Json.Serialization;

namespace PayerGatewayWebAPI.Models.Fhir;

/// <summary>"Only ask this question when ..." rule of a Questionnaire item.</summary>
public class QuestionnaireEnableWhen
{
    /// <summary>linkId of the question the rule looks at.</summary>
    public string Question { get; set; } = string.Empty;
    /// <summary>exists | = | != | &gt; | &lt; | &gt;= | &lt;=</summary>
    public string Operator { get; set; } = "=";
    public bool? AnswerBoolean { get; set; }
    public decimal? AnswerDecimal { get; set; }
    public int? AnswerInteger { get; set; }
    public string? AnswerDate { get; set; }
    public string? AnswerDateTime { get; set; }
    public string? AnswerString { get; set; }
    public Coding? AnswerCoding { get; set; }
    public Quantity? AnswerQuantity { get; set; }

    /// <summary>
    /// Every other property of this element, kept exactly as it was read. The gateway does not use them, but they are
    /// written back when the element is sent on (to a payer or the FHIR API), so nothing the sender put in is lost.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? OtherProperties { get; set; }
}
