# PayPal Sandbox Implementation Changelog — Spike OS

**Branch:** `feat/paypal-sandbox` (branched from `feature/spike-os-redesign` / `9d1e65f`)  
**Gateway:** PayPal REST Orders API v2 (`https://api-m.sandbox.paypal.com` — Sandbox Only)

---

## 1. Configuration & Database Schema

- **`Configuration/PayPalOptions.cs`** *(New)*: Strongly-typed PayPal configuration binding (`PayPal:VndPerUsd`, `Environment`, `BaseUrl`, `ClientId`, `ClientSecret`, `WebhookId`, `Currency`, `BrandName`) with fail-closed validation blocking Live hosts and missing/non-positive FX rates (`VndPerUsd <= 0`), plus single-source `ConvertVndToUsd(int)` (`AwayFromZero` 2-decimal precision).
- **`appsettings.json`** & **`appsettings.Development.json`** *(Edited)*: Added non-secret `PayPal` template (`"VndPerUsd": 0`) and set `"Database": { "AutoMigrateInDevelopment": false }` to protect existing databases against unintended implicit migrations.
- **`Models/TbPayPalTransaction.cs`** *(New)* & **`Models/PickleballContext.cs`** *(Edited)*: Added `tb_PayPalTransaction` entity mapping (`TbPayPalTransactions` DbSet), `SyncTypedColumns()` conversion snapshots, and filtered unique SQL indexes on `PublicAttemptCode`, `CreateIdempotencyKey`, `PayPalOrderId`, `PayPalCaptureId`, `PayPalRefundId`, `LedgerTransactionId`, and `CompensationRefundLedgerTxId`.
- **`Migrations/20261119200000_V5_PayPalSandboxIntegration.cs`** *(New)*: Additive, idempotent T-SQL migration guarded by `IF OBJECT_ID('tb_PayPalTransaction', 'U') IS NULL`.

## 2. Backend Services, Concurrency & Controllers

- **`Services/IPayPalClient.cs`** & **`Services/PayPalClient.cs`** *(New)*: Typed `HttpClient` wrapper over PayPal REST v2/v1 endpoints (`/v1/oauth2/token` thread-safe memory caching, `/v2/checkout/orders`, `/v2/checkout/orders/{id}`, `/v2/checkout/orders/{id}/capture`, `/v2/payments/captures/{id}/refund`, `/v1/notifications/verify-webhook-signature`) with timeout/ambiguous-error classification (`IsAmbiguousTimeout`) and zero secret logging.
- **`Services/GuestRecoveryCookieHelper.cs`** *(New)*: Shared DataProtection cookie utility (`spike_guest_bk`, `SameSite=Lax`, `HttpOnly`) shared between `ApiDbController` and `PaymentController` for guest booking and standalone Pro Shop checkout ownership proof across cross-origin PayPal redirects.
- **`Services/PayPalPaymentService.cs`** *(New)*:
  - Enforces strict SQL/HTTP separation (`Phase A short Serializable lock -> External HTTP with Zero SQL locks -> ChangeTracker.Clear() + Phase B short Serializable lock`).
  - Validates PayPal `GET /v2/checkout/orders/{id}` BEFORE calling `/capture` (`APPROVED`-only capture, refuses `PAYER_ACTION_REQUIRED` / `CREATED` with `409`).
  - Prevents concurrent/duplicate PayPal checkouts via exclusive target leases (`409 PAYPAL_CAPTURE_IN_PROGRESS`) and pre/post-capture remaining-balance validation.
  - Preserves capture evidence (`PayPalCaptureId` + `TbPaymentTransaction` receipt row) before running compensation refunds (`RefundCaptureAsync` outside SQL TX) if a hold expires or settles during redirect, posting offsetting `refund` counter-entries without reopening expired courts.
  - Enforces ownership/IDOR (`403`) on bookings and guest Pro Shop orders and blocks direct `"order"` PayPal checkout on orders merged into bookings (`409 ORDER_MERGED_TO_BOOKING`) to avoid double charging.
