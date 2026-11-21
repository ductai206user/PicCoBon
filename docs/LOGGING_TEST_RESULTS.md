# SPIKE OS — Logging Verification & Test Results

**Verification Date:** 2026-11-21  
**Branch:** `feat/paypal-sandbox`  
**Isolated Live Verification Target:** Port `5397`, isolated SQLEXPRESS database `PickleballClub_LogLive_*` (cleaned up & dropped after run)

---

## 1. Automated Build & Test Summary

| Check | Command | Outcome |
| :--- | :--- | :--- |
| **Solution Build (Release)** | `dotnet build PickleballClub.Tests.csproj -c Release --no-restore --nologo` | **0 Warnings, 0 Errors** (reconfirmed inside `dotnet test`) |
| **Full xUnit Regression + Logging Suite** | `dotnet test e:\pickleball\PickleballClub.Tests\PickleballClub.Tests.csproj -c Release --nologo` | **65 / 65 Passed** (Failed: 0, Skipped: 0, Total: 65, Duration: 39 s) |

### Automated Logging Tests Added (`PickleballClub.Tests/LoggingAndDiagnosticsTests.cs`)
1. `L01_CleanSpikeConsoleFormatter_FormatsCompactSingleLineWithCategoryAndCorrelation` — Passed (verifies `HH:mm:ss  WARN   PayPal         ... [cid:...]` layout).
2. `L01b_CleanSpikeConsoleFormatter_FormatsExceptionDetailsAndCategoryShortNames` — Passed (verifies domain short categories `SpikeOS`, `Database`, `Booking`, `ProShop`, `PayPal`, `QrCheckIn`, `Auth` and clean exception output).
3. `L02_CorrelationContext_SanitizesInvalidHeadersAndMasksPii` — Passed (verifies header trimming, CRLF log-injection rejection, and identifier masking `us***@example.com`, `09***89`, `***`).
4. `L03_EnvironmentConfigFiles_ConfigureSafeDevelopmentSqlDebugAndProduction` — Passed (verifies `Development` suppresses EF/HttpClient Info logs with `spike-clean`, `SqlDebug` enables `Database.Command = Information`, and `Production` selects `json` with `Warning` SQL filter).
5. `L04` (`LoggingPipelineIntegrationTests`) — Passed against real SQL Server (`WebApplicationFactory<Program>`):
   - Verified `/api/db/state` triggers **0** `Information`-level `Microsoft.EntityFrameworkCore.Database.Command` logs while rejecting invalid inbound `X-Correlation-Id` (`"invalid$char!12"`) and issuing `spk-*`.
   - Verified authentication failure (`InvalidPassword`) logs a `Warning` without leaking password strings.
   - Verified end-to-end correlated flow (`Booking` -> `PayPal` order create -> `PaymentController` redirect log -> `PayPal` capture completed) shares one `X-Correlation-Id` (`trace-paypal-e2e-01`).
   - Verified Pro Shop stock conflict (`insufficient stock`) and QR validation rejection (`SignatureMismatch`) warnings.
   - Verified real `SqlException` (`SELECT COUNT(*) FROM dbo.NonExistentLogAuditTable_999`) still logs `LogLevel.Error` from `Microsoft.EntityFrameworkCore.Database.Command`.
   - Verified zero leaks of `SECRET_SHOULD_NEVER_BE_LOGGED_12345`, passwords, full QR signatures, or `ApproveUrl` tokens.

### Defect Caught & Fixed During Verification
- **Test Issue in `L04`:** Initial integration test sent `"bad\r\nheader"` via `HttpRequestMessage.Headers.Add(...)`, which .NET client `HttpHeaders` rejects client-side with `FormatException` before sending the request. Updated `L04` to send `"invalid$char!12"` (proving server-side `CorrelationContext.ResolveSafeId` regex enforcement over HTTP), while `L02` unit test covers `ResolveSafeId("evil\r\n...")` directly. Re-running full suite passed **65 / 65**.

---

## 2. Live Application Console Capture Verification (Isolated SQLEXPRESS + Port `5397`)

Measured counts across three real process launches of `e:\pickleball\bin\Release\net10.0\PickleballClub.dll`:

