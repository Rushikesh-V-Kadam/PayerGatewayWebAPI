# Payer Gateway Web API: CDex Guide (Step 4, additional documents)

**Project:** `PayerGatewayWebAPI` (port 5200, Swagger at `/swagger`)
**Standards:** Da Vinci CDex 2.0.0 (`$submit-attachment`, Task-based attachment request), PAS 2.0.1 Task, FHIR R4
**Controller:** `CdexController`
**Other guides:** `CRD-API-Guide.md`, `DTR-Forms-API-Guide.md`, `DTR-SmartApp-API-Guide.md`, `PAS-API-Guide.md`

This guide replaces the earlier CDex guide and follows the current code. Section 11 lists the code changes it reflects.

> **Updated Oct 9, 2026 (later the same day).** The payer id and the member id now come only from the patient's insurance in the EHR; table `PA_PAYER` no longer has `PAYER_IDENTIFIER`, `MEMBER_ID_SYSTEM` and `PAYER_ID_SYSTEM`. Orders are service and medication orders only (no device orders). When a patient has more than one active insurance and none is chosen, the gateway answers 422 instead of picking one. The Word guides named "PayerGatewayWebAPI - ... API Guide.docx" in this folder are the complete, current description.

---

## 1. What this step does

CDex is used **only when the payer has pended a prior authorization and asks for more**. The first documents and DTR forms already went with the PAS request.

1. The payer creates a **Task**: which document types (LOINC codes) and / or which questionnaires it needs, a tracking id and a due date.
2. The gateway receives the Task and shows it to the EHR as a simple **information request**.
3. The EHR sends the ids of the documents and completed forms.
4. The gateway sends the content to the payer with **`$submit-attachment`**.
5. The payer reviews again. The gateway asks for the new decision and returns it.

```
 EHR                        Payer Gateway (5200)                 FHIR API (5100)              Payer
  |                                |                                   |                        |
  |   the PAS request is "pended"  |                                   |                        |
  |                                |  A. GET {payer}/fhir/Task?owner={organization NPI}         |
  |                                |     (when the EHR calls ...?refresh=true)                  |
  |                                |----------------------------------------------------------->|
  |                                |  B. POST /api/v1/cdex/tasks/{payerName}  (payer pushes)    |
  |                                |<-----------------------------------------------------------|
  |                                |  save PA_CDEX_TASK, add to PA_PRIOR_AUTH.DECISION_JSON     |
  | GET .../information-requests   |                                   |                        |
  |------------------------------->|                                   |                        |
  | 200: what the payer needs      |                                   |                        |
  |<-------------------------------|                                   |                        |
  |                                |                                   |                        |
  | POST .../attachments           |                                   |                        |
  | (documentIds, form ids)        |                                   |                        |
  |------------------------------->|  read DocumentReference + file,   |                        |
  |                                |  QuestionnaireResponse            |                        |
  |                                |---------------------------------->|                        |
  |                                |  POST {payer}/fhir/$submit-attachment                      |
  |                                |----------------------------------------------------------->|
  |                                |  final = true: POST {payer}/fhir/Claim/$inquire            |
  |                                |----------------------------------------------------------->|
  | 200: sent + the new decision   |                                   |                        |
  |<-------------------------------|                                   |                        |
```

---

## 2. Endpoints at a glance

| # | Endpoint | In one sentence | Called by |
|---|---|---|---|
| 1 | `GET /api/v1/cdex/requests/{requestId}/information-requests` | What the payer asked for on a pended request | EHR |
| 2 | `POST /api/v1/cdex/requests/{requestId}/attachments` | Sends documents and / or completed forms to the payer | EHR |
| 3 | `POST /api/v1/cdex/tasks/{payerName}` | The payer pushes a Task that asks for more information | Payer |

`requestId` is the EHR's prior authorization request id from the PAS step.

---

## 3. Common rules

