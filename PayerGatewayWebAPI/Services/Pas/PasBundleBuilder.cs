using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using PayerGatewayWebAPI.Clients;
using PayerGatewayWebAPI.Configuration;
using PayerGatewayWebAPI.Constants;
using PayerGatewayWebAPI.Exceptions;
using PayerGatewayWebAPI.Helpers;
using PayerGatewayWebAPI.Models.Fhir;

namespace PayerGatewayWebAPI.Services.Pas;

/// <summary>
/// Builds PAS 2.0.1 Bundles.
///  - Request Bundle (Claim/$submit): the Claim first, then everything it points to so the payer can decide without
///    calling back: Patient, Coverage, insurer Organization, provider Organization, Practitioner, orders, DTR forms, documents.
///    One Claim.item per order; an order keeps its item number on every update.
///  - Inquiry Bundle (Claim/$inquire): the last request's Claim with the inquiry profile and no attachments.
/// </summary>
public class PasBundleBuilder
{
    private readonly WorkflowSettings _workflow;
    private readonly FhirApiClient _fhir;

    /// <summary>Created by dependency injection.</summary>
    public PasBundleBuilder(IOptions<WorkflowSettings> workflow, FhirApiClient fhir)
    {
        _workflow = workflow.Value;
        _fhir = fhir;
    }

    // ================================================================== request Bundle

