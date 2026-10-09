namespace PayerGatewayWebAPI.Clients;

/// <summary>A SMART launch created at the FHIR API: the launch id and the FHIR base URL (iss).</summary>
public class SmartLaunchResult
{
    /// <summary>Launch id the DTR app sends back to the FHIR API.</summary>
    public string LaunchId { get; set; } = string.Empty;
    /// <summary>FHIR base URL the DTR app must use (the SMART "iss").</summary>
    public string Iss { get; set; } = string.Empty;
}
