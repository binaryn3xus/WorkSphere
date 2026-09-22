using System;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace WorkSphere.Services;

public class UserTimeService : IUserTimeService
{
    public const string TimezoneCookieName = "ws_tz_offset";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IConfiguration _configuration;
    private readonly TimeZoneInfo _configuredTimeZone;

    public UserTimeService(IHttpContextAccessor httpContextAccessor, IConfiguration configuration)
    {
        _httpContextAccessor = httpContextAccessor;
        _configuration = configuration;
        _configuredTimeZone = ResolveConfiguredTimeZone(configuration);
    }

    public DateOnly GetToday()
    {
        return DateOnly.FromDateTime(GetNow());
    }

    public DateTime GetNow()
    {
        // 1. Check browser cookie for offset in minutes (ws_tz_offset)
        try
        {
            var context = _httpContextAccessor.HttpContext;
            if (context != null && context.Request.Cookies.TryGetValue(TimezoneCookieName, out var cookieVal))
            {
                if (int.TryParse(cookieVal, out var offsetMinutes))
                {
                    // JS getTimezoneOffset returns minutes difference between UTC and local (positive for west of UTC, negative for east)
                    return DateTime.UtcNow.AddMinutes(-offsetMinutes);
                }
            }
        }
        catch
        {
            // Context might not be accessible outside of HTTP request
        }

        // 2. Use configured time zone (from appsettings or TZ env var)
        try
        {
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _configuredTimeZone);
        }
        catch
        {
            return DateTime.Now;
        }
    }

    public DateTime GetUtcNow() => DateTime.UtcNow;

    public TimeZoneInfo GetTimeZone() => _configuredTimeZone;

    private static TimeZoneInfo ResolveConfiguredTimeZone(IConfiguration configuration)
    {
        var tzId = configuration["TimeZone"]
            ?? configuration["APP_TIMEZONE"]
            ?? Environment.GetEnvironmentVariable("TZ")
            ?? Environment.GetEnvironmentVariable("TIMEZONE");

        if (!string.IsNullOrWhiteSpace(tzId))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(tzId.Trim());
            }
            catch
            {
                // In case of invalid or unrecognized ID, fallback
            }
        }

        return TimeZoneInfo.Local;
    }
}
