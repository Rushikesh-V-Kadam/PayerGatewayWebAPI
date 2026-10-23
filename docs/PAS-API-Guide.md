# Payer Gateway Web API: PAS Guide (Step 3, prior authorization request)

**Project:** `PayerGatewayWebAPI` (port 5200, Swagger at `/swagger`)
**Standards:** Da Vinci PAS 2.0.1 (`Claim/$submit`, `Claim/$inquire`, PAS Subscription), FHIR R4
**Controller:** `PasController`
**Other guides:** `CRD-API-Guide.md`, `DTR-Forms-API-Guide.md`, `DTR-SmartApp-API-Guide.md`, `CDex-API-Guide.md`

This guide replaces the earlier PAS guide and follows the current code. Section 13 lists the code changes it reflects.

> **Updated Oct 9, 2026 (later the same day).** The payer id and the member id now come only from the patient's insurance in the EHR; table `PA_PAYER` no longer has `PAYER_IDENTIFIER`, `MEMBER_ID_SYSTEM` and `PAYER_ID_SYSTEM`. Orders are service and medication orders only (no device orders). When a patient has more than one active insurance and none is chosen, the gateway answers 422 instead of picking one. The Word guides named "PayerGatewayWebAPI - ... API Guide.docx" in this folder are the complete, current description.

---

## 1. What this step does

When order-sign says `callPas = true`, the EHR asks the payer for a prior authorization. The EHR sends plain JSON with ids. The gateway reads the FHIR data, builds the PAS Bundle, calls the payer and returns the decision in plain JSON.

```
 EHR                      Payer Gateway (5200)                   FHIR API (5100)              Payer
  |                              |                                     |                        |
  | POST /api/v1/pas/requests    |                                     |                        |
  |----------------------------->|  read Patient, orders, Coverage,    |                        |
  |                              |  Organization, Practitioner,        |                        |
  |                              |  completed DTR forms, documents     |                        |
  |                              |------------------------------------>|                        |
  |                              |  build the PAS request Bundle       |                        |
  |                              |  POST {payer}/fhir/Claim/$submit                             |
  |                              |------------------------------------------------------------->|
  |                              |  response Bundle with ClaimResponse                          |
  |                              |<-------------------------------------------------------------|
  |                              |  save PA_PRIOR_AUTH                 |                        |
  |                              |  POST {payer}/fhir/Subscription (once per payer + organization)
  |                              |------------------------------------------------------------->|
  | 201: decision                |                                     |                        |
  |<-----------------------------|                                     |                        |
  |                              |                                     |                        |
  |   later, when the request is pended (payer notification, or the EHR calls inquire):         |
  |                              |  POST /api/v1/pas/notifications/{payerName}  (from the payer) |
  |                              |<-------------------------------------------------------------|
  |                              |  POST {payer}/fhir/Claim/$inquire                             |
  |                              |------------------------------------------------------------->|
  |                              |  save the new decision in PA_PRIOR_AUTH                      |
  | GET /api/v1/pas/requests/{id}  (or read PA_PRIOR_AUTH)             |                        |
  |----------------------------->|                                     |                        |
```

**The first clinical documents and the DTR forms go with the PAS request.** CDex is used only when the payer pends the request and asks for more.

---

## 2. Endpoints at a glance

| # | Endpoint | In one sentence | Called by |
|---|---|---|---|
| 1 | `POST /api/v1/pas/requests` | Submits a new prior authorization request | EHR |
| 2 | `PUT /api/v1/pas/requests/{requestId}` | Changes a request: add / remove orders, add forms or documents | EHR |
| 3 | `POST /api/v1/pas/requests/{requestId}/cancel` | Cancels the whole request | EHR |
| 4 | `GET /api/v1/pas/requests/{requestId}` | Returns the latest stored decision (no payer call) | EHR |
| 5 | `POST /api/v1/pas/requests/{requestId}/inquire` | Asks the payer for the latest decision now | EHR |
| 6 | `POST /api/v1/pas/notifications/{payerName}` | The payer tells the gateway that a decision changed | Payer |

Every endpoint 1 to 5 returns the same object: the **decision** (section 5).

---

## 3. Common rules

- **Base URL:** `http://localhost:5200` locally.
- **Format:** `Content-Type: application/json`, camelCase. Endpoint 6 also accepts `application/fhir+json`.
- **Payer:** `payerName` must match `PA_PAYER.PAYER_NAME` (case does not matter) and the payer must be enabled.
- **Authentication:** none is built in. It is added outside this code.
- **Errors:** `{ "error": "...", "details": ["..."], "traceId": "..." }`. One exception: when the **payer rejects** a request, the body is a decision with `overallStatus = "error"` and the payer's messages in `errors` (section 4.1).
- **`requestId`** is the EHR's own id of the request. It becomes `Claim.identifier` and is the key for update, cancel, inquire and CDex.
- **The requesting facility (`Claim.provider`).** The client has many facilities, each with its own NPI; the parent organization has only a tax id. The EHR keeps each facility as a location (name, address, NPI). The FHIR API serves every location also as an Organization, `Organization/facility-{locationId}`, with the facility NPI, the parent's tax id and `partOf` = the parent organization. The gateway takes the facility from
  `orderLocationCode` (required): `Location/{orderLocationCode}` → `managingOrganization`. The client's orders carry no facility, so the orders' location is not read or compared. The payer subscription and the CDex Task search run per payer and facility NPI.


### What is required (FHIR R4 cardinality)

