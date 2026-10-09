using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.OpenApi;
using PayerGatewayWebAPI.Background;
using PayerGatewayWebAPI.Clients;
using PayerGatewayWebAPI.Configuration;
using PayerGatewayWebAPI.Constants;
using PayerGatewayWebAPI.Helpers;
using PayerGatewayWebAPI.Middleware;
using PayerGatewayWebAPI.Repositories.Database;
using PayerGatewayWebAPI.Repositories.InMemory;
using PayerGatewayWebAPI.Repositories.Interfaces;
using PayerGatewayWebAPI.Services.Cdex;
using PayerGatewayWebAPI.Services.Crd;
using PayerGatewayWebAPI.Services.Dtr;
using PayerGatewayWebAPI.Services.Pas;
using PayerGatewayWebAPI.Services.Payers;
using Swashbuckle.AspNetCore.SwaggerGen;
using Swashbuckle.AspNetCore.SwaggerUI;

namespace PayerGatewayWebAPI;

/// <summary>
/// Wires the application together. Every class has its own file and comments; this class only registers them.
///
///   STEP 0  Payers   GET  /api/v1/payers, POST /api/v1/payers/{payerName}/discover
///   STEP 1  CRD      POST /api/v1/crd/order-sign, GET /api/v1/crd/decisions/{orderId}
///   STEP 2  DTR      POST /api/v1/dtr/forms, GET /api/v1/dtr/forms/{formId}, POST /api/v1/dtr/forms/{formId}/answers
///                    payer SMART apps: GET /api/v1/dtr/launch/{linkId}, GET /api/v1/dtr/saved-forms, relay endpoints
///   STEP 3  PAS      POST /api/v1/pas/requests, PUT / cancel / GET / inquire, POST /api/v1/pas/notifications/{payerName}
///   STEP 4  CDex     GET  /api/v1/cdex/requests/{requestId}/information-requests, POST .../attachments,
///                    POST /api/v1/cdex/tasks/{payerName}
/// </summary>
public class Startup
{
    private readonly IConfiguration _configuration;

