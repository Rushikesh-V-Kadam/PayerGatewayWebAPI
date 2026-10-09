using System.Text.Json.Nodes;
using PayerGatewayWebAPI.Helpers;

namespace PayerGatewayWebAPI.Services.Dtr;

/// <summary>
/// Makes an R5 payer's Questionnaire readable by the gateway forms, which work in R4. The only Questionnaire
/// differences that matter for DTR forms:
///   R5 item.type "coding" + answerConstraint "optionsOnly" (or none)             -> R4 "choice"
///   R5 item.type "coding" + answerConstraint "optionsOrString" / "optionsOrType" -> R4 "open-choice"
///   R5 item.type "question" (a heading with no answer)                         -> R4 "display"
///   answerConstraint, disabledDisplay are removed (not in R4)
/// Works on the payer's answer as it came back: a Questionnaire, a Bundle / Parameters holding Questionnaires, or a
/// QuestionnaireResponse with a contained Questionnaire (adaptive forms). Used only for payers with FhirVersion = R5.
/// </summary>
public static class R5QuestionnaireNormalizer
{
    /// <summary>Changes every Questionnaire found in the node, in place.</summary>
    public static void Normalize(JsonNode? node)
    {
        JsonObject? resource = node as JsonObject;
        if (resource == null)
        {
            return;
        }
        string? type = FhirJson.ResourceType(resource);
        if (type == "Questionnaire")
        {
            NormalizeItems(resource["item"] as JsonArray);
        }

        // Bundle.entry[].resource, Parameters.parameter[].resource / part[], contained[]
        NormalizeList(resource["entry"] as JsonArray, "resource");
        NormalizeList(resource["parameter"] as JsonArray, "resource");
        NormalizeList(resource["contained"] as JsonArray, null);
        JsonArray? parameters = resource["parameter"] as JsonArray;
        if (parameters != null)
        {
            foreach (JsonNode? parameter in parameters)
            {
                JsonObject? parameterObject = parameter as JsonObject;
                if (parameterObject != null)
                {
                    NormalizeList(parameterObject["part"] as JsonArray, "resource");
                }
            }
        }
    }

    /// <summary>Normalizes each element of a list (or its <paramref name="property"/>).</summary>
    private static void NormalizeList(JsonArray? list, string? property)
    {
        if (list == null)
        {
            return;
        }
        foreach (JsonNode? element in list)
        {
            JsonObject? elementObject = element as JsonObject;
            if (elementObject == null)
            {
                continue;
            }
            if (property == null)
            {
                Normalize(elementObject);
            }
            else
            {
                Normalize(elementObject[property]);
            }
        }
    }

    /// <summary>Changes the item types of a Questionnaire item list and its sub-items.</summary>
    private static void NormalizeItems(JsonArray? items)
    {
        if (items == null)
        {
            return;
        }
        foreach (JsonNode? node in items)
        {
            JsonObject? item = node as JsonObject;
            if (item == null)
            {
                continue;
            }
            string? type = FhirJson.GetString(item, "type");
            string? constraint = FhirJson.GetString(item, "answerConstraint");
            if (type == "coding")
            {
                bool open = constraint == "optionsOrString" || constraint == "optionsOrType";
                item["type"] = open ? "open-choice" : "choice";
            }
            else if (type == "question")
            {
                item["type"] = "display";
            }
            item.Remove("answerConstraint");
            item.Remove("disabledDisplay");
            NormalizeItems(item["item"] as JsonArray);
        }
    }
}
