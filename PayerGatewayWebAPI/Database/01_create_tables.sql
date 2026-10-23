-- =====================================================================================================
-- Prior authorization tables for the EHR database (Oracle)
--
-- WHO RUNS THIS: the EHR team, once, in the EHR schema.
-- WHO USES THE TABLES:
--   * The Payer Gateway and the FHIR API read and save the rows - but NEVER with a database connection.
--     They call the EHR data API (docs/EHR-Data-API.md), and the EHR data API runs the SQL on these tables.
--   * The EHR itself reads the tables directly (order screen, prior authorization screen, reports).
--     That is why the gateway no longer "writes back" decisions: they are already in the EHR database.
--
-- NAMING RULE (the whole contract in one line):
--   one JSON property of the data API = one column, camelCase -> UPPER_SNAKE_CASE
--   e.g.  requestId -> REQUEST_ID,  decisionJson -> DECISION_JSON,  signCdsHooksRequests -> SIGN_CDS_HOOKS_REQUESTS
--
-- COLUMN TYPES
--   * "..._JSON", "..._BUNDLE" and APP_CONTEXT columns are CLOBs holding text the gateway produced.
--     Store the text exactly as received and return it exactly as stored (do not re-format it).
--   * TIMESTAMP columns hold UTC. The API sends / returns them as ISO-8601 with Z, e.g. "2026-10-07T18:36:17Z".
--   * NUMBER(1) columns are booleans: 1 = true, 0 = false. The API sends / returns JSON true / false.
--     (When reading, the gateway also understands 1 / 0 and "Y" / "N", but true / false is the agreed format.)
--   * Id columns (PATIENT_ID, ORDER_ID, COVERAGE_ID, ORGANIZATION_ID ...) hold the same ids the EHR sends to the
--     gateway. Change their length, or add foreign keys to your own tables, as you see fit.
--   * You may add your own columns (audit columns, foreign keys). The gateway ignores properties it does not know.
--
-- Oracle version: written for 12.2 or later; every name is 30 characters or shorter, so it also runs on 12.1.
-- The CHECK (... IS JSON) constraints need 12.1.0.2 or later - remove those lines on an older database.
-- =====================================================================================================

