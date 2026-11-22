# SPIKE OS — Logging System Implementation

**Branch:** `feat/paypal-sandbox`  
**Formatter:** `CleanSpikeConsoleFormatter` (`"spike-clean"`)  
**Approach:** Built-in ASP.NET Core `Microsoft.Extensions.Logging` + `ConsoleFormatter` + `AsyncLocal<string?>` (zero external logging packages)

---

## 1. Architecture & Components Added

### 1.1 `Logging/CorrelationContext.cs`
Provides lightweight request and background-worker correlation propagation plus safe formatting helpers:
- **Header:** `X-Correlation-Id` (`CorrelationContext.HeaderName`)
- **Validation:** Inbound client correlation headers are accepted only if matching `^[A-Za-z0-9_-]{8,48}$` (`CorrelationContext.ResolveSafeId`). Forged/malformed or control-character-injected headers are rejected and replaced with a generated `spk-<12-hex>` ID (`CorrelationContext.CreateNewId`).
- **AsyncLocal Context:** `EnsureActive()`, `Push(string)`, and `BeginWorkerCycle(ILogger, string)` (which generates `wrk-<12-hex>` and attaches a structured logger scope with `CorrelationId` and `Worker`).
- **Sanitization & Masking:**
  - `MaskIdentifier(string?)` masks emails (`us***@example.com`), usernames/phones (`09***89`), and short strings (`***`).
  - `StripControlChars(string?, int maxLength = 200)` strips `\r`, `\n`, `\t` and truncates overly long error fragments.

### 1.2 `Logging/CleanSpikeConsoleFormatter.cs`
Implements `ConsoleFormatter` (`FormatterName = "spike-clean"`) with `CleanSpikeConsoleFormatterOptions`:
- **Format:**
  ```text
  HH:mm:ss  LEVEL  Category       Message [cid:<correlation-id>]
  ```
- **ANSI Color Support:** Level badges (`TRACE`, `DEBUG`, `INFO `, `WARN `, `ERROR`, `CRIT `), muted timestamps, cyan domain categories, and magenta `[cid:...]` tags. Automatically honors `ColorBehavior` (`Default` / `Enabled` / `Disabled`), terminal redirection (`Console.IsOutputRedirected`), and the `NO_COLOR` environment variable.
- **Readable Category Mapping (`FormatCategoryName`):**
  - `PickleballClub.Program`, `Microsoft.Hosting.Lifetime`, `SpikeOS.*` -> `SpikeOS`
  - `BookingService`, `BookingLifecycleWorker`, `BookingController`, `BookingsController` -> `Booking`
  - `*PayPal*` -> `PayPal`
  - `PaymentController` -> `Payments`
  - `OrderService`, `ProductController`, `ProductsController` -> `ProShop`
  - `*QrToken*` -> `QrCheckIn`
  - `AccountController`, `LoginController`, `RegisterController` -> `Auth`
  - `*.Areas.Admin.*` -> `Admin`
  - `ApiDbController` -> `Api`
  - `Microsoft.EntityFrameworkCore.*` -> `Database`
  - `Microsoft.AspNetCore.*` -> `AspNetCore`
- **Exception Output:** Appends indented exception type/message/stack trace lines cleanly beneath the log line (`           -> ...`).

---

## 2. Pipeline & Domain Instrumentation

### 2.1 Pipeline, Security & Startup (`Program.cs`)
- Registers `CleanSpikeConsoleFormatter` (`spike-clean`) and binds logging options.
- Loads opt-in `appsettings.SqlDebug.json` only when `!IsProduction()` and either `IsEnvironment("SqlDebug")`, `--sql-debug` CLI flag, or `SPIKE_SQL_DEBUG=true`.
- Adds Correlation middleware reading `X-Correlation-Id`, resolving a safe ID via `CorrelationContext.ResolveSafeId`, pushing `AsyncLocal` context + `ILogger.BeginScope`, echoing `X-Correlation-Id` on responses, and logging unhandled exceptions under `SpikeOS.Request`.
- Logs structured `Warning` diagnostics on security events:
  - Cookie `OnValidatePrincipal` revocations
  - `401` / `403` redirects
  - Rate limiter `429` rejections
  - CSRF validation failures (`Security`)
- Runs genuine non-fake startup checks when `!IsEnvironment("Testing")`: verifies SQL connectivity / schema presence and logs honest PayPal gateway readiness (`PayPal gateway ready ...` vs `PayPal gateway inactive at startup: Code=...`).

### 2.2 Background Workers
- `Services/BookingLifecycleWorker.cs`: wraps each sweep cycle with `CorrelationContext.BeginWorkerCycle(...)`.
- `Services/PayPalReconciliationWorker.cs`: wraps each reconciliation sweep with `CorrelationContext.BeginWorkerCycle(...)`.

### 2.3 Services & Controllers (`ILogger<T>?` Backward-Compatible Injection)
Every service preserves backwards compatibility with existing manual test constructors via `ILogger<T>? logger = null` defaulting to `NullLogger<T>.Instance`:
- `Services/BookingService.cs`: booking create, approve, hold expiration, check-in completion & rejection reasons, cancel/reject, duration extension, court move, POS item addition, session finish, full/partial payment recording, idempotency replay/conflict, refund, and lifecycle reconciliation (`expired` / `no_show`).
- `Services/PayPalPaymentService.cs` & `Services/PayPalClient.cs`: PayPal attempt creation, order creation, failures/timeouts, cancel/refund failures, webhook verification, authorization denials (`403`), replay, capture initiated/completed (with LedgerTxId) / failed / amount mismatch / approval required, eligibility-loss compensation refunds, and finalized refunds.
- `Services/OrderService.cs`: Pro Shop order creation, insufficient stock conflicts, idempotency replay/conflict, standalone full/partial payment, refund, fulfill, cancel & restock.
- `Services/QrTokenService.cs`: Critical startup diagnostic on invalid production key, and structured warnings on invalid/expired/signature-mismatched QR tokens.
- `Controllers/PaymentController.cs`, `Controllers/ApiDbController.cs`, `Controllers/AccountController.cs`, `Areas/Admin/Controllers/LoginController.cs`, `Areas/Admin/Controllers/AccountsController.cs`, `Areas/Admin/Controllers/CourtsController.cs`, `Areas/Admin/Controllers/ProductsController.cs`: auth success/failure (with `CorrelationContext.MaskIdentifier`), admin CRUD/state mutations, scanner decode failures, and PayPal return validation rejections.

---

## 3. Privacy & Security Safeguards Verified

- `EnableSensitiveDataLogging()` and `EnableDetailedErrors()` remain disabled everywhere.
- Never logged anywhere in application logs or exception summaries:
  - `PayPal:ClientSecret`, OAuth access tokens, webhook secrets
  - Passwords, password hashes, one-time temporary passwords
  - Session/auth cookies, CSRF tokens, guest access tokens
  - Full signed `SPK1.*` QR payloads or PayPal `ApproveUrl` query tokens
  - Unmasked auth identifiers on failed logins (always masked via `CorrelationContext.MaskIdentifier`)
