using System.Text.Json.Nodes;
using PayerGatewayWebAPI.Exceptions;
using PayerGatewayWebAPI.Helpers;
using PayerGatewayWebAPI.Models.Fhir;

namespace PayerGatewayWebAPI.Services;

/// <summary>
/// Checks shared by CRD, DTR and PAS: every requested order exists and belongs to the patient, and which Coverage to use.
/// </summary>
public static class OrderHelper
{
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

    /// <summary>The first order's Coverage id (Order.insurance), or null.</summary>
    public static string? CoverageIdOfOrders(List<JsonObject> orders)
    {
        foreach (JsonObject raw in orders)
        {
            string? coverageId = ReadOrder(raw).GetCoverageId();
            if (coverageId != null)
            {
                return coverageId;
            }
        }
        return null;
    }

    /// <summary>The first order's requester (ordering practitioner) id, or null.</summary>
    public static string? RequesterOfOrders(List<JsonObject> orders)
    {
        foreach (JsonObject raw in orders)
        {
            string? requesterId = ReadOrder(raw).GetRequesterId();
            if (requesterId != null)
            {
                return requesterId;
            }
        }
        return null;
    }

    /// <summary>
    /// Which Coverage (insurance) to use, in this order:
    ///   1. the one asked for in the request (coverageId)
    ///   2. the one on the orders (Order.insurance)
    ///   3. the patient's only active insurance
    /// Returns null when the patient has no active insurance, or the one asked for is not active.
    /// When the patient has several active insurances and neither the request nor the orders say which one, the
    /// gateway does not pick one (it could send the member id of the wrong plan to the payer): 422, with the ids to
    /// choose from.
    /// </summary>
    public static Coverage? ChooseCoverage(List<Coverage> activeCoverages, string? requestedCoverageId, List<JsonObject> orders)
    {
        string? coverageId = requestedCoverageId;
        if (string.IsNullOrWhiteSpace(coverageId))
        {
            coverageId = CoverageIdOfOrders(orders);
        }

        if (!string.IsNullOrWhiteSpace(coverageId))
        {
            foreach (Coverage coverage in activeCoverages)
            {
                if (coverage.Id == coverageId.Trim())
                {
                    return coverage;
                }
            }
            return null;
        }

        if (activeCoverages.Count == 0)
        {
            return null;
        }
        if (activeCoverages.Count == 1)
        {
            return activeCoverages[0];
        }

        List<string> ids = new List<string>();
        foreach (Coverage coverage in activeCoverages)
        {
            ids.Add(coverage.Id ?? "(no id)");
        }
        throw new ApiException(422, "The patient has " + activeCoverages.Count + " active insurances (" + string.Join(", ", ids)
            + ") and the orders do not say which one to use. Send coverageId.");
    }
}