- **Base URL:** `http://localhost:5200` locally.
- **Format:** `Content-Type: application/json`, camelCase. Endpoint 3 also accepts `application/fhir+json`.
- **Authentication:** none is built in. It is added outside this code.
- **Errors:** `{ "error": "...", "details": ["..."], "traceId": "..." }`. One exception: when the **payer rejects** the attachments, the body is the attachment result with `status = "rejected"` (section 4.2).

---

## 4. Endpoints

### 4.1 `GET /api/v1/cdex/requests/{requestId}/information-requests`: what does the payer need?

**Why we use it.** The payer's request is a FHIR Task. This call gives the EHR the same information in plain JSON: tracking id, due date, document types and questionnaires.

**Request**

```http
GET /api/v1/cdex/requests/PA-2026-0102/information-requests?refresh=true
```

| Parameter | Default | Meaning |
|---|---|---|
| `refresh` | `false` | `false` = return what is stored. `true` = search the payer's Tasks now, then return the result |

`refresh = true` calls the payer only when the request is still `pended`. For any other status the stored list is returned.

**Response 200** (example)

```json
[
  {
    "trackingId": "TRK-3f9a1c2b7d4e5f60",
    "taskId": "5b2c9a7e4d1f4c0e8a36f1d2c3b4a5e6",
    "dueDate": "2026-10-23",
    "status": "requested",
    "documentsNeeded": [
      { "loincCode": "11502-2", "display": null, "lineNumber": 1 }
    ],
    "questionnairesNeeded": []
  }
]
```

| Field | Meaning |
|---|---|
| `trackingId` | The payer's tracking id. Send it back with the attachments |
| `taskId` | Id of the payer's Task |
| `dueDate` | Date by which the payer wants the information |
| `status` | The Task status from the payer: `requested`, `in-progress`, `completed`, `cancelled` ... The gateway sets `completed` (final) or `in-progress` after it sent the attachments |
| `documentsNeeded` | Document types the payer needs, as LOINC codes. `lineNumber` = the claim line it is for, when the payer said |
| `questionnairesNeeded` | Payer questionnaires to complete (URLs) |

An empty list `[]` means the payer has not asked for anything (yet).

**The same list is inside the PAS decision** (`informationRequests`), so `GET /api/v1/pas/requests/{requestId}` and `PA_PRIOR_AUTH.DECISION_JSON` show it too.

**What to do with each request**

| The payer needs | What the EHR does |
|---|---|
| `documentsNeeded` | Find the EHR documents of those LOINC types (for example `11502-2` = laboratory report) and send their ids in `documentIds` |
| `questionnairesNeeded` | Fill in the questionnaire with DTR (below), then send the `questionnaireResponseId` in `questionnaireResponseIds` |

Questionnaire through the gateway forms (see `DTR-Forms-API-Guide.md`):

```http
POST /api/v1/dtr/forms
Content-Type: application/json

{
  "payerName": "MockPayer",
  "patientId": "pat-1",
  "orderIds": ["ord-1-hd"],
  "questionnaireUrls": ["http://example.org/fhir/Questionnaire/dialysis-incenter-hd"],
  "coverageAssertionId": "TRK-3f9a1c2b7d4e5f60"
}
```

Then `POST /api/v1/dtr/forms/{formId}/answers`, which returns the `questionnaireResponseId`.

| HTTP | When |
|---|---|
| 200 | The information requests (may be empty) |
| 404 | Unknown `requestId` |
| 502 | `refresh = true` and the payer could not be reached |
| 503 | The EHR data API is not available |
| 504 | `refresh = true` and the payer did not answer in time |

Note: with `refresh = true`, a payer that answers the Task search with an HTTP error (4xx / 5xx) does not cause an error here. The problem is written to the log and the stored list is returned.

---

### 4.2 `POST /api/v1/cdex/requests/{requestId}/attachments`: send documents and forms

**Why we use it.** CDex says additional documentation for a prior authorization is sent with `$submit-attachment`. The gateway builds the FHIR `Parameters` with the file content inside, so the EHR only sends ids.

