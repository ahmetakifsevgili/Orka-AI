# Swagger Readiness

- Swagger index: `/swagger/index.html`.
- Swagger JSON: `/swagger/v1/swagger.json`.
- Routes discovered: 95.
- Controllers from source: 21.

Findings: route coverage is good, but Swagger does not consistently mark JWT security. Use `api-inventory.json` and `frontend-contract.md` as freeze source for auth, empty states, provider fallbacks, and readiness.