The gateway requires only what FHIR R4 itself requires ([resource list](https://hl7.org/fhir/R4/resourcelist.html): minimum cardinality 1). Everything optional (0..1 or 0..*) is **sent when the EHR has it and left out (null) when it does not**; a missing optional value never rejects a request. The client has no real payer yet; when one is onboarded, its own rules (the PAS profiles, X12 278) may make some optional fields necessary, and the code is changed then.

| Required in FHIR R4 | Comes from | Missing → |
|---|---|---|
| `Claim.status`, `use`, `type`, `created` | Set by the gateway (`active`, `preauthorization`, `professional`, now) | — |
| `Claim.patient` (1..1) | `patientId` | 400 empty, 404 not found |
| `Claim.provider` (1..1) | The facility of `orderLocationCode` | 400 / 404 |
| `Claim.priority` (1..1) | `priority` (default `routine`) | — |
| `Claim.insurance` (1..*) with `coverage` (1..1) | The patient's active insurance | 422 |
| `Claim.item.sequence`, `productOrService` (1..1 per item) | Line number; the order's CPT / HCPCS / drug code | 422 order without code |
| `Coverage.status`, `beneficiary` (1..1), `payor` (1..*) | The insurance; `payor` = the payer of `payerName` | — |
| `Claim.careTeam.sequence`, `provider` / `diagnosis.sequence`, `diagnosis[x]` / `supportingInfo.sequence`, `category` | Only when such an entry is sent; the gateway fills them | — |

Optional, sent only when present (null otherwise): member id (`Coverage.subscriberId`), facility NPI and tax id (`Organization.identifier`), referring practitioner (`Claim.careTeam`), practitioner NPI, diagnoses (`Claim.diagnosis`), the renewed authorization number (`Claim.related`), quantity, service dates, place of service, forms, documents and notes.

Not FHIR fields, but needed to run the gateway: `requestId` (the key for update, cancel, inquire and CDex), `payerName` (which payer to call), at least one `orderIds` entry (a request without lines has nothing to authorize), `orderLocationCode` (the facility, see above) and `requestingPractitionerId` (`Claim.careTeam` is 0..* in R4, but the gateway requires the requesting clinician).

Two values are worked out by the gateway rather than taken from the EHR: `Claim.type` = `professional`, and `Claim.item.category` (X12 service type) from the order's code (e.g. CPT 90935-90999 = 76 dialysis, medication = 88 pharmacy, otherwise 1 medical care). Both are listed here so they can be changed when a real payer asks for other values.

---

## 4. Endpoints

### 4.1 `POST /api/v1/pas/requests`: submit

**Why we use it.** PAS says a prior authorization is requested with `Claim/$submit` and a Bundle that holds the Claim and everything it points to. The gateway builds that Bundle, so the EHR only sends ids.

**Scenario.** Order-sign said `callDtr = true` and `callPas = true` for `ord-2-home-pd`. The clinician completed the payer's questionnaire. The EHR now submits the request.

#### Request

| Field | Required | Default | Meaning |
|---|---|---|---|
| `requestId` | Yes | | EHR id of the request, for example `PA-2026-0001` |
| `payerName` | Yes | | Payer to send the request to |
| `patientId` | Yes | | EHR patient id |
| `orderLocationCode` | Yes | | Id of the location (facility) in the EHR that asks for the authorization. The facility, with its own NPI, becomes `Claim.provider` (section 3); its CMS place-of-service code becomes the place of service of every line |
| `orderIds` | Yes, at least one | | Orders to authorize. One claim line per order |
| `coverageId` | Yes on submit | | The patient's active insurance for these orders: the EHR's coverage id (unique in the EHR), read with `GET Coverage/{coverageId}` at the FHIR API. The orders carry no insurance. On an update, empty = the insurance of the request |
| `requestingPractitionerId` | Yes | | Requesting clinician (`Claim.careTeam`, role `primary`). Must exist at the FHIR API (404 otherwise). The order's ordering practitioner is not used instead. Required by the gateway (`Claim.careTeam` is 0..* in FHIR R4) |
| `priority` | No | `routine` | `routine` or `urgent`. `urgent` = expedited review (72 hours under CMS-0057-F) |
| `requestType` | No | `new` | `new` or `renewal` |
| `previousAuthorizationNumber` | No | | Authorization number being renewed. Sent as `Claim.related` when given; empty = not sent (`Claim.related` is 0..*) |
| `previousServiceStart` | No | | First date of service of the previous authorization (`yyyy-MM-dd`) |
| `previousServiceEnd` | No | | Last date of service of the previous authorization (`yyyy-MM-dd`) |
| `previousTreatments` | No | | Number of treatments given under the previous authorization (0 or more) |
| `patientType` | No | `permanent` | `permanent` or `transient` (a patient visiting the facility) |
| `referringPractitionerId` | No | | Physician who referred the patient |
| `includeDtr` | No | `true` | `true`: completed DTR forms of the orders are found and attached. `false`: no DTR form is read or attached and no DTR call is made. Send `false` when CRD answered `callDtr = false` |
| `questionnaireResponseIds` | No | `[]` | Extra DTR forms to attach. Completed forms of these orders are attached automatically. Not allowed with `includeDtr = false` (400) |
| `documentIds` | No | `[]` | EHR documents (DocumentReference ids) to attach now. The file content is embedded |
| `notes` | No | | Free-text note for the reviewer. The first 264 characters are sent |

A new request:

```http
POST /api/v1/pas/requests
Content-Type: application/json

{
  "requestId": "PA-ORD-2-HOME-PD-UAV6LK",
  "payerName": "MockPayer",
  "patientId": "pat-2",
  "requestingPractitionerId": "prac-3",
  "orderLocationCode": "loc-main",
  "orderIds": ["ord-2-home-pd"],
  "priority": "routine",
  "referringPractitionerId": "prac-4",
  "documentIds": [],
  "notes": "Home dialysis training completed on 2026-09-25."
}
```

A renewal for a transient patient:

```json
{
  "requestId": "PA-2026-0102",
  "payerName": "MockPayer",
  "patientId": "pat-1",
  "orderLocationCode": "loc-main",
  "requestingPractitionerId": "prac-1",
  "orderIds": ["ord-1-hd"],
  "priority": "routine",
  "requestType": "renewal",
  "previousAuthorizationNumber": "PA20260706451203",
  "previousServiceStart": "2026-07-06",
  "previousServiceEnd": "2026-10-04",
  "previousTreatments": 39,
  "patientType": "transient",
  "documentIds": ["doc-1-labs", "doc-1-note"]
}
```

#### What the gateway does

1. **Checks the input.** All problems are returned together (400).
2. **Finds the payer.** Checks that `requestId` was not submitted before (409), unless that earlier request is cancelled.
3. **Reads from the FHIR API:** the patient, the orders (each must exist and belong to the patient), the active coverage, the facility Organization of `orderLocationCode` (section 3; its NPI when it has one), the requesting practitioner and the referring practitioner.
4. **Collects the forms:** the ones in `questionnaireResponseIds`, plus every form of the patient that is `completed` or `amended` and whose `qr-context` names one of the orders.
5. **Collects the documents** in `documentIds` and downloads each file, so the content itself is in the Bundle.
6. **Builds the PAS request Bundle** (section 6) and calls `POST {payer}/fhir/Claim/$submit`.
7. **Reads the ClaimResponse** into the decision (section 5).
8. **Saves** the request, both Bundles and the decision in `PA_PRIOR_AUTH`.
9. **Subscribes** at the payer for decision updates, once per payer and organization NPI (section 8). Skipped when the organization has no NPI: the EHR still gets the decision by calling inquire (section 4.5).

Nothing is saved when the payer rejects the request. Fix the problem and send the same `requestId` again.

#### Response 201

This example is a real answer from the mock payer.

```json
{
  "requestId": "PA-ORD-2-HOME-PD-UAV6LK",
  "payerName": "MockPayer",
  "patientId": "pat-2",
  "orderIds": ["ord-2-home-pd"],
  "overallStatus": "approved",
  "authorizationNumber": "PA20261008769512",
  "validFrom": "2026-10-05",
  "validTo": "2027-04-04",
  "decidedAt": "2026-10-08T19:12:02Z",
  "decisionDueBy": "2026-10-15T19:12:02Z",
  "lines": [
    {
      "sequence": 1,
      "orderId": "ord-2-home-pd",
      "code": "90945",
      "codeSystem": "http://www.ama-assn.org/go/cpt",
      "decision": "approved",
      "x12ActionCode": "A1",
      "requestedQuantity": 1,
      "approvedQuantity": null,
      "reason": "Line 1: Approved.",
      "validFrom": "2026-10-05",
      "validTo": "2027-04-04"
    }
  ],
  "informationRequests": [],
  "errors": []
}
```

A pended request looks like this (example):

```json
{
  "requestId": "PA-2026-0102",
  "payerName": "MockPayer",
  "patientId": "pat-1",
  "orderIds": ["ord-1-hd"],
  "overallStatus": "pended",
  "authorizationNumber": null,
  "validFrom": null,
  "validTo": null,
  "decidedAt": null,
  "decisionDueBy": "2026-10-16T05:30:00Z",
  "lines": [
    {
      "sequence": 1,
      "orderId": "ord-1-hd",
      "code": "90935",
      "codeSystem": "http://www.ama-assn.org/go/cpt",
      "decision": "pended",
      "x12ActionCode": "A4",
      "requestedQuantity": 39,
      "approvedQuantity": null,
      "reason": "Additional information requested: LOINC 11502-2",
      "validFrom": null,
      "validTo": null
    }
  ],
  "informationRequests": [],
  "errors": []
}
```

`informationRequests` is empty right after the submit. It is filled when the gateway receives the payer's Task: when the EHR calls `GET /api/v1/cdex/requests/{requestId}/information-requests?refresh=true` (section 8), or when the payer pushes the Task. See `CDex-API-Guide.md`.

#### When the payer rejects the request

The payer answered, but with an error (for example a validation error with an `OperationOutcome`) or without a ClaimResponse. The body is a decision with `overallStatus = "error"` (example):

```json
{
  "requestId": "PA-2026-0102",
  "payerName": "MockPayer",
  "patientId": "pat-1",
  "orderIds": ["ord-1-hd"],
  "overallStatus": "error",
  "authorizationNumber": null,
  "validFrom": null,
  "validTo": null,
  "decidedAt": null,
  "decisionDueBy": null,
  "lines": [],
  "informationRequests": [],
  "errors": ["Claim.item[0].productOrService: a procedure code is required."]
}
```

| Payer answered with | Gateway returns | Body |
|---|---|---|
| HTTP 4xx | **422** | Decision with `errors` |
| HTTP 5xx, or 2xx without a ClaimResponse | **502** | Decision with `errors` |
| Nothing (could not be reached) | **502** | Normal error body |
| Nothing in time | **504** | Normal error body |

So for 502 the EHR should accept both bodies: look for `errors` first, then for `error`.

#### Errors

| HTTP | When |
|---|---|
| 201 | Accepted by the payer. The decision may be `pended` |
| 400 | Missing field (`orderLocationCode` and `requestingPractitionerId` included); `priority`, `requestType` or `patientType` has a wrong value; negative `previousTreatments`; unknown `payerName`; an order of another patient |
| 404 | Patient, order, facility (`orderLocationCode`), organization, requesting or referring practitioner, form or document not found at the FHIR API |
| 409 | `requestId` was already submitted. Use `PUT` to change it |
| 422 | The insurance (`coverageId`) is not found, belongs to another patient, or is not active; or the payer rejected the request (decision body) |
| 502 | The payer or the FHIR API failed |
| 503 | The EHR data API is not available |
| 504 | The payer did not answer within `TIMEOUT_SECONDS` |

---

### 4.2 `PUT /api/v1/pas/requests/{requestId}`: update

**Why we use it.** PAS allows a request to be changed after it was sent (Claim Update): orders added or removed, more forms or documents. The payer answers with a new decision.

**Request.** The same body as the submit. `requestId` in the body may be empty; when it is sent it must match the URL.

```http
PUT /api/v1/pas/requests/PA-2026-0102
Content-Type: application/json

{
  "payerName": "MockPayer",
  "patientId": "pat-1",
  "orderLocationCode": "loc-main",
  "requestingPractitionerId": "prac-1",
  "orderIds": ["ord-1-hd", "ord-1-retacrit"],
  "priority": "routine",
  "documentIds": ["doc-1-labs"]
}
```

**Rules**

- **Send the full list of orders that should stay.** An order that was in the request before and is left out now is sent to the payer as a **cancelled line**.
- **Line numbers never change.** An order keeps its `sequence` on every update. A new order gets the next free number.
- **Patient and payer cannot change.** Cancel the request and submit a new one.
- **Coverage.** When `coverageId` is empty, the coverage of the first submission is used.
- **Forms and documents** are collected again, as in the submit. Send the `documentIds` you want the payer to have with this version.
- Each update raises the version: the Claim id becomes `{requestId}-v2`, `-v3` and so on.

**Response 200:** the payer's new decision (section 5).

| HTTP | When |
|---|---|
| 200 | The payer's new decision |
| 400 | Invalid body; `requestId` in the body does not match the URL; `patientId` or the payer changed |
| 404 | Unknown `requestId`; or data not found at the FHIR API |
| 409 | The request is cancelled. Submit it again with `POST` |
| 422 | The insurance is not found, belongs to another patient, or is not active; no NPI; or the payer rejected the update (decision body) |
| 502 / 503 / 504 | As for the submit |

---

### 4.3 `POST /api/v1/pas/requests/{requestId}/cancel`: cancel

**Why we use it.** The order was cancelled or the authorization is no longer needed. Every line is sent to the payer again as cancelled.

**Request.** The body is optional.

```http
POST /api/v1/pas/requests/PA-2026-0102/cancel
Content-Type: application/json

{ "reason": "Order cancelled by the physician." }
```

`reason` is sent to the payer as the free-text note.

**Response 200:** the decision, normally with `overallStatus = "cancelled"` and every line `cancelled` (X12 code `C`).

Notes:

- Cancelling a request that is already cancelled returns the stored decision. The payer is not called.
- The gateway reads the patient, the coverage, the organization and the orders again. If one of them can no longer be read (for example the coverage is no longer active), the answer is 422.
- After a cancel, the same `requestId` may be submitted again with `POST`.

| HTTP | When |
|---|---|
| 200 | The cancelled decision |
| 404 | Unknown `requestId` |
| 422 | The request's data can no longer be read; the payer is no longer configured; or the payer rejected the cancel (decision body) |
| 502 / 504 | The payer failed or did not answer in time |

---

### 4.4 `GET /api/v1/pas/requests/{requestId}`: latest stored decision

**Why we use it.** To show the current state without calling the payer. For a pended request the stored decision changes only when the payer sends a notification or the EHR calls inquire (section 8).

```http
GET /api/v1/pas/requests/PA-ORD-2-HOME-PD-UAV6LK
```

**Response 200:** the decision (section 5).

The EHR can also read the same JSON straight from `PA_PRIOR_AUTH.DECISION_JSON`.

| HTTP | When |
|---|---|
| 200 | The stored decision |
| 404 | Unknown `requestId` |
| 503 | The EHR data API is not available |

---

### 4.5 `POST /api/v1/pas/requests/{requestId}/inquire`: ask the payer now

**Why we use it.** A pended request can take days. `Claim/$inquire` asks the payer for its latest decision. The gateway has no background job: **the EHR calls this endpoint for each pended request, on its own schedule** (section 8).

```http
POST /api/v1/pas/requests/PA-2026-0102/inquire
```

No body.

**What the gateway does.** It builds the inquiry Bundle from the last request Bundle (same Claim identifier and lines, no forms or documents), calls `POST {payer}/fhir/Claim/$inquire`, reads the newest ClaimResponse and saves the decision. When the payer has nothing newer, the stored decision is returned.

**Response 200:** the decision (section 5).

| HTTP | When |
|---|---|
| 200 | The latest decision |
| 404 | Unknown `requestId` |
| 422 | The payer is no longer configured |
| 502 | The payer answered with an error (`details` has the payer's messages) or could not be reached |
| 504 | The payer did not answer in time |

---

### 4.6 `POST /api/v1/pas/notifications/{payerName}`: called by the payer

**Why it exists.** With a PAS Subscription the payer tells the gateway when a pended decision changes, so the gateway does not have to ask again and again. The gateway registers this URL at the payer (section 8).

**Request** (from the payer; `application/fhir+json` or `application/json`)

```http
POST /api/v1/pas/notifications/MockPayer
Content-Type: application/fhir+json

{
  "resourceType": "Bundle",
  "type": "history",
  "entry": [
    {
      "resource": {
        "resourceType": "Parameters",
        "parameter": [
          { "name": "subscription", "valueReference": { "reference": "Subscription/e1e22f47532f4b7ab195e6849e45c65c" } },
          { "name": "type", "valueCode": "event-notification" }
        ]
      }
    }
  ]
}
```

**What the gateway does**

| Notification `type` | Action |
|---|---|
| `handshake`, `heartbeat` | Nothing. The answer has `status` = the type |
| `event-notification`, or a body without a type | Runs `$inquire` for **every pended request of that payer** and saves the new decisions. The notification is id-only: it does not say which request changed |

A failed inquiry of one request is logged and does not stop the others.

**Response 200**

```json
{ "status": "2 open request(s) checked", "count": 2 }
```

| HTTP | When |
|---|---|
| 200 | Handled |
| 400 | The body is not a valid FHIR Bundle |
| 404 | `payerName` is not configured or not enabled |
| 503 | The EHR data API is not available |

---

## 5. The decision

| Field | Meaning |
|---|---|
| `requestId`, `payerName`, `patientId`, `orderIds` | The request |
| `overallStatus` | `approved`, `partially-approved`, `denied`, `pended`, `not-required`, `cancelled`, `contact-payer`, `error` |
| `authorizationNumber` | The payer's authorization number. Put it on the claim later |
| `validFrom`, `validTo` | The period the authorization covers |
| `decidedAt` | When the gateway received a final decision. Empty while `pended` |
| `decisionDueBy` | Latest time the payer must decide: first submission + 72 hours (`urgent`) or + 7 days (`routine`) |
| `lines` | One entry per order / claim line |
| `informationRequests` | Documents or forms the payer asked for (CDex) |
| `errors` | Payer errors |

**One line**

| Field | Meaning |
|---|---|
| `sequence` | `Claim.item.sequence`. Stays the same for an order across updates |
| `orderId` | The EHR order of this line |
| `code`, `codeSystem` | The requested procedure / product code |
| `decision` | `approved`, `partially-approved`, `denied`, `pended`, `not-required`, `cancelled`, `contact-payer`, `modified` |
| `x12ActionCode` | The payer's X12 306 code |
| `requestedQuantity`, `approvedQuantity` | Units asked for and units approved |
| `reason` | The payer's reason or note for the line |
| `validFrom`, `validTo` | The period approved for the line |

**X12 306 code to line decision**

| Code | Meaning | `decision` |
|---|---|---|
| `A1` | Certified in total | `approved` |
| `A2` | Certified, partial | `partially-approved` |
| `A3` | Not certified | `denied` |
| `A4` | Pended | `pended` |
| `A6` | Modified | `modified` |
| `C` | Cancelled | `cancelled` |
| `CT` | Contact payer | `contact-payer` |
| `NA` | No action required | `not-required` |
| anything else, or missing | | `pended` |

**Line decisions to `overallStatus`** (cancelled lines are ignored unless all lines are cancelled)

| Lines | `overallStatus` |
|---|---|
| All cancelled | `cancelled` |
| At least one `pended` | `pended` |
| All `not-required` | `not-required` |
| All `approved` / `modified` / `not-required` | `approved` |
| Some `approved`, `modified` or `partially-approved` | `partially-approved` |
| All `denied` / `not-required` | `denied` |
| Anything else | `contact-payer` |
| The payer's outcome is `error` and there are no lines | `error` |

**Where the values come from in the ClaimResponse**

| Decision field | ClaimResponse element |
|---|---|
| `authorizationNumber` | `preAuthRef`, else the `number` of the first line's reviewAction |
| `validFrom`, `validTo` | `preAuthPeriod`, else the earliest start and latest end of the lines |
| line `x12ActionCode` | `item.extension` reviewAction, part reviewActionCode |
| line `reason` | reviewAction `reasonCode`, else the first `processNote` linked to the line |
| line `validFrom`, `validTo` | `item.extension` itemPreAuthPeriod |
| line `approvedQuantity` | `item.extension` itemAuthorizedDetail, part `quantity` |
| `errors` | `ClaimResponse.error` ("Line 2: ..."), or the `OperationOutcome` of a rejected request |

When the payer's Bundle has several ClaimResponses, the one with the newest `created` is used.

---

## 6. The PAS request Bundle the gateway builds

A `collection` Bundle with profile `profile-pas-request-bundle`. The Claim is the first entry; everything it points to follows, so the payer can decide without calling back.

Entries of a real request:

| # | Resource | Id |
|---|---|---|
| 1 | Claim | `PA-ORD-2-HOME-PD-UAV6LK-v1` |
| 2 | Patient | `pat-2` |
| 3 | Coverage | `cov-2` |
| 4 | Organization (the payer, `Claim.insurer`) | `payer-MockPayer` |
| 5 | Organization (the provider: the facility) | `facility-loc-main` |
| 6 | Practitioner (requesting) | `prac-3` |
| 7 | Practitioner (referring) | `prac-4` |
| 8 | ServiceRequest (one per order) | `ord-2-home-pd` |
| 9 | QuestionnaireResponse (one per form) | `qr-frm-a8e1c91da03c` |
| 10 | DocumentReference (one per document, file embedded) | |

Each `fullUrl` is `{FHIR API}/{type}/{id}`, for example `http://localhost:5100/fhir/r4/Patient/pat-2`.

**EHR request field to Claim element**

| EHR field | Claim element |
|---|---|
| `requestId` | `Claim.identifier` (system `Workflow:ClaimIdentifierSystem`); `Claim.id` = `{requestId}-v{version}` |
| first submit / update | `meta.profile` = `profile-claim` (version 1) or `profile-claim-update` (version 2 and up) |
| | `status` = `active`, `use` = `preauthorization`, `type` = `professional` |
| `patientId` | `Claim.patient` |
| `payerName` | Chooses the payer to call. `Claim.insurer` = an Organization built from the patient's insurance (payer name and payer id, `Coverage.payor`); no identifier when the insurance has no payer id |
| `orderLocationCode` (section 3) | `Claim.provider`, and the place of service of every line (`Claim.item.locationCodeableConcept`) |
| `priority` | `Claim.priority`: `routine` = `normal`, `urgent` = `stat` |
| coverage | `Claim.insurance[1]`, focal. The Coverage is sent as the FHIR API returns it; the gateway adds no identifier system |
| `requestingPractitionerId` | `Claim.careTeam[1]`, role `primary` |
| `referringPractitionerId` | `Claim.careTeam[2]`, role `other` ("Referring provider"). Left out when it is the same person as the requesting practitioner |
| `requestType = renewal` | `Claim.related`: relationship `prior`, reference = `previousAuthorizationNumber` (only when given). Every line gets certification type `R` |
| `previousServiceStart`, `previousServiceEnd`, `previousTreatments` | One `supportingInfo` text (`additionalInformation`): "Renewal of authorization PA20260706451203, previous DOS 2026-07-06 to 2026-10-04, 39 treatments." |
| `patientType = transient` | The same `supportingInfo` text starts with "Transient patient (visiting this facility)." |
| DTR forms | `supportingInfo` (`additionalInformation`) with a reference to the QuestionnaireResponse; linked to its order's line |
| `documentIds` | `supportingInfo` (`additionalInformation`) with a reference to the DocumentReference; linked to every line |
| `notes` | `supportingInfo` (`freeFormMessage`), first 264 characters |
| orders' diagnoses (`reasonCode`) | `Claim.diagnosis`, each distinct diagnosis once |
| `orderIds` | `Claim.item`, one per order |

**One `Claim.item`**

| Element | Value |
|---|---|
| `sequence` | The order's line number |
| `category` | X12 service type (table below) |
| `productOrService` | The order's code |
| `quantity` | The order's quantity |
| `servicedPeriod` / `servicedDate` | The order's period, else its date |
| `locationCodeableConcept` | The CMS place-of-service code of the facility of `orderLocationCode` (`Location.type`), the same on every line. Not taken from the orders. Left out when the location has none |
| `careTeamSequence`, `diagnosisSequence`, `informationSequence` | Links to the care team, diagnoses and supporting information |
| extension certificationType | X12 1322: `I` initial, `R` renewal, `3` cancel |
| extension serviceItemRequestType | X12 1525: `HS` (health services review) |
| extension requestedService | Reference to the order |
| extension infoChanged = `added` | On an update: an order that was not in the previous version |
| modifierExtension infoCancelled = `true` | A cancelled line |

**X12 service type (`Claim.item.category`)**

| Order | Code |
|---|---|
| MedicationRequest | `88` Pharmacy |
| A code starting with `E` or `K` | `12` DME purchase |
| CPT 90935 to 90999 | `76` Dialysis |
| CPT 97010 to 97799 | `PT` Physical therapy |
| CPT 70010 to 79999 | `4` Diagnostic X-ray |
| CPT 10004 to 69990 | `2` Surgical |
| Anything else | `1` Medical care |

The Claim of the real request (shortened):

```json
{
  "resourceType": "Claim",
  "id": "PA-ORD-2-HOME-PD-UAV6LK-v1",
  "meta": { "profile": ["http://hl7.org/fhir/us/davinci-pas/StructureDefinition/profile-claim"] },
  "identifier": [ { "system": "http://provider.example.org/fhir/sid/prior-auth-request-id", "value": "PA-ORD-2-HOME-PD-UAV6LK" } ],
  "status": "active",
  "type": { "coding": [ { "system": "http://terminology.hl7.org/CodeSystem/claim-type", "code": "professional", "display": "Professional" } ] },
  "use": "preauthorization",
  "patient": { "reference": "Patient/pat-2" },
  "created": "2026-10-08T19:12:01Z",
  "insurer": { "reference": "Organization/payer-MockPayer" },
  "provider": { "reference": "Organization/facility-loc-main" },
  "priority": { "coding": [ { "system": "http://terminology.hl7.org/CodeSystem/processpriority", "code": "normal" } ] },
  "careTeam": [
    { "sequence": 1, "provider": { "reference": "Practitioner/prac-3" }, "role": { "coding": [ { "system": "http://terminology.hl7.org/CodeSystem/claimcareteamrole", "code": "primary", "display": "Primary provider" } ] } },
    { "sequence": 2, "provider": { "reference": "Practitioner/prac-4" }, "role": { "coding": [ { "system": "http://terminology.hl7.org/CodeSystem/claimcareteamrole", "code": "other", "display": "Referring provider" } ] } }
  ],
  "supportingInfo": [
    {
      "sequence": 1,
      "category": { "coding": [ { "system": "http://hl7.org/fhir/us/davinci-pas/CodeSystem/PASTempCodes", "code": "additionalInformation" } ] },
      "valueReference": { "reference": "QuestionnaireResponse/qr-frm-a8e1c91da03c" }
    }
  ],
  "diagnosis": [
    { "sequence": 1, "diagnosisCodeableConcept": { "coding": [ { "system": "http://hl7.org/fhir/sid/icd-10-cm", "code": "N18.5", "display": "Chronic kidney disease, stage 5" } ] } }
  ],
  "insurance": [ { "sequence": 1, "focal": true, "coverage": { "reference": "Coverage/cov-2" } } ],
  "item": [
    {
      "extension": [
        { "url": "http://hl7.org/fhir/us/davinci-pas/StructureDefinition/extension-certificationType", "valueCodeableConcept": { "coding": [ { "system": "https://codesystem.x12.org/005010/1322", "code": "I" } ] } },
        { "url": "http://hl7.org/fhir/us/davinci-pas/StructureDefinition/extension-serviceItemRequestType", "valueCodeableConcept": { "coding": [ { "system": "https://codesystem.x12.org/005010/1525", "code": "HS" } ] } },
        { "url": "http://hl7.org/fhir/us/davinci-pas/StructureDefinition/extension-requestedService", "valueReference": { "reference": "ServiceRequest/ord-2-home-pd" } }
      ],
      "sequence": 1,
      "careTeamSequence": [1],
      "diagnosisSequence": [1],
      "informationSequence": [1],
      "category": { "coding": [ { "system": "https://codesystem.x12.org/005010/1365", "code": "76" } ] },
      "productOrService": { "coding": [ { "system": "http://www.ama-assn.org/go/cpt", "code": "90945", "display": "Home peritoneal dialysis (APD cycler)" } ] },
      "servicedPeriod": { "start": "2026-10-05", "end": "2027-04-04" },
      "locationCodeableConcept": { "coding": [ { "system": "https://www.cms.gov/Medicare/Coding/place-of-service-codes/Place_of_Service_Code_Set", "code": "12" } ] },
      "quantity": { "value": 1 }
    }
  ]
}
```

---

## 7. The inquiry Bundle (`Claim/$inquire`)

Built from the last request Bundle that was sent:

- Bundle profile `profile-pas-inquiry-request-bundle`, Claim profile `profile-claim-inquiry`.
- The same `Claim.identifier` and the same lines.
- `supportingInfo` is removed, and the QuestionnaireResponse and DocumentReference entries are left out.
- Patient, Coverage, Organizations, Practitioners and orders stay.

---

## 8. How a pended decision is followed

Two ways: the payer tells the gateway (automatic), and the EHR asks (the EHR decides when).

**1. PAS Subscription (the fast way).** After every accepted submit, the gateway makes sure it has an active Subscription at the payer for the provider organization (`PA_PAYER.USE_SUBSCRIPTIONS = 1`). It sends this once per payer and organization NPI (no NPI = no Subscription; the EHR's own check below still finds the decision):

```json
{
  "resourceType": "Subscription",
  "meta": { "profile": ["http://hl7.org/fhir/us/davinci-pas/StructureDefinition/profile-subscription"] },
  "status": "requested",
  "end": "2027-10-08T19:12:02Z",
  "reason": "Prior authorization decision updates",
  "criteria": "http://hl7.org/fhir/us/davinci-pas/SubscriptionTopic/PASSubscriptionTopic",
  "_criteria": {
    "extension": [
      {
        "url": "http://hl7.org/fhir/uv/subscriptions-backport/StructureDefinition/backport-filter-criteria",
        "valueString": "ClaimResponse?orgIdentifier=http://hl7.org/fhir/sid/us-npi|1999999992"
      }
    ]
  },
  "channel": {
    "type": "rest-hook",
    "endpoint": "http://localhost:5200/api/v1/pas/notifications/MockPayer",
    "payload": "application/fhir+json",
    "_payload": {
      "extension": [
        { "url": "http://hl7.org/fhir/uv/subscriptions-backport/StructureDefinition/backport-payload-content", "valueCode": "id-only" }
      ]
    }
  }
}
```

The result is saved in `PA_PAYER_SUBSCRIPTION` (`active` or `error`). A refused Subscription never fails the submit; the EHR's own check below still works.

**2. The EHR checks its pended requests (the safety net).** The gateway has no background job. The EHR decides when to check, for example with a scheduled job of its own. Not every payer sends notifications and a notification can be lost, so this check is what makes sure nothing is missed:

1. Read the requests with `STATUS = 'pended'` from `PA_PRIOR_AUTH`.
2. For each one, call `POST /api/v1/pas/requests/{requestId}/inquire` (section 4.5). The gateway asks the payer and saves the new decision in `PA_PRIOR_AUTH`.
3. For each one, also call `GET /api/v1/cdex/requests/{requestId}/information-requests?refresh=true` (CDex guide). The gateway searches the payer's Tasks, so a payer request for more documents is found. Inquire alone does not find these. Skipped by the gateway when the request's organization has no NPI.
4. When documents are requested, send them with CDex. When the decision is final, the request is no longer `pended` and drops out of step 1.

A failed call (payer down, timeout) leaves the stored decision unchanged; call it again next time. If the EHR never runs this check, a payer that sends no notification is never asked again, and a request for documents can pass its due date unseen.

The EHR reads the result with `GET /api/v1/pas/requests/{requestId}` or straight from `PA_PRIOR_AUTH`. The gateway does not send a second copy to the EHR.

---

## 9. What to do with each `overallStatus`

| `overallStatus` | What the EHR does |
|---|---|
| `approved` | Keep `authorizationNumber`, `validFrom`, `validTo` for the claim |
| `partially-approved` | Look at each line: `approvedQuantity`, `reason` |
| `denied` | Show the line `reason` to the clinician |
| `pended` | Wait. Check `informationRequests`; when the payer asks for more, use CDex |
| `not-required` | No authorization is needed for these orders |
| `contact-payer` | The payer must be contacted outside this flow |
| `cancelled` | The request was cancelled |
| `error` | The payer rejected the request. Read `errors`, fix and send again |

---

## 10. Where the data is stored

| Table | Key | What |
|---|---|---|
| `PA_PRIOR_AUTH` | `REQUEST_ID` | One row per request: payer, patient, coverage, member id, organization and practitioner (ids and NPIs), priority, `STATUS`, `AUTHORIZATION_NUMBER`, `VERSION_NO`, `ORDER_IDS_JSON`, `LINE_NUMBERS_JSON`, `DECISION_JSON`, `LAST_REQUEST_BUNDLE`, `LAST_RESPONSE_BUNDLE`, `SUBMITTED_AT`, `UPDATED_AT` |
| `PA_PAYER_SUBSCRIPTION` | `PAYER_NAME` + `ORGANIZATION_NPI` | The Subscription at a payer |

`DECISION_JSON` has the same structure as the decision in section 5. `STATUS` and `AUTHORIZATION_NUMBER` are copies kept as columns for queries.

---

## 11. Settings that affect PAS

| Setting | Default | Meaning |
|---|---|---|
| `PA_PAYER.BASE_URL` + `FHIR_PATH` | `fhir` | Where `Claim/$submit`, `Claim/$inquire` and `Subscription` are |
| `PA_PAYER.TIMEOUT_SECONDS` | 30 | Seconds to wait for the payer |
| The payer id of the patient's insurance (FHIR API, `Coverage.payor.identifier`) | | The payer's id in `Claim.insurer` |
| The member id of the patient's insurance (FHIR API, `Coverage.subscriberId`) | | The member id in the Coverage. Optional: sent without it when the insurance has none |
| `PA_PAYER.USE_SUBSCRIPTIONS` | 1 | Create the PAS Subscription |
| `Workflow:GatewayPublicBaseUrl` | `http://localhost:5200` | Start of the notification URL given to the payer. The payer must be able to reach it |
| `Workflow:ClaimIdentifierSystem` | `http://provider.example.org/fhir/sid/prior-auth-request-id` | System of `Claim.identifier` |
| `Workflow:BundleIdentifierSystem` | `http://provider.example.org/fhir/sid/pas-bundle-id` | System of `Bundle.identifier` |
| `Workflow:ExpeditedDecisionHours` | 72 | `decisionDueBy` for `urgent` |
| `Workflow:StandardDecisionDays` | 7 | `decisionDueBy` for `routine` |

PAS uses FHIR R4, like every other step (the Da Vinci PAS guide exists only for R4).

---

## 12. Test it

Run order-sign and DTR for the order first, so a completed form exists. The ids are in the mock EHR data.

```http
### 1. submit without the lab report: the mock payer pends (it needs LOINC 11502-2 for CPT 90935)
POST http://localhost:5200/api/v1/pas/requests
Content-Type: application/json

{
  "requestId": "PA-2026-0102",
  "payerName": "MockPayer",
  "patientId": "pat-1",
  "orderLocationCode": "loc-main",
  "requestingPractitionerId": "prac-1",
  "orderIds": ["ord-1-hd"],
  "priority": "routine",
  "documentIds": ["doc-1-note"]
}

### 2. stored decision
GET http://localhost:5200/api/v1/pas/requests/PA-2026-0102

### 3. ask the payer now
POST http://localhost:5200/api/v1/pas/requests/PA-2026-0102/inquire

### 4. add an order and the lab report
PUT http://localhost:5200/api/v1/pas/requests/PA-2026-0102
Content-Type: application/json

{
  "payerName": "MockPayer",
  "patientId": "pat-1",
  "orderLocationCode": "loc-main",
  "requestingPractitionerId": "prac-1",
  "orderIds": ["ord-1-hd", "ord-1-retacrit"],
  "documentIds": ["doc-1-labs", "doc-1-note"]
}

### 5. cancel
POST http://localhost:5200/api/v1/pas/requests/PA-2026-0102/cancel
Content-Type: application/json

{ "reason": "Order cancelled by the physician." }
```

Note: the examples in Swagger and in `PayerGatewayWebAPI.http` use `pat-1001`, `ord-1001`, `org-3001`, `doc-7001`. Those ids are not in the mock EHR data.

---

## 13. Code changes this guide reflects

If you still have the earlier PAS guide, these are the points where it no longer matches the code.

| Change | Where |
|---|---|
| Request fields from the authorization request form: `requestType`, `previousAuthorizationNumber`, `previousServiceStart`, `previousServiceEnd`, `previousTreatments`, `patientType`, `referringPractitionerId` | Sections 4.1 and 6 |
| Renewal lines are sent with X12 certification type `R`, and the previous authorization goes in `Claim.related` | Section 6 |
| No write-back. The decision is saved in `PA_PRIOR_AUTH` in the EHR database and the EHR reads it there. The `writeBack` request fields no longer exist and are ignored when sent | Sections 8 and 10 |
| No database of its own. All rows go through the EHR data API. A new status 503 means the EHR data API is not available | Sections 3 and 10 |
| The payer is found by `payerName` in table `PA_PAYER`, not in `appsettings.json` (that list is used only with `Storage:Provider = InMemory`) | Section 3 |
| No authentication on any call (gateway to payer, payer to gateway) | Section 3 |
| No background job. The EHR checks its pended requests by calling inquire and `information-requests?refresh=true` for each one. `Workflow:PollMinutes` is removed | Sections 4.5 and 8 |
| Project and namespaces renamed to `PayerGatewayWebAPI` | Section 14 |

---

## 14. Code map

| File | What it does |
|---|---|
| `Controllers/PasController.cs` | The six endpoints |
| `Services/Pas/PasService.cs` | Submit, update, cancel, read, inquire, notification |
| `Services/Pas/PasBundleBuilder.cs` | The request Bundle and the inquiry Bundle |
| `Services/Pas/PasDecisionReader.cs` | ClaimResponse to decision |
| `Services/Pas/PasSubscriptionService.cs` | The PAS Subscription |
| `Services/OrderHelper.cs` | Order checks and the coverage choice |
| `Repositories/Database/EhrApiPriorAuthRepository.cs`, `EhrApiPayerSubscriptionRepository.cs` | `PA_PRIOR_AUTH` and `PA_PAYER_SUBSCRIPTION` through the EHR data API |
| `Constants/PasCodes.cs`, `PasProfiles.cs`, `PasExtensions.cs`, `X12Codes.cs`, `X12ReviewActions.cs`, `DecisionStatus.cs` | Codes and URLs |
| `Models/Requests/PriorAuthRequest.cs`, `CancelPriorAuthRequest.cs` | Request JSON |
| `Models/Responses/PriorAuthDecision.cs`, `PriorAuthLine.cs`, `InformationRequest.cs`, `NotificationResult.cs` | Response JSON |