**Scenario.** Request `PA-2026-0102` for in-center hemodialysis is pended. The payer asks for a laboratory report (LOINC 11502-2) for line 1. The EHR sends document `doc-1-labs`.

#### Request

| Field | Required | Default | Meaning |
|---|---|---|---|
| `trackingId` | No | | Tracking id of the payer's information request. Empty = the latest open one; when there is none, the attachment is sent as unsolicited |
| `documentIds` | One of the two | `[]` | EHR document ids (DocumentReference ids at the FHIR API) |
| `questionnaireResponseIds` | One of the two | `[]` | QuestionnaireResponse ids (from DTR) |
| `lineNumbers` | No | `[]` | Claim line numbers the attachments support. Empty = the whole request |
| `final` | No | `true` | `true` = nothing more will be sent; the payer reviews again now. `false` = more will follow |

```http
POST /api/v1/cdex/requests/PA-2026-0102/attachments
Content-Type: application/json

{
  "trackingId": "TRK-3f9a1c2b7d4e5f60",
  "documentIds": ["doc-1-labs"],
  "questionnaireResponseIds": [],
  "lineNumbers": [1],
  "final": true
}
```

The shortest request that works:

```json
{ "documentIds": ["doc-1-labs"] }
```

#### Which tracking id is sent

| Situation | `TrackingId` sent to the payer |
|---|---|
| `trackingId` is given and belongs to a Task of this request | That id, with the identifier system the payer used |
| `trackingId` is given but is not a Task of this request | Nothing is sent: 400 |
| `trackingId` is empty and there is an open information request | The latest one that is not `completed` or `cancelled` |
| `trackingId` is empty and nothing is open, or `trackingId` equals `requestId` | **Unsolicited:** the EHR's own `requestId` (system `Workflow:ClaimIdentifierSystem`). The payer matches it to the Claim identifier |

#### What the gateway does

1. Reads the stored request (`PA_PRIOR_AUTH`). It needs the member id saved at the PAS submit (`MemberId` is 1..1), and the facility NPI or the practitioner NPI (`OrganizationId` / `ProviderId`: one of them SHALL be present); each NPI is sent when it was saved.
2. Chooses the tracking id.
3. Reads each document from the FHIR API **with its file embedded**, and each QuestionnaireResponse.
4. Calls `POST {payer BASE_URL}/{FHIR_PATH}/$submit-attachment`.
5. Marks the information request `completed` (`final = true`) or `in-progress` (`final = false`) and saves it.
6. With `final = true`: runs `Claim/$inquire` and puts the new decision in `decision`.

The request does not have to be `pended`: the gateway sends attachments for any stored request.

#### What the gateway sends to the payer

```json
{
  "resourceType": "Parameters",
  "meta": { "profile": ["http://hl7.org/fhir/us/davinci-cdex/StructureDefinition/cdex-parameters-submit-attachment"] },
  "parameter": [
    { "name": "TrackingId", "valueIdentifier": { "system": "http://example.org/cdex/tracking-ids", "value": "TRK-3f9a1c2b7d4e5f60" } },
    { "name": "AttachTo", "valueCode": "preauthorization" },
    { "name": "PayerId", "valueIdentifier": { "value": "PAYER001" } },
    { "name": "OrganizationId", "valueIdentifier": { "system": "http://hl7.org/fhir/sid/us-npi", "value": "1999999992" } },
    { "name": "ProviderId", "valueIdentifier": { "system": "http://hl7.org/fhir/sid/us-npi", "value": "1111111112" } },
    { "name": "MemberId", "valueIdentifier": { "value": "MBR-1001" } },
    {
      "name": "Attachment",
      "part": [
        { "name": "LineItem", "valueString": "1" },
        { "name": "Code", "valueCodeableConcept": { "coding": [ { "system": "http://loinc.org", "code": "11502-2", "display": "Laboratory report" } ] } },
        {
          "name": "Content",
          "resource": {
            "resourceType": "DocumentReference",
            "id": "doc-1-labs",
            "content": [ { "attachment": { "contentType": "text/plain", "data": "TU9OVEhMWSBESUFMWVNJUyBMQUJTIC4uLg==" } } ],
            "...": "..."
          }
        }
      ]
    },
    { "name": "Final", "valueBoolean": true }
  ]
}
```

