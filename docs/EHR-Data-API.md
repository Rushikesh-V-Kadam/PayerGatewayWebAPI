# EHR Data API: tables and API details

**For the EHR team.** This is what the Payer Gateway needs from the EHR so that all prior authorization data is kept in the EHR's own Oracle database.

- Tables to create: `PayerGatewayWebAPI/Database/01_create_tables.sql` (7 tables, all named `PA_...`). It is the only
  script: no script has been run yet, so there are no ALTER scripts.
- Web API to build: 15 small endpoints, described below. Each one reads or saves **one row of one table**.
- A working example of the whole API (in memory, for testing): `MockEhr.Api/Controllers/PriorAuthDataController.cs`.
- Swagger file of the API (every endpoint and every property, with its meaning, column, example and required mark):
  `EHR-API-swagger.json`, next to this document. Open it in Swagger Editor or import it into Postman. The same document
  is live in the mock EHR: `http://localhost:5090/swagger`, then "Select a definition" > "For the EHR team".
  It also holds the EHR read APIs the FHIR API calls (patient, insurance, orders, clinical data, documents).

## 1. How it fits together

```
EHR screens ──(existing gateway API: order-sign, forms, prior auth)──▶ Payer Gateway ──(FHIR)──▶ Payer
     │                                                                      │
     │ SQL (read)                                                           │ EHR Data API (this document)
     ▼                                                                      ▼
  EHR Oracle database:  PA_PAYER, PA_DTR_FORM, PA_PRIOR_AUTH, ...  ◀── EHR Web API (runs the SQL)
```

- The gateway has **no database connection**. It calls the EHR Data API for every read and every save.
- The API must offer **read (GET) and save (PUT)**. Save alone is not enough: the gateway reads its rows back, for
  example when a payer answers days later, when a form is answered, or to find the requests that are still pended.
- The EHR reads the same tables with its own SQL. Because the prior authorization decisions are already in the EHR
  database, the gateway **no longer sends a second copy to the EHR** (the old "write-back" calls
  `PUT api/orders/{id}/coverage-decision` and `PUT api/prior-auth-requests/{id}` are gone and do not need to be built).
- **CRD order-sign decisions are not stored by the gateway.** The EHR stores the order-sign response on its side.
  DTR and PAS do not read the decisions back: the EHR sends the values they need (`insurancePlanId`, `coverageId`,
  `questionnaireUrls`, `coverageAssertionId`) in its own requests. (Changed Oct 30, 2026: table `PA_COVERAGE_DECISION`
  and its two endpoints are removed.)
- The payer list moves from the gateway's configuration file to table `PA_PAYER`. The EHR team maintains it; the
  gateway reads it.

## 2. Rules for every endpoint

| Topic | Rule |
| --- | --- |
| Base URL | `{EHR base URL}/api/prior-auth-data` (the path can be changed: gateway setting `Ehr:DataPath`) |
| Security | No authentication is built into the gateway; it is added outside the gateway code. Use HTTPS. |
| Format | JSON, UTF-8, `Content-Type: application/json`, property names in camelCase. |
| Row = columns | A row is a flat JSON object. **One property = one column**: `requestId` ↔ `REQUEST_ID`, `decisionJson` ↔ `DECISION_JSON`. |
| JSON text columns | Properties ending in `Json` or `Bundle`, and `appContext`, are **strings** that contain JSON text. Store the string unchanged in the CLOB column and return it unchanged. Do not parse or re-format it. |
| Timestamps | UTC, ISO-8601 with `Z`: `"2026-10-07T18:36:17Z"` (fractions of a second are allowed). |
| Booleans | JSON `true` / `false` (stored as `NUMBER(1)` 1 / 0). When it reads a row, the gateway also understands `1` / `0` and `"Y"` / `"N"`, but `true` / `false` is the agreed format. |
| Empty values | `null` (or leave the property out). |
| PUT | **Insert, or update when the key in the URL already exists** (Oracle `MERGE`). Answer `204` (or `200`). The key in the URL wins over the key in the body. |
| GET one row | `200` with the row, or `404` when there is no such row. |
| GET a list | `200` with a JSON array `[ {...}, {...} ]`; an empty array `[]` when nothing matches. |
| Errors | Any other status (`400`, `401`, `500` ...) means failure. A short text or JSON body with the reason helps: the gateway logs the first 300 characters. |
| Ids in the URL | URL-encoded by the gateway (`PA%2F2026%2F1`). Decode them before use. |
| Payer key | `insurancePlanId` (a whole number, column `INSURANCE_PLAN_ID`) is the payer's key everywhere: in `PA_PAYER` and in every other table, path and filter. The gateway sends it as a plain number (`1001`). |
| Extra properties | You may return extra properties (your own audit columns); the gateway ignores them. |

