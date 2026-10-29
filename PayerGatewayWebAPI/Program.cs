// =====================================================================================================
// Payer Gateway Web API  (http://localhost:5200, Swagger UI at /swagger)
//
// Runs the Da Vinci prior authorization workflow for the EHR:
//   STEP 1  CRD   order-sign                      -> does the order need prior auth / documentation?
//   STEP 2  DTR   questionnaire forms              -> fill in the payer's questions (no UI needed: plain JSON)
//   STEP 3  PAS   prior authorization request      -> the payer's decision
//   STEP 4  CDex  attachments                      -> extra documents when the payer pends the request
//
// The EHR sends piInsurancePlanId; the gateway reads that payer's configuration (table PA_PAYER) and calls it.
// All gateway data lives in the EHR's Oracle database and is read / saved through the EHR data API (docs/EHR-Data-API.md).
// Controllers only (no minimal API endpoints). Registrations are in Startup.cs.
// =====================================================================================================

using PayerGatewayWebAPI;
using PayerGatewayWebAPI.EncryptedConfiguration;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Load the encrypted configuration, the same way as FHIRRestAPI (ConfigureAppConfiguration -> config.Add(...)).
// It is added after appsettings.json and the environment variables, so its values win.
// (ConfigurationManager has Add only as an explicit interface method, so the source is added to Sources.)
builder.Configuration.Sources.Add(new EncryptedConfigurationProvider());

Startup startup = new Startup(builder.Configuration);
startup.ConfigureServices(builder.Services);

WebApplication app = builder.Build();
startup.Configure(app);

app.Run();
