# Payer Gateway Web API: CRD Guide (Step 1, order-sign)

**Project:** `PayerGatewayWebAPI` (port 5200, Swagger at `/swagger`)
**Standards:** Da Vinci CRD 2.0.1, CDS Hooks 2.0 (`order-sign` hook only)
**Controllers:** `PayersController`, `CrdController`
**Other guides:** `DTR-Forms-API-Guide.md`, `DTR-SmartApp-API-Guide.md`, `PAS-API-Guide.md`, `CDex-API-Guide.md`

> **Updated Oct 9, 2026 (later the same day).** The payer id and the member id now come only from the patient's insurance in the EHR; table `PA_PAYER` no longer has `PAYER_IDENTIFIER`, `MEMBER_ID_SYSTEM` and `PAYER_ID_SYSTEM`. Orders are service and medication orders only (no device orders). When a patient has more than one active insurance and none is chosen, the gateway answers 422 instead of picking one. The Word guides named "PayerGatewayWebAPI - ... API Guide.docx" in this folder are the complete, current description.

---

## 1. What this step does

When the clinician signs an order, the EHR asks the gateway one question: **does this order need prior authorization and / or documentation?**

The EHR sends plain JSON (ids only). The gateway reads the FHIR data, calls the payer's CDS Hooks service and gives the EHR one simple decision per order.

```
 EHR                      Payer Gateway (5200)                 FHIR API (5100)              Payer
  |                              |                                   |                        |
  |  POST /api/v1/crd/order-sign |                                   |                        |
  |  (eventId, payerName,        |                                   |                        |
  |   patientId, orderIds)       |                                   |                        |
  |----------------------------->|  find payer in PA_PAYER           |                        |
  |                              |  read Patient, orders, Coverage   |                        |
  |                              |---------------------------------->|                        |
  |                              |  POST /internal/access-tokens     |                        |
  |                              |---------------------------------->|                        |
  |                              |  GET  {payer}/cds-services  (discovery, kept 12 hours)     |
  |                              |----------------------------------------------------------->|
  |                              |  POST {payer}/cds-services/{id}  (order-sign hook)         |
  |                              |----------------------------------------------------------->|
  |                              |        the payer may read more data with the token         |
  |                              |                                   |<-----------------------|
  |                              |  cards + systemActions (coverage-information)              |
  |                              |<-----------------------------------------------------------|
  |                              |  save PA_COVERAGE_DECISION, PA_DTR_LINK                    |
  |  200: one decision per order |                                   |                        |
  |<-----------------------------|                                   |                        |
```

What the EHR does with the answer:

| Decision | Next step |
|---|---|
| `callDtr = true` | Fill in the payer's questionnaire: see the DTR guides |
| `callPas = true` | Submit the prior authorization: see the PAS guide (after DTR when both are `true`) |
| both `false` | Nothing more for this order |

---

## 2. Endpoints at a glance

| # | Endpoint | In one sentence | Called by |
|---|---|---|---|
| 0a | `GET /api/v1/payers` | Lists the payers the EHR may name in `payerName` | EHR |
| 0b | `POST /api/v1/payers/{payerName}/discover` | Tests a payer connection (CDS Hooks discovery) | Support / onboarding |
| 1 | `POST /api/v1/crd/order-sign` | Asks the payer about the signed orders and returns one decision per order | EHR |
| 2 | `GET /api/v1/crd/decisions/{orderId}` | Returns the latest stored decision of an order (no payer call) | EHR |

---

## 3. Common rules

- **Base URL:** `http://localhost:5200` locally (`Urls` in `appsettings.json`).
- **Format:** `Content-Type: application/json`, property names in camelCase.
- **Payer:** every request carries `payerName`. It must match `PA_PAYER.PAYER_NAME` (upper / lower case does not matter) and the payer must be enabled.
- **Authentication:** none is built in. It is added outside this code.
- **Errors:** every error has the same body.

```json
{
  "error": "Payer 'UnknownPayer' is not configured or not enabled. Configured payers: MockPayer.",
  "details": [],
  "traceId": "0HN6Q2L3J7K1B:00000001"
}
```

