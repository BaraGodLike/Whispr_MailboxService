# Whispr Mailbox Service

[Русская версия](README.ru.md)

Whispr Mailbox Service issues random mailbox identifiers bound to specific users.

Core behavior:
- each user has a current mailbox;
- a mailbox stays active for 6 days;
- a user is registered together with the initial mailbox set;
- the service can return the current mailbox for a user;
- the service can resolve a user by a non-expired mailbox;
- a separate worker performs daily mailbox rotation;
- realtime auth is completed with a signed nonce challenge.

## Features

- Register a user with `auth_alg` and `public_key`.
- Get the current mailbox for a user.
- Get the user by mailbox while that mailbox is still active.
- Begin and complete realtime auth over gRPC.
- Expose the standard gRPC health service.

## Solution structure

- `Services` - gRPC API and gRPC health service.
- `Application` - application logic and business rules.
- `Infrastructure.Storage` - Postgres and Redis integration.
- `Migrator` - database migrations.
- `Worker` - one-shot daily mailbox rotation worker.
- `UnitTests` - tests.

## Mailbox lifecycle

- `RegisterUser` creates the user and prepares two mailbox records for that user: the current mailbox and the next mailbox.
- The current mailbox is the mailbox with `ExpiresDay = today + 6`.
- A mailbox owner lookup is considered active while `today < expires_day`.
- `CompleteRealtimeAuth` returns 6 active mailboxes for the user.
- Daily rotation prepares the next mailbox period and removes stale data.

## gRPC API

Default local gRPC endpoint in Docker Compose: `http://localhost:${GRPC_PORT}` with `8443` as the default from `.env`.

Proto file: [Services/mailbox.proto](Services/mailbox.proto)

Service:

```proto
service MailboxApi {
  rpc GetMailbox (GetMailboxRequest) returns (MailboxResponse);
  rpc GetUser (GetUserRequest) returns (GetUserResponse);
  rpc RegisterUser (RegisterUserRequest) returns (google.protobuf.Empty);
  rpc BeginRealtimeAuth (BeginRealtimeAuthRequest) returns (BeginRealtimeAuthResponse);
  rpc CompleteRealtimeAuth (CompleteRealtimeAuthRequest) returns (CompleteRealtimeAuthResponse);
}
```

### RegisterUser

Creates a user and prepares the initial two mailbox records in one operation.

Request example:

```json
{
  "userId": "alice",
  "authAlg": "Ed25519",
  "publicKey": "MCowBQYDK2VwAyEAJ665pMyVe5AIbj0f0jthwUnEuKPeWcgUI11epFjYwJ0="
}
```

Response:
- empty payload

Typical errors:
- `ALREADY_EXISTS` - the user already exists.
- `INVALID_ARGUMENT` - `userId`, `authAlg`, or `publicKey` is empty.

### GetMailbox

Request example:

```json
{
  "userId": "alice"
}
```

Response example:

```json
{
  "mailboxAddress": "11111111-2222-3333-4444-555555555555",
  "refreshAfterUtc": "2026-05-17T00:00:00Z"
}
```

Typical errors:
- `NOT_FOUND` - the user was not found.
- `INVALID_ARGUMENT` - `userId` is empty.

### GetUser

Request example:

```json
{
  "mailbox": "11111111-2222-3333-4444-555555555555"
}
```

Response example:

```json
{
  "userId": "alice"
}
```

Typical errors:
- `NOT_FOUND` - the mailbox was not found or is no longer active.
- `INVALID_ARGUMENT` - `mailbox` is not a valid GUID.

## Realtime auth

`BeginRealtimeAuth` accepts `userId`, generates a random nonce, stores `rtauth:{nonce} -> user_id` in Redis for 60 seconds, and returns:

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

Notes:
- the signed binary payload is `"realtime-auth" || user_id || nonce`;
- the request `alg` must be present;
- the stored `auth_alg` selects the verifier implementation used by the service;
- if the nonce is valid it is consumed with Redis `GETDEL`.

Success response:

```json
{
  "mailboxes": [
    {
      "mailboxAddress": "11111111-2222-3333-4444-555555555555",
      "refreshAfterUtc": "2026-05-17T00:00:00Z"
    }
  ]
}
```

Typical errors:
- `NOT_FOUND` - the user was not found.
- `INVALID_ARGUMENT` - `userId`, `alg`, or `nonce` is invalid, or `signature` is empty.
- `UNAUTHENTICATED` - signature verification failed.
- `FAILED_PRECONDITION` - the nonce is missing, expired, already used, the stored public key is invalid, or the stored auth algorithm is not supported.

## gRPC health

The service exposes the standard gRPC health service `grpc.health.v1.Health`.

## Storage notes

- Postgres stores users in `Users(user, auth_alg, public_key)`.
- Redis stores realtime auth challenges as `rtauth:{nonce} -> user_id` with TTL 60 seconds.
- Mailbox caches are filled lazily on mailbox lookup paths.

## Logging

- `Services` and `Worker` write structured single-line JSON logs to stdout.
- Loki-friendly structured fields are written directly into log events:
  - `Service`
  - `Instance`
  - `RequestId` for sanitized API error logs
- Logs do not include user identifiers, mailbox values, nonces, signatures, or public keys.
- Raw exceptions are not written to logs; only sanitized metadata such as `ExceptionType` is logged.

## Running with Docker Compose

The Docker Compose setup includes:
- Postgres
- Redis
- Redis Insight
- `Migrator` for database migrations
- `Services` for the gRPC API

The current Compose setup does not start `Worker`. The worker is expected to be run separately by an external scheduler.

### 1. Prepare `.env`

Copy [.env.example](.env.example) to `.env` and fill the required values:
- `POSTGRES_DB`
- `POSTGRES_USER`
- `POSTGRES_PASSWORD`
- `POSTGRES_PORT`
- `REDIS_PASSWORD`
- `REDIS_PORT`
- `REDIS_INSIGHT_PORT`
- `GRPC_PORT`

### 2. Start the services

```powershell
docker compose up --build
```

Default local endpoint after startup:
- gRPC: `http://localhost:${GRPC_PORT}` with `8443` as the default

## Testing gRPC in Postman

1. Create a `gRPC Request`.
2. Set the server to `http://localhost:${GRPC_PORT}`.
3. Import [Services/mailbox.proto](Services/mailbox.proto).
4. Choose a `MailboxApi` method.

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
