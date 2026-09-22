using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Moq;
using WorkSphere.Services;
using Xunit;

namespace WorkSphere.Tests;

public class UserTimeServiceTests
{
    [Fact]
    public void GetNow_WithBrowserCookieOffset_AppliesOffsetCorrectly()
    {
        // Arrange: 240 minutes offset (EDT = UTC-4)
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["Cookie"] = $"{UserTimeService.TimezoneCookieName}=240";

        var contextAccessorMock = new Mock<IHttpContextAccessor>();
        contextAccessorMock.Setup(a => a.HttpContext).Returns(httpContext);

        var config = new ConfigurationBuilder().Build();
        var service = new UserTimeService(contextAccessorMock.Object, config);

        // Act
        var now = service.GetNow();
        var utcNow = DateTime.UtcNow;

        // Assert: now should be ~4 hours behind utcNow (allow small clock tolerance)
        var diff = utcNow - now;
        Assert.True(Math.Abs(diff.TotalMinutes - 240) < 1, $"Expected diff ~240 min, but got {diff.TotalMinutes}");
    }

    [Fact]
    public void GetNow_WithNegativeCookieOffset_AppliesOffsetForAheadOfUtc()
    {
        // Arrange: -540 minutes offset (JST = UTC+9)
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["Cookie"] = $"{UserTimeService.TimezoneCookieName}=-540";

        var contextAccessorMock = new Mock<IHttpContextAccessor>();
        contextAccessorMock.Setup(a => a.HttpContext).Returns(httpContext);

        var config = new ConfigurationBuilder().Build();
        var service = new UserTimeService(contextAccessorMock.Object, config);

        // Act
        var now = service.GetNow();
        var utcNow = DateTime.UtcNow;

        // Assert: now should be ~9 hours ahead of utcNow
        var diff = now - utcNow;
        Assert.True(Math.Abs(diff.TotalMinutes - 540) < 1, $"Expected diff ~540 min, but got {diff.TotalMinutes}");
    }

    [Fact]
    public void GetNow_WithoutCookie_FallsBackToConfiguredTimeZone()
    {
        // Arrange: No cookie, configured as UTC
        var contextAccessorMock = new Mock<IHttpContextAccessor>();
        contextAccessorMock.Setup(a => a.HttpContext).Returns((HttpContext?)null);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TimeZone"] = "UTC"
            })
            .Build();

        var service = new UserTimeService(contextAccessorMock.Object, config);

        // Act
        var now = service.GetNow();
        var utcNow = DateTime.UtcNow;

        var diff = Math.Abs((utcNow - now).TotalSeconds);
        Assert.True(diff < 2, $"Expected now to match UTC within 2s, but diff was {diff}s");
    }

    [Fact]
    public void GetToday_MatchesDateOfGetNow()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["Cookie"] = $"{UserTimeService.TimezoneCookieName}=0";

        var contextAccessorMock = new Mock<IHttpContextAccessor>();
        contextAccessorMock.Setup(a => a.HttpContext).Returns(httpContext);

        var config = new ConfigurationBuilder().Build();
        var service = new UserTimeService(contextAccessorMock.Object, config);

        var today = service.GetToday();
        var expected = DateOnly.FromDateTime(service.GetNow());

        Assert.Equal(expected, today);
    }
}