`traceId` is also written to the gateway log. Give it to support.

---

## 4. Endpoints

### 4.1 `GET /api/v1/payers`: which payers are connected

**Why we use it.** The EHR must send the exact payer name. This call shows the names the gateway knows, with each payer's FHIR release and DTR mode.

**Request**

```http
GET /api/v1/payers
```

**Response 200**

```json
[
  {
    "payerName": "MockPayer",
    "displayName": "Mock Da Vinci Payer",
    "enabled": true,
    "baseUrl": "http://localhost:5080",
    "dtrMode": "Forms"
  }
]
```

| Field | Meaning |
|---|---|
| `payerName` | The value to send as `payerName` in every other request |
| `enabled` | `false` = the payer is in the list but switched off; requests for it get "not configured or not enabled" |
| `dtrMode` | `Forms` = questionnaire through the gateway forms. `SmartApp` = the payer's own SMART DTR app |

The list comes from table `PA_PAYER` (read through the EHR data API) and is kept for `Storage:PayerCacheMinutes` (5 minutes). A name that is not in the kept list is looked up again right away, so a new payer row works without a restart.

| HTTP | When |
|---|---|
| 200 | The list (disabled payers included) |
| 503 | The EHR data API is not available |

---

### 4.2 `POST /api/v1/payers/{payerName}/discover`: test a payer connection

**Why we use it.** CDS Hooks says every payer publishes its services at `{payer}/cds-services`. This call reads that list, so you can check that a new payer answers and that it has an `order-sign` service. It also refreshes the gateway's 12-hour discovery cache.

**Request**

```http
POST /api/v1/payers/MockPayer/discover
```

No body.

**Response 200** (shortened)

```json
{
  "services": [
    {
      "hook": "order-sign",
      "id": "crd-order-sign",
      "title": "(payer name) Coverage Requirements Discovery (order-sign)",
      "description": "Returns coverage, prior authorization and documentation requirements (Da Vinci CRD 2.0.1).",
      "prefetch": {
        "coverage": "Coverage?patient={{context.patientId}}&status=active",
        "patient": "Patient/{{context.patientId}}",
        "serviceRequestBundle": "ServiceRequest?_id={{context.draftOrders.ServiceRequest.id}}&_include=ServiceRequest:patient&..."
      }
    }
  ]
}
```

The gateway uses the first service whose `hook` is `order-sign`. Its `id` becomes the URL of the hook call and its `prefetch` templates tell the gateway which data to send along.

| HTTP | When |
|---|---|
| 200 | The payer's CDS services |
| 404 | The payer is not configured or not enabled |
| 502 | The payer answered with an error, could not be reached, or sent something that is not JSON |
| 504 | The payer did not answer within `TIMEOUT_SECONDS` |

---

### 4.3 `POST /api/v1/crd/order-sign`: the order is signed

**Why we use it.** This is the CRD step. It tells the EHR, per order, whether the payer covers it, whether prior authorization is needed and whether a questionnaire must be filled in.

**Scenario.** Dr. Patel signs order `ord-2-home-pd` (home peritoneal dialysis, CPT 90945) for patient `pat-2`. The EHR calls order-sign. The payer answers "covered, prior authorization needed, clinical documentation needed" and names a questionnaire. The EHR then runs DTR and PAS for that order.

#### Request

| Field | Required | Meaning |
|---|---|---|
| `eventId` | Yes | Id of this signing event in the EHR. Saved with each decision as a reference. Every call asks the payer again, also with an `eventId` sent before |
| `payerName` | Yes | Payer to ask (`PA_PAYER.PAYER_NAME`) |
| `patientId` | Yes | EHR patient id |
| `orderIds` | Yes, at least one | EHR order ids being signed. An order is a ServiceRequest (service order) or a MedicationRequest (medication order) at the FHIR API. Device orders are not used |
| `coverageId` | Yes | The patient's active insurance for these orders: the EHR's coverage id (unique in the EHR). The gateway reads it with `GET Coverage/{coverageId}` at the FHIR API. The orders carry no insurance |
| `encounterId` | No | Current encounter (CDS Hooks `encounterId`) |
| `userPractitionerId` | Yes | Clinician signing (CDS Hooks `context.userId`, 1..1). The order's ordering practitioner is not used instead |