| Parameter | Where it comes from |
|---|---|
| `TrackingId` | See the table above |
| `AttachTo` | Always `preauthorization` |
| `PayerId` | The payer id of the patient's insurance, as sent in the PAS request. Left out when the insurance has none |
| `OrganizationId` | NPI of the requesting organization (saved at the PAS submit). Left out when the organization has no NPI |
| `ProviderId` | NPI of the requesting practitioner, when known |
| `MemberId` | The member id of the patient's insurance, as sent in the PAS request. No identifier system is added |
| `Attachment` | One per document and one per form |
| `Attachment.LineItem` | One per number in `lineNumbers` |
| `Attachment.Code` | Document: the LOINC code of its type. Form: LOINC `102089-0` |
| `Attachment.Content` | The DocumentReference (file in `attachment.data`, the link to our server removed) or the QuestionnaireResponse |
| `Final` | `final` |

#### Response 200 (example)

```json
{
  "requestId": "PA-2026-0102",
  "trackingId": "TRK-3f9a1c2b7d4e5f60",
  "status": "sent",
  "attachmentCount": 1,
  "messages": [],
  "decision": {
    "requestId": "PA-2026-0102",
    "payerName": "MockPayer",
    "patientId": "pat-1",
    "orderIds": ["ord-1-hd"],
    "overallStatus": "approved",
    "authorizationNumber": "PA20261009482011",
    "validFrom": "2026-10-05",
    "validTo": "2027-01-03",
    "decidedAt": "2026-10-09T05:40:12Z",
    "decisionDueBy": "2026-10-16T05:30:00Z",
    "lines": [
      {
        "sequence": 1,
        "orderId": "ord-1-hd",
        "code": "90935",
        "codeSystem": "http://www.ama-assn.org/go/cpt",
        "decision": "approved",
        "x12ActionCode": "A1",
        "requestedQuantity": 39,
        "approvedQuantity": null,
        "reason": "Line 1: Approved.",
        "validFrom": "2026-10-05",
        "validTo": "2027-01-03"
      }
    ],
    "informationRequests": [
      {
        "trackingId": "TRK-3f9a1c2b7d4e5f60",
        "taskId": "5b2c9a7e4d1f4c0e8a36f1d2c3b4a5e6",
        "dueDate": "2026-10-23",
        "status": "completed",
        "documentsNeeded": [ { "loincCode": "11502-2", "display": null, "lineNumber": 1 } ],
        "questionnairesNeeded": []
      }
    ],
    "errors": []
  }
}
```

| Field | Meaning |
|---|---|
| `status` | `sent` or `rejected` |
| `attachmentCount` | Documents + forms sent |
| `messages` | Messages from the payer's `OperationOutcome` |
| `decision` | The decision after the payer reviewed again. `null` when `final = false`, or when the inquiry after the send failed |

When `decision` is `null` or still `pended`, the EHR keeps checking the request on its own schedule: `POST /api/v1/pas/requests/{requestId}/inquire` for the decision and `GET .../information-requests?refresh=true` for new Tasks (section 6). The gateway has no background job.

#### When the payer rejects the attachments

```json
{
  "requestId": "PA-2026-0102",
  "trackingId": "TRK-3f9a1c2b7d4e5f60",
  "status": "rejected",
  "attachmentCount": 1,
  "messages": ["Unknown tracking id TRK-3f9a1c2b7d4e5f60."],
  "decision": null
}
```