## 3. Endpoints

All paths are under `{EHR base URL}/api/prior-auth-data`.

| # | Method and path | Table | Used for |
| --- | --- | --- | --- |
| 1 | `GET payers/{insurancePlanId}` | `PA_PAYER` | One payer's configuration by insurance plan id (the only payer endpoint; there is no payer list). `WHERE INSURANCE_PLAN_ID = :insurancePlanId`. Answer in the client's wrapper `{ "PayerDetail": {...}, "TOKEN_EXPIRED": "", "ERROR_MESSAGE": "" }`; `PayerDetail` is `null` when there is no such payer (section 4.1). |
| 2 | `GET dtr-forms/{formId}` | `PA_DTR_FORM` | One payer questionnaire and its answers. |
| 3 | `PUT dtr-forms/{formId}` | `PA_DTR_FORM` | Save it. |
| 4 | `GET dtr-links/{linkId}` | `PA_DTR_LINK` | One payer SMART DTR app link. |
| 5 | `PUT dtr-links/{linkId}` | `PA_DTR_LINK` | Save it. |
| 6 | `GET prior-auths/{requestId}` | `PA_PRIOR_AUTH` | One prior authorization request. |
| 7 | `PUT prior-auths/{requestId}` | `PA_PRIOR_AUTH` | Save it (new request, update, cancel, new payer decision). |
| 8 | `GET prior-auths?status=pended[&insurancePlanId=X]` | `PA_PRIOR_AUTH` | Requests still waiting for a payer. `WHERE STATUS = :status [AND INSURANCE_PLAN_ID = :insurancePlanId]` |
| 9 | `GET payer-subscriptions/{insurancePlanId}/{organizationNpi}` | `PA_PAYER_SUBSCRIPTION` | Did we already subscribe at this payer for this organization? |
| 10 | `PUT payer-subscriptions/{insurancePlanId}/{organizationNpi}` | `PA_PAYER_SUBSCRIPTION` | Save the subscription. |
| 11 | `PUT cdex-tasks/{insurancePlanId}/{taskId}` | `PA_CDEX_TASK` | Save a payer information request (Task). |
| 12 | `GET cdex-tasks?requestId=X` | `PA_CDEX_TASK` | All Tasks of one prior authorization request. |
| 13 | `GET questionnaire-responses/{id}` | `PA_QUESTIONNAIRE_RESPONSE` | One filled-in form in FHIR format (called by the FHIR API). |
| 14 | `PUT questionnaire-responses/{id}` | `PA_QUESTIONNAIRE_RESPONSE` | Save it. |
| 15 | `GET questionnaire-responses?patientId=X` | `PA_QUESTIONNAIRE_RESPONSE` | All FHIR forms of a patient, newest first (`ORDER BY LAST_UPDATED_UTC DESC`). |

There is no DELETE. Nothing is removed by the gateway.

## 4. The rows

Types: *text* = JSON string, *JSON text* = JSON string that holds JSON (CLOB), *time* = UTC timestamp, *bool* = true / false, *int* = whole number. **Bold** = never empty.

### 4.1 `PA_PAYER` (endpoint 1; maintained by the EHR team)

**Only `insurancePlanId`, `payerName` and `baseUrl` must be filled.** Every other property is optional: it may be `null`, an empty text,
or left out of the JSON, and the gateway then uses the default in the table. A row with only those three values is a
working payer. An optional value the gateway cannot understand (for example text in `timeoutSeconds`) does not switch
the payer off: the default is used and a warning is written to the gateway log. A row without `payerName`, or without a
complete `http` / `https` address in `baseUrl`, is left out (also with a warning).

