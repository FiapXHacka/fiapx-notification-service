using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FiapX.Notification.Api.Auth;

/// <summary>Canonical Auth keys shared by the .NET services (ADR-0011).</summary>
public sealed class AuthOptions
{
    public const string Section = "Auth";

    /// <summary>Public issuer URL, as written in the token's iss claim (e.g. http://localhost:8080 through the gateway).</summary>
    [Required]
    [Url]
    public string Issuer { get; set; } = string.Empty;

    [Required]
    public string Audience { get; set; } = "fiapx-api";

    /// <summary>OIDC discovery document reachable by this service, e.g. http://identity-service:8080/.well-known/openid-configuration.</summary>
    [Required]
    [Url]
    public string MetadataAddress { get; set; } = string.Empty;

    /// <summary>True by default; false only locally (Development, docker-compose).</summary>
    public bool RequireHttpsMetadata { get; set; } = true;
}

/// <summary>
/// Validates RS256 access tokens issued by the identity-service. Signing keys come from its OIDC discovery
/// document (jwks_uri), cached and refreshed by the JwtBearer handler, which supports key rotation by kid.
/// Every endpoint requires an authenticated user unless it opts out with AllowAnonymous (health checks do).
/// </summary>
public static class JwtAuthentication
{
    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AuthOptions>()
            .Bind(configuration.GetSection(AuthOptions.Section))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<AuthOptions>>((jwt, authOptions) => Configure(jwt, authOptions.Value));

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        return services;
    }

    internal static void Configure(JwtBearerOptions jwt, AuthOptions auth)
    {
        jwt.MetadataAddress = auth.MetadataAddress;
        jwt.RequireHttpsMetadata = auth.RequireHttpsMetadata;
        jwt.MapInboundClaims = false;
        jwt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = auth.Issuer,
            ValidAudience = auth.Audience,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ValidateIssuerSigningKey = true,
            NameClaimType = "name",
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    }
}