```http
POST /api/v1/crd/order-sign
Content-Type: application/json

{
  "eventId": "evt-ord-2-home-pd-wcbp0q",
  "payerName": "MockPayer",
  "patientId": "pat-2",
  "orderIds": ["ord-2-home-pd"],
  "encounterId": "enc-2",
  "userPractitionerId": "prac-3"
}
```

#### What the gateway does

1. **Checks the input.** All problems are returned together (400).
2. **Finds the payer** by `payerName`.
3. **Reads from the FHIR API:** `Patient/{patientId}`, the orders (`ServiceRequest?_id=...`, `MedicationRequest?_id=...`) and the insurance `Coverage/{coverageId}`.
4. **Checks the coverage:** it must exist, belong to the patient and be active. Otherwise the payer is not called and every order gets `unknown` with the reason (see below).
5. **Discovery:** `GET {payer}/cds-services`, kept 12 hours. Picks the `order-sign` service.
6. **Gets a `fhirAuthorization` token** from the FHIR API (`POST /internal/access-tokens`, valid `Workflow:FhirAuthorizationMinutes`). If this fails, the hook is sent without it.
7. **Fills the prefetch templates** and runs each query against the FHIR API. A template that cannot be filled, or a query that fails, is left out. Tokens: the CDS Hooks ones (`{{context.patientId}}`, `{{context.encounterId}}`, `{{context.userId}}`, `{{context.draftOrders.<Type>.id}}`) and two gateway tokens, `{{userPractitionerId}}` and `{{coverageId}}` (the `coverageId` of the request, e.g. `Coverage?_id={{coverageId}}`). The gateway tokens are not CDS Hooks standard, so only a payer set up for this gateway uses them; standard payers ask for `Coverage?patient={{context.patientId}}&status=active`, which keeps working.
8. **Calls the payer:** `POST {payer}/cds-services/{serviceId}` with timeout `CRD_TIMEOUT_SECONDS`.
9. **Reads the answer** (section 5): `systemActions` become decisions, `cards` become messages.
10. **Sets `callDtr` / `callPas`**, saves each decision, and returns the response.

#### What the gateway sends to the payer

```json
{
  "hook": "order-sign",
  "hookInstance": "5f0a3c1e-7d0b-4c8e-9a51-2f6c1b7e9d10",
  "fhirServer": "http://localhost:5100/fhir/r4",
  "fhirAuthorization": { "access_token": "...", "token_type": "Bearer", "expires_in": 300, "...": "as returned by the FHIR API" },
  "context": {
    "userId": "Practitioner/prac-3",
    "patientId": "pat-2",
    "encounterId": "enc-2",
    "draftOrders": {
      "resourceType": "Bundle",
      "type": "collection",
      "entry": [ { "resource": { "resourceType": "ServiceRequest", "id": "ord-2-home-pd", "...": "..." } } ]
    }
  },
  "prefetch": {
    "coverage": { "resourceType": "Bundle", "type": "searchset", "...": "..." },
    "patient": { "resourceType": "Patient", "id": "pat-2", "...": "..." }
  }
}
```

Prefetch placeholders the gateway can fill:

| Placeholder | Value |
|---|---|
| `{{context.patientId}}` | `patientId` |
| `{{context.encounterId}}` | `encounterId` (when sent) |
| `{{context.userId}}` | `Practitioner/{userPractitionerId}` |
| `{{userPractitionerId}}` | `userPractitionerId` |
| `{{context.draftOrders.ServiceRequest.id}}` | Ids of the signed ServiceRequests, comma separated. The same for `MedicationRequest` |

#### Response 200

This example is a real answer from the mock payer.