    /// <summary>Builds the PAS request Bundle.</summary>
    public Bundle BuildRequest(PasBuildInput input)
    {
        PayerSettings payer = input.Payer;
        string? organizationId = FhirJson.Id(input.Organization);
        string? practitionerId = FhirJson.Id(input.Practitioner);

        // ---- the payer as an Organization (Claim.insurer)
        string insurerId = "payer-" + SafeId(payer.PayerName);
        JsonObject insurer = BuildInsurer(input, insurerId);
        Coverage coverage = CoverageForBundle(input.Coverage, insurerId);

        // ---- Claim header
        Claim claim = new Claim();
        claim.Id = SafeId(input.RequestId) + "-v" + input.Version;
        claim.Meta = new Meta();
        claim.Meta.Profile.Add(input.Version > 1 ? PasProfiles.ClaimUpdate : PasProfiles.Claim);
        claim.Identifier.Add(new Identifier(_workflow.ClaimIdentifierSystem, input.RequestId));
        claim.Type = new CodeableConcept(CodeSystems.ClaimType, "professional", "Professional");
        claim.Patient = new ResourceReference("Patient/" + FhirJson.Id(input.Patient));
        claim.Created = FhirDate.Now();
        claim.Insurer = new ResourceReference("Organization/" + insurerId);
        claim.Provider = new ResourceReference("Organization/" + organizationId);
        claim.Priority = new CodeableConcept(CodeSystems.ProcessPriority, input.Priority == PasCodes.PriorityUrgent ? "stat" : "normal");

        ClaimInsurance insurance = new ClaimInsurance();
        insurance.Coverage = new ResourceReference("Coverage/" + coverage.Id);
        claim.Insurance.Add(insurance);

        // The care team members are for the whole request, not for single lines: PAS wants that said on each member
        // (careTeamClaimScope = true), and such members are not listed again on the lines.
        if (practitionerId != null)
        {
            ClaimCareTeam careTeam = new ClaimCareTeam();
            careTeam.Extension.Add(WholeRequestScope());
            careTeam.Sequence = 1;
            careTeam.Provider = new ResourceReference("Practitioner/" + practitionerId);
            careTeam.Role = new CodeableConcept(CodeSystems.CareTeamRole, "primary", "Primary provider");
            claim.CareTeam.Add(careTeam);
        }

        // ---- referring physician (second care team member)
        string? referringId = FhirJson.Id(input.ReferringPractitioner);
        if (referringId != null && referringId != practitionerId)
        {
            ClaimCareTeam referring = new ClaimCareTeam();
            referring.Extension.Add(WholeRequestScope());
            referring.Sequence = claim.CareTeam.Count + 1;
            referring.Provider = new ResourceReference("Practitioner/" + referringId);
            referring.Role = new CodeableConcept(CodeSystems.CareTeamRole, "other", "Referring provider");
            claim.CareTeam.Add(referring);
        }

        // ---- renewal: the authorization being renewed
        if (input.RequestType == PasCodes.RequestTypeRenewal && !string.IsNullOrWhiteSpace(input.PreviousAuthorizationNumber))
        {
            ClaimRelated related = new ClaimRelated();
            related.Relationship = new CodeableConcept(CodeSystems.RelatedClaimRelationship, "prior", "Prior claim");
            related.Reference = new Identifier(null, input.PreviousAuthorizationNumber);
            claim.Related.Add(related);
        }

        // ---- all lines: active orders, then cancelled ones
        List<PasOrderLine> lines = new List<PasOrderLine>();
        foreach (JsonObject raw in input.Orders)
        {
            PasOrderLine line = new PasOrderLine();
            line.Raw = raw;
            line.Order = ReadOrder(raw);
            line.Cancelled = false;
            lines.Add(line);
        }
        foreach (JsonObject raw in input.CancelledOrders)
        {
            PasOrderLine line = new PasOrderLine();
            line.Raw = raw;
            line.Order = ReadOrder(raw);
            line.Cancelled = true;
            lines.Add(line);
        }

        // ---- diagnoses: every distinct reasonCode of the orders, numbered 1, 2, 3 ...
        Dictionary<string, int> diagnosisNumbers = new Dictionary<string, int>();
        foreach (PasOrderLine line in lines)
        {
            foreach (CodeableConcept diagnosis in line.Order.ReasonCode)
            {
                string key = DiagnosisKey(diagnosis);
                if (diagnosisNumbers.ContainsKey(key))
                {
                    continue;
                }
                diagnosisNumbers[key] = diagnosisNumbers.Count + 1;
                ClaimDiagnosis claimDiagnosis = new ClaimDiagnosis();
                claimDiagnosis.Sequence = diagnosisNumbers[key];
                claimDiagnosis.DiagnosisCodeableConcept = diagnosis;
                claim.Diagnosis.Add(claimDiagnosis);
            }
        }

        // ---- supporting information: forms (linked to their order when known), documents, note
        Dictionary<string, List<int>> infoByOrder = new Dictionary<string, List<int>>();
        List<int> infoForAll = new List<int>();
        foreach (JsonObject form in input.Forms)
        {
            int sequence = AddSupportingInfo(claim, "QuestionnaireResponse/" + FhirJson.Id(form));
            List<string> formOrderIds = FormOrderIds(form);
            if (formOrderIds.Count == 0)
            {
                infoForAll.Add(sequence);   // the form names no order: it supports every line
            }
            foreach (string orderId in formOrderIds)
            {
                // a form can be for several orders: it is linked to the line of each of them
                if (!infoByOrder.ContainsKey(orderId))
                {
                    infoByOrder[orderId] = new List<int>();
                }
                infoByOrder[orderId].Add(sequence);
            }
        }
        foreach (JsonObject document in input.Documents)
        {
            infoForAll.Add(AddSupportingInfo(claim, "DocumentReference/" + FhirJson.Id(document)));
        }
        string? header = HeaderText(input);
        if (header != null)
        {
            // free text goes in a "freeFormMessage" entry (PAS MessageText); "additionalInformation" is for references
            ClaimSupportingInfo details = new ClaimSupportingInfo();
            details.Sequence = claim.SupportingInfo.Count + 1;
            details.Category = new CodeableConcept(CodeSystems.PasTemp, PasCodes.FreeFormMessage);
            details.ValueString = header;
            claim.SupportingInfo.Add(details);
            infoForAll.Add(details.Sequence);
        }
        if (!string.IsNullOrWhiteSpace(input.Notes))
        {
            ClaimSupportingInfo note = new ClaimSupportingInfo();
            note.Sequence = claim.SupportingInfo.Count + 1;
            note.Category = new CodeableConcept(CodeSystems.PasTemp, PasCodes.FreeFormMessage);
            note.ValueString = input.Notes.Length > 264 ? input.Notes.Substring(0, 264) : input.Notes;   // X12 MSG01 limit
            claim.SupportingInfo.Add(note);
        }

        // ---- one item per order
        foreach (PasOrderLine line in lines)
        {
            claim.Item.Add(BuildItem(input, line, diagnosisNumbers, infoByOrder, infoForAll));
        }
        claim.Item.Sort(CompareItems);

        // ---- the Bundle: Claim first, then everything it references
        Bundle bundle = NewCollectionBundle(PasProfiles.RequestBundle);
        AddEntry(bundle, FhirJson.ToNode(claim));
        AddEntry(bundle, input.Patient);
        AddEntry(bundle, FhirJson.ToNode(coverage));
        AddEntry(bundle, insurer);
        AddEntry(bundle, input.Organization);
        if (input.Practitioner != null)
        {
            AddEntry(bundle, input.Practitioner);
        }
        if (input.ReferringPractitioner != null && referringId != practitionerId)
        {
            AddEntry(bundle, input.ReferringPractitioner);
        }
        foreach (PasOrderLine line in lines)
        {
            AddEntry(bundle, line.Raw);
        }
        foreach (JsonObject form in input.Forms)
        {
            AddEntry(bundle, form);
        }
        foreach (JsonObject document in input.Documents)
        {
            AddEntry(bundle, document);
        }
        return bundle;
    }

