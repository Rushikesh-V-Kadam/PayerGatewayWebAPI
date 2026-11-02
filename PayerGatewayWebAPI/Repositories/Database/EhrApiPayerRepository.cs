using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using PayerGatewayWebAPI.Clients;
using PayerGatewayWebAPI.Configuration;
using PayerGatewayWebAPI.Exceptions;
using PayerGatewayWebAPI.Helpers;
using PayerGatewayWebAPI.Repositories.Interfaces;

namespace PayerGatewayWebAPI.Repositories.Database;

/// <summary>
/// One payer's configuration, read by the EHR's insurance plan id: GET payers/{insurancePlanId} on the EHR data API,
/// one row of table PA_PAYER. There is no payer list.
///
/// CLIENT-EHR: the client's API wraps the row (the client's own format):
///   { "PayerDetail": { "InsurancePlanID": 11111111, "PayerName": "MockPayer", "BaseURL": "...", ... },
///     "TOKEN_EXPIRED": "", "ERROR_MESSAGE": "" }
///   no such payer      -> "PayerDetail": null (an HTTP 404 is also understood as "no such payer")
///   TOKEN_EXPIRED or ERROR_MESSAGE filled -> the call failed (503 for the caller, the text is in the log)
/// The EHR team adds and changes payers in PA_PAYER; nothing in the gateway changes when a payer is onboarded.
///
/// The row is the PayerDetail object; one property = one column (PayerName = PAYER_NAME ...). Property names are
/// matched without regard to upper / lower case (BaseURL, baseUrl and BASEURL are the same).
/// InsurancePlanID, PayerName and BaseURL are needed to call the payer. Every other property is optional: it may be
/// missing, null or an empty text, and the gateway then uses its default. The properties the gateway reads:
///   InsurancePlanID                          whole number (1001 or "1001"); required
///   PayerName, BaseURL                       required
///   Enabled                                  true / false; empty = true
///   CDSServicesPath, FHIRPath                text; empty = "cds-services" / "fhir"; "/" = no extra path
///   CRDTimeoutSeconds, TimeoutSeconds        whole number; empty or 0 = 5 / 30
///   DTRAppLaunchURL                          text; empty = the URL on the payer's CRD card
/// DTRMode (column DTR_MODE, default SmartApp) is not read: DTR always opens the payer's SMART DTR app.
/// PA_PAYER also has login columns (AUTH_*, SIGN_CDS_HOOKS_REQUESTS, NOTIFICATION_SECRET). The gateway does not
/// read them: they belong to the authentication layer that is added outside this code.
///
/// Every value is read on its own and in a forgiving way (a yes / no column may arrive as true, 1, "1" or "Y";
/// a number as 30 or "30"). An optional value that still cannot be understood is reported in the log and its
/// default is used - one bad optional value never switches a payer off.
/// The payer is not kept here: PayerRegistry keeps it for Storage:PayerCacheMinutes.
/// </summary>
public class EhrApiPayerRepository : IPayerRepository
{
    private readonly EhrDataClient _ehr;
    private readonly ILogger<EhrApiPayerRepository> _logger;

    /// <summary>Created by dependency injection.</summary>
    public EhrApiPayerRepository(EhrDataClient ehr, ILogger<EhrApiPayerRepository> logger)
    {
        _ehr = ehr;
        _logger = logger;
    }