```json
{
  "eventId": "evt-ord-2-home-pd-wcbp0q",
  "payerName": "MockPayer",
  "decisions": [
    {
      "orderId": "ord-2-home-pd",
      "payerName": "MockPayer",
      "coverageId": "cov-2",
      "covered": "covered",
      "priorAuthNeeded": "auth-needed",
      "documentationNeeded": "clinical",
      "callDtr": true,
      "callPas": true,
      "documentationPurpose": ["withpa"],
      "informationNeeded": [],
      "questionnaireUrls": ["http://example.org/fhir/Questionnaire/home-dialysis-adaptive"],
      "coverageAssertionId": "eeea140086ab438387ae884d045a4946",
      "satisfiedAuthorizationNumber": null,
      "messages": [
        {
          "severity": "info",
          "summary": "Prior authorization required: Home peritoneal dialysis (APD cycler)",
          "detail": "Prior authorization required for Home dialysis (peritoneal or home hemodialysis). Complete the documentation (DTR), then submit the request (PAS).",
          "link": {
            "label": "Coverage policy",
            "url": "https://example.org/policies/home-dialysis",
            "type": "absolute",
            "opensPayerApp": false
          }
        },
        {
          "severity": "info",
          "summary": "Documentation required (clinical): Home peritoneal dialysis (APD cycler)",
          "detail": "Complete the payer documentation for Home peritoneal dialysis (APD cycler). Purpose: withpa.",
          "link": {
            "label": "Launch documentation (DTR)",
            "url": "http://localhost:5200/api/v1/dtr/launch/3a3df604a18c4d479f4ed4f05b9a1896",
            "type": "smart",
            "opensPayerApp": false
          }
        }
      ],
      "decidedAt": "2026-10-08"
    }
  ],
  "messages": [],
  "payerCall": {
    "payerName": "MockPayer",
    "serviceId": "crd-order-sign",
    "outcome": "ok",
    "durationMs": 544,
    "error": null
  }
}
```

**Top level**

| Field | Meaning |
|---|---|
| `decisions` | One entry per order that was found |
| `messages` | Messages not tied to one order: "Order X was not found", general payer cards |
| `payerCall.outcome` | `ok`, `skipped` (no insurance, or the payer has no order-sign service), `timeout`, `error` |
| `payerCall.durationMs` | How long the payer call took |

**One decision**

| Field | Values | Meaning |
|---|---|---|
| `covered` | `covered`, `not-covered`, `conditional`, `unknown` | `unknown` = the payer could not be asked |
| `priorAuthNeeded` | `no-auth`, `auth-needed`, `satisfied`, `performpa`, `conditional` | Payer `pa-needed` |
| `documentationNeeded` | `no-doc`, `clinical`, `admin`, `both`, `conditional` | Payer `doc-needed` |
| `callDtr` | `true` / `false` | Fill in the payer's questionnaire |
| `callPas` | `true` / `false` | Submit a prior authorization request |
| `documentationPurpose` | `withpa`, `withclaim`, `withorder`, `retain-doc` | What the documentation is for |
| `informationNeeded` | `performer`, `location`, `timeframe`, `contract-window` | What the payer still needs to decide |
| `questionnaireUrls` | URLs | Pass them to `POST /api/v1/dtr/forms` |
| `coverageAssertionId` | text | The payer's id of this answer. Pass it to DTR |
| `satisfiedAuthorizationNumber` | text | An existing authorization already covers the order (`priorAuthNeeded = satisfied`) |
| `messages` | list | Payer cards and reasons for this order |
| `decidedAt` | date or date-time | From the payer, else the time of the call |

**How `callDtr` and `callPas` are set**

| Payer answer | `callDtr` | `callPas` |
|---|---|---|
| `covered = not-covered` | `false` | `false` |
| `documentationNeeded` is `clinical`, `admin`, `both` or `conditional` | `true` | |
| At least one questionnaire URL | `true` | |
| `documentationNeeded` is `no-doc` or empty, and no questionnaire | `false` | |
| `priorAuthNeeded = auth-needed` | | `true` |
| `priorAuthNeeded` is anything else (`no-auth`, `satisfied`, `performpa`, `conditional`, empty) | | `false` |

