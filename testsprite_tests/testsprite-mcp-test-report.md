# TestSprite AI Testing Report(MCP)

---

## 1️⃣ Document Metadata
- **Project Name:** Orka
- **Date:** 2026-04-25
- **Prepared by:** TestSprite AI Team / Antigravity

---

## 2️⃣ Requirement Validation Summary

### Requirement: User Registration API (`/api/auth/register`)

#### Test TC001 postapiauthregisterwithvaliddata
- **Test Code:** [TC001_postapiauthregisterwithvaliddata.py](./TC001_postapiauthregisterwithvaliddata.py)
- **Test Error:** `AssertionError: Expected 201 Created, got 200`
- **Test Visualization and Result:** https://www.testsprite.com/dashboard/mcp/tests/811aa690-eee0-4456-b589-2156c260084d/7381c03f-737a-4cb8-ab63-b436e771736c
- **Status:** ❌ Failed
- **Analysis / Findings:** The API currently returns a `200 OK` upon successful registration instead of the REST standard `201 Created`. This is a status code mismatch in `AuthController.cs`.
---

#### Test TC002 postapiauthregisterwithexistingemail
- **Test Code:** [TC002_postapiauthregisterwithexistingemail.py](./TC002_postapiauthregisterwithexistingemail.py)
- **Test Error:** `AssertionError`
- **Test Visualization and Result:** https://www.testsprite.com/dashboard/mcp/tests/811aa690-eee0-4456-b589-2156c260084d/0e8c233b-d8c6-4b37-a1ea-8a576da49cbe
- **Status:** ❌ Failed
- **Analysis / Findings:** The registration endpoint throws a generic `Exception` which maps to `400 Bad Request` in the controller, rather than the expected `409 Conflict` or standard validation error format.
---

### Requirement: User Authentication & Login (`/api/auth/login`)

#### Test TC003 postapiauthloginwithvalidcredentials
- **Test Code:** [TC003_postapiauthloginwithvalidcredentials.py](./TC003_postapiauthloginwithvalidcredentials.py)
- **Test Error:** `AssertionError: Expected status code 200, got 404`
- **Test Visualization and Result:** https://www.testsprite.com/dashboard/mcp/tests/811aa690-eee0-4456-b589-2156c260084d/c79fb1d8-3a8e-4d8c-a40f-bc8d76285bef
- **Status:** ❌ Failed
- **Analysis / Findings:** The prerequisite registration failed or database state wasn't persisted, so the user login resulted in `404 Not Found`.
---

#### Test TC004 postapiauthloginwithinvalidcredentials
- **Test Code:** [TC004_postapiauthloginwithinvalidcredentials.py](./TC004_postapiauthloginwithinvalidcredentials.py)
- **Test Error:** `AssertionError: Expected 401 Unauthorized, got 404`
- **Test Visualization and Result:** https://www.testsprite.com/dashboard/mcp/tests/811aa690-eee0-4456-b589-2156c260084d/ab4ed79e-a134-4b06-915f-73d8022e87d4
- **Status:** ❌ Failed
- **Analysis / Findings:** The codebase is designed to return `404 Not Found` when a user does not exist (catching `NotFoundException`). The test script expects a standard `401 Unauthorized` for any invalid credentials.
---

### Requirement: Token Management & Logout (`/api/auth/refresh`, `/api/auth/logout`)

#### Test TC005 postapiauthrefreshwithvalidrefreshtoken
- **Test Code:** [TC005_postapiauthrefreshwithvalidrefreshtoken.py](./TC005_postapiauthrefreshwithvalidrefreshtoken.py)
- **Test Error:** `AssertionError: Login failed, status code: 404`
- **Test Visualization and Result:** https://www.testsprite.com/dashboard/mcp/tests/811aa690-eee0-4456-b589-2156c260084d/a0fa0108-1bd4-44d6-bf4e-5e08deddcdeb
- **Status:** ❌ Failed
- **Analysis / Findings:** Cannot execute properly because the prerequisite login step failed (due to the 404 issue).
---

#### Test TC006 postapiauthrefreshwithexpiredorrevokedtoken
- **Test Code:** [TC006_postapiauthrefreshwithexpiredorrevokedtoken.py](./TC006_postapiauthrefreshwithexpiredorrevokedtoken.py)
- **Test Visualization and Result:** https://www.testsprite.com/dashboard/mcp/tests/811aa690-eee0-4456-b589-2156c260084d/c2886087-ed4d-4a21-a012-bf69615e45b9
- **Status:** ✅ Passed
- **Analysis / Findings:** The API successfully rejected the invalid/expired token.
---

#### Test TC007 postapiauthlogoutwithvalidrefreshtoken
- **Test Code:** [TC007_postapiauthlogoutwithvalidrefreshtoken.py](./TC007_postapiauthlogoutwithvalidrefreshtoken.py)
- **Test Error:** `AssertionError: Expected 200 OK from login, got 404`
- **Test Visualization and Result:** https://www.testsprite.com/dashboard/mcp/tests/811aa690-eee0-4456-b589-2156c260084d/9dce7d8a-636a-4087-b3b0-6799472b044a
- **Status:** ❌ Failed
- **Analysis / Findings:** Blocked by the underlying login flow failing (`404` error instead of `200`).
---

#### Test TC008 postapiauthlogoutwithoutrefreshtoken
- **Test Code:** [TC008_postapiauthlogoutwithoutrefreshtoken.py](./TC008_postapiauthlogoutwithoutrefreshtoken.py)
- **Test Error:** `AssertionError: Expected 401 Unauthorized, got 400`
- **Test Visualization and Result:** https://www.testsprite.com/dashboard/mcp/tests/811aa690-eee0-4456-b589-2156c260084d/d6739132-455f-4fd1-880f-6527081d088c
- **Status:** ❌ Failed
- **Analysis / Findings:** Missing body params result in ASP.NET Core model validation returning `400 Bad Request` instead of `401 Unauthorized`.
---


## 3️⃣ Coverage & Matching Metrics

- **12.50%** of tests passed

| Requirement                           | Total Tests | ✅ Passed | ❌ Failed  |
|---------------------------------------|-------------|-----------|------------|
| User Registration API                 | 2           | 0         | 2          |
| User Authentication & Login           | 2           | 0         | 2          |
| Token Management & Logout             | 4           | 1         | 3          |
---


## 4️⃣ Key Gaps / Risks
1. **HTTP Status Code Deviations:** The API returns `200 OK` for creation (instead of `201`), `404 Not Found` for invalid email login (instead of `401`), and generic `400 Bad Request` where `401` or `409` might be more semantically appropriate. Test cases are strictly looking for standard REST status codes.
2. **State Persistence Issue:** Tests dependent on state (like login after register, or refresh after login) are failing because the underlying prerequisite steps aren't completing successfully.
3. **Information Disclosure Risk:** Returning `404 Not Found` on login reveals whether an email exists in the system or not, which is a mild security risk compared to returning a generic `401 Unauthorized`.
---