    /// <summary>
    /// GET payers/{insurancePlanId} on the EHR data API. Null when there is no such payer ("PayerDetail": null, or
    /// HTTP 404), or when the row belongs to another insurance plan id (reported in the log). Throws EhrException (503)
    /// when the answer reports an error (TOKEN_EXPIRED or ERROR_MESSAGE filled) or cannot be read. The row is returned as
    /// it is; PayerRegistry fills in the defaults and refuses a row that cannot be used (no name or no valid base URL).
    /// </summary>
    public async Task<PayerSettings?> GetByInsurancePlanIdAsync(int insurancePlanId, CancellationToken ct)
    {
        // TODO: confirm the path with the EHR team; it is the gateway setting Ehr:DataPath + "payers/{id}".
        string path = "payers/" + insurancePlanId.ToString(CultureInfo.InvariantCulture);
        JsonObject? body = await _ehr.GetAsync<JsonObject>(path, ct);
        if (body == null)
        {
            return null;   // HTTP 404 (or an empty answer): no such payer
        }
        JsonElement answer = JsonSerializer.SerializeToElement(body);

        // CLIENT-EHR: TOKEN_EXPIRED and ERROR_MESSAGE are empty texts when the call worked.
        // TODO: confirm with the EHR team which values TOKEN_EXPIRED holds; for now any text in it is an error.
        string? tokenExpired = ReadText(answer, "TOKEN_EXPIRED");
        if (!string.IsNullOrWhiteSpace(tokenExpired))
        {
            throw new EhrException("EHR data API GET " + path + " answered TOKEN_EXPIRED = '" + TextHelper.Shorten(tokenExpired, 100) + "'.");
        }
        string? errorMessage = ReadText(answer, "ERROR_MESSAGE");
        if (!string.IsNullOrWhiteSpace(errorMessage))
        {
            throw new EhrException("EHR data API GET " + path + " answered with an error: " + TextHelper.Shorten(errorMessage, 300));
        }

        // CLIENT-EHR: "PayerDetail": null means there is no payer with this insurance plan id.
        JsonElement? detail = Find(answer, "PayerDetail");
        if (detail == null || detail.Value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (detail.Value.ValueKind != JsonValueKind.Object)
        {
            throw new EhrException("EHR data API GET " + path + " answered a PayerDetail that is not a JSON object.");
        }

        JsonElement row = detail.Value;
        int? rowId = ReadNumber(row, "InsurancePlanID", "insurance plan id " + insurancePlanId);
        if (rowId == null || rowId.Value != insurancePlanId)
        {
            _logger.LogWarning("GET {Path} returned a payer whose InsurancePlanID is '{RowId}', not {Requested}; the row is not used. Correct the EHR data API.",
                path, rowId, insurancePlanId);
            return null;
        }
        return ToSettings(row);
    }

    /// <summary>
    /// One row -> PayerSettings. Only a column that has a value is copied, so an empty (or missing) optional column
    /// keeps the default value of PayerSettings.
    /// </summary>
    private PayerSettings ToSettings(JsonElement row)
    {
        PayerSettings payer = new PayerSettings();
        payer.PayerName = ReadText(row, "payerName") ?? string.Empty;
        payer.BaseUrl = ReadText(row, "baseUrl") ?? string.Empty;
        int? insurancePlanId = ReadNumber(row, "InsurancePlanID", payer.PayerName);
        if (insurancePlanId != null)
        {
            payer.InsurancePlanId = insurancePlanId.Value;
        }

        bool? enabled = ReadFlag(row, "enabled", payer.PayerName);
        if (enabled != null)
        {
            payer.Enabled = enabled.Value;
        }

        string? cdsServicesPath = ReadText(row, "cdsServicesPath");
        if (!string.IsNullOrWhiteSpace(cdsServicesPath))
        {
            payer.CdsServicesPath = cdsServicesPath;
        }
        string? fhirPath = ReadText(row, "fhirPath");
        if (!string.IsNullOrWhiteSpace(fhirPath))
        {
            payer.FhirPath = fhirPath;
        }

        int? crdTimeoutSeconds = ReadNumber(row, "crdTimeoutSeconds", payer.PayerName);
        if (crdTimeoutSeconds != null && crdTimeoutSeconds.Value > 0)
        {
            payer.CrdTimeoutSeconds = crdTimeoutSeconds.Value;
        }
        int? timeoutSeconds = ReadNumber(row, "timeoutSeconds", payer.PayerName);
        if (timeoutSeconds != null && timeoutSeconds.Value > 0)
        {
            payer.TimeoutSeconds = timeoutSeconds.Value;
        }

        payer.DtrAppLaunchUrl = ReadText(row, "dtrAppLaunchUrl");
        return payer;
    }

    // ================================================================== reading one value

    /// <summary>The value of a property of the row (the name is matched without regard to upper / lower case), or null when the row does not have it.</summary>
    private static JsonElement? Find(JsonElement row, string name)
    {
        foreach (JsonProperty property in row.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }
        return null;
    }

    /// <summary>A text value. Null when the property is missing or null; a number is returned as its text.</summary>
    private static string? ReadText(JsonElement row, string name)
    {
        JsonElement? value = Find(row, name);
        if (value == null)
        {
            return null;
        }
        if (value.Value.ValueKind == JsonValueKind.String)
        {
            return value.Value.GetString();
        }
        if (value.Value.ValueKind == JsonValueKind.Number)
        {
            return value.Value.GetRawText();
        }
        return null;
    }

    /// <summary>
    /// A yes / no value: true, false, 1, 0, "true", "false", "1", "0", "Y", "N", "yes", "no".
    /// Null when the property is missing, null or an empty text (the caller then keeps the default), and also - with
    /// a warning in the log - when the value is something else.
    /// </summary>
    private bool? ReadFlag(JsonElement row, string name, string payerName)
    {
        JsonElement? value = Find(row, name);
        if (value == null || value.Value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (value.Value.ValueKind == JsonValueKind.True)
        {
            return true;
        }
        if (value.Value.ValueKind == JsonValueKind.False)
        {
            return false;
        }

        string text = value.Value.GetRawText().Trim();
        if (value.Value.ValueKind == JsonValueKind.String)
        {
            text = (value.Value.GetString() ?? string.Empty).Trim();
        }
        if (text.Length == 0)
        {
            return null;
        }
        string lower = text.ToLowerInvariant();
        if (lower == "true" || lower == "1" || lower == "y" || lower == "yes")
        {
            return true;
        }
        if (lower == "false" || lower == "0" || lower == "n" || lower == "no")
        {
            return false;
        }

        _logger.LogWarning("Payer '{Payer}': {Property} = '{Value}' is not a yes / no value (true, false, 1, 0, Y, N); the default is used. Correct the row in table PA_PAYER.",
            payerName, name, text);
        return null;
    }

    /// <summary>
    /// A whole number: 30 or "30". Null when the property is missing, null or an empty text (the caller then keeps
    /// the default), and also - with a warning in the log - when the value is not a whole number.
    /// </summary>
    private int? ReadNumber(JsonElement row, string name, string payerName)
    {
        JsonElement? value = Find(row, name);
        if (value == null || value.Value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        string text = value.Value.GetRawText().Trim();
        if (value.Value.ValueKind == JsonValueKind.String)
        {
            text = (value.Value.GetString() ?? string.Empty).Trim();
        }
        if (text.Length == 0)
        {
            return null;
        }

        int number;
        if (value.Value.ValueKind == JsonValueKind.Number && value.Value.TryGetInt32(out number))
        {
            return number;
        }
        if (value.Value.ValueKind == JsonValueKind.String && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
        {
            return number;
        }

        _logger.LogWarning("Payer '{Payer}': {Property} = '{Value}' is not a whole number; the default is used. Correct the row in table PA_PAYER.",
            payerName, name, text);
        return null;
    }
}
