-- =====================================================================================================
-- PA_PAYER: remove the three columns the gateway no longer uses.
--
--   PAYER_IDENTIFIER   the payer's own id
--   MEMBER_ID_SYSTEM   identifier system for member ids
--   PAYER_ID_SYSTEM    identifier system for the payer id
--
-- WHY: payers do not give these values per payer. The payer id and the member id belong to the patient's
-- insurance in the EHR (payer id and member id of the insurance); the gateway reads them from there (through the
-- FHIR API, as FHIR Coverage) and sends them to the payer exactly as they are, without adding a "system".
--
-- WHEN TO RUN: ONLY if PA_PAYER was created from an earlier 01_create_tables.sql that still had these columns.
-- A new installation runs the new 01_create_tables.sql, which no longer has them.
--
-- IS IT REQUIRED? No. The gateway ignores columns (and JSON properties) it does not know, so the old columns do
-- no harm. Run this script when you want the table to match 01_create_tables.sql.
--
-- AFTER RUNNING: GET payers (EHR data API) should no longer return payerIdentifier, memberIdSystem and
-- payerIdSystem (EHR-Data-API.md, section 4.1). It is not an error if it still returns them.
-- =====================================================================================================

ALTER TABLE PA_PAYER DROP (PAYER_IDENTIFIER, MEMBER_ID_SYSTEM, PAYER_ID_SYSTEM);

-- Check: the three names must not be in the list any more.
-- SELECT COLUMN_NAME FROM USER_TAB_COLUMNS WHERE TABLE_NAME = 'PA_PAYER' ORDER BY COLUMN_ID;