-- ------------------------------------------------------------------ PAYERS (maintained by the EHR team)
-- One row per connected payer. The gateway only READS this table (GET payers) and keeps the list for a few minutes.
-- To onboard a payer: insert a row. Nothing changes in the gateway.
-- PAYER_NAME is the value the EHR sends as "payerName" in every gateway request (upper / lower case does not matter).
--
-- ONLY TWO COLUMNS MUST BE FILLED: PAYER_NAME and BASE_URL. Every other column is optional: when it is empty the
-- gateway uses the default written next to the column. A row with only those two values is a working payer.
--
-- The payer's own id and the member id are NOT columns of this table. They belong to the patient's insurance in
-- the EHR (payer id and member id of the insurance) and reach the gateway through the FHIR API (Coverage).
CREATE TABLE PA_PAYER (
    PAYER_NAME               VARCHAR2(64)   NOT NULL,               -- REQUIRED
    DISPLAY_NAME             VARCHAR2(200),                         -- friendly name for messages and logs; empty = PAYER_NAME is shown
    ENABLED                  NUMBER(1)      DEFAULT 1 NOT NULL,     -- 0 = configured but switched off
    BASE_URL                 VARCHAR2(500)  NOT NULL,               -- REQUIRED. A complete http / https address, e.g. https://api.payer.com
    CDS_SERVICES_PATH        VARCHAR2(200)  DEFAULT 'cds-services', -- CRD:  {BASE_URL}/{CDS_SERVICES_PATH}.  '/' = no extra path
    FHIR_PATH                VARCHAR2(200)  DEFAULT 'fhir',         -- DTR / PAS / CDex:  {BASE_URL}/{FHIR_PATH}.  '/' = no extra path
    AUTH_TYPE                VARCHAR2(20)   DEFAULT 'none' NOT NULL,-- auth layer only: none | client-secret | smart-backend
    AUTH_TOKEN_URL           VARCHAR2(500),                         -- auth layer only: the payer's OAuth token URL
    AUTH_CLIENT_ID           VARCHAR2(200),                         -- auth layer only
    AUTH_CLIENT_SECRET       VARCHAR2(500),                         -- auth layer only. SENSITIVE - see the note below
    AUTH_SCOPE               VARCHAR2(500),                         -- auth layer only
    SIGN_CDS_HOOKS_REQUESTS  NUMBER(1)      DEFAULT 1 NOT NULL,     -- auth layer only: 1 = sign each CDS Hooks call
    USE_SUBSCRIPTIONS        NUMBER(1)      DEFAULT 1 NOT NULL,     -- 1 = ask the payer to notify us about decisions
    NOTIFICATION_SECRET      VARCHAR2(200),                         -- auth layer only: secret the payer sends on notifications. SENSITIVE
    CRD_TIMEOUT_SECONDS      NUMBER(4),                             -- empty or 0 = gateway default (5)
    TIMEOUT_SECONDS          NUMBER(4),                             -- empty or 0 = gateway default (30)
    -- DTR (questionnaire) settings. Every payer works in FHIR R4.
    DTR_MODE                 VARCHAR2(10)   DEFAULT 'Forms' NOT NULL, -- Forms = EHR shows the questions | SmartApp = payer's own SMART DTR app
    DTR_APP_LAUNCH_URL       VARCHAR2(500),                         -- payer app launch URL; empty = the URL on the payer's CRD card
    CONSTRAINT PK_PA_PAYER PRIMARY KEY (PAYER_NAME),
    CONSTRAINT CK_PA_PAYER_ENABLED CHECK (ENABLED IN (0, 1)),
    CONSTRAINT CK_PA_PAYER_SIGN CHECK (SIGN_CDS_HOOKS_REQUESTS IN (0, 1)),
    CONSTRAINT CK_PA_PAYER_SUBS CHECK (USE_SUBSCRIPTIONS IN (0, 1)),
    CONSTRAINT CK_PA_PAYER_AUTH CHECK (AUTH_TYPE IN ('none', 'client-secret', 'smart-backend')),
    CONSTRAINT CK_PA_PAYER_DTR CHECK (DTR_MODE IN ('Forms', 'SmartApp'))
);
-- The name must be unique without regard to upper / lower case
CREATE UNIQUE INDEX UX_PA_PAYER_NAME ON PA_PAYER (UPPER(PAYER_NAME));

-- NOTE: the gateway has no authentication code. AUTH_*, SIGN_CDS_HOOKS_REQUESTS and NOTIFICATION_SECRET are kept for the
-- authentication layer added outside the gateway; the gateway does not read them. AUTH_CLIENT_SECRET and
-- NOTIFICATION_SECRET are credentials: limit who can select this table, and consider encrypting the two columns.

-- Example: the smallest row that works (everything else uses the defaults)
-- INSERT INTO PA_PAYER (PAYER_NAME, BASE_URL) VALUES ('AcmeHealth', 'https://api.acmehealth.example');

-- Example: a payer with its own SMART DTR app
-- INSERT INTO PA_PAYER (PAYER_NAME, DISPLAY_NAME, BASE_URL, DTR_MODE)
-- VALUES ('AcmeHealth', 'Acme Health Plan', 'https://api.acmehealth.example', 'SmartApp');

-- Example: the local mock payer used for testing
-- INSERT INTO PA_PAYER (PAYER_NAME, DISPLAY_NAME, BASE_URL, AUTH_TYPE)
-- VALUES ('MockPayer', 'Mock Da Vinci Payer', 'http://localhost:5080', 'none');

