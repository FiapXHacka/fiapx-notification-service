using System.Security.Cryptography;
using System.Text.Json;
using FiapX.Notification.Api.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Shouldly;

namespace FiapX.Notification.UnitTests.Auth;

public class JwksConfigurationRetrieverTests
{
    [Fact]
    public async Task ReadsSigningKeysFromJwks()
    {
        using var rsa = RSA.Create(2048);
        var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(rsa) { KeyId = "key-1" });
        jwk.Use = "sig";
        jwk.Alg = SecurityAlgorithms.RsaSha256;
        var jwks = JsonSerializer.Serialize(new { keys = new[] { new { jwk.Kty, jwk.Kid, jwk.Use, jwk.Alg, jwk.N, jwk.E } } },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        var documents = Substitute.For<IDocumentRetriever>();
        documents.GetDocumentAsync("http://identity/.well-known/jwks.json", Arg.Any<CancellationToken>()).Returns(jwks);

        var config = await new JwksConfigurationRetriever()
            .GetConfigurationAsync("http://identity/.well-known/jwks.json", documents, CancellationToken.None);

        config.SigningKeys.Count.ShouldBe(1);
        config.SigningKeys.Single().KeyId.ShouldBe("key-1");
    }

    [Fact]
    public void Configure_ValidatesIssuerAudienceAndRs256()
    {
        var jwt = new JwtBearerOptions();

        JwtAuthentication.Configure(jwt, new AuthOptions
        {
            JwksUrl = "https://identity/.well-known/jwks.json",
            Issuer = "fiapx-identity",
            Audience = "fiapx-api",
            RequireHttpsMetadata = true,
        });

        jwt.MapInboundClaims.ShouldBeFalse();
        jwt.RequireHttpsMetadata.ShouldBeTrue();
        jwt.TokenValidationParameters.ValidIssuer.ShouldBe("fiapx-identity");
        jwt.TokenValidationParameters.ValidAudience.ShouldBe("fiapx-api");
        jwt.TokenValidationParameters.ValidAlgorithms.ShouldBe([SecurityAlgorithms.RsaSha256]);
        jwt.ConfigurationManager.ShouldNotBeNull();
    }
}
