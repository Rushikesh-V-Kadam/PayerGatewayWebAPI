# Payer Gateway Web API (simple version)

Runs the Da Vinci prior authorization flow for the EHR against any connected payer:

| Step | EHR calls | Gateway calls the payer | When |
| --- | --- | --- | --- |
| 1 CRD | `POST /api/v1/crd/order-sign` | CDS Hooks `order-sign` | Every signed order |
| 2 DTR | `POST /api/v1/dtr/forms` → `POST /api/v1/dtr/forms/{formId}/answers` | `Questionnaire/$questionnaire-package` | Decision has `callDtr = true` |
| 3 PAS | `POST /api/v1/pas/requests` (+ PUT / cancel / GET / inquire) | `Claim/$submit`, `Claim/$inquire` | Decision has `callPas = true` |
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
| Settings | `Storage:Provider = EhrApi`, `Ehr:BaseUrl`, `Ehr:DataPath` |

**No write-back.** The EHR reads the decisions from the same tables (`PA_COVERAGE_DECISION`, `PA_PRIOR_AUTH`), so the
gateway does not send a second copy to the EHR. The `writeBack` request fields and the `EHR_SYNC_STATUS` column no
longer exist.

**No background job.** The gateway does not check pended requests by itself (`PendingRequestPoller` and
`Workflow:PollMinutes` are removed). The client EHR decides when to check: for each row with `STATUS = 'pended'` in
`PA_PRIOR_AUTH` it calls `POST /api/v1/pas/requests/{requestId}/inquire` (latest decision) and
`GET /api/v1/cdex/requests/{requestId}/information-requests?refresh=true` (payer requests for documents). The gateway
saves the result in `PA_PRIOR_AUTH` - only when it is different from what is stored, so `UPDATED_AT` moves only when
something changed. Payer notifications (PAS Subscription) and pushed Tasks still update the rows on their own.

`Storage:Provider = InMemory` keeps everything in memory instead (lost on restart) and reads the payers from
`appsettings.json` → `Payers`. Use it only to run the gateway without the EHR.

**Repository folders.** The services use only the interfaces; `Startup.cs` picks the implementation from `Storage:Provider`:

| Folder | Namespace | What is in it |
| --- | --- | --- |
| `Repositories/Interfaces` | `PayerGatewayWebAPI.Repositories.Interfaces` | `I...Repository`: what the services call |
| `Repositories/Database` | `PayerGatewayWebAPI.Repositories.Database` | `EhrApi...Repository`: saves to the EHR database through the EHR data API (`Storage:Provider = EhrApi`) |
| `Repositories/InMemory` | `PayerGatewayWebAPI.Repositories.InMemory` | `InMemory...Repository`: keeps data in memory for local testing (`Storage:Provider = InMemory`) |

## How the payer is chosen

Every EHR request carries `payerName`. The gateway looks it up in table `PA_PAYER` (`PAYER_NAME`, not case-sensitive),
read through the EHR data API, and uses that row's base URL, paths and timeouts. The list is kept for
`Storage:PayerCacheMinutes` (default 5); a name that is not in the kept list is looked up again right away.
To onboard a payer, the EHR team inserts a row; nothing changes in the gateway and no restart is needed.
An unknown or disabled name returns 400 with the list of configured payers.

**Only `PAYER_NAME` and `BASE_URL` must be filled in a payer row.** Every other column is optional and has a default
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

**Payers without their own app (`PA_PAYER.DTR_MODE = Forms`, the default):** the gateway does the DTR work itself and
exposes it as plain JSON:

1. **Get the form.** `POST /api/v1/dtr/forms` with `payerName`, `patientId`, `orderIds` and the `questionnaireUrls` /
   `coverageAssertionId` from the order-sign decision. The gateway calls the payer's `$questionnaire-package` and returns each
   Questionnaire as a list of questions: `linkId`, `text`, `type`, `required`, `options`, `defaultValues`, `showWhen`.
2. **Answer it.** The EHR team shows those questions on an existing EHR screen (a simple form: one field per question).
   During testing the answers can be typed into Swagger or Postman.
3. **Save the answers.** `POST /api/v1/dtr/forms/{formId}/answers` with `{ "linkId": "...", "values": ["..."] }` per question.
   The gateway checks each value against the question type, skips questions hidden by `showWhen`, uses payer defaults
   for questions left empty, and (with `complete = true`) rejects the save with 422 when a required question is missing.
   It then builds the FHIR QuestionnaireResponse (same tree as the payer's Questionnaire, with `qr-context` for the orders
   and coverage) and saves it at the FHIR API (table `PA_QUESTIONNAIRE_RESPONSE`).
4. **Submit.** `POST /api/v1/pas/requests` attaches every completed form of the same orders automatically.

**Payers with their own SMART DTR app (`PA_PAYER.DTR_MODE = SmartApp`):** order-sign returns a message link of type `smart`
with `opensPayerApp = true` (`/api/v1/dtr/launch/{linkId}`). The EHR opens it in a browser window; nothing else changes for the EHR.

1. The gateway creates the SMART launch at the FHIR API (`iss` = `/fhir/r4`), and redirects to the app
   (`DTR_APP_LAUNCH_URL`, or the URL on the payer's card).
2. The app gets its launch context from the FHIR API (SMART App Launch), reads the patient data, gets the questionnaire from its payer (directly, or through
   `/api/v1/dtr/{payerName}/Questionnaire/$questionnaire-package`), and saves the QuestionnaireResponse
   at the FHIR API.
3. `GET /api/v1/dtr/saved-forms?patientId=...&orderId=...` shows the EHR that the form is completed.
4. PAS attaches it automatically, as for step 4 above.

With `DTR_MODE = Forms` (default) the link has `opensPayerApp = false` and the EHR keeps using steps 1 to 4 above.

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
| 400 | Invalid body / missing field / unknown payerName / wrong answer format / order-sign without a signing clinician |
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
3. `Storage:Provider` is `EhrApi` and `Ehr:BaseUrl` points to the mock EHR, which implements the EHR data API
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
