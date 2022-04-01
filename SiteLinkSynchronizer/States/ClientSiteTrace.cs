using SQLite;

namespace SiteLinkSynchronizer.States;

public class ClientSiteTrace
{

    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Unique(Name = "UniqueSite")]
    [NotNull]
    public string Site { get; set; }

    /// <summary>
    /// Recommended StartTime value for next scan.
    /// </summary>
    /// <remarks>This is usually the timestamp of last processed event, or last EndTime value.</remarks>
    [NotNull]
    public DateTime NextStartTime { get; set; }

    /// <summary>
    /// Last processed log event ID.
    /// </summary>
    [NotNull]
    public int LastLogId { get; set; }

}
