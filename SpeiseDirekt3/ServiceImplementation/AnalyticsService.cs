using Microsoft.EntityFrameworkCore;
using SpeiseDirekt3.Data;
using SpeiseDirekt3.ServiceInterface;

namespace SpeiseDirekt3.ServiceImplementation
{
    public class AnalyticsService : IAnalyticsService
    {
        private readonly ApplicationDbContext _context;
        private readonly IUserIdProvider _userIdProvider;

        public AnalyticsService(ApplicationDbContext context, IUserIdProvider userIdProvider)
        {
            _context = context;
            _userIdProvider = userIdProvider;
        }

        public async Task<List<UserTrafficData>> GetTrafficPerUserAsync(TimeRange timeRange)
        {
            var (startDate, endDate) = GetDateRange(timeRange);
            var userId = Guid.Parse(_userIdProvider.GetUserId());

            // Get menu views grouped by session and date/hour
            var menuViewsData = await _context.MenuViews
                .Where(mv => mv.ViewedAt >= startDate && mv.ViewedAt <= endDate)
                .Where(mv => mv.Menu != null && mv.Menu.ApplicationUserId == userId)
                .GroupBy(mv => new {
                    mv.SessionId,
                    Date = timeRange == TimeRange.Last24Hours
                        ? new DateTime(mv.ViewedAt.Year, mv.ViewedAt.Month, mv.ViewedAt.Day, mv.ViewedAt.Hour, 0, 0)
                        : mv.ViewedAt.Date
                })
                .Select(g => new { g.Key.SessionId, g.Key.Date, Count = g.Count() })
                .ToListAsync();

            // Get menu item clicks grouped by session and date/hour
            var menuClicksData = await _context.MenuItemClicks
                .Where(mic => mic.ClickedAt >= startDate && mic.ClickedAt <= endDate)
                .Where(mic => mic.Menu != null && mic.Menu.ApplicationUserId == userId)
                .GroupBy(mic => new {
                    mic.SessionId,
                    Date = timeRange == TimeRange.Last24Hours
                        ? new DateTime(mic.ClickedAt.Year, mic.ClickedAt.Month, mic.ClickedAt.Day, mic.ClickedAt.Hour, 0, 0)
                        : mic.ClickedAt.Date
                })
                .Select(g => new { g.Key.SessionId, g.Key.Date, Count = g.Count() })
                .ToListAsync();

            // Get all sessions
            var allSessions = menuViewsData.Select(mv => mv.SessionId)
                .Union(menuClicksData.Select(mc => mc.SessionId))
                .Distinct()
                .ToList();

            var result = new List<UserTrafficData>();

            foreach (var sessionId in allSessions)
            {
                var sessionViews = menuViewsData.Where(mv => mv.SessionId == sessionId).ToList();
                var sessionClicks = menuClicksData.Where(mc => mc.SessionId == sessionId).ToList();

                // Combine views and clicks by date/hour
                var combinedData = sessionViews.Select(sv => new { sv.Date, ViewCount = sv.Count, ClickCount = 0 })
                    .Union(sessionClicks.Select(sc => new { sc.Date, ViewCount = 0, ClickCount = sc.Count }))
                    .GroupBy(x => x.Date)
                    .Select(g => new DataPoint
                    {
                        Date = g.Key,
                        Count = g.Sum(x => x.ViewCount + x.ClickCount)
                    })
                    .OrderBy(dp => dp.Date)
                    .ToList();

                result.Add(new UserTrafficData
                {
                    SessionId = sessionId,
                    Data = combinedData,
                    TotalViews = sessionViews.Sum(sv => sv.Count),
                    TotalClicks = sessionClicks.Sum(sc => sc.Count)
                });
            }

            //simplify
            int i = 1;
            result.OrderByDescending(u => u.TotalViews + u.TotalClicks)
                .ToList()
                .ForEach(a =>
                {
                    a.SessionId = (i++).ToString();
                });

            return result.OrderByDescending(u => u.TotalViews + u.TotalClicks).ToList();
        }