    /// <summary>
    /// One Claim.item for one order, with the PAS item extensions.
    /// The order must have a code (PasService.CheckOrdersHaveCode runs before every Bundle is built) and a line number.
    /// </summary>
    private ClaimItem BuildItem(PasBuildInput input, PasOrderLine line, Dictionary<string, int> diagnosisNumbers,
        Dictionary<string, List<int>> infoByOrder, List<int> infoForAll)
    {
        OrderResource order = line.Order;
        string orderId = order.Id ?? string.Empty;

        int lineNumber;
        if (!input.LineNumbers.TryGetValue(orderId, out lineNumber))
        {
            throw new ApiException(500, "Order " + orderId + " has no line number in request " + input.RequestId + ".");
        }

        ClaimItem item = new ClaimItem();
        item.Sequence = lineNumber;
        item.Category = new CodeableConcept(CodeSystems.X12ServiceType, ServiceType(order));
        item.ProductOrService = order.GetCode();

        decimal? quantity = order.GetQuantity();
        if (quantity != null)
        {
            item.Quantity = new Quantity();
            item.Quantity.Value = quantity;
        }

        // Service date: a period when the order has one, otherwise a single date
        if (order.OccurrencePeriod != null && order.OccurrencePeriod.Start != null && order.OccurrencePeriod.End != null)
        {
            item.ServicedPeriod = order.OccurrencePeriod;
        }
        else if (order.OccurrenceDateTime != null)
        {
            item.ServicedDate = order.OccurrenceDateTime.Length > 10 ? order.OccurrenceDateTime.Substring(0, 10) : order.OccurrenceDateTime;
        }

        // Place of service (CMS code) from the order's locationCode
        foreach (CodeableConcept location in order.LocationCode)
        {
            Coding? placeOfService = location.CodingWithSystem(CodeSystems.PlaceOfService);
            if (placeOfService != null && placeOfService.Code != null)
            {
                item.LocationCodeableConcept = new CodeableConcept(CodeSystems.PlaceOfService, placeOfService.Code);
                break;
            }
        }

        foreach (CodeableConcept diagnosis in order.ReasonCode)
        {
            int number = diagnosisNumbers[DiagnosisKey(diagnosis)];
            if (!item.DiagnosisSequence.Contains(number))
            {
                item.DiagnosisSequence.Add(number);
            }
        }
        List<int>? orderInfo;
        if (infoByOrder.TryGetValue(orderId, out orderInfo))
        {
            item.InformationSequence.AddRange(orderInfo);
        }
        item.InformationSequence.AddRange(infoForAll);

        // PAS item extensions: certification type (I = initial, 3 = cancel), request type (HS), the order itself
        Extension certification = new Extension(PasExtensions.CertificationType);
        certification.ValueCodeableConcept = new CodeableConcept(CodeSystems.X12CertificationType,
            CertificationCode(input, line));
        item.Extension.Add(certification);

        Extension requestType = new Extension(PasExtensions.ServiceItemRequestType);
        requestType.ValueCodeableConcept = new CodeableConcept(CodeSystems.X12RequestType, X12Codes.HealthServicesReview);
        item.Extension.Add(requestType);

        Extension requested = new Extension(PasExtensions.RequestedService);
        requested.ValueReference = new ResourceReference(order.GetReference());
        item.Extension.Add(requested);

        if (line.Cancelled)
        {
            Extension cancelled = new Extension(PasExtensions.InfoCancelled);
            cancelled.ValueBoolean = true;
            item.ModifierExtension.Add(cancelled);
        }
        else if (input.Version > 1 && !input.PreviousOrderIds.Contains(orderId))
        {
            Extension added = new Extension(PasExtensions.InfoChanged);
            added.ValueCode = PasCodes.Added;
            item.Extension.Add(added);
        }
        return item;
    }

