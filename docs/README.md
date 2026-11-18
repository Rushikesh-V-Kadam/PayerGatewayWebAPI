# Payer Gateway Web API (simple version)

Runs the Da Vinci prior authorization flow for the EHR against any connected payer:

| Step | EHR calls | Gateway calls the payer | When |
| --- | --- | --- | --- |
| 1 CRD | `POST /crd/order-sign` | CDS Hooks `order-sign` | Every signed order |
| 2 DTR | `POST /dtr/forms` → `POST /dtr/forms/{formId}/answers` | `Questionnaire/$questionnaire-package` | Decision has `callDtr = true` |
| 3 PAS | `POST /pas/requests` (+ PUT / cancel / GET / inquire) | `Claim/$submit`, `Claim/$inquire` | Decision has `callPas = true` |
| 4 CDex | `GET .../information-requests`, `POST .../attachments` | `Task` search, `$submit-attachment` | PAS is `pended` and the payer asks for more |

Full request / response specs with examples: run the API and open **http://localhost:5200/swagger**.
`PayerGatewayWebAPI.http` has every call in order.

## Where the data is stored

The gateway has **no database of its own and no database connection**. Everything it needs to remember is kept in the
EHR's Oracle database, in tables named `PA_...`, and is read and saved through a Web API the EHR team provides
(the **EHR data API**):

| What | Where |
| --- | --- |
| Tables (the EHR team runs this once) | `PayerGatewayWebAPI/Database/01_create_tables.sql` (the only script; no ALTER scripts) |
| The API the EHR team builds (17 endpoints: one row of one table each) | `docs/EHR-Data-API.md` |
| The gateway code that calls it | `Clients/EhrDataClient.cs` and `Repositories/Database/EhrApi*Repository.cs` |
| Settings | `Ehr:BaseUrl`, `Ehr:DataPath` |

**No write-back.** The EHR reads the prior authorization decisions from the same table (`PA_PRIOR_AUTH`), so the
gateway does not send a second copy to the EHR. CRD order-sign decisions are not stored by the gateway at all: the EHR
stores the order-sign response on its side. The `writeBack` request fields and the `EHR_SYNC_STATUS` column no
longer exist.

**No background job.** The gateway does not check pended requests by itself (`PendingRequestPoller` and
`Workflow:PollMinutes` are removed). The client EHR decides when to check: for each row with `STATUS = 'pended'` in
`PA_PRIOR_AUTH` it calls `POST /pas/requests/{requestId}/inquire` (latest decision) and
`GET /cdex/requests/{requestId}/information-requests?refresh=true` (payer requests for documents). The gateway
saves the result in `PA_PRIOR_AUTH` - only when it is different from what is stored, so `UPDATED_AT` moves only when
something changed. Payer notifications (PAS Subscription) and pushed Tasks still update the rows on their own.

**Repository folders.** The services use only the interfaces; `Startup.cs` registers the EHR data API implementations:

| Folder | Namespace | What is in it |
| --- | --- | --- |
| `Repositories/Interfaces` | `PayerGatewayWebAPI.Repositories.Interfaces` | `I...Repository`: what the services call |
| `Repositories/Database` | `PayerGatewayWebAPI.Repositories.Database` | `EhrApi...Repository`: saves to the EHR database through the EHR data API |

## How the payer is chosen

