# SPIKE OS — Logging System Audit & Implementation Plan

**Audit Date:** 2026-11-21  
**Target Branch:** `feat/paypal-sandbox`  
**Status:** Implementation & Verification Complete  

---

## 1. Existing Logging Infrastructure Audit

### 1.1 Packages & Logging Providers (`PickleballClub.csproj`, `Program.cs`)
- **Installed Packages:** Standard ASP.NET Core / EF Core 10.0.13 stack (`Microsoft.EntityFrameworkCore.SqlServer`, `QRCoder`, `SixLabors.ImageSharp`, `ZXing.Net`, etc.).
- **Third-Party Loggers:** Serilog is **not** installed. Built-in `Microsoft.Extensions.Logging` (default `WebApplication.CreateBuilder(args)` providers: Console, Debug, EventSource) is used. Per architectural constraints, built-in .NET logging infrastructure is sufficient and preferred without adding unnecessary external dependencies.
- **EF Core DbContext Options:** `Program.cs` registers `AddDbContext<PickleballContext>` with SQL Server and does **not** call `EnableSensitiveDataLogging()` or `EnableDetailedErrors()` (good baseline security).

### 1.2 Configuration Audit (`appsettings.json`, `appsettings.Development.json`)
- **`appsettings.json`:**
  - `Logging.LogLevel`: `Default = "Information"`, `Microsoft.AspNetCore = "Warning"`.
  - **Gap 1:** Missing category overrides for `Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Database.Command`, and `System.Net.Http.HttpClient`.
  - **Resulting Issue:** Every EF Core `SELECT`/`UPDATE` statement prints noisy multiline `info: Microsoft.EntityFrameworkCore.Database.Command[20101] Executed DbCommand ...` blocks on the developer console. Outbound PayPal HTTP calls also emit noisy `System.Net.Http.HttpClient` Info lifecycle events.
- **`appsettings.Development.json`:**
  - Repeats only `Default = "Information"`, `Microsoft.AspNetCore = "Warning"`. No console formatter customization, compact timestamps, or single-line layout.
- **Missing Configurations:**
  - No `appsettings.Production.json` defining safe structured JSON console defaults and conservative framework filtering for production log ingestion.
  - No opt-in `appsettings.SqlDebug.json` (or `--sql-debug` / `SqlDebug` environment profile) for temporary SQL query debugging when needed by developers.

### 1.3 Codebase Logging Coverage (`ILogger`, `Console.WriteLine`, `Debug.WriteLine`)
- **Direct Console/Debug Calls:** `0` occurrences of `Console.WriteLine` or `Debug.WriteLine` found in C# sources (good practice).
- **Existing `ILogger` Usage (Limited Scope):**
  - `Services/PayPalClient.cs`: Logs OAuth2/network/HTTP failures (`LogWarning`) on PayPal API calls without exposing secrets or full payloads. Missing structured `CorrelationId` propagation and capture success/reconciliation info events.
  - `Services/PayPalPaymentService.cs`: Logs `CreateOrderAsync` unhandled exception (`LogError`), unverified webhook rejection (`LogWarning`), reconciliation sweep error (`LogWarning`), and capture/get exception (`LogError`). Missing structured lifecycle events (`AttemptCreated`, `OrderCreated`, `CaptureCompleted`, `CaptureFailed`, `ReconciliationRequired`, `RefundCompleted`, `RefundFailed`, `CompensationRefund`).
  - `Services/BookingLifecycleWorker.cs`: Logs reconciled count (`LogInformation`) and transient loop errors (`LogWarning`).
  - `Services/PayPalReconciliationWorker.cs`: Logs reconciled attempt count (`LogInformation`) and sweep errors (`LogWarning`).
- **Modules with Zero `ILogger` Coverage:**
  - `Services/BookingService.cs` (creates, approves, rejects, cancels, check-ins, extensions, court transfers, session completions, payments, refunds, idempotency replays, expired holds).
  - `Services/OrderService.cs` (Pro Shop order creation, stock conflicts, standalone payments, refunds, fulfillment, cancellation & restock).
  - `Services/QrTokenService.cs` (HMAC signature rejections, expired QR tokens, invalid format).
  - `Controllers/ApiDbController.cs` (QR camera/image decode failures, auth login failures, lockouts, registration, password changes, rate-limit/CSRF/authz denials, CMS/court-lock/admin operations).
  - `Controllers/AccountController.cs` & `Areas/Admin/Controllers/*` (MVC login/admin login failures, account creation/role edits/toggles, admin booking/product/court operations).
  - `Controllers/PaymentController.cs` (PayPal approval redirect initiation, config errors).

