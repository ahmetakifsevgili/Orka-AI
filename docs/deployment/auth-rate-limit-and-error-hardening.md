# Auth rate limiting and error leakage policy

## Auth rate limiter

Auth public endpoints (`login`, `register`, `refresh`) use `RateLimits:Auth` settings.

- `RateLimits:Auth:Backend=Auto` uses `InMemory` in Development and `Redis` outside Development.
- `RateLimits:Auth:Backend=Redis` is the required Staging/Production mode.
- `RateLimits:Auth:AllowInMemoryFallback=false` is the safe Staging/Production default.
- `AllowInMemoryFallback=true` outside Development is an emergency-only setting. It weakens multi-instance brute-force protection and must be time-boxed.

Redis limiter keys use hashed request partitions. Raw email, raw IP, access tokens, and refresh tokens must not be stored in rate-limit keys or logs.

## Error and logging behavior

Production/Staging client responses must not expose internal exception messages, provider config paths, stack traces, secrets, tokens, raw uploaded file names, or raw emails.

Development may keep more diagnostic detail so local debugging remains practical.

Provider configuration failures should be visible as operational failures without leaking config key names to clients. Production logs should use redacted structured fields rather than raw exception messages where possible.