    // ================================================================== inquiry Bundle

    /// <summary>Builds a $inquire Bundle from the last request Bundle: same Claim identifier and lines, no attachments.</summary>
    public Bundle BuildInquiry(string lastRequestBundleJson)
    {
        Bundle? lastRequest = FhirJson.Deserialize<Bundle>(lastRequestBundleJson);
        if (lastRequest == null || lastRequest.Entry.Count == 0)
        {
            throw new ApiException(500, "The stored prior authorization request (PA_PRIOR_AUTH.LAST_REQUEST_BUNDLE) is empty; the payer cannot be asked about it.");
        }
        Claim? claim = FhirJson.Read<Claim>(lastRequest.Entry[0].Resource);
        if (claim == null || FhirJson.ResourceType(lastRequest.Entry[0].Resource) != "Claim")
        {
            throw new ApiException(500, "The stored prior authorization request (PA_PRIOR_AUTH.LAST_REQUEST_BUNDLE) does not start with a Claim; the payer cannot be asked about it.");
        }

        claim.Meta = new Meta();
        claim.Meta.Profile.Add(PasProfiles.ClaimInquiry);
        claim.Created = FhirDate.Now();
        claim.SupportingInfo.Clear();
        foreach (ClaimItem item in claim.Item)
        {
            item.InformationSequence.Clear();
        }

        Bundle inquiry = NewCollectionBundle(PasProfiles.InquiryRequestBundle);
        AddEntry(inquiry, FhirJson.ToNode(claim));
        for (int i = 1; i < lastRequest.Entry.Count; i++)
        {
            BundleEntry entry = lastRequest.Entry[i];
            string? type = FhirJson.ResourceType(entry.Resource);
            if (type == "QuestionnaireResponse" || type == "DocumentReference")
            {
                continue;   // attachments are not part of an inquiry
            }
            inquiry.Entry.Add(entry);
        }
        return inquiry;
    }

    // ================================================================== helpers

    /// <summary>
    /// The orders a DTR form is for: every qr-context reference of the form that is not the Coverage.
    /// A form can be for one order or for several; an empty list means the form names no order.
    /// </summary>
    public static List<string> FormOrderIds(JsonObject form)
    {
        List<string> orderIds = new List<string>();
        QuestionnaireResponse? typed = FhirJson.Read<QuestionnaireResponse>(form);
        if (typed == null)
        {
            return orderIds;
        }
        foreach (Extension extension in typed.Extension)
        {
            if (extension.Url != DtrCodes.QrContextUrl || extension.ValueReference == null)
            {
                continue;
            }
            if (extension.ValueReference.GetResourceType() == "Coverage")
            {
                continue;
            }
            string? id = extension.ValueReference.GetId();
            if (id != null && !orderIds.Contains(id))
            {
                orderIds.Add(id);
            }
        }
        return orderIds;
    }

