# Cortex

ASP.NET Core backend for **[Cortex](https://github.com/IDjinn/Cortex)**, a mobile-first AI chat app with multi-provider LLM support. It aggregates hosted models through [OpenRouter](https://openrouter.ai) and talks directly to a local [Ollama](https://ollama.com) instance, exposing a streaming chat API (SSE) consumed by the Cortex mobile client (Expo/React Native).

## Features

- **Multi-provider chat** — OpenRouter (hosted models) and Ollama (local models) behind a single provider abstraction
- **Token streaming** — Server-Sent Events with `token`, `usage`, `done`, and `error` events
- **Guest mode** — anonymous, persistence-free chat restricted to local Ollama models
- **OAuth sign-in** — Google and GitHub; no passwords stored
- **JWT sessions** — short-lived access tokens with rotating refresh tokens (stored hashed)
- **Conversation history** — per-user conversations and messages with model and token-usage tracking
- **Model catalog** — cached model lists per provider with on-demand refresh

## Tech stack

| Area | Choice |
|---|---|
| Framework | ASP.NET Core 10 (Web API) |
| Database | PostgreSQL 17 + EF Core (Npgsql) |
| Auth | JWT bearer + OAuth (Google, GitHub) |
| LLMs | OpenRouter API, Ollama API (raw `HttpClient`, SSE/NDJSON streaming) |
| Runtime | Docker — API + Postgres via `compose.yaml` |

## Getting started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (version pinned by `global.json`)
- Docker (for PostgreSQL) — or a local PostgreSQL 17 with the `citext` and `uuid-ossp` extensions
- Optional: [Ollama](https://ollama.com) running on `http://localhost:11434`, for local models and guest chat

### 1. Start PostgreSQL

```bash
docker compose up -d postgres
```

This starts Postgres 17 on `localhost:5432` (user `cortex`, password `cortex_dev`, database `cortex`) and auto-creates the required extensions.

### 2. Apply migrations

```bash
dotnet ef database update --project Cortex.Core
```

### 3. Configure secrets

Real credentials live in user-secrets, never in `appsettings.json`:

```bash
cd Cortex.Core
dotnet user-secrets init
dotnet user-secrets set "Jwt:SigningKey" "<random string, at least 32 characters>"
dotnet user-secrets set "OAuth:Google:ClientId" "<client id>"
dotnet user-secrets set "OAuth:Google:ClientSecret" "<client secret>"
dotnet user-secrets set "OAuth:GitHub:ClientId" "<client id>"
dotnet user-secrets set "OAuth:GitHub:ClientSecret" "<client secret>"
dotnet user-secrets set "Providers:OpenRouter:ApiKey" "<openrouter api key>"
```

Note: OAuth providers only accept HTTPS callback URLs, so the backend exposes `/api/auth/{provider}/callback` itself and bounces the user-agent back to the mobile app's custom scheme (`cortex://auth/callback`).

### 4. Run

```bash
dotnet run --project Cortex.Core
```

The API listens on `http://localhost:5172`. Verify with:

```bash
curl http://localhost:5172/health
# {"status":"ok", ...}
```

From a physical device, use your machine's LAN IP and allow port 5172 through the firewall.

## API overview

| Method | Route | Auth | Description |
|---|---|---|---|
| GET | `/health` | — | Liveness probe |
| GET | `/api/auth/{provider}/login?redirectUri=` | — | Start the OAuth flow (`google` or `github`) |
| GET | `/api/auth/{provider}/callback` | — | OAuth callback; redirects back to the app with tokens |
| POST | `/api/auth/refresh` | — | Rotate the refresh token (`{ refreshToken }`) |
| POST | `/api/auth/logout` | — | Revoke a refresh token |
| GET | `/api/me` | Bearer | Current user profile |
| GET | `/api/conversations` | Bearer | List conversations |
| POST | `/api/conversations` | Bearer | Create a conversation (`title`, `provider`, `model`) |
| GET | `/api/conversations/{id}` | Bearer | Conversation detail including messages |
| PATCH | `/api/conversations/{id}` | Bearer | Update title / pinned state |
| DELETE | `/api/conversations/{id}` | Bearer | Delete a conversation |
| GET | `/api/models?provider=&refresh=` | — | Cached model catalog |
| POST | `/api/chat` | Bearer | SSE chat turn (`{ conversationId, content }`) |
| POST | `/api/chat/anonymous` | — | SSE guest chat — Ollama only, caller supplies the message history |

### Streaming events

Chat endpoints respond with `text/event-stream`; the event type is carried in the SSE `event:` field:

| Event | Payload | Meaning |
|---|---|---|
| `token` | `{ value }` | Incremental assistant token |
| `usage` | `{ tokensIn, tokensOut }` | Token counts (anonymous chat) |
| `done` | `{ tokensIn, tokensOut }` | Turn complete |
| `error` | `{ message }` | Turn failed |

Enums (e.g. `provider`, `role`) serialize as camelCase strings.

## Project layout

```
Cortex.Core/
├── Auth/         JWT issuance, refresh-token lifecycle, current user, options
├── Controllers/  API endpoints
├── Data/         AppDbContext (EF Core)
├── Dtos/         Request/response records (single file)
├── Migrations/   EF Core migrations
├── Objects/      Entities and enums
├── Providers/    LLM integrations — OpenRouter + Ollama behind IProvider
└── Services/     Chat orchestration, conversations, model cache, OAuth
```
