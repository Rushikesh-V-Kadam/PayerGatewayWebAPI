# Payer Gateway Web API: DTR Guide, Flow 2: Payer SMART App

**Project:** `PayerGatewayWebAPI` (port 5200, Swagger group "DTR 2 - Payer SMART app")
**Standards:** Da Vinci DTR 2.0.1, SMART App Launch 2.x (EHR launch)
**Controllers:** `DtrPayerAppController` (launch and relay), `DtrFormsController` (saved-forms)
**Used when:** `PA_PAYER.DTR_MODE = SmartApp`. The payer brings its own SMART DTR app.
**Other DTR flow:** `DTR-Forms-API-Guide.md` (questionnaire through the gateway forms)

---

## 1. What this flow does

With real payers the questionnaire is usually filled in inside the **payer's own SMART on FHIR app**. The app shows the questions, pre-fills answers from the chart and saves the completed form.

A SMART app is started by an EHR with two URL parameters: `iss` (the FHIR server) and `launch` (an id for the context). The client's EHR cannot do a SMART launch, so the gateway and the FHIR API do it for the EHR. **The EHR only opens one link in a browser window.**

```
 EHR                 Browser              Payer Gateway (5200)        FHIR API (5100)        Payer DTR app / Payer
  |                     |                        |                          |                        |
  | order-sign answer has a message link:        |                          |                        |
  |   type "smart", opensPayerApp = true         |                          |                        |
  |   url = /api/v1/dtr/launch/{linkId}          |                          |                        |
  | open the link       |                        |                          |                        |
  |-------------------->| GET /api/v1/dtr/launch/{linkId}                   |                        |
  |                     |----------------------->|  read PA_DTR_LINK        |                        |
  |                     |                        |  POST /internal/smart-launches                    |
  |                     |                        |------------------------->|                        |
  |                     |                        |  { launchId, iss }       |                        |
  |                     |                        |<-------------------------|                        |
  |                     | 302 {app URL}?iss=...&launch=...                  |                        |
  |                     |<-----------------------|                          |                        |
  |                     | open the payer's app ------------------------------------------------------>|
  |                     |                        |   SMART: smart-configuration, authorize, token    |
  |                     |                        |                          |<-----------------------|
  |                     |                        |   read chart data (Patient, Observation ...)      |
  |                     |                        |                          |<-----------------------|
  |                     |                        |   the app gets the questionnaire from its payer   |
  |                     |                        |   (directly, or through the gateway relay)        |
  |                     |                        |   save QuestionnaireResponse                      |
  |                     |                        |                          |<-----------------------|
  | GET /api/v1/dtr/saved-forms?patientId=...&orderId=...                   |                        |
  |--------------------------------------------->|  search QuestionnaireResponse                     |
  |                     |                        |------------------------->|                        |
  | 200: the form is "completed"                 |                          |                        |
  |<---------------------------------------------|                          |                        |
  | POST /api/v1/pas/requests (the form is attached automatically)          |                        |
```

**What the EHR team builds for this flow**

1. Read `link` on the order-sign messages. When `type = "smart"` and `opensPayerApp = true`, show a button.
2. The button opens `link.url` in a browser window. Nothing else is sent.
3. Check `GET /api/v1/dtr/saved-forms` until the form is `completed` (or let the user continue).
4. Call PAS.

---

## 2. Endpoints at a glance

| # | Endpoint | In one sentence | Called by |
|---|---|---|---|
| 1 | `GET /api/v1/dtr/launch/{linkId}` | Starts the SMART launch and redirects the browser to the payer's DTR app | The clinician's browser |
| 2 | `GET /api/v1/dtr/saved-forms?patientId=...&orderId=...` | Lists the forms saved for a patient, so the EHR sees that the app finished | EHR |
| 3 | `POST /api/v1/dtr/{payerName}/Questionnaire/$questionnaire-package` | Passes the app's questionnaire request to the payer unchanged | Payer DTR app (optional) |
| 4 | `POST /api/v1/dtr/{payerName}/Questionnaire/$next-question` | Passes the app's adaptive-form request to the payer unchanged | Payer DTR app (optional) |