    /// <summary>X12 1365 service type for Claim.item.category, from the order type and code.</summary>
    public static string ServiceType(OrderResource order)
    {
        string code = string.Empty;
        CodeableConcept? concept = order.GetCode();
        if (concept != null && concept.FirstCode() != null)
        {
            code = concept.FirstCode()!.Trim().ToUpperInvariant();
        }

        if (order.ResourceType == OrderResource.MedicationRequestType)
        {
            return X12Codes.Pharmacy;
        }
        // a service order for equipment or supplies: HCPCS codes that start with E or K
        if (code.StartsWith("E") || code.StartsWith("K"))
        {
            return X12Codes.DmePurchase;
        }

        int cpt;
        if (code.Length == 5 && int.TryParse(code, out cpt))
        {
            if (cpt >= 90935 && cpt <= 90999)
            {
                return X12Codes.Dialysis;
            }

            if (cpt >= 97010 && cpt <= 97799)
            {
                return X12Codes.PhysicalTherapy;
            }

            if (cpt >= 70010 && cpt <= 79999)
            {
                return X12Codes.DiagnosticXRay;
            }

            if (cpt >= 10004 && cpt <= 69990)
            {
                return X12Codes.Surgical;
            }
        }
        return X12Codes.MedicalCare;
    }

    /// <summary>
    /// The payer as an Organization inside the Bundle (Claim.insurer).
    /// Name: the payer's name on the patient's insurance (Coverage.payor.display), else the payer's display name.
    /// Identifier: the payer id of the patient's insurance (Coverage.payor.identifier, i.e. the payer id the EHR keeps
    /// with the insurance), sent exactly as the FHIR API returned it (the system only when the insurance has one).
    /// When the insurance has no payer id, no identifier is sent: the gateway never makes one up. (PAS allows an
    /// insurer without identifier; CDex then also sends no PayerId, so both stay the same.)
    /// </summary>
    private static JsonObject BuildInsurer(PasBuildInput input, string insurerId)
    {
        PayerSettings payer = input.Payer;

        JsonObject insurer = new JsonObject();
        insurer["resourceType"] = "Organization";
        insurer["id"] = insurerId;
        insurer["active"] = true;
        insurer["name"] = input.Coverage.PayorDisplay() ?? payer.GetDisplayName();

        Identifier? payorId = input.Coverage.PayorIdentifier();
        if (payorId != null && !string.IsNullOrWhiteSpace(payorId.Value))
        {
            JsonObject identifier = new JsonObject();
            if (!string.IsNullOrWhiteSpace(payorId.System))
            {
                identifier["system"] = payorId.System;
            }
            identifier["value"] = payorId.Value;

            JsonArray identifiers = new JsonArray();
            identifiers.Add(identifier);
            insurer["identifier"] = identifiers;
        }
        return insurer;
    }

    /// <summary>
    /// Copy of the Coverage for the Bundle: Coverage.payor points at the insurer Organization inside the Bundle
    /// (a Coverage that came without payor gets that reference, because FHIR requires a payor).
    /// The member id and the payer id stay exactly as the FHIR API returned them (the values of the patient's insurance).
    /// </summary>
    private static Coverage CoverageForBundle(Coverage source, string insurerId)
    {
        Coverage? copy = FhirJson.Read<Coverage>(FhirJson.ToNode(source));   // copy, the original stays unchanged
        Coverage coverage = copy ?? new Coverage();
        foreach (ResourceReference payor in coverage.Payor)
        {
            payor.Reference = "Organization/" + insurerId;
        }
        if (coverage.Payor.Count == 0)
        {
            coverage.Payor.Add(new ResourceReference("Organization/" + insurerId));
        }
        return coverage;
    }

    /// <summary>The PAS careTeamClaimScope extension with value true: the care team member is for the whole request.</summary>
    private static Extension WholeRequestScope()
    {
        Extension scope = new Extension(PasExtensions.CareTeamClaimScope);
        scope.ValueBoolean = true;
        return scope;
    }

    /// <summary>Typed view of a raw order.</summary>
    private static OrderResource ReadOrder(JsonObject raw)
    {
        OrderResource? order = FhirJson.Read<OrderResource>(raw);
        return order ?? new OrderResource();
    }

    /// <summary>X12 1322 certification type of a line: 3 = cancel, R = renewal, I = initial.</summary>
    private static string CertificationCode(PasBuildInput input, PasOrderLine line)
    {
        if (line.Cancelled)
        {
            return X12Codes.CertificationCancel;
        }
        if (input.RequestType == PasCodes.RequestTypeRenewal)
        {
            return X12Codes.CertificationRenewal;
        }
        return X12Codes.CertificationInitial;
    }

