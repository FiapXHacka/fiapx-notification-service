# fiapx-notification-service

Serviço de notificações da plataforma **FIAP X** (.NET 10). Mantém uma projeção local dos usuários a partir de
`user.registered` e envia e-mail quando o processamento de um vídeo falha (e, opcionalmente, quando termina).

> Parte da plataforma FIAP X: arquitetura, ADRs e contratos ficam em [`FiapXHacka/fiapx`](https://github.com/FiapXHacka/fiapx).

[![Quality Gate](https://sonarcloud.io/api/project_badges/measure?project=FiapXHacka_fiapx-notification-service&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=FiapXHacka_fiapx-notification-service)

## Platform overview

```
identity-service ── user.registered ─────────┐
video-processor ── video.processed ──────────┤──▶ RabbitMQ (fiapx.events) ──▶ notification-service.events (quorum, DLQ)
video-processor ── video.processing-failed ──┘                                       │
                                                                                     ▼
                                                     notification-service (este repo) ── SMTP ──▶ usuário
                                                              │
                                                       notification_db (Postgres)
```

## Status

| Issue | Escopo | Situação |
|---|---|---|
| #1 | Scaffold: Clean Architecture, consumer RabbitMQ com inbox, EF Core, JWT, health, OTel, CI + Sonar | este PR |
| #2 | Projeção `user_contacts` a partir de `user.registered` | pendente |
| #3 | E-mail de falha (`video.processing-failed`) | pendente |
| #4 / #5 | Retry de envio, e-mail de sucesso | pendente |

Até as issues #2 e #3, os eventos assinados **não são descartados**: sem handler registrado, a mensagem é
reenfileirada até o limite de entregas e vai para a DLQ `notification-service.events.dlq`.

## Estrutura

```
src/
  FiapX.Notification.Domain/          UserContact, UserNotification (+ enums)
  FiapX.Notification.Application/     Messaging: CloudEvent<T>, IIntegrationEventHandler
  FiapX.Notification.Infrastructure/  Persistence (EF Core + Npgsql, migrations), Messaging (RabbitMQ.Client),
                                      health checks, DI
  FiapX.Notification.Api/             Program.cs, JWT (JWKS do identity-service), OpenTelemetry, /health
tests/
  FiapX.Notification.UnitTests/         domínio, envelope CloudEvent, dispatcher, topologia, JWKS
  FiapX.Notification.IntegrationTests/  Postgres + RabbitMQ reais (Testcontainers) atrás da API real
db/schema.sql                          DDL equivalente à migration InitialCreate
```

### Consumo de mensagens (ADR-0006 / ADR-0007)

- Envelope **CloudEvents 1.0** (JSON), sem MassTransit, com `RabbitMQ.Client` 7 (API assíncrona).
- Topologia declarada no startup, de forma idempotente: exchange `fiapx.events` (topic), DLX `fiapx.dlx`,
  fila **quorum** `notification-service.events` (`x-delivery-limit` 5) ligada a `user.registered`,
  `video.processed` e `video.processing-failed`, e a DLQ `notification-service.events.dlq`.
- **Ack manual**: o handler e o registro na **inbox** (`inbox_messages`) são gravados na mesma transação.
  Uma mensagem reentregue com o mesmo `id` é reconhecida como duplicata e só recebe ack.
- Mensagem inválida (não é CloudEvent) ou de tipo não assinado: ack e log, porque nunca daria certo.
- Exceção no handler: nack com requeue, e o limite de entregas da quorum queue manda a mensagem para a DLQ.
- O `traceparent` do evento continua o trace distribuído (ActivitySource `FiapX.Notification.Messaging`).

## Configuração

| Variável | Padrão (`appsettings.json`) | Descrição |
|---|---|---|
| `ConnectionStrings__Postgres` | `Host=localhost;…;Database=notification_db` (sem senha) | Banco do serviço; a senha vem só do ambiente (`…;Password=…`) |
| `RabbitMq__Uri` | `amqp://localhost:5672/` | Broker; usuário e senha vêm do ambiente (`amqp://user:pass@host/`) |
| `RabbitMq__Queue` | `notification-service.events` | Fila consumida |
| `RabbitMq__DeliveryLimit` | `5` | Entregas antes da DLQ |
| `RabbitMq__Prefetch` | `10` | Mensagens em voo por consumidor |
| `RabbitMq__ConsumerEnabled` | `true` | Desliga o consumer (ferramentas e testes) |
| `Auth__JwksUrl` | `http://localhost:8081/.well-known/jwks.json` | JWKS do identity-service |
| `Auth__Issuer` / `Auth__Audience` | `fiapx-identity` / `fiapx-api` | Validação do JWT (RS256) |
| `Auth__RequireHttpsMetadata` | `false` | JWKS por HTTP dentro do cluster/compose |
| `Database__MigrateOnStartup` | `false` | Aplica as migrations no startup (compose) |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | — | Liga a exportação OTLP (traces e métricas) |
| `OTEL_SERVICE_NAME` | `notification-service` | Nome do serviço na telemetria |

Endpoints: `GET /health/live` (processo no ar) e `GET /health/ready` (Postgres + RabbitMQ), ambos anônimos.
Qualquer outro endpoint exige um JWT válido.

## Desenvolvimento

Requisitos: .NET SDK 10 (`global.json`) e Docker (os testes de integração usam Testcontainers).

```bash
dotnet build FiapX.Notification.slnx
dotnet test FiapX.Notification.slnx
docker build -t fiapx-notification-service .
```

Sem o SDK 10 instalado localmente:

```bash
docker run --rm -v "$PWD":/src -w /src \
  -v /var/run/docker.sock:/var/run/docker.sock \
  -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal --add-host=host.docker.internal:host-gateway \
  mcr.microsoft.com/dotnet/sdk:10.0 dotnet test FiapX.Notification.slnx
```

Nova migration:

```bash
dotnet tool install --global dotnet-ef --version 10.0.15
dotnet ef migrations add <Nome> -p src/FiapX.Notification.Infrastructure -s src/FiapX.Notification.Infrastructure -o Persistence/Migrations
```

Atualize o `db/schema.sql` junto: ele é o script de banco entregue no hackathon.

## CI

`.github/workflows/ci.yml`: build → testes (unitários e integração) com cobertura → SonarQube Cloud (secretless, via OIDC)
→ build da imagem. `commit-lint.yml` valida Conventional Commits em inglês e reprova `Co-authored-by`.
