using SpeiseDirekt3.Model;

namespace SpeiseDirekt3.ServiceInterface
{
    public enum TimeRange
    {
        Last24Hours,
        Last7Days,
        Last30Days
    }

    public class TrafficData
    {
        public string Label { get; set; } = string.Empty;
        public List<DataPoint> Data { get; set; } = new();
    }

    public class DataPoint
    {
        public DateTime Date { get; set; }
        public int Count { get; set; }
    }

    public class UserTrafficData
    {
        public string SessionId { get; set; } = string.Empty;
        public List<DataPoint> Data { get; set; } = new();
        public int TotalViews { get; set; }
        public int TotalClicks { get; set; }
    }

    public class MenuTrafficData
    {
        public Guid MenuId { get; set; }
        public string MenuName { get; set; } = string.Empty;
        public List<DataPoint> Data { get; set; } = new();
        public int TotalViews { get; set; }
    }

    public class MenuItemTrafficData
    {
        public Guid MenuItemId { get; set; }
        public string MenuItemName { get; set; } = string.Empty;
        public string MenuName { get; set; } = string.Empty;
        public List<DataPoint> Data { get; set; } = new();
        public int TotalClicks { get; set; }
    }

    public interface IAnalyticsService
    {
        /// <summary>
        /// Gets traffic data per user (session) for the specified time range
        /// </summary>
        Task<List<UserTrafficData>> GetTrafficPerUserAsync(TimeRange timeRange);

        /// <summary>
        /// Gets traffic data per menu for the specified time range
        /// </summary>
        Task<List<MenuTrafficData>> GetTrafficPerMenuAsync(TimeRange timeRange);

        /// <summary>
        /// Gets traffic data per menu item for the specified time range
        /// </summary>
        Task<List<MenuItemTrafficData>> GetTrafficPerMenuItemAsync(TimeRange timeRange);

        /// <summary>
        /// Gets the date range for the specified time range enum
        /// </summary>
        (DateTime StartDate, DateTime EndDate) GetDateRange(TimeRange timeRange);
    }
}
