# Payer Gateway Web API: DTR Guide, Flow 1: Gateway Forms

**Project:** `PayerGatewayWebAPI` (port 5200, Swagger group "DTR 1 - Forms (our questionnaire app or EHR screen)")
**Standards:** Da Vinci DTR 2.0.1, SDC (`$questionnaire-package`, `$next-question`)
**Controller:** `DtrFormsController`
**Used when:** `PA_PAYER.DTR_MODE = Forms` (the default). The payer has no SMART DTR app of its own.
**Other DTR flow:** `DTR-SmartApp-API-Guide.md` (the payer's own SMART app)

---

## 1. What this flow does

The payer wants a questionnaire filled in before it decides. In this flow the **gateway does the FHIR work** and the EHR only sees plain JSON:

1. The gateway gets the payer's questionnaire and returns it as a list of simple questions.
2. A screen shows the questions: an EHR screen, or our questionnaire app in the demo.
3. The screen sends the answers back as text values.
4. The gateway builds the FHIR `QuestionnaireResponse` and saves it at the FHIR API.
5. PAS later attaches the completed form by itself.

```
 EHR / questionnaire app        Payer Gateway (5200)              FHIR API (5100)            Payer
        |                              |                                |                      |
        | POST /api/v1/dtr/forms       |                                |                      |
        |----------------------------->|  read orders + Coverage        |                      |
        |                              |------------------------------->|                      |
        |                              |  POST {payer}/fhir/Questionnaire/$questionnaire-package
        |                              |------------------------------------------------------>|
        |                              |  (adaptive form: POST .../Questionnaire/$next-question)
        |                              |------------------------------------------------------>|
        |                              |  save PA_DTR_FORM              |                      |
        | 200: forms with questions    |                                |                      |
        |<-----------------------------|                                |                      |
        |                              |                                |                      |
        | POST /forms/{formId}/answers |                                |                      |
        |----------------------------->|  check values, build QuestionnaireResponse            |
        |                              |  (adaptive form: $next-question again)                |
        |                              |  PUT QuestionnaireResponse/qr-{formId}                |
        |                              |------------------------------->|  PA_QUESTIONNAIRE_RESPONSE
        | 200: status, QR id           |                                |                      |
        |<-----------------------------|                                |                      |
```

**When to call it**

| Trigger | Where the questionnaire URLs come from |
|---|---|
| Order-sign returned `callDtr = true` and the message link has `opensPayerApp = false` | `questionnaireUrls` and `coverageAssertionId` of the decision |
| A CDex information request lists `questionnairesNeeded` | `questionnairesNeeded` and the `trackingId` of the information request |

---

## 2. Endpoints at a glance

| # | Endpoint | In one sentence |
|---|---|---|
| 1 | `POST /api/v1/dtr/forms` | Gets the payer's questionnaire(s) for the orders, as simple questions |
| 2 | `GET /api/v1/dtr/forms/{formId}` | Reads one form: its questions and the answers saved so far |
| 3 | `POST /api/v1/dtr/forms/{formId}/answers` | Saves answers; builds and stores the FHIR QuestionnaireResponse |
| 4 | `GET /api/v1/dtr/saved-forms?patientId=...&orderId=...` | Lists the forms saved for a patient (used by both DTR flows) |

---

## 3. Common rules

- **Base URL:** `http://localhost:5200` locally.
- **Format:** `Content-Type: application/json`, camelCase.
- **Authentication:** none is built in. It is added outside this code.
- **Errors:** `{ "error": "...", "details": ["..."], "traceId": "..." }`. For answers, `details` lists each wrong or missing question.

---

## 4. Endpoints

### 4.1 `POST /api/v1/dtr/forms`: get the questionnaire

**Why we use it.** DTR says the payer's questionnaire is fetched with `Questionnaire/$questionnaire-package`. That operation needs FHIR `Parameters` with the coverage and the orders. The gateway builds that, calls the payer and turns each FHIR `Questionnaire` into a flat list of questions.

**Scenario.** Order-sign for `ord-1-hd` (in-center hemodialysis) returned `callDtr = true` with questionnaire `http://example.org/fhir/Questionnaire/dialysis-incenter-hd`. The EHR asks for the form.

#### Request

| Field | Required | Meaning |
|---|---|---|
| `payerName` | Yes | Payer to ask |
| `patientId` | Yes | EHR patient id |
| `orderIds` | Yes, at least one | Orders the documentation is for |
| `coverageId` | No | Insurance to use. Empty = the order's insurance, else the first active coverage |
| `questionnaireUrls` | No | From the order-sign decision. Empty = the payer decides from the orders |
| `coverageAssertionId` | No | From the order-sign decision (DTR `context`), or a CDex tracking id |

```http
POST /api/v1/dtr/forms
Content-Type: application/json

{
  "payerName": "MockPayer",
  "patientId": "pat-1",
  "orderIds": ["ord-1-hd"],
  "questionnaireUrls": ["http://example.org/fhir/Questionnaire/dialysis-incenter-hd"],
  "coverageAssertionId": "eeea140086ab438387ae884d045a4946"
}
```

#### What the gateway sends to the payer

`POST {payer}/fhir/Questionnaire/$questionnaire-package`

```json
{
  "resourceType": "Parameters",
  "meta": { "profile": ["http://hl7.org/fhir/us/davinci-dtr/StructureDefinition/dtr-qpackage-input-parameters"] },
  "parameter": [
    { "name": "coverage", "resource": { "resourceType": "Coverage", "id": "cov-1", "...": "..." } },
    { "name": "order", "resource": { "resourceType": "ServiceRequest", "id": "ord-1-hd", "...": "..." } },
    { "name": "questionnaire", "valueCanonical": "http://example.org/fhir/Questionnaire/dialysis-incenter-hd" },
    { "name": "context", "valueString": "eeea140086ab438387ae884d045a4946" }
  ]
}
```

One `order` parameter per order, one `questionnaire` parameter per URL. `context` is sent only when `coverageAssertionId` is given.

The payer's answer may be a `Parameters` with `PackageBundle` parts (the normal case), a plain `Bundle`, or a single `Questionnaire`. Every `Questionnaire` in it becomes one form.

#### Response 200

A list with one form per questionnaire (shortened: 5 of the questions).

```json
[
  {
    "formId": "frm-235b7ad69e85",
    "payerName": "MockPayer",
    "patientId": "pat-1",
    "orderIds": ["ord-1-hd"],
    "coverageId": "cov-1",
    "questionnaireUrl": "http://example.org/fhir/Questionnaire/dialysis-incenter-hd|1.0.0",
    "title": "Outpatient In-Center Hemodialysis (CPT 90935) - Prior Authorization Documentation",
    "status": "new",
    "questionnaireResponseId": null,
    "adaptive": false,
    "moreQuestionsExpected": false,
    "questions": [
      {
        "linkId": "primary-diagnosis",
        "text": "Primary diagnosis requiring dialysis",
        "type": "choice",
        "required": true,
        "repeats": false,
        "readOnly": false,
        "maxLength": null,
        "groupLinkId": "diagnosis",
        "groupText": "Diagnosis and kidney function",
        "options": [
          { "code": "N18.6", "display": "End stage renal disease", "system": "http://hl7.org/fhir/sid/icd-10-cm" },
          { "code": "N18.5", "display": "Chronic kidney disease, stage 5", "system": "http://hl7.org/fhir/sid/icd-10-cm" },
          { "code": "N17.9", "display": "Acute kidney failure, unspecified (dialysis dependent)", "system": "http://hl7.org/fhir/sid/icd-10-cm" }
        ],
        "optionsValueSet": null,
        "defaultValues": [],
        "currentValues": [],
        "showWhen": [],
        "showWhenBehavior": "all"
      },
      {
        "linkId": "egfr",
        "text": "Most recent eGFR (mL/min/1.73 m2)",
        "type": "decimal",
        "required": true,
        "repeats": false,
        "readOnly": false,
        "maxLength": null,
        "groupLinkId": "diagnosis",
        "groupText": "Diagnosis and kidney function",
        "options": [],
        "optionsValueSet": null,
        "defaultValues": [],
        "currentValues": [],
        "showWhen": [],
        "showWhenBehavior": "all"
      },
      {
        "linkId": "treatments-per-week",
        "text": "Prescribed treatments per week",
        "type": "integer",
        "required": true,
        "repeats": false,
        "readOnly": false,
        "maxLength": null,
        "groupLinkId": "treatment",
        "groupText": "Dialysis treatment plan",
        "options": [],
        "optionsValueSet": null,
        "defaultValues": ["3"],
        "currentValues": [],
        "showWhen": [],
        "showWhenBehavior": "all"
      },
      {
        "linkId": "vascular-access",
        "text": "Current vascular access",
        "type": "choice",
        "required": true,
        "repeats": false,
        "readOnly": false,
        "maxLength": null,
        "groupLinkId": "treatment",
        "groupText": "Dialysis treatment plan",
        "options": [
          { "code": "avf", "display": "Arteriovenous fistula (AVF)", "system": "http://example.org/fhir/CodeSystem/dialysis-answers" },
          { "code": "avg", "display": "Arteriovenous graft (AVG)", "system": "http://example.org/fhir/CodeSystem/dialysis-answers" },
          { "code": "cvc", "display": "Central venous catheter (CVC)", "system": "http://example.org/fhir/CodeSystem/dialysis-answers" }
        ],
        "optionsValueSet": null,
        "defaultValues": [],
        "currentValues": [],
        "showWhen": [],
        "showWhenBehavior": "all"
      },
      {
        "linkId": "cvc-plan",
        "text": "Plan and target date to replace the catheter with a permanent access (AVF/AVG)",
        "type": "string",
        "required": true,
        "repeats": false,
        "readOnly": false,
        "maxLength": null,
        "groupLinkId": "treatment",
        "groupText": "Dialysis treatment plan",
        "options": [],
        "optionsValueSet": null,
        "defaultValues": [],
        "currentValues": [],
        "showWhen": [ { "questionLinkId": "vascular-access", "operator": "=", "value": "cvc" } ],
        "showWhenBehavior": "all"
      }
    ]
  }
]
```

**The form**

| Field | Meaning |
|---|---|
| `formId` | Gateway id of the form (`frm-` + 12 characters). Use it for the next calls |
| `questionnaireUrl` | Canonical URL of the payer Questionnaire, with `|version` when the payer sends one |
| `status` | `new`, `in-progress`, `completed` |
| `questionnaireResponseId` | Id of the saved QuestionnaireResponse (after the first save): `qr-{formId}` |
| `adaptive` | `true` = the payer sends its questions step by step (section 6) |
| `moreQuestionsExpected` | Adaptive only: `true` while the payer may still add questions |

**One question**

| Field | Meaning |
|---|---|
| `linkId` | Send this back with the answer |
| `text` | The question (with its prefix when the payer set one) |
| `type` | `boolean`, `decimal`, `integer`, `date`, `dateTime`, `time`, `string`, `text`, `url`, `choice`, `open-choice`, `quantity`. Also `display` (text only, no answer) and `attachment` / `reference` (cannot be answered through the gateway) |
| `required` | Must be answered before the form can be completed |
| `repeats` | `true` = several values allowed |
| `readOnly` | Show only |
| `maxLength` | Longest text allowed, when the payer set one |
| `groupLinkId`, `groupText` | The section the question belongs to. Groups are flattened; the screen can use `groupText` as a heading |
| `options` | Allowed answers of `choice` / `open-choice`. Send the option's `code` |
| `optionsValueSet` | Payer ValueSet with the allowed answers, when the options are not listed |
| `defaultValues` | Payer defaults. They are used when no answer is sent for the question |
| `currentValues` | The answers saved so far |
| `showWhen` | Show (and answer) the question only when these conditions match. Empty = always shown |
| `showWhenBehavior` | `all` or `any`: how several conditions combine |

**A `showWhen` condition:** `questionLinkId`, `operator` (`exists`, `=`, `!=`, `>`, `<`, `>=`, `<=`) and `value` (for `exists`: `"true"` or `"false"`).

#### Errors

| HTTP | When |
|---|---|
| 400 | Missing field, empty `orderIds`, unknown `payerName`, or an order belongs to another patient |
| 404 | An order was not found at the FHIR API |
| 422 | No active insurance; the payer refused the request (HTTP 4xx, `details` has the payer's messages); or the payer returned no questionnaire |
| 502 | The payer answered with HTTP 5xx or could not be reached; or the FHIR API failed |
| 503 | The EHR data API is not available |
| 504 | The payer did not answer within `TIMEOUT_SECONDS` |

---

### 4.2 `GET /api/v1/dtr/forms/{formId}`: read a form

**Why we use it.** The screen that shows the questions loads the form with it, and reads it again to show saved answers (`currentValues`) or, for an adaptive form, the questions the payer added.

```http
GET /api/v1/dtr/forms/frm-235b7ad69e85
```

**Response 200:** one form, the same structure as in section 4.1. No payer call is made.

| HTTP | When |
|---|---|
| 200 | The form |
| 404 | Unknown `formId` |
| 503 | The EHR data API is not available |

---

### 4.3 `POST /api/v1/dtr/forms/{formId}/answers`: save the answers

**Why we use it.** The payer needs a FHIR `QuestionnaireResponse` with the same structure as its `Questionnaire`. The gateway builds it from plain text answers, so the EHR screen needs no FHIR knowledge.

#### Request

| Field | Required | Meaning |
|---|---|---|
| `answers` | Yes | One entry per answered question: `linkId` and `values` |
| `complete` | No, default `true` | `true` = finish the form (status `completed`). `false` = save as `in-progress` and finish later |
| `authorPractitionerId` | No | Clinician who answered (`QuestionnaireResponse.author`) |

```http
POST /api/v1/dtr/forms/frm-235b7ad69e85/answers
Content-Type: application/json

{
  "complete": true,
  "authorPractitionerId": "prac-1",
  "answers": [
    { "linkId": "primary-diagnosis",   "values": ["N18.6"] },
    { "linkId": "esrd-date",           "values": ["2024-02-10"] },
    { "linkId": "egfr",                "values": ["8"] },
    { "linkId": "egfr-date",           "values": ["2026-09-22"] },
    { "linkId": "vascular-access",     "values": ["avf"] },
    { "linkId": "facility",            "values": ["Demo Dialysis Center - Main, CCN 452500"] },
    { "linkId": "new-start",           "values": ["false"] },
    { "linkId": "ktv",                 "values": ["1.45"] },
    { "linkId": "cms-2728",            "values": ["true"] },
    { "linkId": "medicare-status",     "values": ["applied"] },
    { "linkId": "transplant-referral", "values": ["true"] },
    { "linkId": "attestation",         "values": ["true"] }
  ]
}
```

`treatments-per-week` and `session-hours` are not sent here: the payer's defaults (`3` and `4`) are used.

#### How to write a value

| Question type | Write the value like this |
|---|---|
| `boolean` | `"true"` / `"false"` (`"yes"` / `"no"` also work) |
| `integer` | `"12"` |
| `decimal` | `"12.5"` |
| `date` | `"2026-08-10"` (also `"2026-08"` or `"2026"`) |
| `dateTime` | `"2026-08-10T09:30:00Z"` |
| `time` | `"09:30:00"` |
| `string`, `text` | Any text, not longer than `maxLength` |
| `url` | A full web address |
| `choice` | The option `code` (the option `display` also works). Upper / lower case does not matter |
| `open-choice` | An option code, or any other text (kept as free text) |
| `quantity` | `"12 mL/min"`: number, space, unit |

#### Rules

- **Merge.** Answers are merged with the ones saved before: the latest value of a `linkId` wins.
- **Remove.** An entry with an empty `values` list removes that answer.
- **Defaults.** A question with no answer gets the payer's default value when there is one.
- **Hidden questions.** A question hidden by `showWhen` is left out. If an answer was sent for it, the answer is ignored and a warning is returned.
- **One value.** Several values are allowed only when the question has `repeats = true`.
- **Choice without listed options** (`optionsValueSet` only): the code is sent to the payer as given.
- **Unknown `linkId`, a group or a display item:** ignored, with a warning.
- **`complete = true`:** every required question that is shown must have an answer, otherwise 422 and nothing is saved.
- **`complete = false`:** saved as `in-progress`; `missingRequired` lists what is still open.
- **Wrong value** (for example `"abc"` for an integer): 400 and nothing is saved.

Every successful save stores the QuestionnaireResponse at the FHIR API again, also when it is `in-progress`.

#### Response 200

```json
{
  "formId": "frm-235b7ad69e85",
  "status": "completed",
  "questionnaireResponseId": "qr-frm-235b7ad69e85",
  "answeredCount": 14,
  "missingRequired": [],
  "adaptive": false,
  "newQuestions": 0,
  "moreQuestionsExpected": false,
  "warnings": []
}
```

| Field | Meaning |
|---|---|
| `status` | `in-progress` or `completed` |
| `questionnaireResponseId` | Id of the QuestionnaireResponse at the FHIR API. Needed for CDex; PAS finds it by itself |
| `answeredCount` | Questions with an answer, payer defaults included |
| `missingRequired` | Required questions without an answer, as `linkId (question text)`. Filled when saving as `in-progress` |
| `adaptive`, `newQuestions`, `moreQuestionsExpected` | Adaptive forms only (section 6) |
| `warnings` | Answers that were ignored, and "The payer has more questions" for adaptive forms |

#### Error examples

Wrong values (400):

```json
{
  "error": "Some answers are not valid. Nothing was saved.",
  "details": [
    "egfr: 'abc' is not a number.",
    "vascular-access: 'fistula' is not one of the options (avf, avg, cvc)."
  ],
  "traceId": "0HN6Q2L3J7K1B:00000002"
}
```

Required questions missing with `complete = true` (422):

```json
{
  "error": "The form cannot be completed: required questions are not answered. Nothing was saved.",
  "details": [
    "esrd-date (Date of ESRD diagnosis or first regular dialysis)",
    "attestation (I attest that the information above is accurate and supported by the medical record.)"
  ],
  "traceId": "0HN6Q2L3J7K1B:00000003"
}
```

| HTTP | When |
|---|---|
| 200 | Saved |
| 400 | A value has the wrong format or is not one of the options. Nothing saved |
| 404 | Unknown `formId` |
| 422 | `complete = true` but required questions are unanswered; adaptive form: the payer is no longer configured, or refused `$next-question` |
| 502 | The FHIR API failed to save the form; adaptive form: the payer failed |
| 503 | The EHR data API is not available |
| 504 | Adaptive form: the payer did not answer in time |

---

### 4.4 `GET /api/v1/dtr/saved-forms`: forms saved for a patient

**Why we use it.** It shows which QuestionnaireResponses exist at the FHIR API for a patient, with their status and order. Use it to check that a form is completed before PAS, or to find a `questionnaireResponseId` for CDex.

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
    "questionnaireResponseId": "qr-frm-a8e1c91da03c",
    "questionnaire": "#adaptive-questionnaire",
    "status": "completed",
    "authored": "2026-10-08T19:11:55Z",
    "orderId": "ord-2-home-pd"
  }
]
```

| Field | Meaning |
|---|---|
| `questionnaire` | Canonical URL of the payer's questionnaire. For an adaptive form it is `#adaptive-questionnaire` (the questionnaire is inside the form) |
| `status` | `in-progress`, `completed`, `amended`, `stopped`, `entered-in-error`. Only `completed` and `amended` go to PAS automatically |
| `orderId` | The order named in the form's `qr-context` extension |