    /// <summary>Receives appsettings.json (and environment overrides).</summary>
    public Startup(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>Registers settings, clients, storage, workflow services, MVC, error handling and Swagger.</summary>
    public void ConfigureServices(IServiceCollection services)
    {
        // ------------------------------------------------------------ 1. settings (appsettings.json sections)
        services.Configure<FhirApiSettings>(_configuration.GetSection(FhirApiSettings.SectionName));
        services.Configure<EhrSettings>(_configuration.GetSection(EhrSettings.SectionName));
        services.Configure<WorkflowSettings>(_configuration.GetSection(WorkflowSettings.SectionName));
        services.Configure<StorageSettings>(_configuration.GetSection(StorageSettings.SectionName));
        // appsettings "Payers" is only used in memory mode; with Storage:Provider = EhrApi payers come from table PA_PAYER
        services.Configure<List<PayerSettings>>(_configuration.GetSection(PayerSettings.SectionName));

        StorageSettings storage = new StorageSettings();
        _configuration.GetSection(StorageSettings.SectionName).Bind(storage);

        // ------------------------------------------------------------ 2. HTTP (one factory; each client sets its own timeout per call)
        // HttpClient has a built-in limit of 100 seconds. It is switched off here (ConfigureHttpClient), because every
        // call already has its own limit from the settings (payer timeouts, FhirApi:TimeoutSeconds, Ehr:TimeoutSeconds).
        services.AddHttpClient(Microsoft.Extensions.Options.Options.DefaultName, ConfigureHttpClient);

        // ------------------------------------------------------------ 3. clients and payers (one instance for the app)
        services.AddSingleton<FhirApiClient>();
        services.AddSingleton<EhrDataClient>();
        services.AddSingleton<PayerRegistry>();
        services.AddSingleton<PayerClient>();

        // ------------------------------------------------------------ 4. storage: the EHR database (through the EHR data API) or in memory
        // The gateway has no database connection of its own. "EhrApi" keeps every table (PA_*) and the payer list
        // in the EHR's Oracle database; the EHR team's Web API reads and saves the rows (docs/EHR-Data-API.md).
        // Folders: Repositories\Interfaces (what the services use), Repositories\Database (EHR data API),
        // Repositories\InMemory (local testing; data is lost when the app stops).
        if (storage.IsEhrApi())
        {
            services.AddSingleton<IPayerRepository, EhrApiPayerRepository>();
            services.AddSingleton<ICrdRepository, EhrApiCrdRepository>();
            services.AddSingleton<IDtrLinkRepository, EhrApiDtrLinkRepository>();
            services.AddSingleton<IDtrFormRepository, EhrApiDtrFormRepository>();
            services.AddSingleton<IPriorAuthRepository, EhrApiPriorAuthRepository>();
            services.AddSingleton<ICdexTaskRepository, EhrApiCdexTaskRepository>();
            services.AddSingleton<IPayerSubscriptionRepository, EhrApiPayerSubscriptionRepository>();
        }
        else
        {
            services.AddSingleton<IPayerRepository, InMemoryPayerRepository>();
            services.AddSingleton<ICrdRepository, InMemoryCrdRepository>();
            services.AddSingleton<IDtrLinkRepository, InMemoryDtrLinkRepository>();
            services.AddSingleton<IDtrFormRepository, InMemoryDtrFormRepository>();
            services.AddSingleton<IPriorAuthRepository, InMemoryPriorAuthRepository>();
            services.AddSingleton<ICdexTaskRepository, InMemoryCdexTaskRepository>();
            services.AddSingleton<IPayerSubscriptionRepository, InMemoryPayerSubscriptionRepository>();
        }

        // ------------------------------------------------------------ 5. workflow services (one instance per request)
        services.AddScoped<CdsHookBuilder>();
        services.AddScoped<CrdService>();
        services.AddScoped<DtrService>();
        services.AddScoped<PasBundleBuilder>();
        services.AddScoped<PasSubscriptionService>();
        services.AddScoped<PasService>();
        services.AddScoped<CdexService>();

        // Safety net for pended requests: reads payer Tasks and inquires the payer (see PendingRequestPoller)
        services.AddHostedService<PendingRequestPoller>();

        // ------------------------------------------------------------ 6. MVC controllers, validation errors, exceptions
        services.AddControllers(ConfigureMvc);
        services.Configure<ApiBehaviorOptions>(ConfigureApiBehavior);
        services.AddExceptionHandler<ApiExceptionHandler>();
        services.AddProblemDetails();

        // The SMART DTR app (future) runs in a browser and calls the relay endpoints. Restrict origins in production.
        services.AddCors(ConfigureCors);

        // ------------------------------------------------------------ 7. Swagger
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(ConfigureSwagger);
    }

    /// <summary>The request pipeline: errors first, then Swagger, CORS and the controllers.</summary>
    public void Configure(WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseSwagger();
        app.UseSwaggerUI(ConfigureSwaggerUi);
        app.UseCors();
        app.MapControllers();
    }

    // ================================================================== option methods (named methods instead of lambdas)

    /// <summary>
    /// Removes HttpClient's own 100 second limit. PayerClient, FhirApiClient and EhrDataClient stop each call
    /// themselves after the configured number of seconds, so a configured timeout above 100 seconds works too.
    /// </summary>
    private static void ConfigureHttpClient(HttpClient client)
    {
        client.Timeout = Timeout.InfiniteTimeSpan;
    }

    /// <summary>Accepts "application/fhir+json" request bodies (payer notifications, pushed Tasks, DTR app relay).</summary>
    private static void ConfigureMvc(MvcOptions options)
    {
        // Only [Required] fields are required (services check the rest and return clear messages)
        options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;

        foreach (IInputFormatter formatter in options.InputFormatters)
        {
            SystemTextJsonInputFormatter? json = formatter as SystemTextJsonInputFormatter;
            if (json != null)
            {
                json.SupportedMediaTypes.Add(ApiHeaders.FhirJson);
            }
        }
    }

    /// <summary>Invalid request bodies (wrong JSON, missing required fields) return our ApiError format with 400.</summary>
    private static void ConfigureApiBehavior(ApiBehaviorOptions options)
    {
        options.InvalidModelStateResponseFactory = ModelValidationResponse.Create;
    }

    /// <summary>Allows any origin (the DTR app is a browser app). Restrict in production.</summary>
    private static void ConfigureCors(CorsOptions options)
    {
        CorsPolicyBuilder policy = new CorsPolicyBuilder();
        policy.AllowAnyOrigin();
        policy.AllowAnyHeader();
        policy.AllowAnyMethod();
        options.AddDefaultPolicy(policy.Build());
    }

    /// <summary>Swagger document: title and XML comments (descriptions + examples).</summary>
    private static void ConfigureSwagger(SwaggerGenOptions options)
    {
        OpenApiInfo info = new OpenApiInfo();
        info.Title = "Payer Gateway Web API";
        info.Version = "v1";
        info.Description =
            "Da Vinci prior authorization for the EHR. Call order: " +
            "1) POST /api/v1/crd/order-sign  2) DTR forms (when documentation is needed)  " +
            "3) POST /api/v1/pas/requests (when prior auth is needed)  4) CDex attachments (when the payer pends and asks for more). " +
            "Every EHR request carries payerName; the gateway finds that payer in table PA_PAYER. " +
            "All gateway data is stored in the EHR database through the EHR data API, so decisions are not written back separately. " +
            "No authentication is built in (it is added outside this code). " +
            "Errors always return { \"error\": \"...\", \"details\": [ ... ], \"traceId\": \"...\" }.";
        options.SwaggerDoc("v1", info);

        string xmlFile = Path.Combine(AppContext.BaseDirectory, "PayerGatewayWebAPI.xml");
        if (File.Exists(xmlFile))
        {
            options.IncludeXmlComments(xmlFile, true);
        }

        // non-nullable properties are shown as required
        options.SupportNonNullableReferenceTypes();
    }

    /// <summary>Swagger UI options: page title, request duration and the Schema tab opened first.</summary>
    private static void ConfigureSwaggerUi(SwaggerUIOptions options)
    {
        options.DocumentTitle = "Payer Gateway Web API";
        options.DisplayRequestDuration();

        // open the "Schema" tab first, so each property is shown with its description, required mark and default
        options.DefaultModelRendering(ModelRendering.Model);
        // show the properties of the body already expanded
        options.DefaultModelExpandDepth(2);
    }
}
