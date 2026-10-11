// UIAMovie.Infrastructure/Services/VnClock.cs

using UIAMovie.Application.Interfaces;

namespace UIAMovie.Infrastructure.Services;

public class VnClock : IClock
{
    // Windows: "SE Asia Standard Time"; Linux/macOS (cần tzdata/ICU): "Asia/Ho_Chi_Minh"
    private static readonly TimeZoneInfo Tz = ResolveTimeZone();

    public DateOnly Today =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Tz));

    private static TimeZoneInfo ResolveTimeZone()
    {
        foreach (var id in new[] { "Asia/Ho_Chi_Minh", "SE Asia Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        // Fallback an toàn (container thiếu tzdata): Việt Nam không dùng DST nên cố định UTC+7 là chính xác.
        return TimeZoneInfo.CreateCustomTimeZone("VN", TimeSpan.FromHours(7), "VN", "VN");
    }
}