| HTTP | When |
|---|---|
| 200 | The saved forms, newest first (an empty list when there are none) |
| 400 | `patientId` is missing |
| 502 | The FHIR API failed |

---

## 5. The QuestionnaireResponse the gateway builds

| Element | Value |
|---|---|
| `id` | `qr-{formId}` (the same id on every save, so the form is replaced, not duplicated) |
| `meta.profile` | `http://hl7.org/fhir/us/davinci-dtr/StructureDefinition/dtr-questionnaireresponse` |
| `questionnaire` | The payer's canonical URL. Adaptive form: `#adaptive-questionnaire` with the Questionnaire in `contained` |
| `status` | `completed` or `in-progress` |
| `subject` | `Patient/{patientId}` |
| `authored` | Time of the save |
| `author` | `Practitioner/{authorPractitionerId}` when sent |
| `extension` qr-context | One per order (`ServiceRequest/ord-1-hd`) and one for the coverage (`Coverage/cov-1`). PAS uses it to attach the form to the right order |
| `item` | The same tree as the Questionnaire: groups contain their questions |
| `item.answer` | The value in the FHIR type of the question (`valueBoolean`, `valueDecimal`, `valueCoding` ...) |
| `item.answer.extension` information-origin | `manual` = entered by the provider, `auto` = payer default |