Endpoints 3 and 4 are needed only when a DTR app reaches its payer through the gateway. Most payer apps call their own payer directly.

---

## 3. Where the link comes from (CRD)

The link is created during order-sign. When the payer's CRD card has a link of type `smart`, the gateway:

1. Saves the launch details in table `PA_DTR_LINK`: payer, patient, encounter, user, the orders and coverage (`fhirContext`), the payer's `appContext` and the app URL.
2. Gives the EHR a plain gateway URL instead of the SMART link.

```json
{
  "severity": "info",
  "summary": "Documentation required (clinical): Home peritoneal dialysis (APD cycler)",
  "detail": "Complete the payer documentation for Home peritoneal dialysis (APD cycler). Purpose: withpa.",
  "link": {
    "label": "Launch documentation (DTR)",
    "url": "http://localhost:5200/api/v1/dtr/launch/3a3df604a18c4d479f4ed4f05b9a1896",
    "type": "smart",
    "opensPayerApp": true
  }
}
```

| Field | Meaning |
|---|---|
| `link.type = "smart"` | A DTR app link (always a gateway URL) |
| `link.opensPayerApp = true` | `PA_PAYER.DTR_MODE = SmartApp`: open `url` in a browser window |
| `link.opensPayerApp = false` | `PA_PAYER.DTR_MODE = Forms`: use the gateway forms instead (`DTR-Forms-API-Guide.md`) |

**Which app URL is opened**, in this order:

1. `PA_PAYER.DTR_APP_LAUNCH_URL` (a fixed URL for this payer)
2. `Workflow:DtrAppUrl` (one provider-wide app for every payer)
3. The URL on the payer's CRD card (the normal case)

**How long the link works:** `Workflow:DtrLinkHours` (72 hours). After that the user signs the order again (a new `eventId`) to get a new link.

One row of `PA_DTR_LINK` (real data from the mock payer):

| Column | Value |
|---|---|
| `LINK_ID` | `3a3df604a18c4d479f4ed4f05b9a1896` |
| `PAYER_NAME` | `MockPayer` |
| `PATIENT_ID` | `pat-2` |
| `ENCOUNTER_ID` | `enc-2` |
| `USER_PRACTITIONER_ID` | `prac-3` |
| `FHIR_CONTEXT_JSON` | `["ServiceRequest/ord-2-home-pd","Coverage/cov-2"]` |
| `APP_CONTEXT` | `{"coverage-assertion-id":"eeea1400...","questionnaire":["http://example.org/fhir/Questionnaire/home-dialysis-adaptive"],"order":"ServiceRequest/ord-2-home-pd","coverage":"Coverage/cov-2"}` |
| `APP_URL` | `https://dtr.example.org/launch` |
| `EXPIRES_AT` | `2026-10-11T19:11:15Z` |

---

## 4. Endpoints

### 4.1 `GET /api/v1/dtr/launch/{linkId}`: open the payer's DTR app

**Why we use it.** It turns the stored link into a real SMART EHR launch. The EHR does not need to know SMART: it opens a URL.

