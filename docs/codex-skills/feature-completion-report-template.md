# Feature Completion Report Template

Use this structure at the end of feature implementation.

```markdown
## Feature Completion Report

### Summary
- What changed:
- User-visible behavior:
- Runtime/API behavior:

### Changed Files
- ...

### Constitution Checks
- Backend Feature Constitution:
- AI/RAG Feature Constitution:
- Frontend Contract Constitution:
- Data Lifecycle Constitution:
- Testing Gate Constitution:

### Contract / Lifecycle / Migration Impact
- API response shape:
- Frontend types/client:
- Ownership/cross-user boundary:
- Redis/cache/session data:
- Account/topic/source/session delete:
- Migration/deploy notes:

### Tests
- Passed:
- Not run:
- Failed or flaky:

### Remaining Risks
- Must fix now:
- Backlog after current feature:
- Production/enterprise later:

### Git
- Stage/commit performed: No, unless the user explicitly requested it.
```