    /// <summary>
    /// The request-form header facts that have no own PAS element, as one line of text for the reviewer:
    /// transient patient, and the previous authorization's dates and number of treatments. Null when there is nothing to say.
    /// </summary>
    private static string? HeaderText(PasBuildInput input)
    {
        List<string> parts = new List<string>();
        if (input.PatientType == PasCodes.PatientTypeTransient)
        {
            parts.Add("Transient patient (visiting this facility)");
        }
        if (input.RequestType == PasCodes.RequestTypeRenewal)
        {
            string text = "Renewal of authorization " + (input.PreviousAuthorizationNumber ?? "(number not given)");
            if (!string.IsNullOrWhiteSpace(input.PreviousServiceStart) || !string.IsNullOrWhiteSpace(input.PreviousServiceEnd))
            {
                text = text + ", previous DOS " + (input.PreviousServiceStart ?? "?") + " to " + (input.PreviousServiceEnd ?? "?");
            }
            if (input.PreviousTreatments != null)
            {
                text = text + ", " + input.PreviousTreatments.Value + " treatments";
            }
            parts.Add(text);
        }
        if (parts.Count == 0)
        {
            return null;
        }
        string all = string.Join(". ", parts) + ".";
        return all.Length > 264 ? all.Substring(0, 264) : all;   // X12 MSG01 limit
    }

    /// <summary>Adds a supportingInfo entry pointing to a form or document; returns its sequence number.</summary>
    private static int AddSupportingInfo(Claim claim, string reference)
    {
        ClaimSupportingInfo info = new ClaimSupportingInfo();
        info.Sequence = claim.SupportingInfo.Count + 1;
        info.Category = new CodeableConcept(CodeSystems.PasTemp, PasCodes.AdditionalInformation);
        info.ValueReference = new ResourceReference(reference);
        claim.SupportingInfo.Add(info);
        return info.Sequence;
    }

    /// <summary>An empty "collection" Bundle with identifier and timestamp (required by PAS).</summary>
    private Bundle NewCollectionBundle(string profile)
    {
        Bundle bundle = new Bundle();
        bundle.Id = Guid.NewGuid().ToString("N");
        bundle.Meta = new Meta();
        bundle.Meta.Profile.Add(profile);
        bundle.Type = "collection";
        bundle.Timestamp = FhirDate.Now();
        bundle.Identifier = new Identifier(_workflow.BundleIdentifierSystem, Guid.NewGuid().ToString("N"));
        return bundle;
    }

    /// <summary>Adds a resource with fullUrl = {FHIR API}/{type}/{id}.</summary>
    private void AddEntry(Bundle bundle, JsonObject resource)
    {
        BundleEntry entry = new BundleEntry();
        entry.FullUrl = _fhir.GetBaseUrl() + "/" + FhirJson.ResourceType(resource) + "/" + FhirJson.Id(resource);
        entry.Resource = FhirJson.Copy(resource);
        bundle.Entry.Add(entry);
    }

    /// <summary>"system|code" of a diagnosis (to number each diagnosis once).</summary>
    private static string DiagnosisKey(CodeableConcept diagnosis)
    {
        Coding? coding = diagnosis.FirstCoding();
        if (coding != null)
        {
            return coding.System + "|" + coding.Code;
        }
        return diagnosis.Text ?? string.Empty;
    }

    /// <summary>Sort order of Claim items: by sequence number.</summary>
    private static int CompareItems(ClaimItem first, ClaimItem second)
    {
        return first.Sequence.CompareTo(second.Sequence);
    }

    /// <summary>FHIR ids allow letters, digits, '-' and '.', max 64 characters (56 kept, room for "-vN").</summary>
    private static string SafeId(string id)
    {
        StringBuilder safe = new StringBuilder();
        foreach (char ch in id)
        {
            if (safe.Length >= 56)
            {
                break;
            }
            if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '.')
            {
                safe.Append(ch);
            }
            else
            {
                safe.Append('-');
            }
        }
        return safe.ToString();
    }
}
