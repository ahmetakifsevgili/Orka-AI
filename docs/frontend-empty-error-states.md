# Frontend Empty And Error States

- 200: success; empty arrays are valid.
- 201: register created.
- 400: validation/missing required input.
- 401: missing/invalid bearer.
- 403: role forbidden when used.
- 404: missing or foreign user-owned resource hidden.
- 409: conflict such as duplicate email.
- 413: upload/quota too large.
- 415: unsupported media type if introduced; current upload often returns 400.
- 429: provider/rate/quota if surfaced.
- 500: real app bug only.

Empty states: no topics `[]`; no wiki list `[]` and export 404; no sources `[]`; deleted source 404; no due review `[]`; no flashcards `[]`; no notifications `[]`; no badges `[]`; unknown audio 404; daily challenge no history returns fallback challenge.

Cross-user access should be 404/hidden unless a controller explicitly returns 403.