- **`Services/PayPalReconciliationWorker.cs`** *(New)*: `BackgroundService` periodically invoking `ReconcileStaleAttemptsAsync` to recover `AmbiguousTimeout`, stuck `Capturing`, aged `AwaitingApproval`, and `CompensationRefundRequired` rows.
- **`Services/BookingService.cs`** & **`Services/OrderService.cs`** *(Edited)*: Added native `PaymentMethod = "paypal"` support when creating pending bookings and unpaid orders (blocking walk-in auto-approve on PayPal bookings and keeping manual `"paypal"` cashier spoofing rejected with `400` and legacy `"vnpay"/"online"` returning `422`), plus atomic in-tx ledger posting helpers `PostPayPalLedgerInCurrentTxAsync`, `PostPayPalRefundLedgerInCurrentTxAsync`, `PostPayPalOrderLedgerInCurrentTxAsync`, and `PostPayPalOrderRefundLedgerInCurrentTxAsync`.
- **`Controllers/PaymentController.cs`** *(New)*: Read-only `GET /Payment/PayPalReturn` & `GET /Payment/PayPalCancel` views; public config `GET /api/payments/paypal/config`; CSRF-protected `POST /api/payments/paypal/{create-order,capture,cancel,reconcile}`; signature-verified `POST /api/webhooks/paypal`; and Admin/Staff endpoints (`GET /api/admin/paypal/transactions`, `POST /api/admin/paypal/{attemptCode}/{reconcile,refund}`).
- **`Controllers/ApiDbController.cs`** & **`Program.cs`** *(Edited)*: Integrated `GuestRecoveryCookieHelper`, exposed `paypalNetCollected` & `payPalReconciliationCount` in `/api/admin/reports/summary`, enforced `ValidateForCheckout()` on `paymentMethod="paypal"` creates, issued guest Pro Shop proofs, registered `PayPalClient`, `PayPalPaymentService`, `PayPalReconciliationWorker`, `IMemoryCache`, and `PayPalLimiter`.

## 3. Frontend UI (Spike OS Soft Green Identity)

- **`wwwroot/js/shared.js`**: Added `window.SpikePayPal` helper for server config lookup and authoritative dual VND/USD conversion.
- **`Views/Booking/Index.cshtml`** & **`wwwroot/js/booking.js`**: Added Soft Green payment method cards (`Thanh toán tại quầy` vs `PayPal Sandbox (USD)` + `SANDBOX TEST` pill + live USD conversion preview + checkout redirect with fallback QR ticket modal on error).
- **`Views/Product/Index.cshtml`** & **`wwwroot/js/menu.js`**: Added PayPal Sandbox payment selection supporting standalone orders and linked active-booking balance settlement without double charging.
- **`Views/Lookup/Index.cshtml`**, **`wwwroot/js/lookup.js`**, **`Views/Account/Profile.cshtml`**, **`wwwroot/js/pages/profile.js`**: Added PayPal remaining-balance payment buttons on active bookings.
- **`Views/Payment/PayPalReturn.cshtml`**, **`Views/Payment/PayPalCancel.cshtml`**, **`wwwroot/js/pages/paypal-checkout.js`**: Dedicated post-redirect verification and cancel pages covering Loading, Completed (+ embedded QR digital ticket), Cancelled, Pending/Reconcile, RefundedAfterExpiry, and Error states.
- **`Areas/Admin/Views/Home/Reports.cshtml`**, **`wwwroot/js/reports.js`**, **`wwwroot/js/management.js`**: Added PayPal net revenue breakdown (`paypalNetCollected`), transaction ledger labels (`PayPal Sandbox`), reconciliation alert banner, and Admin Reconcile / Refund actions.
- **`wwwroot/css/pages/public-pages.css`** & **`wwwroot/css/pages/admin-pages.css`**: Styled PayPal cards, Sandbox pills, USD conversion boxes, and outcome cards in Light and Dark themes.
