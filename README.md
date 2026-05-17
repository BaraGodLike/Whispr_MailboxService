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
- Expose both HTTP and gRPC APIs.
- Expose a health-check endpoint.

## Solution structure

- `Services` - HTTP API, gRPC API, and health endpoint.
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

## HTTP API

Default local HTTP endpoint in the Docker Compose setup: `http://localhost:8080`

These addresses are local development defaults, not required production addresses.

### Health

```http
GET /health
```

Typical response body:

```text
Healthy
```

### Create a mailbox for a user

```http
POST /Mailbox/new
Content-Type: application/json
```

Request body:

```json
"alice"
```

Response:
- `201 Created`
- empty response body

Important:
- the current REST implementation does not return the created mailbox in the response body;
- the gRPC `CreateMailbox` method follows the same command-style behavior and also returns no payload.

### Get the current mailbox by user

```http
POST /Mailbox/mb
Content-Type: application/json
```

Request body:

```json
"alice"
```

Successful response:

```json
{
  "mailboxAddress": "11111111-2222-3333-4444-555555555555",
  "refreshAfter": "2026-05-17T00:00:00Z"
}
```

If the user is not found:

```json
{
  "error": "User not found."
}
```

### Get the user by mailbox

```http
POST /Mailbox/user
Content-Type: application/json
```

Request body:

```json
"11111111-2222-3333-4444-555555555555"
```

Successful response:

```json
{
  "user": "alice"
}
```

If the mailbox is not found or has expired:

```json
{
  "error": "User with this mailbox not found."
}
```

Notes:
- the request body must be a JSON string containing a GUID;
- with an invalid GUID, ASP.NET Core model binding returns `400 Bad Request`.

## gRPC API

Default local gRPC endpoint in the Docker Compose setup: `https://localhost:8443`

Proto file: [Services/mailbox.proto](Services/mailbox.proto)

Service:

```proto
service MailboxApi {
  rpc GetMailbox (GetMailboxRequest) returns (MailboxResponse);
  rpc GetUser (GetUserRequest) returns (GetUserResponse);
  rpc CreateMailbox (CreateMailboxRequest) returns (google.protobuf.Empty);
}
```

### CreateMailbox

Request:

```json
{
  "user": "alice"
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
- HTTP API: `http://localhost:8080`
- Health: `http://localhost:8080/health`
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