| Payer answered with | Gateway returns | Body |
|---|---|---|
| HTTP 4xx | **422** | Attachment result, `status = "rejected"` |
| HTTP 5xx | **502** | Attachment result, `status = "rejected"` |
| Nothing (could not be reached) | **502** | Normal error body |
| Nothing in time | **504** | Normal error body |

#### Errors

| HTTP | When |
|---|---|
| 200 | Sent to the payer |
| 400 | No `documentIds` and no `questionnaireResponseIds`; or `trackingId` is not an information request of this request |
| 404 | Unknown `requestId`; a document or a form was not found at the FHIR API |
| 422 | The request has no member id, or neither a facility NPI nor a practitioner NPI; the payer is no longer configured; or the payer rejected the attachments (result body) |
| 502 | The payer or the FHIR API failed |
| 503 | The EHR data API is not available |
| 504 | The payer did not answer in time |

---

### 4.3 `POST /api/v1/cdex/tasks/{payerName}`: called by the payer

**Why it exists.** A payer may send its Task to the provider instead of waiting to be asked. Give the payer this URL: `{Workflow:GatewayPublicBaseUrl}/api/v1/cdex/tasks/{payerName}`.

**Request** (from the payer; a `Task`, or a `Bundle` of Tasks; `application/fhir+json` or `application/json`)

```http
POST /api/v1/cdex/tasks/MockPayer
Content-Type: application/fhir+json

{
  "resourceType": "Task",
  "id": "5b2c9a7e4d1f4c0e8a36f1d2c3b4a5e6",
  "meta": { "profile": ["http://hl7.org/fhir/us/davinci-pas/StructureDefinition/profile-task"] },
  "identifier": [
    {
      "type": { "coding": [ { "system": "http://hl7.org/fhir/us/davinci-cdex/CodeSystem/cdex-temp", "code": "tracking-id", "display": "Tracking Id" } ] },
      "system": "http://example.org/cdex/tracking-ids",
      "value": "TRK-3f9a1c2b7d4e5f60"
    }
  ],
  "status": "requested",
  "intent": "order",
  "code": { "coding": [ { "system": "http://hl7.org/fhir/us/davinci-pas/CodeSystem/PASTempCodes", "code": "attachment-request-code" } ] },
  "owner": { "identifier": { "system": "http://hl7.org/fhir/sid/us-npi", "value": "1999999992" } },
  "reasonReference": {
    "type": "Claim",
    "identifier": { "system": "http://provider.example.org/fhir/sid/prior-auth-request-id", "value": "PA-2026-0102" }
  },
  "restriction": { "period": { "end": "2026-10-23" } },
  "input": [
    {
      "type": { "coding": [ { "system": "http://hl7.org/fhir/us/davinci-pas/CodeSystem/PASTempCodes", "code": "payer-url" } ] },
      "valueUrl": "http://localhost:5080/fhir/$submit-attachment"
    },
    {
      "extension": [ { "url": "http://hl7.org/fhir/us/davinci-pas/StructureDefinition/extension-paLineNumber", "valueInteger": 1 } ],
      "type": { "coding": [ { "system": "http://hl7.org/fhir/us/davinci-pas/CodeSystem/PASTempCodes", "code": "attachments-needed" } ] },
      "valueCodeableConcept": { "coding": [ { "system": "http://loinc.org", "code": "11502-2" } ] }
    }
  ]
}
```

**Response 202**

```json
{ "status": "accepted", "count": 1 }
```

`count` = the number of Tasks that matched a request of this gateway.

| HTTP | When |
|---|---|
| 202 | At least one Task was accepted |
| 400 | The body is not a Task or a Bundle |
| 404 | `payerName` is not configured or not enabled |
| 422 | No Task in the body matches a prior authorization request sent by this gateway |
| 503 | The EHR data API is not available |

---

## 5. How a Task becomes an information request

The same code handles a Task from the search and a pushed Task.

**Matching the Task to a request.** A Task is accepted when all of these are true; otherwise it is skipped:

