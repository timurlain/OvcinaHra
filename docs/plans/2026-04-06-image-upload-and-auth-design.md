# Image Upload & Shared Auth — Design

> **Date**: 2026-04-06 | **Status**: Approved

## 1. Image Upload

### Storage

Azure Blob Storage, single container `ovcinahra-images`. Blobs organized by entity type and ID:

```
locations/{id}/image.jpg
locations/{id}/placement.jpg
secretstashes/{id}/image.jpg
items/{id}/image.jpg
monsters/{id}/image.jpg
quests/{id}/image.jpg
```

Entity fields store blob keys (e.g. `locations/14/image.jpg`), never full URLs. The API resolves keys to time-limited SAS URLs on read.

### Local Development

Azurite (Azure Storage Emulator) in Docker, added to existing `docker-compose.yml`. Connection string: `UseDevelopmentStorage=true`. No code changes between local and production — same SDK, same API.

### API Endpoints

- `POST /api/images/{entityType}/{entityId}` — multipart file upload. Stores blob, updates entity's `ImagePath` in DB. For Location's second field: `?field=placement`.
- `GET /api/images/{entityType}/{entityId}` — returns JSON with SAS URL(s). For Location: both `imageUrl` and `placementUrl` if present.
- `DELETE /api/images/{entityType}/{entityId}` — removes blob and clears `ImagePath`. Optional `?field=placement`.

### Validation

- Max file size: 5 MB
- Allowed types: JPEG, PNG, WebP (validated by content type + magic bytes)
- Server-side only — WASM client can show a friendly error but the API is the gate

### Client Integration

Each entity edit popup gets a file picker section:
- Upload button → picks file → `POST /api/images/...` → preview refreshes
- Preview shows the image via SAS URL from `GET /api/images/...`
- Delete button (small X) to remove the image

### Entities with Images

| Entity | Fields | Notes |
|---|---|---|
| Location | `ImagePath`, `PlacementPhotoPath` | Fantasy illustration + real placement photo |
| SecretStash | `ImagePath` | Clue illustration shown to players |
| Item | `ImagePath` | Item illustration |
| Monster | `ImagePath` | Monster illustration |
| Quest | — | No image field in current model |

### Blob Service

`IBlobStorageService` with two implementations:
- `AzureBlobStorageService` — production, uses `Azure.Storage.Blobs` SDK
- Both local (Azurite) and prod use the same implementation, just different connection strings

## 2. Authentication — Shared External Providers (Approach B)

### Overview

OvčinaHra uses the same OAuth2 providers as registrace-ovčina (Google, Microsoft, Seznam). After external auth succeeds, OvčinaHra verifies the user exists in registrace via its Integration API, then issues a JWT.

### Auth Flow

```
User clicks "Přihlásit se přes Google"
  → Browser redirects to /api/auth/login/google
    → ASP.NET OAuth middleware → Google login
      → Callback: /api/auth/callback
        → API calls registrace: GET /api/v1/users/by-email?email=user@example.com
          → registrace returns { exists: true, displayName: "Jan Novák", roles: ["Organizer"] }
        → API issues JWT with claims (email, name, role)
        → Redirect to WASM client with token in URL fragment
          → Client stores JWT in localStorage, sets Bearer header
```

### Registrace Integration API Addition

One new endpoint needed in registrace-ovčina:

```
GET /api/v1/users/by-email?email=user@example.com
Authorization: X-Api-Key: {configured key}

Response 200:
{ "exists": true, "displayName": "Jan Novák", "roles": ["Organizer"] }

Response 200 (not found):
{ "exists": false }
```

Protected by the existing `ApiKeyEndpointFilter`.

### OvčinaHra API Changes

- Add OAuth2 middleware for Google, Microsoft, Seznam (same pattern as registrace)
- Server-side login endpoints: `GET /api/auth/login/{provider}` — initiates OAuth flow
- Callback endpoint: `GET /api/auth/callback` — handles OAuth callback, verifies with registrace, issues JWT
- Keep `POST /api/auth/dev-token` for local development (behind `IsDevelopment` check)
- Keep `POST /api/auth/refresh` unchanged

### OvčinaHra Client Changes

- Replace login page: show provider buttons (Google, Microsoft, Seznam) instead of dev-token form
- Each button navigates to `/api/auth/login/{provider}` (full page redirect, not AJAX)
- After callback redirect, client extracts token from URL fragment and stores it
- Dev mode: keep dev-token button visible alongside provider buttons

### Configuration

```json
{
  "ExternalAuth": {
    "Google": { "ClientId": "...", "ClientSecret": "..." },
    "Microsoft": { "ClientId": "...", "ClientSecret": "..." },
    "Seznam": { "ClientId": "...", "ClientSecret": "..." }
  },
  "Registrace": {
    "BaseUrl": "https://registrace.ovcina.cz",
    "ApiKey": "..."
  }
}
```

Providers are opt-in: only enabled if both `ClientId` and `ClientSecret` are non-empty (same pattern as registrace).

### Security

- JWT signing key: same as current (`Jwt:Key` in config)
- OAuth state parameter: ASP.NET middleware handles CSRF protection
- Registrace API key: stored in Azure Key Vault in production, user secrets locally
- Token lifetime: 60 minutes (current), refresh within 7-day grace window (current)

### Roles

- All verified users get `Organizer` role (this is an organizer-only app)
- If registrace reports the user doesn't exist → login rejected, show "Nemáš registraci na ovcina.cz"
- Admin role: reserved for future use, granted manually via DB