**Message links**

| `link.type` | `opensPayerApp` | What the EHR does |
|---|---|---|
| `absolute` | `false` | A normal web page (for example the coverage policy). Open it in a browser if the user asks |
| `smart` | `false` | The payer works with the gateway forms (`DTR_MODE = Forms`). Use `POST /api/v1/dtr/forms`. See `DTR-Forms-API-Guide.md` |
| `smart` | `true` | The payer has its own SMART DTR app (`DTR_MODE = SmartApp`). Open `link.url` in a browser window. See `DTR-SmartApp-API-Guide.md` |

A `smart` link is always a gateway URL (`/api/v1/dtr/launch/{linkId}`). The payer's real app URL and launch context are kept in table `PA_DTR_LINK` for `Workflow:DtrLinkHours` (72 hours).

#### Special cases (still HTTP 200)

| Situation | What comes back |
|---|---|
| The payer does not answer in time | `payerCall.outcome = "timeout"`. Every order gets `covered = "unknown"`, `callDtr = false`, `callPas = false` and a warning message |
| The payer returns an error or cannot be reached | `payerCall.outcome = "error"`, `payerCall.error` has the reason. Decisions as above |
| The insurance (`coverageId`) is not found, belongs to another patient, or is not active | `payerCall.outcome = "skipped"`, `payerCall.error` has the reason. Decisions are `unknown` with the reason |
| The payer has no `order-sign` service | `payerCall.outcome = "skipped"`. Decisions are `unknown` |
| One of several orders is not found | A warning in top-level `messages`; the other orders get their decisions |
| The payer returns no coverage information for an order | That order gets `covered = "unknown"` with a message |
| The payer sends several coverage-information entries for one order | The strictest is kept: not-covered, then auth-needed / performpa, then conditional, then the rest |

Example when the payer times out:

```json
{
  "eventId": "evt-ord-1-hd-0001",
  "payerName": "MockPayer",
  "decisions": [
    {
      "orderId": "ord-1-hd",
      "payerName": "MockPayer",
      "coverageId": "cov-1",
      "covered": "unknown",
      "priorAuthNeeded": null,
      "documentationNeeded": null,
      "callDtr": false,
      "callPas": false,
      "documentationPurpose": [],
      "informationNeeded": [],
      "questionnaireUrls": [],
      "coverageAssertionId": null,
      "satisfiedAuthorizationNumber": null,
      "messages": [
        {
          "severity": "warning",
          "summary": "Mock Da Vinci Payer did not answer the coverage check. Continue; requirements are checked again at prior authorization.",
          "detail": "Mock Da Vinci Payer did not answer the coverage check. Continue; requirements are checked again at prior authorization.",
          "link": null
        }
      ],
      "decidedAt": "2026-10-09T05:10:00Z"
    }
  ],
  "messages": [],
  "payerCall": {
    "payerName": "MockPayer",
    "serviceId": "crd-order-sign",
    "outcome": "timeout",
    "durationMs": 5003,
    "error": "Mock Da Vinci Payer did not answer within 5 seconds."
  }
}
```

**How to recognise a timeout.** The HTTP status is **200**, not 504. Check the body instead:

- `payerCall.outcome` is `timeout`, and `payerCall.error` says how long the gateway waited (`CRD_TIMEOUT_SECONDS` of the payer, default 5 seconds).
- Every order has `covered = "unknown"`, `priorAuthNeeded` and `documentationNeeded` are `null`, and the order has a `warning` message.
- `callDtr` and `callPas` are `false`. **This does not mean that nothing is needed**: the payer could not be asked. Do not treat it as "no prior authorization needed".
- The `unknown` decisions are saved in `PA_COVERAGE_DECISION` like any other decision.
- `payerCall.serviceId` is `null` when the payer's discovery call (`GET cds-services`) timed out, before the hook was sent.

