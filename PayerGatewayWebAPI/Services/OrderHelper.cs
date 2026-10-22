using System.Text.Json.Nodes;
using PayerGatewayWebAPI.Exceptions;
using PayerGatewayWebAPI.Helpers;
using PayerGatewayWebAPI.Models.Fhir;

namespace PayerGatewayWebAPI.Services;

/// <summary>
/// Checks shared by CRD, DTR and PAS: every requested order exists and belongs to the patient, and the insurance
/// (Coverage) sent by the EHR can be used.
/// </summary>
public static class OrderHelper
{
    /// <summary>FHIR R4 Coverage.status of an insurance in force.</summary>
    private const string CoverageActiveStatus = "active";

    /// <summary>Typed view of a raw order (never null: an empty OrderResource when it cannot be read).</summary>
    public static OrderResource ReadOrder(JsonObject raw)
    {
        OrderResource? order = FhirJson.Read<OrderResource>(raw);
        if (order == null)
        {
            return new OrderResource();
        }
        return order;
    }

    /// <summary>The raw order with this id, or null.</summary>
    public static JsonObject? FindOrder(List<JsonObject> orders, string orderId)
    {
        foreach (JsonObject raw in orders)
        {
            if (FhirJson.Id(raw) == orderId)
            {
                return raw;
            }
        }
        return null;
    }

    /// <summary>
    /// Every requested order must have been found and must belong to the patient.
    /// Missing order -> 404, order of another patient -> 400.
    /// </summary>
    public static void CheckOrders(List<string> requestedIds, List<JsonObject> orders, string patientId)
    {
        foreach (string orderId in TextHelper.Distinct(requestedIds))
        {
            JsonObject? raw = FindOrder(orders, orderId);
            if (raw == null)
            {
                throw new ApiException(404, "Order " + orderId + " was not found at the FHIR API.");
            }
            OrderResource order = ReadOrder(raw);
            if (order.GetPatientId() != patientId)
            {
                throw new ApiException(400, "Order " + orderId + " does not belong to patient " + patientId + ".");
            }
        }
    }

    /// <summary>
    /// Why the insurance the EHR sent (coverageId) cannot be used, or "" when it can be used.
    /// It must exist at the FHIR API, belong to the patient (Coverage.beneficiary) and be active (Coverage.status).
    /// The EHR sends the active insurance; the check still runs (the insurance is read anyway) because it catches a
    /// wrong id, an insurance of another patient, or an insurance that ended between order-sign and prior auth.
    /// </summary>
    // CLIENT-EHR: the EHR sends the patient's active insurance (coverageId); the orders carry no insurance, so the
    // gateway no longer picks one from the patient's insurances.
    public static string CoverageProblem(Coverage? coverage, string coverageId, string patientId)
    {
        if (coverage == null)
        {
            return "Insurance " + coverageId + " was not found at the FHIR API.";
        }

        // Coverage.beneficiary is 1..1 in FHIR R4: the patient the insurance is for
        string? beneficiaryId = null;
        if (coverage.Beneficiary != null)
        {
            beneficiaryId = coverage.Beneficiary.GetId();
        }
        if (beneficiaryId != patientId)
        {
            return "Insurance " + coverageId + " does not belong to patient " + patientId + ".";
        }

        // Coverage.status is 1..1 in FHIR R4: active | cancelled | draft | entered-in-error
        if (coverage.Status != CoverageActiveStatus)
        {
            string status = "missing";
            if (!string.IsNullOrEmpty(coverage.Status))
            {
                status = coverage.Status;
            }
            return "Insurance " + coverageId + " is not active (status: " + status + ").";
        }
        return string.Empty;
    }
}
