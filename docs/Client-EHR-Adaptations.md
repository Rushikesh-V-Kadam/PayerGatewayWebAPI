# Client EHR adaptations and required fields

The client's EHR is not complete for the Da Vinci workflows. This page lists **what the gateway really needs** in each
workflow (CRD, DTR, PAS, CDex) and **every place where the code works around the client's data**. In the code each such
place has a comment starting with `CLIENT-EHR:` (search for it in PayerGatewayWebAPI and FHIRAPI).

## The rule

Only what the standards make mandatory (minimum cardinality 1) can stop a request:

- FHIR R4 resources: [hl7.org/fhir/R4/resourcelist.html](https://hl7.org/fhir/R4/resourcelist.html) (each property of the
  gateway's FHIR classes has its cardinality in a `// R4:` comment, `Models/Fhir/`).
- The operations the payer is called with: CDS Hooks `order-sign` (CRD), `$questionnaire-package` (DTR),
  `$submit-attachment` (CDex).

Everything optional (0..1, 0..*) is **sent when the EHR has it and left out (null) when it does not**. A missing optional
value never rejects a request. The client has no real payer yet; a real payer may need some optional values (its PAS
profile and X12 278 rules), and the code is changed when that payer is onboarded.

## What the client's EHR does not have, and what the gateway does

| Missing in the client's EHR | What the gateway / FHIR API does | Code (`CLIENT-EHR:`) |
|---|---|---|
| Coverage (insurance) id | Not missing any more: the EHR sends its own unique coverage id (`coverageId`). The FHIR API reads that one insurance with `GET api/insurances/{coverageId}` and uses the id as `Coverage.id` | FHIRAPI `EhrClient.GetInsuranceAsync`, `FhirReadService.ReadCoverageAsync`, `AdministrativeMapper.ToCoverage` |
| Insurance on the orders | The EHR sends the patient's active insurance as `coverageId` (required in order-sign and DTR; PAS on submit). The gateway reads it with `GET Coverage/{coverageId}` and checks that it belongs to the patient and is active | `Services/OrderHelper.CoverageProblem`, `FhirApiClient.ReadCoverageAsync` |
| Organization NPI (the organization has only a tax id) | Each facility (location) has its own NPI. The FHIR API serves every location also as `Organization/facility-{locationId}` with the facility NPI, the parent's tax id and `partOf` = the parent. That facility is the requester | FHIRAPI `Helpers/FacilityIds.cs`, `AdministrativeMapper.ToFacilityOrganization`, `FhirReadService` |
| Which facility a request is for | PAS: `orderLocationCode` (required; `Claim.provider` is 1..1). Order-sign: no facility is sent (no field, and the client's orders carry none) | `Services/Pas/FacilityResolver.cs`, `OrderLocationCode` in `PriorAuthRequest` |
| Facility on a medication order | `MedicationRequest.performer` = the facility (R4 MedicationRequest has no location) | FHIRAPI `OrderMapper.ToMedicationRequest` |
| Practitioner id | The EHR sends the practitioner's **NPI** as `userPractitionerId` (CRD) and `requestingPractitionerId` (PAS). The FHIR API reads `GET api/practitioners/{npi}`; the id is also used as the NPI identifier when the answer has no `npi` field and the id is a valid NPI (10 digits, check digit). A name in one field (`name`) is sent as `HumanName.text` | FHIRAPI `Helpers/NpiNumbers.cs`, `AdministrativeMapper.ToPractitioner`; `OrderSignRequest`, `PriorAuthRequest` |
| Member id (policy number) | PAS is sent without it (`Coverage.subscriberId` 0..1). CDex needs it (see below) | `Services/Pas/PasService.cs` |

## What stops a request, per workflow

### CRD (order-sign)

| Required | Why | Missing → |
|---|---|---|
| `eventId`, `payerName`, at least one order | Gateway: the key of the event, which payer to call, what to check | 400 |
| Patient | CDS Hooks `context.patientId` 1..1 | 400 / 404 |
| Signing clinician `userPractitionerId` | CDS Hooks `context.userId` 1..1 | 400 |

Not required: insurance (without one the payer is not called and every order gets `covered = unknown`), encounter,
everything else on the orders.

### DTR (gateway forms)

| Required | Why | Missing → |
|---|---|---|
| `payerName`, `patientId`, at least one order | Gateway | 400 |
| Active insurance | `$questionnaire-package` input `coverage` 1..1 | 422 |

### PAS

| Required | Why | Missing → |
|---|---|---|
| `requestId`, `payerName`, at least one order | Gateway: the key for update / cancel / inquire / CDex, which payer, what to authorize | 400 |
| Patient | `Claim.patient` 1..1 | 400 / 404 |
| Facility `orderLocationCode` | `Claim.provider` 1..1 | 400 / 404 |
| Requesting clinician `requestingPractitionerId` | Gateway rule (`Claim.careTeam` is 0..* in R4) | 400 / 404 |
| Active insurance | `Claim.insurance` 1..*, `insurance.coverage` 1..1 | 422 |
| A code on every order (CPT / HCPCS / drug) | `Claim.item.productOrService` 1..1 | 422 |

Not required, sent only when present: member id, facility NPI and tax id, practitioner NPI, referring practitioner, diagnoses, renewed authorization
number, quantity, dates, place of service, forms, documents, notes. See `PAS-API-Guide.md`, "What is required".

### CDex ($submit-attachment)

| Required | Why | Missing → |
|---|---|---|
| Member id | `$submit-attachment` `MemberId` 1..1 | 422 |
| Facility NPI **or** practitioner NPI | `OrganizationId` and `ProviderId` are 0..1, but one of them SHALL be present | 422 |
| At least one document or form | `Attachment` 1..* | 400 |

`PayerId` (0..1) is sent only when the insurance has a payer id.

## Before a real payer is onboarded

- Ask the client to fill the **facility NPI** on every location and the **member id** on every insurance. Payers find
  the requester and the member by them, and CDex cannot work without them.
- Check the payer's companion guide for values it requires beyond FHIR R4 (for example `Claim.item.category`, which the
  gateway fills from the order code).
