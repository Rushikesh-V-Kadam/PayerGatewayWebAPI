// =====================================================================================================
// Payer Gateway Web API  (http://localhost:5200, Swagger UI at /swagger)
//
// Runs the Da Vinci prior authorization workflow for the EHR:
//   STEP 1  CRD   order-sign                      -> does the order need prior auth / documentation?
//   STEP 2  DTR   questionnaire forms              -> fill in the payer's questions (no UI needed: plain JSON)
//   STEP 3  PAS   prior authorization request      -> the payer's decision
//   STEP 4  CDex  attachments                      -> extra documents when the payer pends the request
//
// The EHR sends payerName; the gateway finds that payer in table PA_PAYER and calls it.
// All gateway data lives in the EHR's Oracle database and is read / saved through the EHR data API (docs/EHR-Data-API.md).
// Controllers only (no minimal API endpoints). Registrations are in Startup.cs.
// =====================================================================================================

using PayerGatewayWebAPI;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

Startup startup = new Startup(builder.Configuration);
startup.ConfigureServices(builder.Services);

WebApplication app = builder.Build();
startup.Configure(app);

app.Run();