Every EHR request carries `insurancePlanId` (a whole number, the EHR's insurance plan id). The gateway reads that one
payer's configuration (table `PA_PAYER`, key `INSURANCE_PLAN_ID`) and uses its base URL, paths and timeouts. There is no payer list.
It is read with `GET payers/{insurancePlanId}` on the EHR data API (the mock EHR has the mock payer as plan id `1001`).
The payer is read for every request (no cache), so a new or changed payer row is used right away.
To onboard a payer, the EHR team inserts a row; nothing changes in the gateway and no restart is needed.
An unknown or disabled id returns 400 (404 when the id is in the URL).

**Only `INSURANCE_PLAN_ID`, `PAYER_NAME` and `BASE_URL` must be filled in a payer row.** Every other column is optional and has a default
(`PayerSettings.ApplyDefaults`); a row without a name or without a complete http / https address is left out with a
warning in the log. The payer row has no payer id and no identifier systems: the payer id and the member id come from
the patient's insurance in the EHR (FHIR `Coverage.payor.identifier` and `Coverage.subscriberId`) and are sent to the
payer as they are. The gateway never makes up an id or a "system".

**Orders** are service orders and medication orders (FHIR `ServiceRequest` and `MedicationRequest`). Device orders
(`DeviceRequest`) are not read.

**Which insurance is used:** always the `coverageId` of the request, the EHR's own id of the patient's active insurance
(required in order-sign, DTR forms and the PAS submit; a PAS update without it keeps the insurance of the request). The
client's orders carry no insurance, so the gateway never picks one. It reads `Coverage/{coverageId}` and checks that it
belongs to the patient and is active: in order-sign an unusable insurance gives `unknown` decisions (the payer is not
called), in DTR and PAS it gives 422.

## How the questionnaire is filled

The DTR flow is not decided yet, so the gateway has no DTR endpoint (Nov 18, 2026: `/dtr/launch/{linkId}` and
`/dtr/saved-forms` are removed, and the gateway stores no DTR links).

- **Order-sign** passes the payer's DTR link to the EHR exactly as the payer sent it on its card: `link.label`,
  `link.url` (for type `smart`, the payer's SMART DTR app launch URL), `link.type` and `link.appContext`.
- **PAS** attaches the completed forms (QuestionnaireResponse) saved at the FHIR API for the same orders automatically
  when `includeDtr = true`, or the forms named in `questionnaireResponseIds`.

**Client EHR adaptations and required fields:** see `Client-EHR-Adaptations.md` (what stops a request in CRD, DTR, PAS and CDex, and every `CLIENT-EHR:` workaround in the code).

**FHIR release:** everything is FHIR R4 (CRD, DTR, PAS, CDex and the FHIR API). There is no R5 support.

## Authentication

None is built in. The EHR calls the gateway, the gateway calls payers, the FHIR API and the EHR data API, and payers
call the notification endpoints, all without keys, tokens or signatures. Authentication is added outside this code.
The one token kept is CDS Hooks `fhirAuthorization`: the gateway gets it from the FHIR API (`/internal/access-tokens`)
and sends it to the payer in the order-sign call, as before.

## Errors

Every error has the same body: `{ "error": "...", "details": ["..."], "traceId": "..." }`.

| Status | Meaning |
| --- | --- |
| 400 | Invalid body / missing field / unknown insurancePlanId / wrong answer format / order-sign without a signing clinician |
| 404 | Patient, order, form, document or request not found |
| 409 | Request already submitted (use PUT), or it is cancelled |
| 422 | Cannot be processed: the insurance (`coverageId`) is not found, belongs to another patient or is not active, insurance without member id, order without code, no NPI, required questions missing, payer rejected (PAS returns the decision with errors) |
| 502 | Payer or FHIR API failed, or data received from them could not be read |
| 503 | EHR data API not available (gateway data could not be read or saved; `details` has the failing call) |
| 504 | Payer did not answer in time |
| 500 | Unexpected error (look up the traceId in the log) |

## Run locally

1. Start the FHIR API (5100), the mock EHR (5090) and the mock payer (5080).
2. `dotnet run --project PayerGatewayWebAPI` → http://localhost:5200/swagger
3. `Ehr:BaseUrl` points to the mock EHR, which implements the EHR data API
   (`MockEhr.Api/Controllers/PriorAuthDataController.cs`) and adds the mock payer to its payer list by itself.
   The saved rows are in the mock EHR's `Data/ehr-data.json` under `gatewayTables`.
4. For the real EHR: the EHR team runs `Database/01_create_tables.sql`, inserts the payers into `PA_PAYER` and
   provides the API of `docs/EHR-Data-API.md`. Then set `Ehr:BaseUrl` and `Ehr:DataPath`.

## Code rules used

- Controllers only (no minimal API endpoints); `Program.cs` uses top-level statements and calls `Startup`.
- No lambdas: plain loops and named methods (option setup uses named methods such as `ConfigureSwagger`).
- One class per file, a comment on every class and method, all FHIR / CDS Hooks models written in the project.
- Curly braces around every block, also a block of one statement (`.editorconfig` in the project folder makes the
  compiler and `dotnet format` report a missing brace).
- Every address that must be changed for another environment or payer has a `TODO` comment: in `appsettings.json`
  and on the default values in `Configuration/*.cs`. Search the project for `TODO` before installing it somewhere else.
- Nothing is made up: a value the EHR or the payer did not give (an id, an identifier system) is not sent.