**Request** (opened in the clinician's browser)

```http
GET /api/v1/dtr/launch/3a3df604a18c4d479f4ed4f05b9a1896
```

#### What the gateway does

1. Reads the link from `PA_DTR_LINK` and checks that it has not expired.
2. Reads the payer's current settings to get its FHIR release (`PA_PAYER.FHIR_VERSION`).
3. Creates the launch at the FHIR API:

```http
POST http://localhost:5100/internal/smart-launches
Content-Type: application/json

{
  "patientId": "pat-2",
  "userPractitionerId": "prac-3",
  "encounterId": "enc-2",
  "fhirContext": ["ServiceRequest/ord-2-home-pd", "Coverage/cov-2"],
  "appContext": "{\"coverage-assertion-id\":\"eeea1400...\",\"questionnaire\":[\"http://example.org/fhir/Questionnaire/home-dialysis-adaptive\"], ...}",
  "fhirVersion": "r4"
}
```

   The FHIR API answers with `launchId` and `iss`.

4. Redirects the browser to the app with both values.

**Response 302**

```http
HTTP/1.1 302 Found
Location: https://dtr.example.org/launch?iss=http%3A%2F%2Flocalhost%3A5100%2Ffhir%2Fr4&launch=Xq3vT9bK2mWfP0aZrY7c
```

When the app URL already has a query string, `iss` and `launch` are added with `&`.

#### What the app does next (not gateway endpoints)

These calls go to the **FHIR API**. They are described in `FHIRAPI/docs/FHIRAPI-Guide.md`, sections 3.7 to 3.12.

| Step | Call at the FHIR API |
|---|---|
| Find the SMART endpoints | `GET {iss}/.well-known/smart-configuration` |
| Get a code | `GET {iss}/auth/authorize?launch=...` |
| Get the launch context (patient, encounter, user, orders, coverage, `appContext`) | `POST {iss}/auth/token` |
| Read chart data for pre-fill | `GET {iss}/Patient/{id}`, `GET {iss}/Observation?...` and so on |
| Save the form | `POST {iss}/QuestionnaireResponse` or `PUT {iss}/QuestionnaireResponse/{id}` |

The FHIR API launch must be used within 15 minutes. If the app is opened later, open the gateway link again: each open creates a new launch.

#### Errors

The browser gets a short HTML page ("Documentation link unavailable") for these:

| HTTP | Page text | When |
|---|---|---|
| 404 | "This documentation link has expired. Sign the order again to get a new link." | Unknown `linkId`, or the link is older than `Workflow:DtrLinkHours` |
| 404 | "Payer '...' is no longer configured or enabled." | The payer row was removed or disabled |
| 422 | "No DTR app URL is known for this link ..." | No URL in `PA_PAYER.DTR_APP_LAUNCH_URL`, `Workflow:DtrAppUrl` or the payer's card |

These return the normal JSON error body:

| HTTP | When |
|---|---|
| 502 | The FHIR API could not create the launch |
| 503 | The EHR data API is not available (the link could not be read) |

---

### 4.2 `GET /api/v1/dtr/saved-forms`: did the app finish?

**Why we use it.** The payer's app saves its `QuestionnaireResponse` at the FHIR API, not at the gateway. This call lets the EHR see the form and its status without FHIR.

**Request**

```http
GET /api/v1/dtr/saved-forms?patientId=pat-2&orderId=ord-2-home-pd
```

| Parameter | Required | Meaning |
|---|---|---|
| `patientId` | Yes | Patient id |
| `orderId` | No | Only the forms of this order |

**Response 200**

```json
[
  {
    "questionnaireResponseId": "qr-8c1d2e",
    "questionnaire": "http://example.org/fhir/Questionnaire/home-dialysis-adaptive",
    "status": "completed",
    "authored": "2026-10-08T19:11:55Z",
    "orderId": "ord-2-home-pd"
  }
]
```

| Field | Meaning |
|---|---|
| `questionnaireResponseId` | Id at the FHIR API. Use it in PAS (`questionnaireResponseIds`) or CDex when needed |
| `questionnaire` | The payer's questionnaire URL as the app saved it |
| `status` | `in-progress`, `completed`, `amended`, `stopped`, `entered-in-error` |
| `authored` | When the form was last saved |
| `orderId` | The order named in the form's `qr-context` extension. Empty when the app did not name one |

**How to read it**

| Result | Meaning |
|---|---|
| Empty list | The app has not saved anything yet |
| `status = "in-progress"` | The user started but did not finish |
| `status = "completed"` or `"amended"` | The form is ready. Go on to PAS |

| HTTP | When |
|---|---|
| 200 | The saved forms, newest first |
| 400 | `patientId` is missing |
| 502 | The FHIR API failed |

---

### 4.3 `POST /api/v1/dtr/{payerName}/Questionnaire/$questionnaire-package`: relay to the payer

**Why it exists.** A DTR app normally calls its own payer for the questionnaire. An app that knows only the gateway can send the same request here; the gateway adds the payer's URL and passes the body on **unchanged**.

**Request** (from the DTR app)

```http
POST /api/v1/dtr/MockPayer/Questionnaire/$questionnaire-package
Content-Type: application/fhir+json

{
  "resourceType": "Parameters",
  "parameter": [
    { "name": "coverage", "resource": { "resourceType": "Coverage", "id": "cov-2", "...": "..." } },
    { "name": "order", "resource": { "resourceType": "ServiceRequest", "id": "ord-2-home-pd", "...": "..." } },
    { "name": "questionnaire", "valueCanonical": "http://example.org/fhir/Questionnaire/home-dialysis-adaptive" }
  ]
}
```

`Content-Type` may be `application/fhir+json` or `application/json`.

**The gateway calls:** `POST {payer BASE_URL}/{FHIR_PATH}/Questionnaire/$questionnaire-package` with the same body.

**Response:** the payer's own status code and body, as `application/fhir+json`. A payer error (for example 400 with an `OperationOutcome`) is passed through as it is. An empty payer body becomes `{}`.

| HTTP | When |
|---|---|
| The payer's status | The payer answered (2xx, 4xx or 5xx) |
| 404 | `payerName` is not configured or not enabled (JSON error body) |
| 502 | The payer could not be reached |
| 504 | The payer did not answer within `TIMEOUT_SECONDS` |

---

### 4.4 `POST /api/v1/dtr/{payerName}/Questionnaire/$next-question`: relay for adaptive forms

The same as 4.3, for adaptive questionnaires. The app sends its `QuestionnaireResponse` (or a `Parameters` holding it) and gets the payer's answer with the next questions.

```http
POST /api/v1/dtr/MockPayer/Questionnaire/$next-question
Content-Type: application/fhir+json

{ "resourceType": "QuestionnaireResponse", "status": "in-progress", "contained": [ { "resourceType": "Questionnaire", "...": "..." } ], "...": "..." }
```

**The gateway calls:** `POST {payer BASE_URL}/{FHIR_PATH}/Questionnaire/$next-question`. Status codes are the same as in 4.3.

---

## 5. How the form reaches PAS

`POST /api/v1/pas/requests` searches the patient's forms at the FHIR API and attaches a form **automatically** when all of these are true:

- its status is `completed` or `amended`
- its `qr-context` extension names an order
- that order is in the `orderIds` of the PAS request

So the payer's app must write `qr-context` (DTR requires it):

```json
"extension": [
  { "url": "http://hl7.org/fhir/us/davinci-dtr/StructureDefinition/qr-context", "valueReference": { "reference": "ServiceRequest/ord-2-home-pd" } },
  { "url": "http://hl7.org/fhir/us/davinci-dtr/StructureDefinition/qr-context", "valueReference": { "reference": "Coverage/cov-2" } }
]
```

If `saved-forms` shows the form with an empty `orderId`, the app did not name the order. In that case send the id yourself in the PAS request: `"questionnaireResponseIds": ["qr-8c1d2e"]`.

---

## 6. R4 and R5 payers

| `PA_PAYER.FHIR_VERSION` | `fhirVersion` sent to the FHIR API | `iss` the app gets |
|---|---|---|
| `R4` (default) | `r4` | `{FHIR API}/fhir/r4` |
| `R5` | `r5` | `{FHIR API}/fhir/r5` |

The payer's current setting is read at launch time, so changing `FHIR_VERSION` works for links that were already given out.

---

## 7. Payer setup checklist

| Setting | Value for this flow |
|---|---|
| `PA_PAYER.DTR_MODE` | `SmartApp` |
| `PA_PAYER.DTR_APP_LAUNCH_URL` | Empty (use the URL on the payer's card), or the payer's fixed launch URL |
| `PA_PAYER.FHIR_VERSION` | `R4` or `R5`, as the payer's app expects |
| `Workflow:DtrAppUrl` | Normally empty |
| `Workflow:GatewayPublicBaseUrl` | The gateway URL the clinician's browser can reach |
| `Workflow:DtrLinkHours` | 72 |
| `FhirServer:PublicBaseUrl` (FHIR API) | The FHIR API URL the payer's app can reach. It becomes `iss` |

Check with `GET /api/v1/payers`: the payer must show `"dtrMode": "SmartApp"`.

Things to know:

- **No authentication is built in.** The gateway and the FHIR API check no tokens. When authentication is added outside this code, the payer's app must be allowed through it.
- **CORS** is open to any origin, because DTR apps run in a browser. Restrict the origins in production.
- **A link is created for every `smart` card**, also for payers in `Forms` mode. Only `opensPayerApp` tells the EHR which flow to use.

---

## 8. Where the data is stored

| Table | Key | What | Saved by |
|---|---|---|---|
| `PA_DTR_LINK` | `LINK_ID` | The launch details of one link | Gateway (at order-sign) |
| `PA_QUESTIONNAIRE_RESPONSE` | `ID` | The FHIR QuestionnaireResponse the app saved | FHIR API |

The launch itself (`launchId`) is kept by the FHIR API for 15 minutes and is not stored in the database.

---

## 9. Test it

There is no payer SMART app in the demo set, so the whole flow cannot be shown end to end yet. What can be tested now:

```http
### 1. set the mock payer to SmartApp (mock EHR only: PUT replaces the whole PA_PAYER row)
PUT http://localhost:5090/api/prior-auth-data/payers/MockPayer
Content-Type: application/json

{
  "payerName": "MockPayer",
  "displayName": "Mock Da Vinci Payer",
  "enabled": true,
  "payerIdentifier": "PAYER001",
  "baseUrl": "http://localhost:5080",
  "cdsServicesPath": "cds-services",
  "fhirPath": "fhir",
  "authType": "none",
  "signCdsHooksRequests": true,
  "useSubscriptions": true,
  "memberIdSystem": "http://example.org/member-ids",
  "payerIdSystem": "http://example.org/payer-ids",
  "crdTimeoutSeconds": 300,
  "timeoutSeconds": 300,
  "fhirVersion": "R4",
  "dtrMode": "SmartApp",
  "dtrAppLaunchUrl": null
}

### 1b. check: "dtrMode": "SmartApp" (the gateway keeps the payer list for Storage:PayerCacheMinutes, so wait 5 minutes or restart the gateway)
GET http://localhost:5200/api/v1/payers

### 2. sign an order: the DTR message link now has "opensPayerApp": true
POST http://localhost:5200/api/v1/crd/order-sign
Content-Type: application/json

{
  "eventId": "evt-smart-0001",
  "payerName": "MockPayer",
  "patientId": "pat-2",
  "orderIds": ["ord-2-home-pd"],
  "encounterId": "enc-2",
  "userPractitionerId": "prac-3"
}

### 3. open the link from step 2 in a browser: it redirects to the app URL with iss and launch
GET http://localhost:5200/api/v1/dtr/launch/{linkId}

### 4. forms saved for the order
GET http://localhost:5200/api/v1/dtr/saved-forms?patientId=pat-2&orderId=ord-2-home-pd

### 5. relay to the payer
POST http://localhost:5200/api/v1/dtr/MockPayer/Questionnaire/$questionnaire-package
Content-Type: application/fhir+json

{ "resourceType": "Parameters", "parameter": [ { "name": "questionnaire", "valueCanonical": "http://example.org/fhir/Questionnaire/home-dialysis-adaptive" } ] }
```

In step 3 the mock payer's card points to `https://dtr.example.org/launch`, which is not a real app. The redirect itself (the `Location` header with `iss` and `launch`) shows that the launch was created.

---

## 10. Code map

| File | What it does |
|---|---|
| `Controllers/DtrPayerAppController.cs` | `launch/{linkId}` and the two relay endpoints |
| `Controllers/DtrFormsController.cs` | `saved-forms` |
| `Services/Dtr/DtrService.cs` | `LaunchAsync`, `GetSavedFormsAsync`, `RelayToPayerAsync` |
| `Services/Crd/CrdService.cs` | `ToMessageAsync` (saves the link), `ChooseDtrAppUrl` |
| `Clients/FhirApiClient.cs` | `CreateSmartLaunchAsync` (`POST /internal/smart-launches`) |
| `Services/Payers/PayerClient.cs` | `PostFhirAsync` (the relay call) |
| `Repositories/Database/EhrApiDtrLinkRepository.cs` | `PA_DTR_LINK` through the EHR data API |
| `Models/Entities/DtrLinkRecord.cs`, `Models/Responses/EhrMessageLink.cs`, `SavedForm.cs` | The data |
