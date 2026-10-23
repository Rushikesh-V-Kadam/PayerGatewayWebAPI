namespace PayerGatewayWebAPI.Configuration;

/// <summary>Workflow options (appsettings "Workflow").</summary>
public class WorkflowSettings
{
    /// <summary>Name of the appsettings section.</summary>
    public const string SectionName = "Workflow";

    /// <summary>
    /// Public URL of the gateway; used in DTR links and in the PAS Subscription endpoint the payer calls back.
    /// TODO: the default is the local test address. Set Workflow:GatewayPublicBaseUrl in appsettings.json to the
    /// address payers and the EHR can reach in each environment (test, production).
    /// </summary>
    public string GatewayPublicBaseUrl { get; set; } = "http://localhost:5200";

    /// <summary>
    /// Identifier system of our prior auth request ids (Claim.identifier). It is the same for every payer.
    /// TODO: "provider.example.org" is a placeholder. Set Workflow:ClaimIdentifierSystem in appsettings.json to a URL
    /// that belongs to the provider organization before going live. If a payer asks for its own value, this becomes
    /// a per-payer setting (a new column of PA_PAYER).
    /// </summary>
    public string ClaimIdentifierSystem { get; set; } = "http://provider.example.org/fhir/sid/prior-auth-request-id";

    /// <summary>
    /// Identifier system of PAS Bundle ids. It is the same for every payer.
    /// TODO: "provider.example.org" is a placeholder. Set Workflow:BundleIdentifierSystem in appsettings.json to a URL
    /// that belongs to the provider organization before going live.
    /// </summary>
    public string BundleIdentifierSystem { get; set; } = "http://provider.example.org/fhir/sid/pas-bundle-id";

    /// <summary>
    /// Provider-wide SMART DTR app launch URL, used for every payer that has no DTR_APP_LAUNCH_URL of its own.
    /// Empty (normal) = the URL on the payer's CRD card.
    /// </summary>
    public string? DtrAppUrl { get; set; }

    /// <summary>How long a DTR link written to the EHR stays valid, in hours.</summary>
    public int DtrLinkHours { get; set; } = 72;

    /// <summary>Lifetime of the fhirAuthorization token given to payers during CRD, in minutes.</summary>
    public int FhirAuthorizationMinutes { get; set; } = 5;

    /// <summary>Decision deadline for urgent requests (CMS-0057-F: 72 hours).</summary>
    public int ExpeditedDecisionHours { get; set; } = 72;

    /// <summary>Decision deadline for routine requests (CMS-0057-F: 7 days).</summary>
    public int StandardDecisionDays { get; set; } = 7;

    /// <summary>GatewayPublicBaseUrl without a trailing slash.</summary>
    public string GetGatewayBaseUrl()
    {
        return GatewayPublicBaseUrl.TrimEnd('/');
    }
}
