using System.ComponentModel.DataAnnotations;
using FiapX.Notification.Api.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Shouldly;

namespace FiapX.Notification.UnitTests.Auth;

public class JwtAuthenticationTests
{
    private static AuthOptions ValidOptions() => new()
    {
        Issuer = "http://localhost:8080",
        Audience = "fiapx-api",
        MetadataAddress = "http://identity-service:8080/.well-known/openid-configuration",
        RequireHttpsMetadata = false,
    };

    [Fact]
    public void Configure_UsesDiscoveryAndValidatesIssuerAudienceAndRs256()
    {
        var jwt = new JwtBearerOptions();

        JwtAuthentication.Configure(jwt, ValidOptions());

        jwt.MetadataAddress.ShouldBe("http://identity-service:8080/.well-known/openid-configuration");
        jwt.RequireHttpsMetadata.ShouldBeFalse();
        jwt.MapInboundClaims.ShouldBeFalse();
        jwt.TokenValidationParameters.ValidIssuer.ShouldBe("http://localhost:8080");
        jwt.TokenValidationParameters.ValidAudience.ShouldBe("fiapx-api");
        jwt.TokenValidationParameters.ValidAlgorithms.ShouldBe([SecurityAlgorithms.RsaSha256]);
        jwt.TokenValidationParameters.ClockSkew.ShouldBe(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Options_RequireHttpsByDefault() => new AuthOptions().RequireHttpsMetadata.ShouldBeTrue();

    [Theory]
    [InlineData("", "http://identity/.well-known/openid-configuration")]
    [InlineData("not a url", "http://identity/.well-known/openid-configuration")]
    [InlineData("http://localhost:8080", "")]
    public void Options_RejectMissingOrNonUrlValues(string issuer, string metadataAddress)
    {
        var options = ValidOptions();
        options.Issuer = issuer;
        options.MetadataAddress = metadataAddress;

        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true).ShouldBeFalse();
    }
}
