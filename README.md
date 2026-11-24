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

Até as issues #2 e #3 não existe nenhum handler, então o serviço **declara a fila mas não a consome**:
as mensagens esperam na fila durável, sem descarte e sem ir para a DLQ.

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
- Topologia declarada no startup, idêntica a `fiapx/local/rabbitmq/definitions.json`: exchange `fiapx.events` (topic),
  DLX `fiapx.dlx`, fila **quorum** `notification-service.events` (`x-delivery-limit` 5) ligada a `user.registered`,
  `video.processed` e `video.processing-failed`, e a DLQ `notification-service.events.dlq`.
- O `BasicConsume` só acontece se houver ao menos um `IIntegrationEventHandler` registrado.
- Paralelismo: `PrefetchCount` 16 e `ConsumerConcurrency` 4. Cada mensagem tem seu próprio escopo de DI e sua transação.
  Um *advisory lock* por id de mensagem garante que duas cópias da mesma mensagem não rodem o handler ao mesmo tempo.

| Situação | Resposta ao broker |
|---|---|
| Handler concluiu (efeito + inbox na mesma transação) | `ack` |
| Duplicata (`(message_id, consumer)` já está na inbox) | `ack` |
| Tipo não tratado por este serviço, ou envelope sem `id`/`type` | `ack` + log de aviso |
| Tipo conhecido com `data` inválido (`InvalidMessageDataException`) | `reject` sem requeue → DLQ, sem retry |
| Handler lança exceção | retry em processo (`MaxHandlerAttempts`, padrão 3), backoff exponencial com jitter a partir de `HandlerRetryDelay` (200 ms); esgotou → `reject` → DLQ |
| Desligamento durante o processamento | `nack` com requeue (não conta tentativa) |
| Processo cai no meio | o broker reentrega e conta a entrega; o `x-delivery-limit` manda para a DLQ |

`nack` com requeue nunca é usado para falha: no RabbitMQ 4 ele não incrementa o `delivery-count`, e a mensagem ficaria em loop.
Os handlers lançam `InvalidMessageDataException` para qualquer dado que não passe na validação.

## Configuração

Chaves canônicas, iguais nos três serviços .NET. Credenciais nunca ficam no `appsettings.json`: vêm só do ambiente.

| Chave (variável: `Seção__Chave`) | Padrão | Descrição |
|---|---|---|
| `ConnectionStrings:Postgres` | `Host=localhost;…;Database=notification_db` (sem senha) | A senha vem do ambiente (`…;Password=…`) |
| `RabbitMq:Uri` | `amqp://localhost:5672/` | Usuário e senha vêm do ambiente (`amqp://user:pass@host/`) |
| `RabbitMq:ClientName` | `notification-service` | Nome da conexão no RabbitMQ |
| `RabbitMq:Exchange` / `RabbitMq:DeadLetterExchange` | `fiapx.events` / `fiapx.dlx` | Exchanges da plataforma |
| `RabbitMq:PrefetchCount` | `16` | Mensagens sem ack entregues ao consumidor |
| `RabbitMq:ConsumerConcurrency` | `4` | Mensagens processadas em paralelo |
| `RabbitMq:MaxHandlerAttempts` | `3` | Tentativas em processo antes da DLQ |
| `RabbitMq:HandlerRetryDelay` | `00:00:00.200` | Primeiro intervalo de retry (dobra a cada tentativa) |
| `Auth:Issuer` | `http://localhost:8080` | Issuer público (URL do gateway), igual ao `iss` do token |
| `Auth:Audience` | `fiapx-api` | |
| `Auth:MetadataAddress` | `http://localhost:8091/.well-known/openid-configuration` | Discovery OIDC do identity-service, alcançável pelo serviço |
| `Auth:RequireHttpsMetadata` | `true` (`false` em Development) | `false` só localmente e no compose |
| `Database:MigrateOnStartup` | `false` | `true` no docker-compose |
| `OTEL_EXPORTER_OTLP_ENDPOINT` / `OTEL_SERVICE_NAME` | — / `notification-service` | Exportação OTLP |

A fila, a DLQ e o limite de entregas são constantes no código (`RabbitMqTopology`), porque fazem parte do contrato da topologia.

Endpoints: `GET /health/live` e `GET /health/ready` (anônimos) e `GET /notifications`, que exige JWT e lista as 50
notificações mais recentes do usuário do token (`sub`). O token é validado (RS256, issuer, audience e expiração) com as
chaves do documento de discovery do identity-service.

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

### Banco (ADR-0009)

As migrations do EF Core são a fonte da verdade. O `db/schema.sql`, que é o script entregue no hackathon, é a saída
de `migrations script --idempotent`, e o CI falha se o modelo tiver mudanças sem migration ou se o script estiver
desatualizado.

```bash
dotnet tool restore   # dotnet-ef 10.0.15, fixado em .config/dotnet-tools.json
EF="-p src/FiapX.Notification.Infrastructure -s src/FiapX.Notification.Infrastructure"
dotnet ef migrations add <Nome> $EF -o Persistence/Migrations
dotnet ef migrations script --idempotent $EF -o db/schema.sql
```

`Database:MigrateOnStartup=true` é seguro com várias réplicas subindo juntas: o EF Core 9+ pega um lock de migration,
e o provider Npgsql o implementa (`NpgsqlMigrationDatabaseLock`). Um teste de integração roda 4 `MigrateAsync`
simultâneos num banco vazio.

## CI

`.github/workflows/ci.yml`: build → testes (unitários e integração) com cobertura → SonarQube Cloud (secretless, via OIDC)
→ build da imagem. `commit-lint.yml` valida Conventional Commits em inglês e reprova `Co-authored-by`.
