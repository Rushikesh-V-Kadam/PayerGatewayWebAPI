-- =====================================================================================================
-- PA_PAYER: new columns for the FHIR release and the DTR mode of each payer.
-- Run this ONLY if PA_PAYER was already created from an earlier 01_create_tables.sql.
-- A new installation runs the new 01_create_tables.sql, which already has these columns.
-- (Replaces the earlier 02_alter_pa_payer.sql: the DTR_APP_CLIENT_* / JWKS / REDIRECT / SCOPES columns are no longer needed.)
-- =====================================================================================================

ALTER TABLE PA_PAYER ADD (
    FHIR_VERSION        VARCHAR2(5)   DEFAULT 'R4' NOT NULL,     -- R4 | R5: release the payer works in
    DTR_MODE            VARCHAR2(10)  DEFAULT 'Forms' NOT NULL,  -- Forms = EHR shows the questions | SmartApp = payer's own SMART DTR app
    DTR_APP_LAUNCH_URL  VARCHAR2(500)                            -- payer app launch URL; empty = the URL on the payer's CRD card
);

ALTER TABLE PA_PAYER ADD CONSTRAINT CK_PA_PAYER_FHIR CHECK (FHIR_VERSION IN ('R4', 'R5'));
ALTER TABLE PA_PAYER ADD CONSTRAINT CK_PA_PAYER_DTR CHECK (DTR_MODE IN ('Forms', 'SmartApp'));

-- GET payers now also returns fhirVersion, dtrMode and dtrAppLaunchUrl (EHR-Data-API.md, section 4.1).
-- AUTH_*, SIGN_CDS_HOOKS_REQUESTS and NOTIFICATION_SECRET stay; the gateway no longer reads them
-- (they are kept for the authentication layer added outside the gateway).
