using System.Security.Cryptography;
using FiapX.Notification.Application.Messaging;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace FiapX.Notification.IntegrationTests;

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}

/// <summary>Real Postgres and RabbitMQ (Testcontainers) behind the real API host.</summary>
public sealed class ApiFixture : IAsyncLifetime
{
    public const string Issuer = "test-issuer";
    public const string Audience = "fiapx-api";
    public const int DeliveryLimit = 2;

    private readonly RSA _rsa = RSA.Create(2048);

    public PostgreSqlContainer Postgres { get; } = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("notification_db")
        .WithUsername("notification_svc")
        .WithPassword("notification_svc")
        .Build();

    public RabbitMqContainer RabbitMq { get; } = new RabbitMqBuilder("rabbitmq:4.1-management-alpine")
        .WithUsername("fiapx")
        .WithPassword("fiapx")
        .Build();

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public RsaSecurityKey SigningKey { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(Postgres.StartAsync(), RabbitMq.StartAsync());
        SigningKey = new RsaSecurityKey(_rsa) { KeyId = "test-key" };

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Postgres", Postgres.GetConnectionString());
            builder.UseSetting("RabbitMq:Uri", RabbitMq.GetConnectionString());
            builder.UseSetting("RabbitMq:DeliveryLimit", DeliveryLimit.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.UseSetting("Database:MigrateOnStartup", "true");
            builder.UseSetting("Auth:JwksUrl", "http://identity.invalid/.well-known/jwks.json");
            builder.UseSetting("Auth:Issuer", Issuer);
            builder.UseSetting("Auth:Audience", Audience);
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped<IIntegrationEventHandler, UserRegisteredTestHandler>();
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    var configuration = new OpenIdConnectConfiguration();
                    configuration.SigningKeys.Add(SigningKey);
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                });
            });
        });

        // Starts the host: migrations run and the consumer connects.
        _ = Factory.Server;
    }

    public string CreateToken(string audience = Audience, SecurityKey? key = null) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            Expires = DateTime.UtcNow.AddMinutes(5),
            Claims = new Dictionary<string, object> { ["sub"] = Guid.CreateVersion7().ToString(), ["name"] = "Test" },
            SigningCredentials = new SigningCredentials(key ?? SigningKey, SecurityAlgorithms.RsaSha256),
        });

    public async ValueTask DisposeAsync()
    {
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }

        await Postgres.DisposeAsync();
        await RabbitMq.DisposeAsync();
        _rsa.Dispose();
    }
}
