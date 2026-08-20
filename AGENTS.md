# Cortex — Backend

ASP.NET Core (.NET 10) backend for **Cortex**, an AI chat app with multi-provider LLM support: OpenRouter aggregation + direct connectors (OpenAI, Anthropic, Gemini, xAI, Mistral, DeepSeek) + local endpoints (Ollama, LM Studio, llama.cpp). Single project: `Cortex.Core`. OAuth-only auth (Google/GitHub) + JWT. PostgreSQL 17 via EF Core / Npgsql.

The companion **mobile client** (Expo SDK 56 / React Native) lives at `C:\dev\react-native\Cortex` locally and is published at https://github.com/IDjinn/Cortex. It has its own `AGENTS.md` and an OpenAPI spec at `docs/openapi.yaml`. The mobile app is the authoritative consumer of this API — see "API contract" below before changing DTOs or endpoints.

## Commands

```bash
dotnet build Cortex.sln                    # build
dotnet run --project Cortex.Core           # run (dev port 5172)
docker compose up                          # Postgres 17 + API (5172), builds Dockerfile
dotnet ef migrations add <Name> -p Cortex.Core   # add migration
dotnet ef database update -p Cortex.Core   # apply migrations
```

- SDK pinned by `global.json` (10.0.201, no prereleases).
- Migrations are auto-applied on startup (`Database.MigrateAsync()` early in `Program.cs`) — no manual `database update` needed after adding one.
- Ollama is expected already running at `http://localhost:11434` (not part of compose).
- No test project exists. No linter — compile is the only check.

## Architecture

```
Controllers/   API surface (route prefixes: api/auth, api/chat, api/chat/anonymous, api/conversations, api/conversations/import, api/keys, api/me, api/models, api/providers, api/usage)
Services/      Business logic (ChatService orchestrates a full turn: persist user msg → stream with fallback → finalize + cost)
Providers/     LLM integrations — raw HttpClient with hand-rolled SSE/NDJSON parsing, NOT OpenAI SDKs
Data/          AppDbContext (EF Core)
Objects/       EF entities + enums (ChatProviderKind, MessageRole, AuthProvider)
Dtos/          ALL request/response records in a single file: Dtos/Dtos.cs
Auth/          AuthService (JWT + refresh rotation), CurrentUser, SecretProtector (Data Protection), all options POCOs
Migrations/    EF migrations
```

- Streaming is hand-rolled SSE over `Response.Body` (no SignalR). Use `IAsyncEnumerable` + `[EnumeratorCancellation]`.
- `ModelService` is a singleton with a 10-min `IMemoryCache` per provider (`refresh=true` bypasses; any `ProviderCallContext` with a key/baseUrl bypasses too — per-user data must not leak through the shared cache entry).
- **Providers**: `OpenAiCompatibleProvider` is the base for OpenRouter/OpenAI/xAI/Mistral/DeepSeek/LM Studio (SSE `data:` lines, `[DONE]` sentinel, `delta.tool_calls` accumulation → normalized `ChatChunk.ToolCall`). `AnthropicProvider` (typed SSE events, `tool_use` blocks) and `GeminiProvider` (`streamGenerateContent?alt=sse`, `functionCall` parts) have their own parsers. Adding a provider: enum member + `ProviderFactory.Get` + `ProviderOptions.For` + named HttpClient + singleton registration in `Program.cs`. Static capability/pricing tables for direct connectors live in `Providers/ModelCatalog.cs` (review periodically).
- **BYOK resolution** (in this order): `X-Provider-Key` request header → user vault (`provider_keys` table, encrypted via Data Protection `ISecretProtector`; set `DataProtection:KeyRingPath` in production) → server-configured key (`Providers:*:ApiKey`; `REPLACE_*` placeholders are treated as absent via `KeyConfigured`). The header key belongs to the conversation's primary provider and is never reused for the routing fallback.
- **Routing fallback**: a conversation may carry `FallbackProvider`/`FallbackModel` (empty string = cleared). `ChatService` retries with the fallback only when the primary fails **before any token**; mid-stream failures surface as errors. A `notice` SSE event tells the client the reserve was used.
- **Cost**: `Message.Cost` (USD, `numeric(18,6)`) is computed at finalize from `ModelService` prices; `GET /api/usage?month=yyyy-MM` aggregates per provider.

## API contract with the mobile app (critical)

When changing any DTO or endpoint shape, the mobile app **must be updated in tandem**:

- `C:\dev\react-native\Cortex\api\types.ts` mirrors `Cortex.Core/Dtos/Dtos.cs` — keep both in sync.
- Enums serialize as **camelCase strings** (`JsonStringEnumConverter`) — the client depends on this.
- SSE event discriminator lives in the **`event:` header** (`user` / `assistant` / `token` / `toolCall` / `notice` / `usage` / `done` / `error`), not in the JSON payload. Client ignores `: heartbeat` comment lines. `done` carries `{tokensIn, tokensOut, provider, model, costUsd}`.
- Authed chat: `POST /api/chat` with `conversationId` in the **body** (not the URL path); optional `X-Provider-Key` header (BYOK). Anonymous chat: `POST /api/chat/anonymous`, no auth, full `messages` history supplied by caller — local providers (Ollama, LmStudio) always allowed, remote providers require the caller's own `X-Provider-Key` (403 otherwise); `baseUrl` body field overrides local endpoints only; per-IP rate limit (30 req/min).
- `docs/openapi.yaml` in the mobile repo is the current spec (regenerated for Phase 2).

## Auth

- No local passwords. OAuth (Google, GitHub) → HS256 JWT access token (15 min) + rotating refresh token (30 days, stored as SHA-256 hash only, revoked + reissued on refresh).
- CORS allows Expo dev origins `http://localhost:8081` / `exp://localhost:8081` (config `Cors:AllowedOrigins`).
- OAuth callback redirects to the app's custom scheme `cortex://auth/callback` with `?data=<json>`.

## Database

- Npgsql; snake_case tables; enums stored as strings; `timestamptz` everywhere (`DateTimeOffset`).
- `users.email` is **`citext`** (case-insensitive unique) — the extension is created by `postgres/init/01-extensions.sql`, mounted into the compose container. A local non-Docker Postgres needs that script applied too.
- Cascade deletes on FKs; conversations auto-title from the first user message (60-char truncation, default title `"Nova conversa"`).

## Secrets — warning

`appsettings.json` currently contains real-looking credentials (GitHub OAuth client secret, OpenRouter API key). **Never echo, log, or commit new secrets there** — use user-secrets or environment variables. `Jwt:SigningKey` is already a `REPLACE_IN_USER_SECRETS` placeholder.

## Conventions

- `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`, file-scoped namespaces.
- Records for DTOs/events/chunks; sealed nested-record hierarchies as discriminated unions (`ChatChunk`, `ChatTurnEvent`).
- Constructor injection; interface + implementation in the same file; `_camelCase` private fields.
- `CancellationToken ct` passed through all async paths.
- Note: `Cortex.Core.http` is stale template boilerplate (leftover weatherforecast request).
