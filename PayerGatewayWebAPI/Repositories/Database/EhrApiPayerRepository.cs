using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using PayerGatewayWebAPI.Clients;
using PayerGatewayWebAPI.Configuration;
using PayerGatewayWebAPI.Repositories.Interfaces;

namespace PayerGatewayWebAPI.Repositories.Database;

/// <summary>
/// One payer's configuration, read by the EHR's insurance plan id: GET payers/{piInsurancePlanId} on the EHR data API,
/// one row of table PA_PAYER (404 = no such payer). There is no payer list.
/// The EHR team adds and changes payers in PA_PAYER; nothing in the gateway changes when a payer is onboarded.
///
/// One row = one JSON object; one property = one column (payerName = PAYER_NAME ...).
/// piInsurancePlanId, payerName and baseUrl are needed to call the payer. Every other property is optional: it may be
/// missing, null or an empty text, and the gateway then uses its default. The properties the gateway reads:
///   piInsurancePlanId                        whole number (1001 or "1001"); required
///   payerName, baseUrl                       required
///   enabled, useSubscriptions                true / false; empty = true
///   cdsServicesPath, fhirPath                text; empty = "cds-services" / "fhir"; "/" = no extra path
///   crdTimeoutSeconds, timeoutSeconds        whole number; empty or 0 = 5 / 30
///   dtrMode                                  Forms | SmartApp; empty = Forms
///   dtrAppLaunchUrl                          text; empty = the URL on the payer's CRD card
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
    /// GET payers/{piInsurancePlanId} on the EHR data API: one JSON object (not an array). Null when the EHR answers
    /// 404, or when the row it returns belongs to another insurance plan id (reported in the log). The row is returned
    /// as it is; PayerRegistry fills in the defaults and refuses a row that cannot be used (no name or no valid base URL).
    /// </summary>
    public async Task<PayerSettings?> GetByPiInsurancePlanIdAsync(int piInsurancePlanId, CancellationToken ct)
    {
        string path = "payers/" + piInsurancePlanId.ToString(CultureInfo.InvariantCulture);
        JsonObject? body = await _ehr.GetAsync<JsonObject>(path, ct);
        if (body == null)
        {
            return null;
        }

        JsonElement row = JsonSerializer.SerializeToElement(body);
        int? rowId = ReadNumber(row, "piInsurancePlanId", "piInsurancePlanId " + piInsurancePlanId);
        if (rowId == null || rowId.Value != piInsurancePlanId)
        {
            _logger.LogWarning("GET {Path} returned a payer row whose piInsurancePlanId is '{RowId}', not {Requested}; the row is not used. Correct the EHR data API.",
                path, rowId, piInsurancePlanId);
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
        int? piInsurancePlanId = ReadNumber(row, "piInsurancePlanId", payer.PayerName);
        if (piInsurancePlanId != null)
        {
            payer.PiInsurancePlanId = piInsurancePlanId.Value;
        }

        bool? enabled = ReadFlag(row, "enabled", payer.PayerName);
        if (enabled != null)
        {
            payer.Enabled = enabled.Value;
        }
        bool? useSubscriptions = ReadFlag(row, "useSubscriptions", payer.PayerName);
        if (useSubscriptions != null)
        {
            payer.UseSubscriptions = useSubscriptions.Value;
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

        string? dtrMode = ReadText(row, "dtrMode");
        if (!string.IsNullOrWhiteSpace(dtrMode))
        {
            payer.DtrMode = dtrMode;
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
