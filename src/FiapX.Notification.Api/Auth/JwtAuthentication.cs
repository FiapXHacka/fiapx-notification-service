using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace FiapX.Notification.Api.Auth;

public sealed class AuthOptions
{
    public const string Section = "Auth";

    /// <summary>identity-service JWKS, e.g. http://identity-service:8080/.well-known/jwks.json</summary>
    [Required]
    public string JwksUrl { get; set; } = string.Empty;

    [Required]
    public string Issuer { get; set; } = string.Empty;

    [Required]
    public string Audience { get; set; } = "fiapx-api";

    /// <summary>Inside the cluster/compose the JWKS is fetched over plain HTTP.</summary>
    public bool RequireHttpsMetadata { get; set; } = true;
}

/// <summary>
/// Validates RS256 access tokens issued by the identity-service (ADR-0011). Every endpoint requires an
/// authenticated user unless it opts out with AllowAnonymous (health checks do).
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
        jwt.MapInboundClaims = false;
        jwt.RequireHttpsMetadata = auth.RequireHttpsMetadata;
        jwt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = auth.Issuer,
            ValidAudience = auth.Audience,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ValidateIssuerSigningKey = true,
            NameClaimType = "name",
            ClockSkew = TimeSpan.FromSeconds(30),
        };
        jwt.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            auth.JwksUrl,
            new JwksConfigurationRetriever(),
            new HttpDocumentRetriever { RequireHttps = auth.RequireHttpsMetadata });
    }
}

/// <summary>
/// The identity-service publishes only a JWKS document (no OpenID discovery), so the signing keys are read
/// straight from it. The ConfigurationManager caches and refreshes them, which supports key rotation by kid.
/// </summary>
internal sealed class JwksConfigurationRetriever : IConfigurationRetriever<OpenIdConnectConfiguration>
{
    public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(
        string address, IDocumentRetriever retriever, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(retriever);
        var json = await retriever.GetDocumentAsync(address, cancel);
        var keySet = new JsonWebKeySet(json);
        var configuration = new OpenIdConnectConfiguration();
        foreach (var key in keySet.GetSigningKeys())
        {
            configuration.SigningKeys.Add(key);
        }

        return configuration;
    }
}