The payer may still have received the hook, but its late answer is lost: the gateway stops waiting at the time limit, and CRD has no callback.

**What the EHR should do.** Call order-sign again a little later (with the same or a new `eventId`). Every call asks the payer; nothing is answered from a stored copy. The new decisions replace the `unknown` ones in `PA_COVERAGE_DECISION`. If the payer still does not answer, staff can decide, or the EHR can go straight to PAS: PAS gives the payer's binding answer, and `not-required` when no authorization is needed.

#### Errors

| HTTP | When |
|---|---|
| 400 | A required field is missing (`coverageId` and `userPractitionerId` included) or `orderIds` is empty (`details` lists each problem); unknown or disabled `payerName`; an order belongs to another patient; invalid JSON |
| 404 | The patient is not found at the FHIR API, or none of the orders are found |
| 502 | The FHIR API failed or timed out |
| 503 | The EHR data API is not available (the payer list could not be read, or a decision could not be saved) |
| 500 | Unexpected error. Look up `traceId` in the log |

A payer failure is **not** an HTTP error here. It returns 200 with `covered = "unknown"`, so the clinician is never blocked at signing.

---

### 4.4 `GET /api/v1/crd/decisions/{orderId}`: latest decision of an order

**Why we use it.** The EHR can read the decision again later (for example when the user opens the order) without calling the payer.

**Request**

```http
GET /api/v1/crd/decisions/ord-2-home-pd
```

**Response 200:** one decision, the same structure as one entry of `decisions` in section 4.3.

Only the latest decision of an order is kept. A new order-sign for the same order replaces it.

| HTTP | When |
|---|---|
| 200 | The stored decision |
| 404 | No order-sign was run for this order |
| 503 | The EHR data API is not available |

---

## 5. How the payer's answer becomes a decision

The payer returns each order in `systemActions` (type `update`) with the CRD **coverage-information** extension
(`http://hl7.org/fhir/us/davinci-crd/StructureDefinition/ext-coverage-information`). The gateway reads its parts:

| Extension part | Decision field |
|---|---|
| `coverage` | `coverageId` (else the coverage the gateway chose) |
| `covered` | `covered` (when missing: `conditional`) |
| `pa-needed` | `priorAuthNeeded` |
| `doc-needed` | `documentationNeeded` |
| `doc-purpose` (repeats) | `documentationPurpose` |
| `info-needed` (repeats) | `informationNeeded` |
| `questionnaire` (repeats) | `questionnaireUrls` |
| `coverage-assertion-id` | `coverageAssertionId` |
| `satisfied-pa-id` | `satisfiedAuthorizationNumber` |
| `reason` (repeats) | An `info` message on the decision |
| `date` | `decidedAt` |

The payer's **cards** become messages:

| Card field | Message field |
|---|---|
| `indicator` | `severity` (`info`, `warning`, `critical`) |
| `summary` | `summary` |
| `detail` | `detail` |
| `links` | `link`: the first `smart` link, else the first link |

A card goes on the order it is about: the only order in the request, or the order whose `Type/id` appears in the card text or link `appContext`. Otherwise it goes to the top-level `messages`.

Which DTR app a `smart` link opens, in this order: `PA_PAYER.DTR_APP_LAUNCH_URL`, then `Workflow:DtrAppUrl`, then the URL on the payer's card.

---

## 6. FHIR release

Every payer is called in FHIR R4: `fhirServer` is the FHIR API's R4 URL (`FhirApi:BaseUrl`), and `draftOrders` and the
prefetch results are R4. The gateway and the FHIR API have no R5 support.

---

## 7. Where the data is stored

All rows are in the EHR's Oracle database and are read and saved through the EHR data API (`docs/EHR-Data-API.md`).

| Table | Key | What |
|---|---|---|
| `PA_PAYER` | `PAYER_NAME` | The payers (maintained by the EHR team) |
| `PA_COVERAGE_DECISION` | `ORDER_ID` | The latest decision of an order (`DECISION_JSON` plus `COVERED`, `PRIOR_AUTH_NEEDED`, `DOCUMENTATION_NEEDED` as columns) |
| `PA_DTR_LINK` | `LINK_ID` | One row per `smart` link given to the EHR |