| Property | Type | Meaning |
| --- | --- | --- |
| **insurancePlanId** | int | The EHR's insurance plan id of this payer (`INSURANCE_PLAN_ID`). Key of the table and of endpoint 1. The EHR sends it as `insurancePlanId` in every gateway request. |
| **payerName** | text | Name of the payer. Only shown in the gateway's log lines and messages. |
| enabled | bool | `false` = switched off (the gateway answers "payer not configured"). Empty = `true`. |
| **baseUrl** | text | Payer base URL: a complete `http` / `https` address, e.g. `https://api.payer.com`. |
| cdsServicesPath | text | CDS Hooks path under baseUrl. Empty = `cds-services`. `/` = no extra path (the address is baseUrl itself). |
| fhirPath | text | FHIR path under baseUrl. Empty = `fhir`. `/` = no extra path (the address is baseUrl itself). |
| authType | text | *Not read by the gateway (kept for the authentication layer).* `none`, `client-secret` or `smart-backend`. Empty = `none`. |
| authTokenUrl | text | *Not read by the gateway (kept for the authentication layer).* The payer's OAuth token URL. |
| authClientId | text | *Not read by the gateway (kept for the authentication layer).* Client id registered with the payer. |
| authClientSecret | text | *Not read by the gateway (kept for the authentication layer).* Client secret (`client-secret` only). **Sensitive.** |
| authScope | text | *Not read by the gateway (kept for the authentication layer).* OAuth scopes. |
| signCdsHooksRequests | bool | *Not read by the gateway (kept for the authentication layer).* Send a signed JWT with each CDS Hooks call. |
| useSubscriptions | bool | Ask the payer to notify the gateway about decisions. Empty = `true`. |
| notificationSecret | text | *Not read by the gateway (kept for the authentication layer).* Secret the payer sends with notifications. **Sensitive.** |
| crdTimeoutSeconds | int | Seconds to wait for the payer at order-sign. Empty or 0 = 5. |
| timeoutSeconds | int | Seconds to wait for every other payer call. Empty or 0 = 30. |
| dtrMode | text | `Forms` (the EHR shows the payer's questions) or `SmartApp` (the payer's own SMART DTR app). Empty = `Forms`. |
| dtrAppLaunchUrl | text | Launch URL of the payer's DTR app. Empty = the URL on the payer's CRD card. |

**Changed (Oct 30, 2026):** the payer key is renamed: property `piInsurancePlanId` is now `insurancePlanId` and column
`PI_INSURANCE_PLAN_ID` is now `INSURANCE_PLAN_ID`, in every table, path and filter. In the payer answer the client's
name `InsurancePlanID` is used (the gateway does not mind upper / lower case).

**Changed (Oct 29, 2026):** `insurancePlanId` is now a whole number (`NUMBER(10)`) and the key of `PA_PAYER`
(previously `PAYER_NAME`). The list endpoint `GET payers` is removed: the gateway only reads one payer by its id. Every other
table stores `insurancePlanId` instead of `payerName` (`INSURANCE_PLAN_ID` instead of `PAYER_NAME`), and the paths
and filters use it (`payer-subscriptions/{insurancePlanId}/...`, `cdex-tasks/{insurancePlanId}/...`,
`prior-auths?status=pended&insurancePlanId=...`). The gateway reads payers only through this endpoint.

**Changed (Oct 28, 2026):** added `insurancePlanId` (column `INSURANCE_PLAN_ID`), so one payer's configuration can
be read by the EHR's insurance plan id (endpoint 1a); removed `displayName` (column `DISPLAY_NAME`) - the gateway shows
`payerName` in its messages. The member id still belongs only to the patient's insurance (`memberId`). `memberIdSystem` and `payerIdSystem` stay removed (Oct 9, 2026): the gateway sends the ids
without an identifier "system"; if the API still returns them, the gateway ignores them.

