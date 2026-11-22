# SPIKE OS — PayPal Sandbox Integration Plan (Orders API v2 — Revised)

**Document Version:** 2.0 (Revised per Plan Review)  
**Branch:** `feat/paypal-sandbox` (branched cleanly from `feature/spike-os-redesign` / `9d1e65f`)  
**Target Gateway:** PayPal REST Orders API v2 (`https://api-m.sandbox.paypal.com`, **Sandbox Only**)  

---

## 1. Critical Plan Review Resolutions (10 Mandatory Architecture Rules)

1. **Authoritative Exchange Rate (`PayPal:VndPerUsd`)**:
   - Bind strictly to `PayPal:VndPerUsd` (`appsettings.json` defaults to `0`, failing closed if unset; developer `.NET User Secrets` supplies `26000`). Zero fallback `25000` constants anywhere in C# or JS. All VND & USD figures rendered in UI come from the same server conversion `Math.Round(amountVnd / VndPerUsd, 2, MidpointRounding.AwayFromZero)`.
2. **SQL Transaction Safety (Zero Locks Across HTTP Calls)**:
   - Never hold SQL transactions, `Serializable` locks, or `UPDLOCK`/`HOLDLOCK` while calling PayPal HTTP endpoints. Split every Create/Capture/Refund flow into short DB state-transition transactions (`Before HTTP` -> `External PayPal HTTP Call` -> `After HTTP`) linked by durable state (`Creating` -> `AwaitingApproval` -> `Capturing` -> `Completed` / `AmbiguousTimeout` / `CompensationRefundRequired`).
3. **Correct Capture Eligibility**:
   - Inspect PayPal order status (`GET /v2/checkout/orders/{id}`) before calling `/capture`. Only invoke `POST /v2/checkout/orders/{id}/capture` when PayPal status is `APPROVED`. Refuse `PAYER_ACTION_REQUIRED` (`409`); reconcile `COMPLETED` immediately without a duplicate `/capture`.
4. **Booking Expiration & Compensation/Refund Integrity**:
   - Re-check hold expiry (`HoldExpiresAtUtc`) before calling Capture (abort `409` cleanly without charging PayPal if already expired).
   - If Capture succeeds (`COMPLETED`) while the booking became `expired`/`cancelled`/`rejected`/`unavailable`:
     - **Persist durable Capture evidence FIRST** (`PayPalCaptureId`, `PayPalStatus`, USD/VND snapshot) and record an auditable `TbPaymentTransaction` receipt row before closing SQL locks so capture evidence is **never lost**.
     - Release SQL locks, invoke PayPal Refund API (`POST /v2/payments/captures/{id}/refund`, with durable `RefundIdempotencyKey`) outside any SQL transaction, then open a short SQL transaction to record the offsetting `TransactionType = "refund"` row in `TbPaymentTransaction`, recompute `PaidAmount = 0` (`PaymentStatus = "refunded"`), and mark `LocalStatus = "RefundedAfterExpiry"`—never reopening or auto-approving expired courts.
     - If refund HTTP fails/times out, mark `CompensationRefundRequired` so automatic background reconciler + admin action recover it deterministically.
5. **No Misrepresentation as Cash (`PaymentMethod = "paypal"`)**:
   - Extend `BookingService.CreateBookingAsync` & `OrderService.CreateOrderAsync` to persist genuine `PaymentMethod = "paypal"` (`PaymentStatus = "unpaid"`, `Status = "pending"` for bookings, `"confirmed"` for orders) when PayPal checkout is chosen & configured, keeping `"vnpay"/"online"` blocked (`422`) and preventing manual `"paypal"` spoofing on cashier `/payments` endpoints.
   - Extend `GetFinancialReportSummary` & `/Reports` UI to calculate `paypalNetCollected` separately alongside `cashNetCollected` and `transferNetCollected`.
6. **Isolated Database Migration Safety**:
   - Set `"Database:AutoMigrateInDevelopment": false` in `appsettings.Development.json` so dev boots never implicitly alter `PickleballClub`.
   - Create additive migration `V5_PayPalSandboxIntegration` guarded by `IF OBJECT_ID('tb_PayPalTransaction', 'U') IS NULL`, verify it exclusively on isolated `PickleballClub_Test_*` and `PickleballClub_E2E_*` databases first, and do **not** apply migrations to or modify production `PickleballClub` without explicit user approval and verified backup.
