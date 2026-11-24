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
    private static readonly Uri Notifications = new("/notifications", UriKind.Relative);

    private async Task<HttpResponseMessage> GetNotificationsAsync(string? token)
    {
        using var client = fixture.Factory.CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await client.GetAsync(Notifications);
    }

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
    public async Task ValidToken_Returns200WithTheCallersNotifications()
    {
        var response = await GetNotificationsAsync(fixture.CreateToken());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("[]");
    }

    [Fact]
    public async Task MissingToken_Returns401() =>
        (await GetNotificationsAsync(null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task WrongIssuer_Returns401() =>
        (await GetNotificationsAsync(fixture.CreateToken(issuer: "http://evil.test"))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task WrongAudience_Returns401() =>
        (await GetNotificationsAsync(fixture.CreateToken(audience: "other-api"))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task ExpiredToken_Returns401() =>
        (await GetNotificationsAsync(fixture.CreateToken(expires: DateTime.UtcNow.AddMinutes(-5)))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task Hs256Token_Returns401()
    {
        var secret = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32)) { KeyId = "test-key" };

        var response = await GetNotificationsAsync(
            fixture.CreateToken(credentials: new SigningCredentials(secret, SecurityAlgorithms.HmacSha256)));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TokenSignedByAnotherKey_Returns401()
    {
        using var rsa = RSA.Create(2048);
        var otherKey = new RsaSecurityKey(rsa) { KeyId = "test-key" };

        var response = await GetNotificationsAsync(
            fixture.CreateToken(credentials: new SigningCredentials(otherKey, SecurityAlgorithms.RsaSha256)));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