**Answer format of `GET payers/{insurancePlanId}` (the client EHR's format, Oct 30, 2026).** The row is wrapped in
`PayerDetail` and uses the client's property names. The gateway matches property names without regard to upper / lower
case, so `BaseURL` and `baseUrl` are the same; only the plan id is called `InsurancePlanID` here.

```http
GET {EHR base URL}/api/prior-auth-data/payers/1001
```

```json
{
  "PayerDetail": {
    "InsurancePlanID": 1001, "PayerName": "MockPayer", "Enabled": true,
    "BaseURL": "http://localhost:5080",
    "CDSServicesPath": "cds-services", "FHIRPath": "fhir",
    "AuthType": "none", "AuthTokenURL": null, "AuthClientID": null, "AuthClientSecret": null, "AuthScope": null,
    "SignCDSHooksRequests": true, "UseSubscriptions": true, "NotificationSecret": null,
    "CRDTimeoutSeconds": 30, "TimeoutSeconds": 30,
    "DTRMode": "Forms", "DTRAppLaunchURL": null
  },
  "TOKEN_EXPIRED": "",
  "ERROR_MESSAGE": ""
}
```

| Case | Answer | What the gateway does |
| --- | --- | --- |
| Payer found | `PayerDetail` = the row, both texts empty | Uses the row (a disabled payer is returned too; the gateway refuses it) |
| No payer with this id | `{ "PayerDetail": null, "TOKEN_EXPIRED": "", "ERROR_MESSAGE": "" }` | "Not configured": `400` / `404` to its caller. An HTTP `404` is understood the same way. |
| Error | `TOKEN_EXPIRED` or `ERROR_MESSAGE` filled | The call failed: `503` to its caller; the text is written to the gateway log. (Which values `TOKEN_EXPIRED` can hold is still to be confirmed; for now any text means failure.) |

The smallest row that works: `{ "PayerDetail": { "InsurancePlanID": 2001, "PayerName": "AcmeHealth", "BaseURL": "https://api.acmehealth.example" }, "TOKEN_EXPIRED": "", "ERROR_MESSAGE": "" }`.

A new or changed payer is picked up by the gateway within `Storage:PayerCacheMinutes` (default 5 minutes); an insurance plan id the
gateway has no payer for yet is looked up again immediately.

### 4.2 `PA_DTR_FORM` (endpoints 2, 3)

| Property | Type | Meaning |
| --- | --- | --- |
| **formId** | text | Key (created by the gateway, e.g. `frm-235b7ad69e85`). |
| **insurancePlanId** | int | Insurance plan id of the payer. |
| **patientId** | text | |
| coverageId | text | |
| orderIdsJson | JSON text | `["ord-1-hd"]` |
| **questionnaireUrl** | text | Up to 1000 characters. |
| **questionnaireJson** | JSON text | The payer's FHIR Questionnaire (can be 100 KB or more). |
| **status** | text | `new`, `in-progress`, `completed` |
| questionnaireResponseId | text | Id of the row in `PA_QUESTIONNAIRE_RESPONSE`. |
| answersJson | JSON text | `[{"linkId":"egfr","values":["9"]}]` |
| **adaptive** | bool | |
| **payerFinished** | bool | |
| adaptiveResponseJson | JSON text | |
| **createdAt** | time | |
| **updatedAt** | time | |

### 4.3 `PA_DTR_LINK` (endpoints 4, 5)

| Property | Type | Meaning |
| --- | --- | --- |
| **linkId** | text | Key (created by the gateway). |
| **insurancePlanId** | int | Insurance plan id of the payer. |
| **patientId** | text | |
| encounterId | text | |
| userPractitionerId | text | |
| fhirContextJson | JSON text | `["ServiceRequest/ord-1001","Coverage/cov-1001"]` |
| appContext | text (CLOB) | Free text from the payer. Store unchanged. |
| **appUrl** | text | Up to 1000 characters. |
| **expiresAt** | time | |

### 4.4 `PA_PRIOR_AUTH` (endpoints 6, 7, 8)

| Property | Type | Meaning |
| --- | --- | --- |
| **requestId** | text | The EHR's request id. Key. |
| **insurancePlanId** | int | Insurance plan id of the payer. |
| **patientId** | text | |
| **coverageId** | text | |
| memberId | text | |
| **organizationId** | text | |
| organizationNpi | text | |
| practitionerId | text | |
| practitionerNpi | text | |
| **priority** | text | `routine`, `urgent` |
| **status** | text | `approved`, `partially-approved`, `denied`, `pended`, `not-required`, `cancelled`, `contact-payer`, `error` |
| authorizationNumber | text | Filled when the payer approved. |
| **versionNo** | int | 1 = first submission, +1 for every update / cancel. |
| orderIdsJson | JSON text | `["ord-1-hd"]` |
| lineNumbersJson | JSON text | `{"ord-1-hd":1}` |
| decisionJson | JSON text | The full decision, same structure as the response of `POST /pas/requests` (`overallStatus`, `lines`, `informationRequests`, `decisionDueBy` ...). |
| lastRequestBundle | JSON text | Last FHIR Bundle sent to the payer. **Can be several MB** (the clinical documents are inside). |
| lastResponseBundle | JSON text | Last FHIR Bundle received from the payer. |
| **submittedAt** | time | |
| **updatedAt** | time | Changes on every save. |

```json
{
  "requestId": "PA-2026-0001", "insurancePlanId": 1001, "patientId": "pat-1", "coverageId": "cov-1",
  "memberId": "MBR-1001", "organizationId": "facility-loc-main", "organizationNpi": "1999999992",
  "practitionerId": "prac-1", "practitionerNpi": "1111111112",
  "priority": "routine", "status": "pended", "authorizationNumber": null, "versionNo": 1,
  "orderIdsJson": "[\"ord-1-hd\"]",
  "lineNumbersJson": "{\"ord-1-hd\":1}",
  "decisionJson": "{\"requestId\":\"PA-2026-0001\",\"overallStatus\":\"pended\",\"lines\":[{\"sequence\":1,\"orderId\":\"ord-1-hd\",\"decision\":\"pended\"}],\"informationRequests\":[],\"errors\":[]}",
  "lastRequestBundle": "{\"resourceType\":\"Bundle\", ...}",
  "lastResponseBundle": "{\"resourceType\":\"Bundle\", ...}",
  "submittedAt": "2026-10-07T18:36:17.0159473Z",
  "updatedAt": "2026-10-07T18:36:17.2908898Z"
}
```

### 4.5 `PA_PAYER_SUBSCRIPTION` (endpoints 9, 10)

| Property | Type | Meaning |
| --- | --- | --- |
| **insurancePlanId** | int | Insurance plan id of the payer. Key, part 1. |
| **organizationNpi** | text | Key, part 2. |
| subscriptionId | text | The payer's id of the subscription. |
| **status** | text | `active`, `error` |
| **createdAt** | time | |

### 4.6 `PA_CDEX_TASK` (endpoints 11, 12)

| Property | Type | Meaning |
| --- | --- | --- |
| **insurancePlanId** | int | Insurance plan id of the payer. Key, part 1. |
| **taskId** | text | Key, part 2 (the payer's Task id). |
| **requestId** | text | The prior authorization request it belongs to. |
| **trackingId** | text | |
| trackingSystem | text | |
| status | text | `requested`, `in-progress`, `completed`, `cancelled` ... |
| dueDate | text | Date text as the payer sent it, e.g. `2026-10-21`. |
| **taskJson** | JSON text | The FHIR Task as received. |
| **receivedAt** | time | |

### 4.7 `PA_QUESTIONNAIRE_RESPONSE` (endpoints 13, 14, 15; called by the FHIR API)

| Property | Type | Meaning |
| --- | --- | --- |
| **id** | text | FHIR id of the form. Key. |
| **patientId** | text | |
| orderId | text | |
| coverageId | text | |
| questionnaireUrl | text | |
| **status** | text | `in-progress`, `completed`, `amended`, `entered-in-error`, `stopped` |
| **resourceJson** | JSON text | The complete FHIR QuestionnaireResponse. It is sent to the payer unchanged, so it must come back exactly as it was saved. |
| ehrResponseId | text | Id of the readable chart copy in the EHR, if the EHR keeps one. |
| **ehrSyncStatus** | text | `PENDING`, `SENT`, `FAILED` (state of that chart copy). |
| **lastUpdatedUtc** | time | |

## 5. What the EHR reads itself

Nothing is pushed to the EHR. The EHR screens read the tables (example SQL is at the end of `01_create_tables.sql`):

| The EHR wants to show | Read |
| --- | --- |
| Status and authorization number of a request | `PA_PRIOR_AUTH` by `REQUEST_ID` (`STATUS`, `AUTHORIZATION_NUMBER`) |
| Decision per order line, validity dates | `PA_PRIOR_AUTH.DECISION_JSON` → `lines[]` |
| What the payer still needs (documents, due date) | `PA_PRIOR_AUTH.DECISION_JSON` → `informationRequests[]` |
| The questionnaire and the answers given | `PA_DTR_FORM` (`ANSWERS_JSON`), or `GET /dtr/forms/{formId}` on the gateway |

**Decisions that arrive later.** A payer can pend a request and decide days later. The payer's notification, or the
EHR's own call to inquire for a pended request (the gateway has no background job), then updates the same `PA_PRIOR_AUTH` row. The EHR can notice this in any of three ways:
read the row when the screen opens; ask for rows with `UPDATED_AT > :lastCheck`; or react inside the Web API when
endpoint 7 is called (for example, raise a task for the user when `status` changes).
The gateway saves a row only when something in it changed: asking a payer again and getting the same answer does not
save, so `UPDATED_AT` of `PA_PRIOR_AUTH` and `RECEIVED_AT` of `PA_CDEX_TASK` move only when there is something new.

The same data is also available from the gateway: `GET /pas/requests/{requestId}`. The CRD order-sign decisions are
not in these tables: the EHR keeps the order-sign response itself.

## 6. What the gateway expects from the API

- **Speed.** The clinician waits during order-sign, and that call reads the payer (and saves payer app links). Aim for well
  under 1 second per call. The gateway gives up after `Ehr:TimeoutSeconds` (default 30).
- **Retries.** The gateway tries a failed PUT up to 3 times. PUT must therefore be safe to repeat (insert or update).
- **Availability.** If the data API is down, the gateway answers its callers with `503` and nothing can be processed.
- **Size.** Allow request and response bodies of at least 20 MB: `lastRequestBundle` contains the attached clinical
  documents. (Many web servers default to a few MB, so this usually has to be configured.)
- **Secrets.** `PA_PAYER` holds payer credentials. Restrict access to the table and to endpoint 1.

Data API calls per gateway operation (for sizing):

| Gateway operation | Data API calls |
| --- | --- |
| Order-sign | `GET payers/{insurancePlanId}` (kept 5 minutes), and `PUT dtr-links` for each payer app link |
| Get a form | `PUT dtr-forms` per questionnaire |
| Save answers | `GET dtr-forms`, `GET` + `PUT questionnaire-responses`, `PUT dtr-forms` |
| Submit / update a request | `GET prior-auths/{id}`, `GET questionnaire-responses?patientId`, `PUT prior-auths/{id}`, `GET payer-subscriptions` (+ `PUT` the first time) |
| Ask the payer again (inquire) | `GET prior-auths/{id}`; `PUT prior-auths/{id}` only when the decision changed |
| Look for payer Tasks (`information-requests?refresh=true`, called by the EHR for a pended request) | `GET prior-auths/{id}`, then per Task the payer has open `GET prior-auths/{id}` + `GET cdex-tasks?requestId`. A `PUT` (`prior-auths`, `cdex-tasks`) follows only when something changed |
| Payer notification | `GET prior-auths?status=pended&insurancePlanId={insurancePlanId}`, then the inquire calls above per request |

## 7. Testing

- The mock EHR (`MockEhr.Api`, port 5090) implements the endpoints. Start it, the FHIR API and the mock
  payer, set `Storage:Provider` to `EhrApi` in the gateway, and run `PayerGatewayWebAPI.http` from top to bottom.
- To test the real API, change `Ehr:BaseUrl` and `Ehr:DataPath` in the gateway's `appsettings.json`
  (and `Ehr:BaseUrl` / `Ehr:DataPath` in the FHIR API) and run the same file.
- Quick checks with curl:

```
curl {EHR}/api/prior-auth-data/payers/1001
curl -i {EHR}/api/prior-auth-data/prior-auths/DOES-NOT-EXIST        (expect 404)
curl "{EHR}/api/prior-auth-data/prior-auths?status=pended"           (expect [] or rows)
```