| Check | Task element |
|---|---|
| It has an id | `Task.id` |
| It names one of our requests | `Task.reasonReference.identifier.value` (else `Task.focus.identifier.value`) = the EHR's `requestId` |
| It has a tracking id | The `Task.identifier` whose type is `tracking-id`, else the first identifier |
| The request belongs to this payer | The stored request's payer = the payer of the search or of the URL |

**Reading the Task**

| Information request field | Task element |
|---|---|
| `trackingId` | The tracking identifier's `value` (its `system` is kept and sent back unchanged) |
| `taskId` | `Task.id` |
| `dueDate` | `Task.restriction.period.end` |
| `status` | `Task.status` (empty = `requested`) |
| `documentsNeeded` | Each `Task.input` of type `attachments-needed`: the LOINC coding of `valueCodeableConcept` |
| `documentsNeeded.lineNumber` | The input's `paLineNumber` extension, else `serviceLineNumber` |
| `questionnairesNeeded` | Each `Task.input` of type `questionnaires-needed` or `questionnaire`: `valueCanonical`, else `valueIdentifier.value`. Each URL once |

There is one information request per tracking id on a decision. A Task with a tracking id that is already there replaces the earlier entry.

The Task's `payer-url` input is not used. Attachments always go to the payer URL configured in `PA_PAYER`.

---

## 6. How the gateway finds new Tasks