| Metric | Run A (`Development`) | Run B (`--sql-debug`) | Run C (`Production`) |
| :--- | :---: | :---: | :---: |
| **`Executed DbCommand` SQL Logs** | **0** | **8** | **0** |
| **Sensitive Value Leaks (Password / SigningKey)** | **False** | **False** | **False** |
| **Standard Error Output (`stderr`)** | **0 bytes** | **0 bytes** | **0 bytes** |

### Run A: Actual `Development` Console Output (Observed Verbatim)
Requests executed: `/`, `/Booking`, `/Product`, `/Admin/Login`, `/api/db/state`, `/Payment/PayPalReturn?state=tampered&token=FAKEORDER123`, `/health`, and `POST /api/auth/login`.
```text
21:52:07  INFO   Program        Spike OS initialized (Environment=Development, SqlDebug=False)
21:52:09  WARN   Database       The query uses the 'First'/'FirstOrDefault' operator without 'OrderBy' and filter operators. This may lead to unpredictable results.
21:52:09  INFO   PayPal         PayPal gateway ready (Env=Sandbox, Currency=USD, Rate=26000)
21:52:09  INFO   SpikeOS        Now listening on: http://127.0.0.1:5397
21:52:09  INFO   SpikeOS        Application started. Press Ctrl+C to shut down.
21:52:09  INFO   SpikeOS        Hosting environment: Development
21:52:09  INFO   SpikeOS        Content root path: E:\pickleball
21:52:09  WARN   AspNetCore     Failed to determine the https port for redirect. [cid:spk-9da03a104964]
21:52:11  WARN   Payments       PayPal return rejected: invalid or expired state signature [cid:live-dev-check01]
21:52:12  WARN   Security       CSRF token validation failed on POST /api/auth/login [cid:live-dev-auth01]
```

### Run B: Actual `--sql-debug` Console Output Excerpt (Observed Verbatim)
Confirms EF Core SQL diagnostics are cleanly restored on demand:
```text
21:52:13  INFO   Program        Spike OS initialized (Environment=Development, SqlDebug=True)
21:52:14  INFO   Database       Executed DbCommand (28ms) [Parameters=[], CommandType='Text', CommandTimeout='30']
SELECT 1
...
21:52:15  INFO   Database       Executed DbCommand (1ms) [Parameters=[], CommandType='Text', CommandTimeout='30']
SELECT [t].[CourtId], [t].[Alias], [t].[BasePrice], [t].[CourtName], [t].[CourtType], [t].[Description], [t].[Image], [t].[IsActive], [t].[Surcharge], [t].[Surface], [t].[TypeLabel]
FROM [tb_Court] AS [t]
WHERE [t].[IsActive] = CAST(1 AS bit)
ORDER BY [t].[CourtId] [cid:live-sqldebug-01]
```

### Run C: Actual `Production` JSON Console Excerpt (Observed Verbatim)
Confirms single-line UTC JSON formatting, correlation scopes, and honest unconfigured PayPal diagnostic:
```json
{"Timestamp":"2026-11-21T14:52:16.931Z","EventId":0,"LogLevel":"Information","Category":"Program","Message":"Spike OS initialized (Environment=Production, SqlDebug=False)","State":{"Environment":"Production","SqlDebug":false,"{OriginalFormat}":"Spike OS initialized (Environment={Environment}, SqlDebug={SqlDebug})"},"Scopes":[]}
{"Timestamp":"2026-11-21T14:52:18.066Z","EventId":0,"LogLevel":"Warning","Category":"PickleballClub.Services.PayPalPaymentService","Message":"PayPal gateway inactive at startup: Code=PAYPAL_CREDENTIALS_MISSING","State":{"ErrorCode":"PAYPAL_CREDENTIALS_MISSING","{OriginalFormat}":"PayPal gateway inactive at startup: Code={ErrorCode}"},"Scopes":[]}
```
*(Note: `GET /` over plain HTTP on `127.0.0.1:5397` in Production intentionally triggered the Antiforgery SSL check, which was cleanly logged by `SpikeOS.Request` in JSON format with `CorrelationId: "live-prod-check01"` and zero secret leaks.)*