-- ------------------------------------------------------------------ STEP 1: CRD (order-sign)
-- Latest coverage decision per order. THE EHR ORDER SCREEN READS THIS TABLE (replaces the old write-back
-- PUT api/orders/{orderId}/coverage-decision).
CREATE TABLE PA_COVERAGE_DECISION (
    ORDER_ID               VARCHAR2(64)  NOT NULL,
    EVENT_ID               VARCHAR2(100) NOT NULL,
    PAYER_NAME             VARCHAR2(64),
    PATIENT_ID             VARCHAR2(64)  NOT NULL,
    COVERAGE_ASSERTION_ID  VARCHAR2(100),            -- the payer's id for this answer (needed for DTR)
    COVERED                VARCHAR2(20)  NOT NULL,   -- covered | not-covered | conditional | unknown
    PRIOR_AUTH_NEEDED      VARCHAR2(20),             -- no-auth | auth-needed | satisfied | performpa | conditional
    DOCUMENTATION_NEEDED   VARCHAR2(20),             -- no-doc | clinical | admin | both | conditional
    DECISION_JSON          CLOB          NOT NULL,   -- the full decision: callDtr, callPas, questionnaireUrls, messages ...
    UPDATED_AT             TIMESTAMP     NOT NULL,   -- UTC
    CONSTRAINT PK_PA_COVERAGE_DECISION PRIMARY KEY (ORDER_ID),
    CONSTRAINT CK_PA_COVERAGE_DECISION_JSON CHECK (DECISION_JSON IS JSON)
);
CREATE INDEX IX_PA_COVERAGE_DECISION_PAT ON PA_COVERAGE_DECISION (PATIENT_ID);

-- ------------------------------------------------------------------ STEP 2: DTR
-- Payer questionnaires fetched for orders (POST /api/v1/dtr/forms) and their latest plain answers.
CREATE TABLE PA_DTR_FORM (
    FORM_ID                    VARCHAR2(64)   NOT NULL,
    PAYER_NAME                 VARCHAR2(64)   NOT NULL,
    PATIENT_ID                 VARCHAR2(64)   NOT NULL,
    COVERAGE_ID                VARCHAR2(64),
    ORDER_IDS_JSON             CLOB,                      -- ["ord-1001"]
    QUESTIONNAIRE_URL          VARCHAR2(1000) NOT NULL,
    QUESTIONNAIRE_JSON         CLOB           NOT NULL,   -- the payer's FHIR Questionnaire
    STATUS                     VARCHAR2(20)   NOT NULL,   -- new | in-progress | completed
    QUESTIONNAIRE_RESPONSE_ID  VARCHAR2(64),              -- = PA_QUESTIONNAIRE_RESPONSE.ID
    ANSWERS_JSON               CLOB,                      -- [{"linkId":"egfr","values":["12"]}]
    ADAPTIVE                   NUMBER(1)      DEFAULT 0 NOT NULL,   -- 1 = the payer adds questions step by step
    PAYER_FINISHED             NUMBER(1)      DEFAULT 0 NOT NULL,   -- adaptive: 1 = the payer has no more questions
    ADAPTIVE_RESPONSE_JSON     CLOB,                      -- adaptive: last FHIR QuestionnaireResponse from the payer
    CREATED_AT                 TIMESTAMP      NOT NULL,   -- UTC
    UPDATED_AT                 TIMESTAMP      NOT NULL,   -- UTC
    CONSTRAINT PK_PA_DTR_FORM PRIMARY KEY (FORM_ID),
    CONSTRAINT CK_PA_DTR_FORM_JSON CHECK (QUESTIONNAIRE_JSON IS JSON)
);
CREATE INDEX IX_PA_DTR_FORM_PAT ON PA_DTR_FORM (PATIENT_ID);