### 1.4 Correlation, Tracing & Security Findings
- **Correlation Tracking:** No middleware currently generates or validates an inbound correlation ID. Only `HomeController.Error()` touches `HttpContext.TraceIdentifier`. End-to-end payment tracing (`Checkout -> PayPal CreateOrder -> Capture -> Ledger -> Result`) lacks a unified correlation identifier.
- **Security Posture Check:**
  - Verified no existing logs print `ClientSecret`, OAuth access tokens, passwords, password hashes, cookies, `GuestAccessToken`, or full `SPK1.*` QR tokens.
  - Audit ensures any newly added logs **must** mask user identifiers/phones on auth failures, truncate error messages, never log `ApproveUrl` query tokens, raw PayPal HTTP bodies, request cookies, CSRF tokens, or sensitive `TbAccount` columns.
  - Note: `Areas/Admin/Controllers/AccountsController.cs` displays a one-time temporary password via `TempData["AdminAccountSuccess"]` in the UI only; logging for that action must record `AccountId`, `Username`, `RoleId`, and `HasTempPassword` boolean **never** the plaintext temporary password.

---

## 2. Concise Implementation Plan

1. **Correlation Infrastructure (`Logging/CorrelationContext.cs` & Middleware in `Program.cs`):**
   - Add lightweight `AsyncLocal<string?>` `CorrelationContext` plus regex-validated (`^[A-Za-z0-9_-]{8,48}$`) request `CorrelationMiddleware` setting `HttpContext.TraceIdentifier`, `X-Correlation-Id` response header, and `ILogger.BeginScope`.
   - Propagate `CorrelationId` into PayPal outbound `PayPalClient` headers (`PayPal-Request-Id` / diagnostics) & background worker cycle scopes (`BookingLifecycleWorker`, `PayPalReconciliationWorker`).
2. **Clean Developer Console Formatter (`Logging/CleanSpikeConsoleFormatter.cs`):**
   - Implement a zero-dependency custom `ConsoleFormatter` (`"spike-clean"`) outputting compact single-line records:
     `HH:mm:ss  LEVEL  Category       [cid:...] Message`
   - Support ANSI colors with automatic TTY/`NO_COLOR` safety, short readable domain category names (`SpikeOS`, `Database`, `Booking`, `PayPal`, `Payments`, `ProShop`, `QrCheckIn`, `Auth`, `Admin`, `Api`), and clean exception/stack-trace formatting.
3. **Environment & SQL-Debug Configuration:**
   - Update `appsettings.json` and `appsettings.Development.json` to suppress routine `Microsoft.EntityFrameworkCore.Database.Command` (`Warning`), `System.Net.Http.HttpClient` (`Warning`), `Microsoft.AspNetCore` (`Warning`), while keeping SQL errors, DB warnings, and concurrency/transaction exceptions fully visible.
   - Add `appsettings.Production.json` using structured `Json` console formatting and safe Production log levels (no sensitive data logging, no SQL spam).
   - Add `appsettings.SqlDebug.json` as an opt-in override (usable via `ASPNETCORE_ENVIRONMENT=SqlDebug` or `--sql-debug` / `SPIKE_SQL_DEBUG=true` strictly outside Production) to re-enable EF Core `Database.Command: Information` on demand.
   - Emit verified startup readiness diagnostics (`SpikeOS`, `Database`, `Security`, `PayPal`) on boot without fabricating external connections.
4. **Structured Business & Security Logging Across Services and Controllers:**
   - Inject `ILogger<T>` (using optional `NullLogger<T>.Instance` fallback in services instantiated manually by unit tests so existing test constructors stay 100% compatible) across:
     - `BookingService`: all booking transitions, payments (full & partial), refunds, idempotency replays/conflicts, lifecycle expirations/no-shows.
     - `PayPalPaymentService` & `PayPalClient` & `PaymentController`: full Checkout -> CreateOrder -> Redirect -> Capture -> Ledger -> Refund / Reconciliation / Webhook trail.
     - `OrderService`: order created, stock conflicts, payment recorded (full & partial), fulfilled, cancelled & restocked, refunds.
     - `QrTokenService` & `ApiDbController`: QR validation rejections, scanner decode failures, check-in rejections.
     - `ApiDbController`, `AccountController`, `Areas/Admin/Controllers/*`, and `Program.cs` security middleware: auth failures, `401`/`403` access denials, CSRF/rate-limit rejections, admin config/court-lock/account/booking/product/court mutations.
5. **Verification & Documentation:**
   - Run `dotnet restore`, `dotnet build`, full automated `xUnit` suite (`60+` tests + new logging/correlation/SQL-suppression regression tests), live console verification in `Development`, `SqlDebug`, and `Production` profiles, simulated warning/error/payment/SQL failure flows, and complete `docs/LOGGING_IMPLEMENTATION.md`, `docs/LOGGING_CONFIGURATION.md`, and `docs/LOGGING_TEST_RESULTS.md`.
