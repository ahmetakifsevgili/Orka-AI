# Orka Contract Test Suite

Permanent core contract test suite for the Orka API.

## Design Principles
- **Strict Contracts:** Exact status codes and schema validation.
- **Port Governance:** Uses port 5101 by default via `ORKA_API_URL`.
- **Deterministic:** No AI/heavy dependencies.
- **Independent Setup:** Unique test data (emails/topics) per run.

## Setup
```bash
pip install pytest requests
```

## Running Tests
```bash
# Default (http://localhost:5101)
pytest contract_tests/

# Custom URL
$env:ORKA_API_URL="http://localhost:5101"
pytest contract_tests/
```

## Quarantine Policy
Do not add AI-dependent or non-deterministic tests (Korteks, DeepPlan, Chat Tutor) to this suite.