-- The filled-in forms in FHIR format (QuestionnaireResponse). Saved by the FHIR API; sent UNCHANGED to the
-- payer with the prior authorization request, so RESOURCE_JSON must be returned exactly as stored.
CREATE TABLE PA_QUESTIONNAIRE_RESPONSE (
    ID                 VARCHAR2(64)   NOT NULL,   -- FHIR id (QuestionnaireResponse.id)
    PATIENT_ID         VARCHAR2(64)   NOT NULL,
    ORDER_ID           VARCHAR2(64),              -- the order the form was filled for
    COVERAGE_ID        VARCHAR2(64),
    QUESTIONNAIRE_URL  VARCHAR2(1000),            -- the payer Questionnaire it answers
    STATUS             VARCHAR2(20)   NOT NULL,   -- in-progress | completed | amended | entered-in-error | stopped
    RESOURCE_JSON      CLOB           NOT NULL,   -- the complete FHIR QuestionnaireResponse
    EHR_RESPONSE_ID    VARCHAR2(64),              -- id of the readable chart copy in the EHR (if the EHR keeps one)
    EHR_SYNC_STATUS    VARCHAR2(10)   NOT NULL,   -- PENDING | SENT | FAILED  (state of that chart copy)
    LAST_UPDATED_UTC   TIMESTAMP      NOT NULL,   -- UTC
    CONSTRAINT PK_PA_QUESTIONNAIRE_RESPONSE PRIMARY KEY (ID),
    CONSTRAINT CK_PA_QR_JSON CHECK (RESOURCE_JSON IS JSON)
);
CREATE INDEX IX_PA_QR_PATIENT ON PA_QUESTIONNAIRE_RESPONSE (PATIENT_ID, LAST_UPDATED_UTC);
CREATE INDEX IX_PA_QR_ORDER   ON PA_QUESTIONNAIRE_RESPONSE (ORDER_ID);

-- Payer SMART DTR app links (future: when payers bring their own SMART apps).
CREATE TABLE PA_DTR_LINK (
    LINK_ID               VARCHAR2(64)   NOT NULL,
    PAYER_NAME            VARCHAR2(64)   NOT NULL,
    PATIENT_ID            VARCHAR2(64)   NOT NULL,
    ENCOUNTER_ID          VARCHAR2(64),
    USER_PRACTITIONER_ID  VARCHAR2(64),
    FHIR_CONTEXT_JSON     CLOB,                       -- ["ServiceRequest/ord-1001","Coverage/cov-1001"]
    APP_CONTEXT           CLOB,                       -- appContext from the payer's card (free text)
    APP_URL               VARCHAR2(1000) NOT NULL,
    EXPIRES_AT            TIMESTAMP      NOT NULL,    -- UTC
    CONSTRAINT PK_PA_DTR_LINK PRIMARY KEY (LINK_ID)
);