| Way | When | What happens |
|---|---|---|
| **Refresh (called by the EHR)** | The EHR calls `GET .../information-requests?refresh=true` for each request with `STATUS = 'pended'` in `PA_PRIOR_AUTH`, on its own schedule | `GET {payer}/fhir/Task?owner={organization NPI}` (skipped when the request's organization has no NPI) |
| **Push** | The payer calls `POST /api/v1/cdex/tasks/{payerName}` | The Task is processed at once |

The gateway has no background job. Not every payer pushes Tasks, so the EHR's refresh call is what makes sure a request for more documents is not missed. `POST /api/v1/pas/requests/{requestId}/inquire` (PAS guide) gets the decision but does not find Tasks, so the EHR calls both for each pended request.

Both save the Task in `PA_CDEX_TASK` and update `informationRequests` in `PA_PRIOR_AUTH.DECISION_JSON`. The EHR can read it there; the gateway does not send a second copy.

---

## 7. A complete example

```http
### 1. the PAS request was pended (see PAS-API-Guide.md, section 12)

### 2. what does the payer need?
GET http://localhost:5200/api/v1/cdex/requests/PA-2026-0102/information-requests?refresh=true

### 3. send the laboratory report; final = true asks the payer for the new decision
POST http://localhost:5200/api/v1/cdex/requests/PA-2026-0102/attachments
Content-Type: application/json

{
  "documentIds": ["doc-1-labs"],
  "lineNumbers": [1],
  "final": true
}

### 4. the decision later (when "decision" in step 3 was null or still pended)
GET http://localhost:5200/api/v1/pas/requests/PA-2026-0102
```

In the mock data, CPT 90935 (`ord-1-hd`) needs a laboratory report (LOINC 11502-2). `doc-1-labs` is a laboratory report of `pat-1`; `doc-1-note` is a progress note (LOINC 11506-3).

Sending in two steps:

```http
### first part: more will follow
POST http://localhost:5200/api/v1/cdex/requests/PA-2026-0102/attachments
Content-Type: application/json

{ "documentIds": ["doc-1-labs"], "final": false }

### last part: the payer reviews now
POST http://localhost:5200/api/v1/cdex/requests/PA-2026-0102/attachments
Content-Type: application/json

{ "documentIds": ["doc-1-note"], "final": true }
```

After the first call the information request is `in-progress`, so the second call still finds it as the open one.

---

## 8. Where the data is stored

| Table | Key | What |
|---|---|---|
| `PA_CDEX_TASK` | `PAYER_NAME` + `TASK_ID` | Each Task as received (`TASK_JSON`), with `REQUEST_ID`, `TRACKING_ID`, `TRACKING_SYSTEM`, `STATUS`, `DUE_DATE` |
| `PA_PRIOR_AUTH` | `REQUEST_ID` | `DECISION_JSON` holds `informationRequests`; `MEMBER_ID`, `ORGANIZATION_NPI`, `PRACTITIONER_NPI` are used for the `$submit-attachment` header |

Both are in the EHR's Oracle database, through the EHR data API (`docs/EHR-Data-API.md`).

---

## 9. Settings that affect CDex

| Setting | Default | Meaning |
|---|---|---|
| `PA_PAYER.BASE_URL` + `FHIR_PATH` | `fhir` | Where `Task` and `$submit-attachment` are |
| `PA_PAYER.TIMEOUT_SECONDS` | 30 | Seconds to wait for the payer |
| The payer id of the patient's insurance (FHIR API, `Coverage.payor.identifier`) | | `PayerId` |
| The member id of the patient's insurance (FHIR API, `Coverage.subscriberId`) | | `MemberId` |
| `Workflow:ClaimIdentifierSystem` | `http://provider.example.org/fhir/sid/prior-auth-request-id` | System of the tracking id of an unsolicited attachment |
| `Workflow:GatewayPublicBaseUrl` | `http://localhost:5200` | Start of the push URL given to payers |

CDex uses FHIR R4, like every other step.

---

## 10. Things to know

- **File size.** The file content is sent inside the JSON (base64). Large files make large requests; check the payer's limit.
- **Document type.** `Attachment.Code` comes from the document's LOINC type at the FHIR API. A document without a LOINC type is sent without `Code`, and the payer may reject it.
- **Line numbers** are the `sequence` values of the PAS decision lines.
- **A form for CDex** is not picked up automatically (PAS does that, CDex does not). Send its id in `questionnaireResponseIds`.
- **The decision after the send.** A failed `$inquire` after a successful send does not fail the call: the attachments were sent, `decision` is `null`.

---

## 11. Code changes this guide reflects

If you still have the earlier CDex guide, these are the points where it no longer matches the code.

| Change | Where |
|---|---|
| No write-back. Information requests and decisions are saved in `PA_PRIOR_AUTH` in the EHR database, and the EHR reads them there | Sections 4.1 and 6 |
| No database of its own. Tasks are saved in `PA_CDEX_TASK` through the EHR data API. A new status 503 means the EHR data API is not available | Sections 3 and 8 |
| The payer is found by name in table `PA_PAYER` | Section 9 |
| No authentication on any call: the Task search, `$submit-attachment`, and the payer's push to `/api/v1/cdex/tasks/{payerName}` | Section 3 |
| No background job. The EHR calls `information-requests?refresh=true` (and PAS inquire) for each pended request. `Workflow:PollMinutes` is removed | Section 6 |
| Project and namespaces renamed to `PayerGatewayWebAPI` | Section 12 |

---

## 12. Code map

| File | What it does |
|---|---|
| `Controllers/CdexController.cs` | The three endpoints |
| `Services/Cdex/CdexService.cs` | Task search, pushed Tasks, `$submit-attachment` |
| `Services/Cdex/TaskReader.cs` | Task to information request |
| `Services/Pas/PasService.cs` | `InquireAsync` (the new decision after the send) |
| `Clients/FhirApiClient.cs` | `ReadDocumentWithContentAsync` (embeds the file) |
| `Repositories/Database/EhrApiCdexTaskRepository.cs`, `EhrApiPriorAuthRepository.cs` | `PA_CDEX_TASK` and `PA_PRIOR_AUTH` through the EHR data API |
| `Constants/CdexCodes.cs`, `PasCodes.cs`, `PasExtensions.cs` | Codes and URLs |
| `Models/Requests/AttachmentSubmitRequest.cs` | Request JSON |
| `Models/Responses/InformationRequest.cs`, `DocumentNeeded.cs`, `AttachmentResult.cs`, `NotificationResult.cs` | Response JSON |
