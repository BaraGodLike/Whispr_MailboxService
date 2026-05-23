# Whispr Mailbox Service

[Русская версия](README.ru.md)

Whispr Mailbox Service issues random mailbox identifiers bound to specific users.

Core behavior:
- each user has a current mailbox;
- a mailbox lives for 6 days;
- you can create a user mailbox;
- you can fetch the latest mailbox by user;
- you can resolve a user by a non-expired mailbox;
- a separate worker performs daily mailbox rotation.

## Features

- Create a mailbox for a user.
- Get the current mailbox for a user.
- Get the user by mailbox if that mailbox has not expired yet.
- Expose a gRPC API.
- Expose a gRPC health-check service.

## Solution structure

- `Services` - gRPC API and gRPC health service.
- `Application` - application logic and business rules.
- `Infrastructure.Storage` - Postgres and Redis integration.
- `Migrator` - database migrations.
- `Worker` - one-shot daily mailbox rotation worker.
- `UnitTests` - tests.

## Mailbox lifecycle

- When a mailbox is created for a user, it becomes that user's current mailbox.
- A mailbox stays active for 6 days.
- The service can return the current mailbox for a user.
- The service can return the user for a mailbox only while that mailbox is still active.
- Daily rotation prepares fresh mailbox data and removes stale data.

## gRPC API

Default local gRPC endpoint in the Docker Compose setup: `https://localhost:8443`

Proto file: [Services/mailbox.proto](Services/mailbox.proto)

Service:

```proto
service MailboxApi {
  rpc GetMailbox (GetMailboxRequest) returns (MailboxResponse);
  rpc GetUser (GetUserRequest) returns (GetUserResponse);
  rpc BeginRealtimeAuth (BeginRealtimeAuthRequest) returns (BeginRealtimeAuthResponse);
  rpc CompleteRealtimeAuth (CompleteRealtimeAuthRequest) returns (CompleteRealtimeAuthResponse);
}
```

### RegisterUser

Creates a new user, stores the public key metadata, and prepares the first two mailboxes for that user in one operation.

Request:

```json
{
  "user": "alice",
  "authAlg": "Ed25519",
  "publicKey": "bytes"
}
```

Response:
- empty payload

### GetMailbox

Request:

```json
{
  "user": "alice"
}
```

Response:

```json
{
  "mailboxAddress": "11111111-2222-3333-4444-555555555555",
  "refreshAfterUtc": "2026-05-17T00:00:00Z"
}
```

### GetUser

Request:

```json
{
  "mailbox": "11111111-2222-3333-4444-555555555555"
}
```

Response:

```json
{
  "user": "alice"
}
```

Typical gRPC errors:
- `NOT_FOUND` - user or mailbox was not found.
- `INVALID_ARGUMENT` - `mailbox` is not a valid GUID, or `user` is empty.
- `UNAUTHENTICATED` - realtime auth signature verification failed.
- `FAILED_PRECONDITION` - realtime auth nonce was missing, expired, already used, or the stored public key is invalid.

## Realtime auth

`BeginRealtimeAuth` accepts `user_id`, generates a random nonce, stores `rtauth:{nonce} -> user_id` in Redis for 60 seconds, and returns:

```json
{
  "nonce": "base64-nonce",
  "expAt": "2026-05-23T12:34:56Z"
}
```

`CompleteRealtimeAuth` accepts:

```json
{
  "userId": "alice",
  "nonce": "base64-nonce",
  "alg": "Ed25519",
  "signature": "bytes"
}
```

The signature is verified against the binary payload `"realtime-auth" || user_id || nonce`.

On success the response returns the user's 6 active mailboxes.

## gRPC health

The service exposes the standard gRPC health service `grpc.health.v1.Health`.

## Running with Docker Compose

The Docker Compose setup includes:
- Postgres
- Redis
- Redis Insight
- `Migrator` for database migrations
- `Services` for the API

The current Compose setup does not start `Worker`. The worker is intended to be run separately by an external scheduler.

### 1. Prepare `.env`

Copy [.env.example](.env.example) to `.env` and fill in the values.

Required values:
- `POSTGRES_DB`
- `POSTGRES_USER`
- `POSTGRES_PASSWORD`
- `POSTGRES_PORT`
- `REDIS_PASSWORD`
- `REDIS_PORT`
- `REDIS_INSIGHT_PORT`
- `HTTPS_CERT_PASSWORD`

### 2. Prepare a certificate for gRPC over HTTPS

From the repository root:

```powershell
dotnet dev-certs https -ep .\certs\devcert.pfx -p changeit
```

The password must match `HTTPS_CERT_PASSWORD` in `.env`.

### 3. Start the services

```powershell
docker-compose up --build
```

Default local endpoints after startup:
- gRPC: `https://localhost:8443`

## Testing gRPC in Postman

1. Create a `gRPC Request`.
2. Set the server to `https://localhost:8443`.
3. Import [Services/mailbox.proto](Services/mailbox.proto).
4. Choose a `MailboxApi` method.
5. If you use a self-signed certificate, disable `Enable server certificate verification`.

## Running locally without Docker

### Services

```powershell
dotnet run --project Services
```

Local launch settings are defined in [Services/Properties/launchSettings.json](Services/Properties/launchSettings.json).

### Worker

```powershell
dotnet run --project Worker
```

Important:
- `Worker` is not a long-running background service;
- it executes one background task and exits;
- it is a good fit for an external scheduler such as a Kubernetes CronJob.

### Migrator

```powershell
dotnet run --project Migrator
```

## Rotation worker

`Worker` performs the daily mailbox rotation:
- prepares fresh mailbox data;
- maintains data for the next period;
- removes stale mailbox entries from the registry and old partitions.

Expected execution model:
- an external scheduler starts `Worker` once per day;
- `Worker` performs a single run and exits.

## Technology stack

- .NET 10
- ASP.NET Core
- gRPC
- PostgreSQL
- Redis
- Dapper
- FluentMigrator