Example (shortened):

```json
{
  "resourceType": "QuestionnaireResponse",
  "id": "qr-frm-235b7ad69e85",
  "meta": { "profile": ["http://hl7.org/fhir/us/davinci-dtr/StructureDefinition/dtr-questionnaireresponse"] },
  "extension": [
    { "url": "http://hl7.org/fhir/us/davinci-dtr/StructureDefinition/qr-context", "valueReference": { "reference": "ServiceRequest/ord-1-hd" } },
    { "url": "http://hl7.org/fhir/us/davinci-dtr/StructureDefinition/qr-context", "valueReference": { "reference": "Coverage/cov-1" } }
  ],
  "questionnaire": "http://example.org/fhir/Questionnaire/dialysis-incenter-hd|1.0.0",
  "status": "completed",
  "subject": { "reference": "Patient/pat-1" },
  "authored": "2026-10-09T05:20:00Z",
  "author": { "reference": "Practitioner/prac-1" },
  "item": [
    {
      "linkId": "diagnosis",
      "text": "Diagnosis and kidney function",
      "item": [
        {
          "linkId": "egfr",
          "text": "Most recent eGFR (mL/min/1.73 m2)",
          "answer": [
            {
              "valueDecimal": 8,
              "extension": [
                {
                  "url": "http://hl7.org/fhir/us/davinci-dtr/StructureDefinition/information-origin",
                  "extension": [ { "url": "source", "valueCode": "manual" } ]
                }
              ]
            }
          ]
        }
      ]
    }
  ]
}
```