        public async Task<List<MenuTrafficData>> GetTrafficPerMenuAsync(TimeRange timeRange)
        {
            var (startDate, endDate) = GetDateRange(timeRange);
            var userId = Guid.Parse(_userIdProvider.GetUserId());

            var menuViews = await _context.MenuViews
                .Include(mv => mv.Menu)
                .Where(mv => mv.ViewedAt >= startDate && mv.ViewedAt <= endDate)
                .Where(mv => mv.Menu != null && mv.Menu.ApplicationUserId == userId)
                .GroupBy(mv => new { mv.MenuId, mv.Menu!.Name })
                .Select(g => new
                {
                    MenuId = g.Key.MenuId,
                    MenuName = g.Key.Name,
                    Views = g.Select(mv => new { mv.ViewedAt }).ToList()
                })
                .ToListAsync();

            var result = new List<MenuTrafficData>();

            foreach (var menuGroup in menuViews)
            {
                List<DataPoint> dataPoints;

                if (timeRange == TimeRange.Last24Hours)
                {
                    // Group by hour for 24-hour view
                    dataPoints = menuGroup.Views
                        .GroupBy(v => new DateTime(v.ViewedAt.Year, v.ViewedAt.Month, v.ViewedAt.Day, v.ViewedAt.Hour, 0, 0))
                        .Select(g => new DataPoint
                        {
                            Date = g.Key,
                            Count = g.Count()
                        })
                        .OrderBy(dp => dp.Date)
                        .ToList();
                }
                else
                {
                    // Group by date for longer periods
                    dataPoints = menuGroup.Views
                        .GroupBy(v => v.ViewedAt.Date)
                        .Select(g => new DataPoint
                        {
                            Date = g.Key,
                            Count = g.Count()
                        })
                        .OrderBy(dp => dp.Date)
                        .ToList();
                }

                result.Add(new MenuTrafficData
                {
                    MenuId = menuGroup.MenuId,
                    MenuName = menuGroup.MenuName,
                    Data = dataPoints,
                    TotalViews = menuGroup.Views.Count
                });
            }

            return result.OrderByDescending(m => m.TotalViews).ToList();
        }

        public async Task<List<MenuItemTrafficData>> GetTrafficPerMenuItemAsync(TimeRange timeRange)
        {
            var (startDate, endDate) = GetDateRange(timeRange);
            var userId = Guid.Parse(_userIdProvider.GetUserId());

            var menuItemClicks = await _context.MenuItemClicks
                .Include(mic => mic.MenuItem)
                .Include(mic => mic.Menu)
                .Where(mic => mic.ClickedAt >= startDate && mic.ClickedAt <= endDate)
                .Where(mic => mic.Menu != null && mic.Menu.ApplicationUserId == userId)
                .GroupBy(mic => new { mic.MenuItemId, MenuItemName = mic.MenuItem!.Name, MenuName = mic.Menu!.Name })
                .Select(g => new
                {
                    MenuItemId = g.Key.MenuItemId,
                    MenuItemName = g.Key.MenuItemName,
                    MenuName = g.Key.MenuName,
                    Clicks = g.Select(mic => new { mic.ClickedAt }).ToList()
                })
                .ToListAsync();

            var result = new List<MenuItemTrafficData>();

            foreach (var itemGroup in menuItemClicks)
            {
                List<DataPoint> dataPoints;

                if (timeRange == TimeRange.Last24Hours)
                {
                    // Group by hour for 24-hour view
                    dataPoints = itemGroup.Clicks
                        .GroupBy(c => new DateTime(c.ClickedAt.Year, c.ClickedAt.Month, c.ClickedAt.Day, c.ClickedAt.Hour, 0, 0))
                        .Select(g => new DataPoint
                        {
                            Date = g.Key,
                            Count = g.Count()
                        })
                        .OrderBy(dp => dp.Date)
                        .ToList();
                }
                else
                {
                    // Group by date for longer periods
                    dataPoints = itemGroup.Clicks
                        .GroupBy(c => c.ClickedAt.Date)
                        .Select(g => new DataPoint
                        {
                            Date = g.Key,
                            Count = g.Count()
                        })
                        .OrderBy(dp => dp.Date)
                        .ToList();
                }

                result.Add(new MenuItemTrafficData
                {
                    MenuItemId = itemGroup.MenuItemId,
                    MenuItemName = itemGroup.MenuItemName,
                    MenuName = itemGroup.MenuName,
                    Data = dataPoints,
                    TotalClicks = itemGroup.Clicks.Count
                });
            }

            return result.OrderByDescending(mi => mi.TotalClicks).ToList();
        }

        public (DateTime StartDate, DateTime EndDate) GetDateRange(TimeRange timeRange)
        {
            var endDate = DateTime.UtcNow;
            var startDate = timeRange switch
            {
                TimeRange.Last24Hours => endDate.AddHours(-24),
                TimeRange.Last7Days => endDate.AddDays(-7),
                TimeRange.Last30Days => endDate.AddDays(-30),
                _ => endDate.AddDays(-7)
            };

            return (startDate, endDate);
        }
    }
}
