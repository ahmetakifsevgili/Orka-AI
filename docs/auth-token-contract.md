# Auth Token Contract

This document records the current auth token contract after PR 2 refresh-token hardening. The public API response shape remains compatible with PR 1; the storage and rotation internals are now stricter.

## Deployment Requirement

- Production and Staging must provide a strong `JWT:RefreshTokenHashSecret` or equivalent `RefreshTokenHashSecret`.
- The hash secret must be at least 32 bytes.
- Development may fall back to a derived local secret when no refresh-token hash secret is configured.
- Secret values must never be logged or returned in API responses.
- The PR 2 migration is destructive because it drops the legacy plaintext `RefreshTokens.Token` column.
- Take and verify a production DB backup before deployment. A code revert alone cannot restore dropped plaintext tokens; rollback requires DB restore or a rollback migration.

## Endpoints

### `POST /api/auth/register`

- Anonymous endpoint.
- Accepts `firstName`, `lastName`, optional legacy `name`, `email`, and `password`.
- Normalizes email to lowercase before creating the user.
- Requires a non-empty email and a password of at least 8 characters.
- On success, returns `201` with the existing public shape:
  - `token`
  - `jwt`
  - `access_token`
  - `refreshToken`
  - `refresh_token`
  - `user`
- Internally creates a refresh token family and stores only the HMAC-SHA256 token hash.
- Duplicate email currently returns `409`.

### `POST /api/auth/login`

- Anonymous endpoint.
- Accepts `email` and `password`.
- Normalizes email to lowercase before lookup.
- On success, returns `200` with the same token aliases and `user` payload as register.
- Internally starts a new refresh token family and stores only the token hash.
- Current wrong password behavior: returns `401`.
- Current unknown email behavior: returns `404`.
- Security note for a later PR: the different `401` and `404` responses still make account enumeration possible.

### `POST /api/auth/refresh`

- Anonymous endpoint.
- Accepts `refreshToken`.
- The submitted raw token is HMAC-SHA256 hashed and matched against `RefreshTokens.TokenHash`.
- Active token behavior:
  - old token is revoked with `RevokedReason = "Rotated"`;
  - old token stores `ReplacedByTokenHash`;
  - replacement token is inserted in the same `TokenFamilyId`;
  - response shape remains `token`, `jwt`, `access_token`, `refreshToken`, `refresh_token`.
- Reuse/replay behavior:
  - missing, expired, revoked, or invalid tokens return `401`;
  - reusing a rotated token is treated as replay;
  - replay revokes active tokens in the same token family with `RevokedReason = "ReplayDetected"`.
- Concurrency behavior:
  - refresh rotation uses a transaction where supported;
  - `RowVersion` is an EF concurrency token;
  - parallel use of the same refresh token should allow exactly one successful rotation.

### `POST /api/auth/logout`

- Accepts `refreshToken`.
- The submitted raw token is hashed and looked up by `TokenHash`.
- If the token exists, it is revoked with `RevokedReason = "Logout"`.
- If the token does not exist, the endpoint still returns success.
- Refreshing with a logged-out token returns `401`.

## Access Token Lifecycle

- Access tokens are JWTs signed with the configured JWT secret, or the development fallback secret when running in Development.
- Claims currently include:
  - user id as `sub`
  - email
  - `plan`
  - role claim
  - `isAdmin`
- Expiry is controlled by `JWT:AccessTokenExpiryMinutes`, defaulting to 60 minutes.

## Refresh Token Lifecycle

- Register and login each create a new refresh token family.
- Refresh rotates within the same family.
- Logout revokes the submitted refresh token when it exists.
- Refresh token expiry is controlled by `JWT:RefreshTokenExpiryDays`, defaulting to 30 days.
- DB storage contains only `TokenHash`; raw refresh tokens are returned to the client once and are not persisted.

## Migration Behavior

- Existing plaintext refresh tokens are forced out during the PR 2 migration.
- Existing access tokens may continue until their normal expiry.
- Existing refresh tokens issued before the migration cannot be refreshed after migration.
- Affected users must log in again when their current access token expires.

## Regression Coverage

- Successful login returns the same token aliases, refresh token aliases, and user data.
- Wrong password currently returns `401`.
- Unknown email currently returns `404`.
- Valid refresh returns a new access token and a new refresh token.
- Reusing a rotated refresh token returns `401` and revokes the token family.
- Logout followed by refresh with the same token returns `401`.
- DB storage is verified to contain only a 64-character SHA-256 refresh token hash, not the raw token.
- Parallel refresh with the same token is expected to produce exactly one success and one `401`.
- Production/Staging secret policy and Development fallback are covered by tests.