The gateway saves it with `PUT {FHIR API}/fhir/r4/QuestionnaireResponse/qr-{formId}`. The FHIR API stores it in table `PA_QUESTIONNAIRE_RESPONSE`.

---

## 6. Adaptive forms

Some payers do not send all questions at once. They send the next questions after each set of answers (SDC `$next-question`). The gateway detects this when the Questionnaire has the adaptive extension, or has no questions of its own.

How it works for the EHR:

1. `POST /api/v1/dtr/forms` returns the form with `adaptive = true`, `moreQuestionsExpected = true` and the first questions. The gateway has already asked the payer for them.
2. Send the answers with `POST .../answers`. The gateway passes them to the payer.
3. If the payer adds questions, the answer has `newQuestions > 0` and `moreQuestionsExpected = true`. The form stays `in-progress`, also when `complete = true` was sent. A warning says "The payer has more questions".
4. Read the form again (`GET /api/v1/dtr/forms/{formId}`), answer the new questions, save again.
5. When the payer says it is finished, `moreQuestionsExpected = false`. The save with `complete = true` now completes the form.

Example answer when the payer added two questions:

```json
{
  "formId": "frm-a8e1c91da03c",
  "status": "in-progress",
  "questionnaireResponseId": "qr-frm-a8e1c91da03c",
  "answeredCount": 2,
  "missingRequired": [
    "care-partner (Is a trained care partner available for every home hemodialysis treatment?)",
    "home-hd-access (Vascular access for home hemodialysis)"
  ],
  "adaptive": true,
  "newQuestions": 2,
  "moreQuestionsExpected": true,
  "warnings": ["The payer has more questions (2 new). Answer them and submit again."]
}
```

