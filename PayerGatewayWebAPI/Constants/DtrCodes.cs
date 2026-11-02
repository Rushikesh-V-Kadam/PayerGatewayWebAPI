namespace PayerGatewayWebAPI.Constants;

/// <summary>DTR 2.0.1 values the gateway reads in the forms (QuestionnaireResponse) saved by the payer's SMART DTR app.</summary>
public static class DtrCodes
{
    /// <summary>qr-context: which order / coverage a saved form belongs to (read by PAS and GET /dtr/saved-forms).</summary>
    public const string QrContextUrl = "http://hl7.org/fhir/us/davinci-dtr/StructureDefinition/qr-context";
}