-- ------------------------------------------------------------------ STEP 3: PAS
-- One row per prior authorization request, updated on every update / cancel / inquiry / payer answer.
-- THE EHR PRIOR AUTHORIZATION SCREEN READS THIS TABLE (replaces the old write-back PUT api/prior-auth-requests/{id}).
CREATE TABLE PA_PRIOR_AUTH (
    REQUEST_ID            VARCHAR2(64)  NOT NULL,     -- the EHR's request id
    PAYER_NAME            VARCHAR2(64)  NOT NULL,
    PATIENT_ID            VARCHAR2(64)  NOT NULL,
    COVERAGE_ID           VARCHAR2(64)  NOT NULL,
    MEMBER_ID             VARCHAR2(64),                -- member id of the patient's insurance, as sent to the payer
    ORGANIZATION_ID       VARCHAR2(64)  NOT NULL,
    ORGANIZATION_NPI      VARCHAR2(20),
    PRACTITIONER_ID       VARCHAR2(64),
    PRACTITIONER_NPI      VARCHAR2(20),
    PRIORITY              VARCHAR2(10)  NOT NULL,     -- routine | urgent
    STATUS                VARCHAR2(20)  NOT NULL,     -- approved | partially-approved | denied | pended | not-required | cancelled | contact-payer | error
    AUTHORIZATION_NUMBER  VARCHAR2(64),
    VERSION_NO            NUMBER(5)     NOT NULL,     -- 1 = first submission, +1 per update / cancel
    ORDER_IDS_JSON        CLOB,                       -- ["ord-1001"]
    LINE_NUMBERS_JSON     CLOB,                       -- {"ord-1001":1}
    DECISION_JSON         CLOB,                       -- the full decision: lines, information requests, due date ...
    LAST_REQUEST_BUNDLE   CLOB,                       -- last FHIR Bundle sent to the payer (can be several MB: documents are inside)
    LAST_RESPONSE_BUNDLE  CLOB,                       -- last FHIR Bundle received from the payer
    SUBMITTED_AT          TIMESTAMP     NOT NULL,     -- UTC
    UPDATED_AT            TIMESTAMP     NOT NULL,     -- UTC. Changes only when something changed (a repeated inquiry with the same answer is not saved)
    CONSTRAINT PK_PA_PRIOR_AUTH PRIMARY KEY (REQUEST_ID),
    CONSTRAINT CK_PA_PRIOR_AUTH_DECISION CHECK (DECISION_JSON IS JSON)
);
CREATE INDEX IX_PA_PRIOR_AUTH_STATUS ON PA_PRIOR_AUTH (STATUS, PAYER_NAME);   -- GET prior-auths?status=pended
CREATE INDEX IX_PA_PRIOR_AUTH_PAT    ON PA_PRIOR_AUTH (PATIENT_ID);
CREATE INDEX IX_PA_PRIOR_AUTH_UPD    ON PA_PRIOR_AUTH (UPDATED_AT);            -- "what changed since my last check"

-- PAS Subscriptions the gateway created at payers (one per payer + provider organization NPI).
CREATE TABLE PA_PAYER_SUBSCRIPTION (
    PAYER_NAME        VARCHAR2(64)  NOT NULL,
    ORGANIZATION_NPI  VARCHAR2(20)  NOT NULL,
    SUBSCRIPTION_ID   VARCHAR2(100),
    STATUS            VARCHAR2(10)  NOT NULL,         -- active | error
    CREATED_AT        TIMESTAMP     NOT NULL,         -- UTC
    CONSTRAINT PK_PA_PAYER_SUBSCRIPTION PRIMARY KEY (PAYER_NAME, ORGANIZATION_NPI)
);

-- ------------------------------------------------------------------ STEP 4: CDex
-- Payer information requests (FHIR Tasks) received for our prior authorization requests.
CREATE TABLE PA_CDEX_TASK (
    PAYER_NAME       VARCHAR2(64)   NOT NULL,
    TASK_ID          VARCHAR2(100)  NOT NULL,
    REQUEST_ID       VARCHAR2(64)   NOT NULL,         -- = PA_PRIOR_AUTH.REQUEST_ID
    TRACKING_ID      VARCHAR2(100)  NOT NULL,         -- sent back to the payer with the documents
    TRACKING_SYSTEM  VARCHAR2(500),
    STATUS           VARCHAR2(20),                    -- requested | in-progress | completed | cancelled ...
    DUE_DATE         VARCHAR2(30),                    -- date text as the payer sent it, e.g. 2026-10-21
    TASK_JSON        CLOB           NOT NULL,         -- the FHIR Task as received
    RECEIVED_AT      TIMESTAMP      NOT NULL,         -- UTC. Changes only when the payer changed the Task
    CONSTRAINT PK_PA_CDEX_TASK PRIMARY KEY (PAYER_NAME, TASK_ID),
    CONSTRAINT CK_PA_CDEX_TASK_JSON CHECK (TASK_JSON IS JSON)
);
CREATE INDEX IX_PA_CDEX_TASK_REQUEST ON PA_CDEX_TASK (REQUEST_ID);   -- GET cdex-tasks?requestId=...

