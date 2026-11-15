using System.Net;
using Microsoft.AspNetCore.Hosting;

namespace CommunitySos.Api.IntegrationTests;

public sealed class RateLimitTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Auth_endpoints_are_limited_per_ip_with_retry_after()
    {
        var client = factory
            .WithWebHostBuilder(b => b.UseSetting("RateLimiting:AuthPerMinute", "2"))
            .CreateClient();
        var body = new { firebaseIdToken = "invalid-token" };

        await client.PostJsonAsync("/api/v1/auth/login", body);
        await client.PostJsonAsync("/api/v1/auth/login", body);
        var third = await client.PostJsonAsync("/api/v1/auth/login", body);

        var problem = await third.ExpectAsync(HttpStatusCode.TooManyRequests);
        Assert.Equal("rate_limited", problem.Code());
        Assert.NotNull(third.Headers.RetryAfter);
    }
}