The gateway calls `$next-question` at most 25 times per save. Each call sends the current `QuestionnaireResponse` (with the payer Questionnaire in `contained`) in a `Parameters` resource, parameter `in`.

In the mock data, order `ord-2-home-pd` (patient `pat-2`) uses an adaptive questionnaire.

---

## 7. What happens next

| Next step | What to do |
|---|---|
| **PAS** | Call `POST /api/v1/pas/requests` with the same `orderIds`. Every `completed` (or `amended`) form whose qr-context names one of those orders is attached automatically. Nothing to send |
| **CDex** | Send the `questionnaireResponseId` in `POST /api/v1/cdex/requests/{requestId}/attachments` (`questionnaireResponseIds`) |

---

## 8. The questionnaire app (demo)

`E:\FHIR\dtr-questionnaire-app` is a small React app that shows a form and sends the answers. It uses only two endpoints of this guide:

- `GET /api/v1/dtr/forms/{formId}`
- `POST /api/v1/dtr/forms/{formId}/answers`

Open it with the form id and the gateway URL:

```
http://localhost:5174/?formId=frm-235b7ad69e85&gateway=http://localhost:5200
```

The mock EHR UI (`mock-ehr-ui`) calls `POST /api/v1/dtr/forms` and then opens this app. In production the EHR team can show the same questions on an EHR screen instead.

