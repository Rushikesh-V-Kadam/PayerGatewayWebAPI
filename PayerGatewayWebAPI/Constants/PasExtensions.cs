namespace PayerGatewayWebAPI.Constants;

/// <summary>PAS 2.0.1 extension URLs used on Claim (careTeam, item) and ClaimResponse (item, item.adjudication).</summary>
public static class PasExtensions
{
    private const string Base = "http://hl7.org/fhir/us/davinci-pas/StructureDefinition/";

    // Claim.item (request)
    public const string CertificationType = Base + "extension-certificationType";
    public const string ServiceItemRequestType = Base + "extension-serviceItemRequestType";
    public const string RequestedService = Base + "extension-requestedService";
    public const string InfoChanged = Base + "extension-infoChanged";
    public const string InfoCancelled = Base + "modifierextension-infoCancelled";

    /// <summary>Claim.careTeam: true = the care team member is for the whole request, false = for single items.</summary>
    public const string CareTeamClaimScope = Base + "extension-careTeamClaimScope";

    // ClaimResponse.item.adjudication (the decision of a line)
    public const string ReviewAction = Base + "extension-reviewAction";
    public const string ReviewActionCode = Base + "extension-reviewActionCode";

    // ClaimResponse.item (authorized period and detail of a line)
    public const string ItemPreAuthPeriod = Base + "extension-itemPreAuthPeriod";
    public const string ItemAuthorizedDetail = Base + "extension-itemAuthorizedDetail";

    // ClaimResponse.error (where the problem is)
    public const string ErrorElement = Base + "extension-errorElement";
    public const string ErrorPath = Base + "extension-errorPath";

    // Task.input line numbers (CDex)
    public const string PaLineNumber = Base + "extension-paLineNumber";
    public const string ServiceLineNumber = Base + "extension-serviceLineNumber";
}
