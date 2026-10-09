namespace PayerGatewayWebAPI.Helpers;

/// <summary>
/// Timestamps in the tables are UTC. This makes a timestamp read from the EHR data API UTC again,
/// whether the EHR sent it as "2026-10-07T18:20:00Z", with an offset, or without any zone.
/// </summary>
public static class StorageTime
{
    /// <summary>The same moment as a UTC DateTime.</summary>
    public static DateTime ToUtc(DateTime value)
    {
        if (value.Kind == DateTimeKind.Utc)
        {
            return value;
        }
        if (value.Kind == DateTimeKind.Local)
        {
            return value.ToUniversalTime();   // the EHR sent an offset
        }
        return DateTime.SpecifyKind(value, DateTimeKind.Utc);   // no zone: the table value is UTC
    }
}