-- =====================================================================================================
-- QUERIES FOR THE EHR SCREENS (the EHR reads the tables directly; nothing is pushed to the EHR any more)
-- =====================================================================================================
-- Coverage decision of an order (order screen):
--   SELECT COVERED, PRIOR_AUTH_NEEDED, DOCUMENTATION_NEEDED, DECISION_JSON, UPDATED_AT
--     FROM PA_COVERAGE_DECISION WHERE ORDER_ID = :orderId;
--
-- Does the order need a questionnaire (DTR) / a prior authorization (PAS)?
--   SELECT JSON_VALUE(DECISION_JSON, '$.callDtr') AS CALL_DTR, JSON_VALUE(DECISION_JSON, '$.callPas') AS CALL_PAS
--     FROM PA_COVERAGE_DECISION WHERE ORDER_ID = :orderId;
--
-- Prior authorization requests of an order:
--   SELECT p.REQUEST_ID, p.STATUS, p.AUTHORIZATION_NUMBER, p.UPDATED_AT
--     FROM PA_PRIOR_AUTH p,
--          JSON_TABLE(p.ORDER_IDS_JSON, '$[*]' COLUMNS (ORDER_ID VARCHAR2(64) PATH '$')) o
--    WHERE o.ORDER_ID = :orderId;
--
-- The decision per order line of a request:
--   SELECT l.LINE_NO, l.ORDER_ID, l.DECISION, l.APPROVED_QUANTITY, l.VALID_FROM, l.VALID_TO, l.REASON
--     FROM PA_PRIOR_AUTH p,
--          JSON_TABLE(p.DECISION_JSON, '$.lines[*]' COLUMNS (
--              LINE_NO            NUMBER          PATH '$.sequence',
--              ORDER_ID           VARCHAR2(64)    PATH '$.orderId',
--              DECISION           VARCHAR2(30)    PATH '$.decision',
--              APPROVED_QUANTITY  NUMBER          PATH '$.approvedQuantity',
--              VALID_FROM         VARCHAR2(30)    PATH '$.validFrom',
--              VALID_TO           VARCHAR2(30)    PATH '$.validTo',
--              REASON             VARCHAR2(1000)  PATH '$.reason')) l
--    WHERE p.REQUEST_ID = :requestId;
--
-- What the payer still needs for a pended request (documents by LOINC code, due date):
--   SELECT i.TRACKING_ID, i.STATUS, i.DUE_DATE, i.LOINC_CODE
--     FROM PA_PRIOR_AUTH p,
--          JSON_TABLE(p.DECISION_JSON, '$.informationRequests[*]' COLUMNS (
--              TRACKING_ID  VARCHAR2(100) PATH '$.trackingId',
--              STATUS       VARCHAR2(20)  PATH '$.status',
--              DUE_DATE     VARCHAR2(30)  PATH '$.dueDate',
--              NESTED PATH '$.documentsNeeded[*]' COLUMNS (LOINC_CODE VARCHAR2(20) PATH '$.loincCode'))) i
--    WHERE p.REQUEST_ID = :requestId;
--
-- Decisions that changed since the last check (a payer can decide days later; the gateway's background job
-- saves the new decision in the same row):
--   SELECT REQUEST_ID, STATUS, AUTHORIZATION_NUMBER, UPDATED_AT FROM PA_PRIOR_AUTH WHERE UPDATED_AT > :lastCheckUtc;
--
-- Requests still waiting for the payer:
--   SELECT REQUEST_ID, PAYER_NAME, SUBMITTED_AT FROM PA_PRIOR_AUTH WHERE STATUS = 'pended';
--
-- Forms not finished:
--   SELECT FORM_ID, PATIENT_ID, QUESTIONNAIRE_URL FROM PA_DTR_FORM WHERE STATUS <> 'completed';
