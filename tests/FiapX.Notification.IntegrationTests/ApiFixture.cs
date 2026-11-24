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
using Npgsql;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace FiapX.Notification.IntegrationTests;

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}

/// <summary>
/// Real Postgres and RabbitMQ (Testcontainers) behind the real API host. A second host without handlers runs on its own
/// RabbitMQ vhost and database in the same containers, so it never competes for the main queue.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    public const string Issuer = "http://identity.test";
    public const string Audience = "fiapx-api";
    public const string NoHandlersVhost = "no-handlers";
    private const string User = "fiapx";
    private const string Password = "fiapx";

    private readonly RSA _rsa = RSA.Create(2048);

    public PostgreSqlContainer Postgres { get; } = new PostgreSqlBuilder("postgres:17.12-alpine")
        .WithDatabase("notification_db")
        .WithUsername("notification_svc")
        .WithPassword("notification_svc")
        .Build();

    public RabbitMqContainer RabbitMq { get; } = new RabbitMqBuilder("rabbitmq:4.2.9-management")
        .WithUsername(User)
        .WithPassword(Password)
        .Build();

    /// <summary>Host with the test handler for user.registered.</summary>
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    /// <summary>Host with no handlers registered: it must declare the queue but not consume it.</summary>
    public WebApplicationFactory<Program> NoHandlersFactory { get; private set; } = null!;

    public RsaSecurityKey SigningKey { get; private set; } = null!;

    public string RabbitMqUri(string? vhost = null) =>
        vhost is null ? RabbitMq.GetConnectionString() : $"{RabbitMq.GetConnectionString().TrimEnd('/')}/{vhost}";

    public string PostgresConnectionString(string database) =>
        new NpgsqlConnectionStringBuilder(Postgres.GetConnectionString()) { Database = database }.ConnectionString;

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(Postgres.StartAsync(), RabbitMq.StartAsync());
        SigningKey = new RsaSecurityKey(_rsa) { KeyId = "test-key" };

        await RabbitMq.ExecAsync(["rabbitmqctl", "add_vhost", NoHandlersVhost]);
        await RabbitMq.ExecAsync(["rabbitmqctl", "set_permissions", "-p", NoHandlersVhost, User, ".*", ".*", ".*"]);
        await using (var connection = new NpgsqlConnection(Postgres.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("CREATE DATABASE notification_no_handlers", connection);
            await command.ExecuteNonQueryAsync();
        }

        Factory = CreateFactory(Postgres.GetConnectionString(), RabbitMqUri(), registerHandler: true);
        NoHandlersFactory = CreateFactory(PostgresConnectionString("notification_no_handlers"), RabbitMqUri(NoHandlersVhost), registerHandler: false);

        // Start both hosts: migrations run and the consumers connect.
        _ = Factory.Server;
        _ = NoHandlersFactory.Server;
    }

    private WebApplicationFactory<Program> CreateFactory(string postgres, string rabbitMq, bool registerHandler) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Postgres", postgres);
            builder.UseSetting("RabbitMq:Uri", rabbitMq);
            builder.UseSetting("RabbitMq:HandlerRetryDelay", "00:00:00.050");
            builder.UseSetting("Database:MigrateOnStartup", "true");
            builder.UseSetting("Auth:Issuer", Issuer);
            builder.UseSetting("Auth:Audience", Audience);
            builder.UseSetting("Auth:MetadataAddress", "http://identity.test/.well-known/openid-configuration");
            builder.UseSetting("Auth:RequireHttpsMetadata", "false");
            builder.ConfigureTestServices(services =>
            {
                if (registerHandler)
                {
                    services.AddScoped<IIntegrationEventHandler, UserRegisteredTestHandler>();
                }

                // Stub of the discovery document: the signing key the tests use, no network call.
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                    configuration.SigningKeys.Add(SigningKey);
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                });
            });
        });

    public string CreateToken(
        string issuer = Issuer,
        string audience = Audience,
        SigningCredentials? credentials = null,
        DateTime? expires = null)
    {
        var now = DateTime.UtcNow;
        var expiresAt = expires ?? now.AddMinutes(5);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = expiresAt.AddMinutes(-10),
            NotBefore = expiresAt.AddMinutes(-10),
            Expires = expiresAt,
            Claims = new Dictionary<string, object> { ["sub"] = Guid.CreateVersion7().ToString(), ["name"] = "Test" },
            SigningCredentials = credentials ?? new SigningCredentials(SigningKey, SecurityAlgorithms.RsaSha256),
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }

        if (NoHandlersFactory is not null)
        {
            await NoHandlersFactory.DisposeAsync();
        }

        await Postgres.DisposeAsync();
        await RabbitMq.DisposeAsync();
        _rsa.Dispose();
    }
}
