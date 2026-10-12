# Auth CSRF checkpoint

Status: PROPOSED CONTRACT ONLY. No CSRF endpoint, service registration or validation
has been enabled. Existing auth POST endpoints remain unprotected by antiforgery.
Jira: developer must assign a real key before committing.

## 1. Use cases and HTTP contract

- Auth login, refresh and logout require a matching antiforgery cookie/request-token
  pair before service execution. Include anonymous login to prevent login CSRF.
- Proposed GET /api/v1/auth/csrf: AllowAnonymous; 200 body
  `{ "data": { "requestToken": "opaque-antiforgery-proof" } }`.
- The GET stores the framework-owned lv_antiforgery cookie, HttpOnly, path /api/v1,
  SameSite=Lax and Secure outside Development. Do not set Domain.
- Return Cache-Control: no-store; never log either token or put it in a URL.
- Client sends X-CSRF-Token on protected POST requests with credentials enabled.
  Access JWT and refresh token remain HttpOnly and never appear in this DTO.
- Missing/invalid proof: 400 before auth mutation. Keep the framework response
  until a separately tested ProblemDetails mapping is agreed; do not promise a
  custom error code that is not implemented.
- Production cross-site deployment is NOT approved by this checkpoint. Current
  Lax cookie policy requires same-site deployment; HTTPS, proxy trust, CORS origins
  and any SameSite change require independent browser verification.

## 2. Ownership and FSD order

- Backend HTTP layer owns antiforgery issuance/validation; AuthService stays free
  of HttpContext and antiforgery responsibilities.
- FE entities/user: exact DTO + Zod envelope schema.
- FE features/auth: credentialed service call and request-token lifecycle.
- FE app: lifecycle coordination and cross-tab scheduling.
- Shared transport accepts callbacks/configuration; it must not import features.

## 3. File matrix and checkpoints

This checkpoint:
- CREATE src/LegacyVault.Api/Common/AuthCsrfContract.cs (constants and DTO only).
- CREATE docs/AUTH_CSRF_PLAN.md (this blueprint).

Next backend checkpoint, at most three files:
- MODIFY src/LegacyVault.Api/Extensions/ApiServiceExtensions.cs.
- MODIFY src/LegacyVault.Api/Controllers/V1/AuthController.cs.
- MODIFY tests/security/AuthHttpSqlChecks.cs.

Later frontend checkpoints, each at most three files:
- MODIFY entities/user authContract types, schema and schema tests.
- MODIFY features/auth service, service tests and consuming auth hooks.
- Add app-level coordination with its tests before enabling automatic refresh.

## 4. Developer blueprint and micro-tasks

TODO: [DEVELOPER BLUEPRINT]
1. [GOAL]: Validate framework antiforgery proof before cookie-auth mutations;
   maintain login/refresh/logout behavior and strict refresh replay protection.
2. [INPUT/OUTPUT]: Cookie + X-CSRF-Token -> validated request or HTTP 400;
   GET csrf -> ApiResponse<AuthCsrfTokenDto> + framework cookie.
3. [SEQUENCE]:
   - Register AddAntiforgery with the contract header/cookie constants. Inject
     IHostEnvironment through DI for Secure policy, not a manually built provider.
   - Inject IAntiforgery into AuthController. Add the anonymous GET, call
     GetAndStoreTokens and return its non-null RequestToken in OkData.
   - Add ValidateAntiForgeryToken to login, refresh and logout. These are MVC
     actions: use MVC filters, not an assumption that UseAntiforgery validates
     every JSON controller action. Retain current authorization/rate limits.
   - Adjust the HTTP fixture to acquire proof before each protected request;
     do not silently weaken current rate-budget or replay assertions.
   - FE validates the envelope, keeps proof in RAM and sends it only to the
     configured API. No token persistence or secret-bearing BroadcastChannel.
4. [APIS]: AddAntiforgery, IAntiforgery.GetAndStoreTokens,
   ValidateAntiForgeryTokenAttribute, existing OkData and credentialed Axios.
5. [EDGES/ERRORS]:
   - Request tokens are identity-bound. Fetch fresh proof after successful login,
     logout or account change and when the access JWT becomes invalid/expired.
     Never assume an authenticated token validates an anonymous refresh request.
   - Serialize auth transitions; a token fetched before another tab changes the
     auth cookie can be stale. Use a bounded reacquisition only for a proven
     pre-mutation CSRF rejection, never blindly retry refresh after network loss.
   - A refresh response may apply Set-Cookie even when its JS consumer is canceled.
     Coordinate refresh completion before logout; generation guards alone do not
     undo browser cookie updates. Preserve absolute expiry and strict replay.
   - Cover missing/mismatched cookie, invalid token, failed login, expired access,
     absent cookies, rate limiting and two-tab transitions. Never bypass validation
     when token acquisition fails. Never interpret CORS as CSRF authorization.
   - Antiforgery protects against CSRF, not XSS. Other cookie-auth mutation endpoints
     require a separate coverage audit; protecting auth alone is not global coverage.

## 5. Acceptance criteria

Automated, from the backend repository:

```powershell
dotnet build LegacyVault.sln --configuration Release --no-restore -m:1 --disable-build-servers
dotnet run --file tests/security/AuthHttpSqlChecks.cs --configuration Release --property:RestoreIgnoreFailedSources=true --property:NuGetAudit=false --disable-build-servers
```

The SQL-backed check requires the existing development-only
LEGACYVAULT_TEST_CONNECTION_STRING. Do not include credentials in commits/logs.

Required new assertions: issuance cookie flags/envelope/no-store; valid anonymous
login; valid authenticated logout; refresh with expired access and fresh anonymous
proof; missing/invalid/mismatched proof returns 400 without rotating/revoking tokens;
new proof after identity transition; existing replay/rate-limit checks still pass.

Manual browser scenarios: fresh login -> reload -> access expiry -> refresh ->
logout; repeat RememberMe both ways; parallel tabs; inspect preflight and headers;
attempt an untrusted-origin POST. Verify HTTPS Secure behavior on the actual deploy.
Do not claim browser or production acceptance from HttpClient-only checks.

## 6. Reference

Microsoft ASP.NET Core antiforgery guidance (identity-bound token refresh,
header transport and framework validation):
https://learn.microsoft.com/aspnet/core/security/anti-request-forgery

The framework also supports other SPA token transports; RAM/header transport here
is a project-specific choice, not a claim that Microsoft requires RAM storage.
