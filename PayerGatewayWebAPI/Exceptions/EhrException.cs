namespace PayerGatewayWebAPI.Exceptions;

/// <summary>A call to the EHR data API failed (gateway data could not be read or saved in the EHR database).</summary>
public class EhrException : Exception
{
    /// <summary>Creates the exception.</summary>
    public EhrException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception with the original error.</summary>
    public EhrException(string message, Exception inner) : base(message, inner)
    {
    }
}