---

## 9. Limits of the gateway forms

- **No automatic pre-fill from the chart.** A payer Questionnaire can carry CQL expressions that fill answers from patient data. The gateway forms do not run them. Only the payer's fixed default values (`defaultValues`) are filled in. A SMART DTR app does run them (see `DTR-SmartApp-API-Guide.md`).
- **`attachment` and `reference` questions** cannot be answered. An answer sent for them is ignored with a warning.
- **Choice options from a ValueSet** are not expanded. The form shows `optionsValueSet` and accepts any code.
- **R5 payers** (`PA_PAYER.FHIR_VERSION = R5`): the gateway sends the R5 coverage and orders, and converts the payer's R5 Questionnaire so the form looks the same (`coding` becomes `choice` or `open-choice`; `question` becomes `display`).

---

## 10. Where the data is stored

| Table | Key | What |
|---|---|---|
| `PA_DTR_FORM` | `FORM_ID` | The payer Questionnaire (`QUESTIONNAIRE_JSON`), the plain answers (`ANSWERS_JSON`), the status and the adaptive state. Saved by the gateway |
| `PA_QUESTIONNAIRE_RESPONSE` | `ID` | The complete FHIR QuestionnaireResponse (`RESOURCE_JSON`). Saved by the FHIR API |

Both are in the EHR's Oracle database, through the EHR data API (`docs/EHR-Data-API.md`).

