# Whispr Mailbox Service

[English version](README.md)

Whispr Mailbox Service выдает случайные mailbox-идентификаторы, привязанные к конкретным пользователям.

Основное поведение:
- у каждого пользователя есть текущий mailbox;
- mailbox активен 6 дней;
- пользователь регистрируется вместе с начальным набором mailbox;
- сервис умеет вернуть текущий mailbox пользователя;
- сервис умеет определить пользователя по неистекшему mailbox;
- отдельный воркер выполняет ежедневную ротацию mailbox;
- realtime auth завершается через challenge с подписанным nonce.

## Возможности

- Зарегистрировать пользователя с `auth_alg` и `public_key`.
- Получить текущий mailbox пользователя.
- Получить пользователя по mailbox, пока mailbox еще активен.
- Запустить и завершить realtime auth через gRPC.
- Отдавать стандартный gRPC health service.

## Структура решения

- `Services` - gRPC API и gRPC health service.
- `Application` - прикладная логика и бизнес-правила.
- `Infrastructure.Storage` - интеграция с Postgres и Redis.
- `Migrator` - миграции базы данных.
- `Worker` - one-shot воркер ежедневной ротации mailbox.
- `UnitTests` - тесты.

## Жизненный цикл mailbox

- `RegisterUser` создает пользователя и подготавливает для него две записи mailbox: текущую и следующую.
- Текущий mailbox - это mailbox с `ExpiresDay = today + 6`.
- Поиск владельца mailbox считается активным, пока `today < expires_day`.
- `CompleteRealtimeAuth` возвращает 6 активных mailbox пользователя.
- Ежедневная ротация подготавливает следующий период mailbox и удаляет устаревшие данные.

## gRPC API

Локальный gRPC endpoint по умолчанию в Docker Compose: `https://localhost:${GRPC_PORT}`, где значение по умолчанию в `.env` равно `8443`.

Proto-файл: [Services/mailbox.proto](Services/mailbox.proto)

Сервис:

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

Создает пользователя и подготавливает начальные две записи mailbox в одной операции.

Пример запроса:

```json
{
  "userId": "alice",
  "authAlg": "Ed25519",
  "publicKey": "MCowBQYDK2VwAyEAJ665pMyVe5AIbj0f0jthwUnEuKPeWcgUI11epFjYwJ0="
}
```

Ответ:
- пустая полезная нагрузка

Типовые ошибки:
- `ALREADY_EXISTS` - пользователь уже существует.
- `INVALID_ARGUMENT` - `userId`, `authAlg` или `publicKey` пустые.

### GetMailbox

Пример запроса:

```json
{
  "userId": "alice"
}
```

Пример ответа:

```json
{
  "mailboxAddress": "11111111-2222-3333-4444-555555555555",
  "refreshAfterUtc": "2026-05-17T00:00:00Z"
}
```

Типовые ошибки:
- `NOT_FOUND` - пользователь не найден.
- `INVALID_ARGUMENT` - `userId` пустой.

### GetUser

Пример запроса:

```json
{
  "mailbox": "11111111-2222-3333-4444-555555555555"
}
```

Пример ответа:

```json
{
  "userId": "alice"
}
```

Типовые ошибки:
- `NOT_FOUND` - mailbox не найден или уже не активен.
- `INVALID_ARGUMENT` - `mailbox` не является валидным GUID.

## Realtime auth

`BeginRealtimeAuth` принимает `userId`, генерирует случайный nonce, сохраняет `rtauth:{nonce} -> user_id` в Redis на 60 секунд и возвращает:

```json
{
  "nonce": "base64-nonce",
  "expAt": "2026-05-23T12:34:56Z"
}
```

`CompleteRealtimeAuth` принимает:

```json
{
  "userId": "alice",
  "nonce": "base64-nonce",
  "alg": "Ed25519",
  "signature": "bytes"
}
```

Примечания:
- подписываемый бинарный payload - `"realtime-auth" || user_id || nonce`;
- поле `alg` в запросе обязательно;
- сохраненный `auth_alg` выбирает реализацию verifier-а, которую использует сервис;
- если nonce валиден, он потребляется через Redis `GETDEL`.

Успешный ответ:

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

Типовые ошибки:
- `NOT_FOUND` - пользователь не найден.
- `INVALID_ARGUMENT` - `userId`, `alg` или `nonce` некорректны, либо `signature` пустая.
- `UNAUTHENTICATED` - проверка подписи не прошла.
- `FAILED_PRECONDITION` - nonce отсутствует, истек, уже использован, сохраненный public key невалиден, либо сохраненный auth algorithm не поддерживается.

## gRPC health

Сервис отдает стандартный gRPC health service `grpc.health.v1.Health`.

## Заметки по хранению

- Postgres хранит пользователей в `Users(user, auth_alg, public_key)`.
- Redis хранит realtime auth challenge как `rtauth:{nonce} -> user_id` с TTL 60 секунд.
- Кэш mailbox прогревается лениво на путях mailbox lookup.

## Логирование

- `Services` и `Worker` пишут структурированные однострочные JSON-логи в stdout.
- Для удобной отправки в Loki структурированные поля пишутся прямо в события логов:
  - `Service`
  - `Instance`
  - `RequestId` для sanitized API error logs
- Логи не содержат пользовательские идентификаторы, mailbox, nonce, signature или public key.
- Raw exception в логи не пишется; логируется только безопасная метаинформация, например `ExceptionType`.

## Запуск через Docker Compose

Docker Compose поднимает:
- Postgres
- Redis
- Redis Insight
- `Migrator` для миграций базы
- `Services` для gRPC API

Текущий Compose не запускает `Worker`. Воркер предполагается запускать отдельно внешним scheduler.

### 1. Подготовить `.env`

Скопируйте [.env.example](.env.example) в `.env` и заполните обязательные значения:
- `POSTGRES_DB`
- `POSTGRES_USER`
- `POSTGRES_PASSWORD`
- `POSTGRES_PORT`
- `REDIS_PASSWORD`
- `REDIS_PORT`
- `REDIS_INSIGHT_PORT`
- `HTTPS_CERT_PASSWORD`
- `GRPC_PORT`

### 2. Подготовить сертификат для gRPC over HTTPS

Из корня репозитория:

```powershell
dotnet dev-certs https -ep .\certs\devcert.pfx -p changeit
```

Пароль должен совпадать со значением `HTTPS_CERT_PASSWORD` в `.env`.

### 3. Поднять сервисы

```powershell
docker compose up --build
```

Локальный endpoint по умолчанию после запуска:
- gRPC: `https://localhost:${GRPC_PORT}`, где `8443` используется как значение по умолчанию

## Проверка gRPC в Postman

1. Создайте `gRPC Request`.
2. Укажите сервер `https://localhost:${GRPC_PORT}`.
3. Импортируйте [Services/mailbox.proto](Services/mailbox.proto).
4. Выберите нужный метод `MailboxApi`.
5. Если используется self-signed сертификат, отключите `Enable server certificate verification`.

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
