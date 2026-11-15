using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace CommunitySos.Api.IntegrationTests;

public sealed class PipelineTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Liveness_probe_is_anonymous_and_healthy()
    {
        var response = await factory.CreateClient().GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task App_info_is_public_and_lists_112_first()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/app-info");
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;

        Assert.Equal("2026-11", root.GetProperty("policyVersion").GetString());
        Assert.Equal("112", root.GetProperty("emergencyNumbers")[0].GetProperty("number").GetString());
        Assert.Equal("public, max-age=300", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Protected_endpoint_without_token_returns_401_problem_details()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/session");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("unauthenticated", problem.GetProperty("code").GetString());
        Assert.True(problem.TryGetProperty("traceId", out _));
        Assert.True(problem.TryGetProperty("correlationId", out _));
    }

    [Fact]
    public async Task Valid_token_reaches_endpoint_with_role()
    {
        var master = ApiFactory.NewMasterAdmin();
        var client = factory.CreateClientFor(master);

        var session = await client.GetFromJsonAsync<JsonElement>("/api/v1/session");

        Assert.Equal(master.Id, session.GetProperty("userId").GetGuid());
        Assert.Equal("MasterAdmin", session.GetProperty("role").GetString());
    }

    [Fact]
    public async Task Token_of_suspended_account_is_rejected_immediately()
    {
        var master = ApiFactory.NewMasterAdmin();
        var client = factory.CreateClientFor(master);
        factory.UserStatus.Deactivate(master.Id);

        var response = await client.GetAsync("/api/v1/session");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Tampered_token_is_rejected()
    {
        var client = factory.CreateClientFor(ApiFactory.NewMasterAdmin());
        var token = client.DefaultRequestHeaders.Authorization!.Parameter!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", token[..^4] + "AAAA");

        var response = await client.GetAsync("/api/v1/session");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Well_formed_correlation_id_is_echoed_and_bad_one_replaced()
    {
        var client = factory.CreateClient();

        using var good = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        good.Headers.Add("X-Correlation-ID", "mobile-abc-123");
        var goodResponse = await client.SendAsync(good);
        Assert.Equal("mobile-abc-123", goodResponse.Headers.GetValues("X-Correlation-ID").Single());

        using var bad = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        bad.Headers.Add("X-Correlation-ID", "<script>alert(1)</script>");
        var badResponse = await client.SendAsync(bad);
        Assert.NotEqual("<script>alert(1)</script>", badResponse.Headers.GetValues("X-Correlation-ID").Single());
    }

    [Fact]
    public async Task Api_responses_carry_security_headers_and_are_not_cached()
    {
        var response = await factory.CreateClientFor(ApiFactory.NewMasterAdmin()).GetAsync("/api/v1/session");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Unknown_route_returns_404_problem_details()
    {
        var response = await factory.CreateClientFor(ApiFactory.NewMasterAdmin()).GetAsync("/api/v1/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("not_found", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Routes_are_lowercase_kebab_case()
    {
        var response = await factory.CreateClientFor(ApiFactory.NewMasterAdmin()).GetAsync("/api/v1/session");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task OpenApi_document_is_not_exposed_outside_development()
    {
        var response = await factory.CreateClient().GetAsync("/openapi/v1.json");

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }
}
