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
///
/// Only PayerName and BaseUrl must be filled. Every other value is optional and has a default, so a payer row with
/// only those two values works.
///
/// The two paths: empty = the usual path ("cds-services" / "fhir"); "/" = no extra path, the payer's address is
/// BaseUrl itself (for a payer whose FHIR address is, for example, https://api.payer.com/r4 with nothing after it).
///
/// The payer's own id and the member id are NOT settings: they come from the patient's insurance in the EHR
/// (FHIR Coverage.payor.identifier and Coverage.subscriberId), so they are always the values of that patient's plan.
/// The gateway never makes up an id or an identifier system: what the insurance does not have is not sent.
///
/// Login details of a payer (token URL, client id, secret, scope) are columns of PA_PAYER too, but this class does
/// not read them: authentication is added outside the gateway code.
/// </summary>
public class PayerSettings
{
    /// <summary>Name of the appsettings section that holds the payer list (memory mode only).</summary>
    public const string SectionName = "Payers";

    /// <summary>CDS Hooks path used when the payer row has none.</summary>
    public const string DefaultCdsServicesPath = "cds-services";

    /// <summary>FHIR path used when the payer row has none.</summary>
    public const string DefaultFhirPath = "fhir";

    /// <summary>Seconds to wait at order-sign when the payer row has no value.</summary>
    public const int DefaultCrdTimeoutSeconds = 5;

    /// <summary>Seconds to wait for every other payer call when the payer row has no value.</summary>
    public const int DefaultTimeoutSeconds = 30;

    /// <summary>Required. The name the EHR sends (payerName), e.g. "MockPayer". Matched without regard to upper / lower case.</summary>
    public string PayerName { get; set; } = string.Empty;

    /// <summary>Optional. Friendly name for messages and logs, e.g. "Mock Da Vinci Payer". Empty = PayerName is shown.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Optional (default true). false = the payer is configured but switched off.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Required. Payer base URL (http or https), e.g. https://api.payer.com.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Optional. CDS Hooks path under BaseUrl. Empty = "cds-services"; "/" = no extra path.</summary>
    public string CdsServicesPath { get; set; } = DefaultCdsServicesPath;

    /// <summary>Optional. FHIR path under BaseUrl. Empty = "fhir"; "/" = no extra path.</summary>
    public string FhirPath { get; set; } = DefaultFhirPath;

    /// <summary>Optional (default true). Create a PAS Subscription so the payer notifies us when a pended request is decided.</summary>
    public bool UseSubscriptions { get; set; } = true;

    /// <summary>Optional. CRD must be fast (the clinician is waiting): seconds before giving up. 0 or less = 5.</summary>
    public int CrdTimeoutSeconds { get; set; } = DefaultCrdTimeoutSeconds;

    /// <summary>Optional. Timeout of every other payer call, in seconds. 0 or less = 30.</summary>
    public int TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;

    /// <summary>
    /// Optional. How the clinician fills in the payer's questionnaires:
    ///   "Forms"    the gateway gets the questionnaire and the EHR shows it (POST /api/v1/dtr/forms; our questionnaire app)
    ///   "SmartApp" the payer's own SMART DTR app is opened from the CRD message link (/api/v1/dtr/launch/{linkId})
    /// Any other value, or an empty one, is treated as Forms.
    /// </summary>
    public string DtrMode { get; set; } = "Forms";

    /// <summary>
    /// Optional. Launch URL of the payer's SMART DTR app. Empty = the URL on the payer's CRD card (normal case).
    /// Set it when the payer gives a fixed URL or the card URL must be overridden.
    /// </summary>
    public string? DtrAppLaunchUrl { get; set; }

    /// <summary>
    /// Fills in the default of every optional value that is empty and removes spaces around the texts.
    /// Called once for each payer when the list is loaded, so the rest of the code never sees an empty optional value.
    /// </summary>
    public void ApplyDefaults()
    {
        PayerName = (PayerName ?? string.Empty).Trim();
        DisplayName = (DisplayName ?? string.Empty).Trim();
        BaseUrl = (BaseUrl ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(CdsServicesPath))
        {
            CdsServicesPath = DefaultCdsServicesPath;
        }
        if (string.IsNullOrWhiteSpace(FhirPath))
        {
            FhirPath = DefaultFhirPath;
        }
        CdsServicesPath = CdsServicesPath.Trim();
        FhirPath = FhirPath.Trim();

        if (CrdTimeoutSeconds <= 0)
        {
            CrdTimeoutSeconds = DefaultCrdTimeoutSeconds;
        }
        if (TimeoutSeconds <= 0)
        {
            TimeoutSeconds = DefaultTimeoutSeconds;
        }

        if (UsesSmartApp())
        {
            DtrMode = "SmartApp";
        }
        else
        {
            DtrMode = "Forms";
        }

        if (string.IsNullOrWhiteSpace(DtrAppLaunchUrl))
        {
            DtrAppLaunchUrl = null;
        }
        else
        {
            DtrAppLaunchUrl = DtrAppLaunchUrl.Trim();
        }
    }

    /// <summary>
    /// Why this payer cannot be used, or null when it can. A payer needs a name and a base URL that is a complete
    /// http / https address; without them no call to the payer is possible.
    /// </summary>
    public string? GetProblem()
    {
        if (string.IsNullOrWhiteSpace(PayerName))
        {
            return "payerName (PAYER_NAME) is empty";
        }
        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            return "baseUrl (BASE_URL) is empty";
        }

        Uri? address;
        bool isAddress = Uri.TryCreate(BaseUrl, UriKind.Absolute, out address);
        if (!isAddress || address == null || (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps))
        {
            return "baseUrl (BASE_URL) '" + BaseUrl + "' is not a complete http or https address";
        }
        return null;
    }

    /// <summary>True when the payer's own SMART DTR app is used (DtrMode = "SmartApp").</summary>
    public bool UsesSmartApp()
    {
        string mode = (DtrMode ?? string.Empty).Trim();
        return string.Equals(mode, "SmartApp", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>{BaseUrl}/{CdsServicesPath}, or BaseUrl alone when the path is "/".</summary>
    public string GetCdsServicesUrl()
    {
        return Combine(BaseUrl, CdsServicesPath);
    }

    /// <summary>{BaseUrl}/{FhirPath}, or BaseUrl alone when the path is "/".</summary>
    public string GetFhirBaseUrl()
    {
        return Combine(BaseUrl, FhirPath);
    }

    /// <summary>
    /// Joins the base URL and a path with exactly one "/" between them. A path that is only "/" (or empty) adds
    /// nothing, so the result never ends with "/" and never has "//" in it.
    /// </summary>
    private static string Combine(string? baseUrl, string? path)
    {
        string cleanBase = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
        string cleanPath = (path ?? string.Empty).Trim().Trim('/');
        if (cleanPath.Length == 0)
        {
            return cleanBase;
        }
        return cleanBase + "/" + cleanPath;
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
