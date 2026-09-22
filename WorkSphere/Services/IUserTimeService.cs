using System;

namespace WorkSphere.Services;

public interface IUserTimeService
{
    DateOnly GetToday();
    DateTime GetNow();
    DateTime GetUtcNow();
    TimeZoneInfo GetTimeZone();
}
