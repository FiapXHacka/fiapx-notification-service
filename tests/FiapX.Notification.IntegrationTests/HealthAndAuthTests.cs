using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using Shouldly;

namespace FiapX.Notification.IntegrationTests;

[Collection(ApiCollection.Name)]
public class HealthAndAuthTests(ApiFixture fixture)
{
    [Fact]
    public async Task Live_IsAnonymousAndHealthy()
    {
        using var client = fixture.Factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ready_ChecksPostgresAndRabbitMq()
    {
        using var client = fixture.Factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("status").GetString().ShouldBe("Healthy");
        json.RootElement.GetProperty("checks").GetProperty("postgres").GetString().ShouldBe("Healthy");
        json.RootElement.GetProperty("checks").GetProperty("rabbitmq").GetString().ShouldBe("Healthy");
    }

    [Fact]
    public async Task OtherEndpoints_RequireAToken()
    {
        using var client = fixture.Factory.CreateClient();

        var response = await client.GetAsync(new Uri("/notifications", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ValidToken_IsAccepted()
    {
        using var client = fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.CreateToken());

        var response = await client.GetAsync(new Uri("/notifications", UriKind.Relative));

        // Authenticated: no endpoint exists yet, so the request gets past auth and ends in 404.
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TokenForAnotherAudience_IsRejected()
    {
        using var client = fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.CreateToken(audience: "other-api"));

        var response = await client.GetAsync(new Uri("/notifications", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TokenSignedByAnotherKey_IsRejected()
    {
        using var rsa = RSA.Create(2048);
        using var client = fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            fixture.CreateToken(key: new RsaSecurityKey(rsa) { KeyId = "test-key" }));

        var response = await client.GetAsync(new Uri("/notifications", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