The EHR can read the decision straight from `PA_COVERAGE_DECISION`. The gateway does not send a second copy to the EHR.

---

## 8. Settings that affect CRD

| Setting | Default | Meaning |
|---|---|---|
| `PA_PAYER.BASE_URL` + `CDS_SERVICES_PATH` | `cds-services` | Where the payer's CDS services are |
| `PA_PAYER.CRD_TIMEOUT_SECONDS` | 5 | Seconds to wait for the hook call. The clinician is waiting, so keep it short |
| `PA_PAYER.TIMEOUT_SECONDS` | 30 | Seconds to wait for discovery |
| `PA_PAYER.DTR_MODE` | `Forms` | Sets `opensPayerApp` on `smart` links |
| `PA_PAYER.DTR_APP_LAUNCH_URL` | empty | Fixed launch URL of the payer's DTR app |
| `Workflow:GatewayPublicBaseUrl` | `http://localhost:5200` | Start of the `/api/v1/dtr/launch/{linkId}` links |
| `Workflow:DtrLinkHours` | 72 | How long a launch link stays valid |
| `Workflow:FhirAuthorizationMinutes` | 5 | Lifetime of the `fhirAuthorization` token |
| `Storage:PayerCacheMinutes` | 5 | How long the payer list is kept |
| `FhirApi:BaseUrl`, `InternalBaseUrl` | localhost:5100 | The FHIR API (FHIR R4) |

---

## 9. Test it

Start the mock EHR (5090), the FHIR API (5100), the mock payer (5080) and the gateway (5200). The ids below exist in the mock EHR data.

```http
### payers
GET http://localhost:5200/api/v1/payers

### connection test
POST http://localhost:5200/api/v1/payers/MockPayer/discover

### in-center hemodialysis: prior auth + questionnaire + lab report
POST http://localhost:5200/api/v1/crd/order-sign
Content-Type: application/json

{
  "eventId": "evt-ord-1-hd-0001",
  "payerName": "MockPayer",
  "patientId": "pat-1",
  "orderIds": ["ord-1-hd"],
  "encounterId": "enc-1",
  "userPractitionerId": "prac-1"
}

### read the decision again
GET http://localhost:5200/api/v1/crd/decisions/ord-1-hd
```

Note: the examples in Swagger and in `PayerGatewayWebAPI.http` use `pat-1001`, `ord-1001`, `prac-2001`. Those ids are not in the mock EHR data. Use `pat-1`, `ord-1-hd`, `prac-1` and so on.

---

## 10. Code map

| File | What it does |
|---|---|
| `Controllers/PayersController.cs` | `GET /api/v1/payers`, `POST .../discover` |
| `Controllers/CrdController.cs` | `POST /api/v1/crd/order-sign`, `GET /api/v1/crd/decisions/{orderId}` |
| `Services/Crd/CrdService.cs` | The whole order-sign flow |
| `Services/Crd/CdsHookBuilder.cs` | Builds `context` and `prefetch` |
| `Services/Crd/CoverageDecisionReader.cs` | Reads coverage-information, sets `callDtr` / `callPas` |
| `Services/OrderHelper.cs` | Order checks and the coverage choice (shared with DTR and PAS) |
| `Services/Payers/PayerRegistry.cs` | Payer lookup and the discovery cache |
| `Services/Payers/PayerClient.cs` | The HTTP calls to the payer |
| `Clients/FhirApiClient.cs` | The HTTP calls to the FHIR API |
| `Repositories/Database/EhrApiCrdRepository.cs`, `EhrApiDtrLinkRepository.cs` | Save and read through the EHR data API |
| `Models/Requests/OrderSignRequest.cs`, `Models/Responses/OrderSignResponse.cs`, `CoverageDecision.cs`, `EhrMessage.cs`, `EhrMessageLink.cs`, `PayerCallSummary.cs` | The JSON the EHR sends and receives |
