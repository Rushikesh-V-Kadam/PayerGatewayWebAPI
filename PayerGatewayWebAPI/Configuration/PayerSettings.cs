namespace PayerGatewayWebAPI.Configuration;

/// <summary>
/// One connected payer. Loaded from table PA_PAYER in the EHR database (Storage:Provider = EhrApi) or from the
/// appsettings "Payers" list (Storage:Provider = InMemory). The EHR sends <see cref="PayerName"/> in every request;
/// the gateway finds the matching payer and uses its URLs. To add a payer, add a row / an entry.
/// Every payer is called the same way; only these values differ:
///   CRD   {BaseUrl}/{CdsServicesPath}            e.g. http://localhost:5080/cds-services
///   DTR   {BaseUrl}/{FhirPath}/Questionnaire/$questionnaire-package
///   PAS   {BaseUrl}/{FhirPath}/Claim/$submit, Claim/$inquire, Subscription
///   CDex  {BaseUrl}/{FhirPath}/$submit-attachment, Task
/// </summary>
public class PayerSettings
{
    /// <summary>Name of the appsettings section that holds the payer list (memory mode only).</summary>
    public const string SectionName = "Payers";

    /// <summary>The name the EHR sends (payerName), e.g. "MockPayer". Matched without regard to upper / lower case.</summary>
    public string PayerName { get; set; } = string.Empty;

    /// <summary>Friendly name for messages and logs, e.g. "Mock Da Vinci Payer".</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>false = the payer is configured but switched off.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The payer's own id, sent to the payer in PAS (insurer identifier) and CDex (PayerId), e.g. "PAYER001".</summary>
    public string PayerIdentifier { get; set; } = string.Empty;

    /// <summary>Payer base URL, e.g. https://api.payer.com.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>CDS Hooks path under BaseUrl (default "cds-services").</summary>
    public string CdsServicesPath { get; set; } = "cds-services";

    /// <summary>FHIR path under BaseUrl (default "fhir").</summary>
    public string FhirPath { get; set; } = "fhir";

    /// <summary>Create a PAS Subscription so the payer notifies us when a pended request is decided.</summary>
    public bool UseSubscriptions { get; set; } = true;

    /// <summary>Identifier system this payer expects for member ids.</summary>
    public string MemberIdSystem { get; set; } = "http://example.org/member-ids";

    /// <summary>Identifier system this payer expects for its payer id.</summary>
    public string PayerIdSystem { get; set; } = "http://example.org/payer-ids";

    /// <summary>CRD must be fast (the clinician is waiting): seconds before giving up.</summary>
    public int CrdTimeoutSeconds { get; set; } = 5;

    /// <summary>Timeout of every other payer call, in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// FHIR release the payer works in: "R4" (default; all Da Vinci guides today) or "R5". Decides which FHIR base URL
    /// the payer's SMART DTR app is launched with (iss = /fhir/r4 or /fhir/r5 of the FHIR API).
    /// </summary>
    public string FhirVersion { get; set; } = "R4";

    /// <summary>
    /// How the clinician fills in the payer's questionnaires:
    ///   "Forms"    the gateway gets the questionnaire and the EHR shows it (POST /api/v1/dtr/forms; our questionnaire app)
    ///   "SmartApp" the payer's own SMART DTR app is opened from the CRD message link (/api/v1/dtr/launch/{linkId})
    /// </summary>
    public string DtrMode { get; set; } = "Forms";

    /// <summary>
    /// Launch URL of the payer's SMART DTR app. Empty = the URL on the payer's CRD card (normal case).
    /// Set it when the payer gives a fixed URL or the card URL must be overridden.
    /// </summary>
    public string? DtrAppLaunchUrl { get; set; }

    /// <summary>True when the payer works in FHIR R5.</summary>
    public bool IsR5()
    {
        return string.Equals(FhirVersion, "R5", StringComparison.OrdinalIgnoreCase)
            || string.Equals(FhirVersion, "5.0.0", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>"r4" or "r5", as the FHIR API's /internal/smart-launches expects it.</summary>
    public string GetFhirReleaseName()
    {
        return IsR5() ? "r5" : "r4";
    }

    /// <summary>True when the payer's own SMART DTR app is used (DtrMode = "SmartApp").</summary>
    public bool UsesSmartApp()
    {
        return string.Equals(DtrMode, "SmartApp", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>{BaseUrl}/{CdsServicesPath}</summary>
    public string GetCdsServicesUrl()
    {
        return BaseUrl.TrimEnd('/') + "/" + CdsServicesPath.Trim('/');
    }

    /// <summary>{BaseUrl}/{FhirPath}</summary>
    public string GetFhirBaseUrl()
    {
        return BaseUrl.TrimEnd('/') + "/" + FhirPath.Trim('/');
    }

    /// <summary>DisplayName, or PayerName when no display name is set.</summary>
    public string GetDisplayName()
    {
        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            return PayerName;
        }
        return DisplayName;
    }
}