7. **Concurrent / Duplicate Attempt Protection**:
   - Lock target inside short pre-create & pre-capture SQL transactions (`UPDLOCK`), block new/parallel captures when another PayPal attempt on the same target is actively `Capturing` or `AmbiguousTimeout` (`409`), validate `remainingVnd >= tx.AmountVnd` immediately pre-capture AND inside post-capture commit TX, and enforce filtered unique SQL indexes on `PayPalOrderId`, `PayPalCaptureId`, `CreateIdempotencyKey`, `LedgerTransactionId`, and `tb_PaymentTransaction.IdempotencyKey` (`PP:{captureId}`).
8. **Real PayPal Sandbox Verification**:
   - Supplement mocked xUnit fault-injection tests and Chrome Playwright E2E with actual Sandbox API integration verification (`https://api-m.sandbox.paypal.com` OAuth2 + Order Create + real Sandbox Buyer approval & Capture verified in both SQL `tb_PaymentTransaction`/`tb_PayPalTransaction` and PayPal Sandbox Dashboard; pausing cleanly if interactive buyer login or anti-bot gate requires manual buyer sign-in).
9. **Return, Cancel & Webhook Security**:
   - Keep `GET /Payment/PayPalReturn` and `GET /Payment/PayPalCancel` **strictly read-only** (`zero DB mutations in GET`); bind DataProtection-signed `state` (`AttemptCode`, `Nonce`, `ExpiresUtc`) + caller ownership/guest token/`HttpOnly` guest checkout cookie.
   - Perform state transitions exclusively via `[ValidateAntiForgeryToken]` browser `POST` endpoints (`/api/payments/paypal/capture`, `/cancel`, `/reconcile`) and `POST /api/webhooks/paypal` strictly verified by `POST /v1/notifications/verify-webhook-signature` (returning `401/403` whenever `WebhookId` is unconfigured or signature != `SUCCESS`).
10. **Three-Layer Automatic Reconciliation**:
    - (a) Automatic client/return recovery (`ReconcileAttemptAsync` checks `GetOrderAsync` on retry/F5/status check).
    - (b) Scheduled background reconciler (`PayPalReconciliationWorker : BackgroundService`, plus callable directly for deterministic tests) scanning stale `AwaitingApproval` / `Capturing` / `AmbiguousTimeout` / `CompensationRefundRequired` records.
    - (c) Verified PayPal Webhook handler (`PAYMENT.CAPTURE.COMPLETED`, `PAYMENT.CAPTURE.REFUNDED`) completing orphaned checkouts automatically even if the customer closed the browser.

---

## 2. Controlled Implementation Checkpoints

- **Checkpoint 1**: Configuration (`PayPalOptions`, `appsettings.json`, `AutoMigrateInDevelopment: false`), Schema (`TbPayPalTransaction`, `PickleballContext`, additive migration `V5_PayPalSandboxIntegration`), isolated SQL Server migration test (`Migrations_ApplyCleanly...`).
- **Checkpoint 2**: Typed `PayPalClient` + `PayPalPaymentService` + `BookingService` / `OrderService` refactored ledger helpers + `PaymentController` + `PayPalReconciliationWorker` + `ApiDbController` `paypalNetCollected` reporting.
- **Checkpoint 3**: Spike OS Soft Green UI (`Booking`, `Product`, `Lookup`, `Profile`, `Payment/PayPalReturn`, `Payment/PayPalCancel`, `Reports`, `Management` drawer & reconciliation view) with visible `SANDBOX TEST` badge and dual VND/USD displays (`PayPal:VndPerUsd`).
- **Checkpoint 4**: Full automated verification (20+ mocked & SQL concurrency xUnit scenarios + Playwright Chrome multi-viewport E2E) on isolated DBs -> Checkpoint **Checkpoint 5**: Real PayPal Sandbox verification (`api-m.sandbox.paypal.com`), complete documentation suite (`docs/PAYPAL_*.md`), and final Release Gate (no claim of live/production readiness; explicit Sandbox boundary preserved).
