# Whispr Mailbox Service

[English version](README.md)

Whispr Mailbox Service выдает случайные mailbox-идентификаторы, привязанные к конкретным пользователям.

Основное поведение:
- у каждого пользователя есть текущий mailbox;
- mailbox живет 6 дней;
- можно создать mailbox для пользователя;
- можно получить последний mailbox по пользователю;
- можно определить пользователя по неистекшему mailbox;
- отдельный воркер выполняет ежедневную ротацию mailbox.

## Возможности

- Создать mailbox для пользователя.
- Получить текущий mailbox пользователя.
- Получить пользователя по mailbox, если mailbox еще не истек.
- Предоставлять HTTP API и gRPC API.
- Отдавать health-check endpoint.

## Структура решения

- `Services` - HTTP API, gRPC API и health endpoint.
- `Application` - прикладная логика и бизнес-правила.
- `Infrastructure.Storage` - интеграция с Postgres и Redis.
- `Migrator` - миграции базы данных.
- `Worker` - one-shot воркер ежедневной ротации mailbox.
- `UnitTests` - тесты.

## Жизненный цикл mailbox

- Когда для пользователя создается mailbox, он становится текущим mailbox этого пользователя.
- Mailbox остается активным 6 дней.
- Сервис может вернуть текущий mailbox пользователя.
- Сервис может вернуть пользователя по mailbox только пока этот mailbox еще активен.
- Ежедневная ротация подготавливает новые mailbox и удаляет устаревшие данные.

## HTTP API

Локальный HTTP endpoint по умолчанию в Docker Compose: `http://localhost:8080`

Это локальные адреса для разработки, а не обязательные production-адреса.

### Health

```http
GET /health
```

Типовой ответ:

```text
Healthy
```

### Создать mailbox для пользователя

```http
POST /Mailbox/new
Content-Type: application/json
```

Тело запроса:

```json
"alice"
```

Ответ:
- `201 Created`
- тело ответа пустое

Важно:
- текущая REST-реализация не возвращает созданный mailbox в теле ответа;
- gRPC-метод `CreateMailbox` ведет себя так же и тоже не возвращает полезную нагрузку.

### Получить текущий mailbox по пользователю

```http
POST /Mailbox/mb
Content-Type: application/json
```

Тело запроса:

```json
"alice"
```

Успешный ответ:

```json
{
  "mailboxAddress": "11111111-2222-3333-4444-555555555555",
  "refreshAfter": "2026-05-17T00:00:00Z"
}
```

Если пользователь не найден:

```json
{
  "error": "User not found."
}
```

### Получить пользователя по mailbox

```http
POST /Mailbox/user
Content-Type: application/json
```

Тело запроса:

```json
"11111111-2222-3333-4444-555555555555"
```

Успешный ответ:

```json
{
  "user": "alice"
}
```

Если mailbox не найден или истек:

```json
{
  "error": "User with this mailbox not found."
}
```

Примечания:
- тело запроса должно быть JSON-строкой с GUID;
- при невалидном GUID ASP.NET Core model binding вернет `400 Bad Request`.

## gRPC API

Локальный gRPC endpoint по умолчанию в Docker Compose: `https://localhost:8443`

Proto-файл: [Services/mailbox.proto](Services/mailbox.proto)

Сервис:

```proto
service MailboxApi {
  rpc GetMailbox (GetMailboxRequest) returns (MailboxResponse);
  rpc GetUser (GetUserRequest) returns (GetUserResponse);
  rpc CreateMailbox (CreateMailboxRequest) returns (google.protobuf.Empty);
}
```

### CreateMailbox

Запрос:

```json
{
  "user": "alice"
}
```

Ответ:
- пустая полезная нагрузка

### GetMailbox

Запрос:

```json
{
  "user": "alice"
}
```

Ответ:

```json
{
  "mailboxAddress": "11111111-2222-3333-4444-555555555555",
  "refreshAfterUtc": "2026-05-17T00:00:00Z"
}
```

### GetUser

Запрос:

```json
{
  "mailbox": "11111111-2222-3333-4444-555555555555"
}
```

Ответ:

```json
{
  "user": "alice"
}
```

Типовые gRPC ошибки:
- `NOT_FOUND` - пользователь или mailbox не найден.
- `INVALID_ARGUMENT` - `mailbox` не является валидным GUID, либо `user` пустой.

## Запуск через Docker Compose

Docker Compose поднимает:
- Postgres
- Redis
- Redis Insight
- `Migrator` для миграций базы
- `Services` для API

Текущий Compose не запускает `Worker`. Воркер предполагается запускать отдельно внешним scheduler.

### 1. Подготовить `.env`

Скопируй [.env.example](.env.example) в `.env` и заполни значения.

Нужные переменные:
- `POSTGRES_DB`
- `POSTGRES_USER`
- `POSTGRES_PASSWORD`
- `POSTGRES_PORT`
- `REDIS_PASSWORD`
- `REDIS_PORT`
- `REDIS_INSIGHT_PORT`
- `HTTPS_CERT_PASSWORD`

### 2. Подготовить сертификат для gRPC over HTTPS

Из корня репозитория:

```powershell
dotnet dev-certs https -ep .\certs\devcert.pfx -p changeit
```

Пароль должен совпадать со значением `HTTPS_CERT_PASSWORD` в `.env`.

### 3. Поднять сервисы

```powershell
docker-compose up --build
```

Локальные endpoint по умолчанию после запуска:
- HTTP API: `http://localhost:8080`
- Health: `http://localhost:8080/health`
- gRPC: `https://localhost:8443`

## Проверка gRPC в Postman

1. Создать `gRPC Request`.
2. Указать сервер `https://localhost:8443`.
3. Импортировать [Services/mailbox.proto](Services/mailbox.proto).
4. Выбрать нужный метод `MailboxApi`.
5. Если используется self-signed сертификат, выключить `Enable server certificate verification`.

## Локальный запуск без Docker

### Services

```powershell
dotnet run --project Services
```

Локальные launch settings лежат в [Services/Properties/launchSettings.json](Services/Properties/launchSettings.json).

### Worker

```powershell
dotnet run --project Worker
```

Важно:
- `Worker` не является постоянно работающим сервисом;
- он выполняет одну фоновую задачу и завершает процесс;
- для него хорошо подходит внешний scheduler, например Kubernetes CronJob.

### Migrator

```powershell
dotnet run --project Migrator
```

## Воркер ротации

`Worker` выполняет ежедневную ротацию mailbox:
- подготавливает новые mailbox;
- поддерживает данные для следующего периода;
- удаляет устаревшие mailbox из registry и старых partition.

Ожидаемая модель запуска:
- внешний scheduler запускает `Worker` раз в сутки;
- `Worker` делает один проход и завершает работу.

## Технологический стек

- .NET 10
- ASP.NET Core
- gRPC
- PostgreSQL
- Redis
- Dapper
- FluentMigrator