---

## 11. Test it

```http
### 1. get the form (use questionnaireUrls and coverageAssertionId from order-sign)
POST http://localhost:5200/api/v1/dtr/forms
Content-Type: application/json

{
  "payerName": "MockPayer",
  "patientId": "pat-1",
  "orderIds": ["ord-1-hd"],
  "questionnaireUrls": ["http://example.org/fhir/Questionnaire/dialysis-incenter-hd"]
}

### 2. save part of the answers (replace the formId)
POST http://localhost:5200/api/v1/dtr/forms/frm-000000000000/answers
Content-Type: application/json

{
  "complete": false,
  "answers": [
    { "linkId": "primary-diagnosis", "values": ["N18.6"] },
    { "linkId": "egfr", "values": ["8"] }
  ]
}

### 3. read the form back (currentValues are filled)
GET http://localhost:5200/api/v1/dtr/forms/frm-000000000000

### 4. forms saved for the order
GET http://localhost:5200/api/v1/dtr/saved-forms?patientId=pat-1&orderId=ord-1-hd
```

---

## 12. Code map

| File | What it does |
|---|---|
| `Controllers/DtrFormsController.cs` | The four endpoints |
| `Services/Dtr/DtrService.cs` | `CreateFormsAsync`, `GetFormAsync`, `SaveAnswersAsync`, `GetSavedFormsAsync`, adaptive `$next-question` |
| `Services/Dtr/QuestionnaireReader.cs` | FHIR Questionnaire to simple questions |
| `Services/Dtr/QuestionnaireResponseBuilder.cs` | Text answers to FHIR answers: type checks, `showWhen`, defaults, required questions |
| `Services/Dtr/AnswerText.cs` | FHIR values to text (defaults, conditions) |
| `Services/Dtr/R5QuestionnaireNormalizer.cs` | R5 Questionnaire to the R4 form shape |
| `Repositories/Database/EhrApiDtrFormRepository.cs` | `PA_DTR_FORM` through the EHR data API |
| `Models/Requests/DtrFormRequest.cs`, `DtrAnswersRequest.cs`, `DtrAnswerInput.cs` | Request JSON |
| `Models/Responses/DtrForm.cs`, `DtrQuestion.cs`, `DtrOption.cs`, `DtrCondition.cs`, `DtrAnswersResult.cs`, `SavedForm.cs` | Response JSON |
