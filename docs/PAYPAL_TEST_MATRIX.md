# PayPal Sandbox Verification & Test Matrix — Spike OS

**Branch:** `feat/paypal-sandbox`  
**Build Configuration:** `Release` (`net10.0`)  
**Isolated Databases:** Temporary per-run SQL Server Express DBs (`PickleballClub_PayPalTest_*` & `PickleballClub_PayPalE2E_*`, dropped automatically after run; real `PickleballClub` database untouched).

---

## 1. Automated Integration & Regression Suite (`PickleballClub.Tests`)

Executed via `dotnet test e:\pickleball\PickleballClub.Tests\PickleballClub.Tests.csproj -c Release --nologo` (`58 / 58` scenarios: `38` pre-existing regression/concurrency/QR/UI-parity tests + `20` dedicated PayPal Sandbox integration tests using `FakePayPalClient` + real SQL Server transactions & filtered unique constraints).

| Test ID | Scenario Verified | Outcome |
|---|---|---|
| `TC01` | Create booking with `paymentMethod="paypal"` persists `"paypal"` + `pending` + `unpaid`; creates PayPal checkout at authoritative `PayPal:VndPerUsd = 26000` | PASS |
| `TC02` | `GET /Payment/PayPalReturn` performs zero DB mutations; `POST /capture` writes `TbPaymentTransaction` receipt (`paypal`), updates `PaidAmount`, keeps `Status="pending"`, and adds to `paypalNetCollected` | PASS |
| `TC03` | `GET /Payment/PayPalCancel` + `POST /api/payments/paypal/cancel` transitions attempt to `Cancelled` with zero ledger rows | PASS |
| `TC04` | Invalid PayPal REST credentials fail closed (`502`) and record `CreateFailed` cleanly | PASS |
| `TC05` | Missing/zero `PayPal:VndPerUsd` (`0`) disables `/api/payments/paypal/config` (`enabled=false`) and blocks checkout fail-closed (`503`) | PASS |
| `TC06` | Forged `state` signature or mismatched `payPalOrderId` rejected (`400`) with zero PayPal capture calls | PASS |
| `TC07` | Duplicate capture / F5 replay returns `200 deduplicated=true`, calls `CaptureOrderAsync` once (`CaptureCallCount == 1`), single ledger row | PASS |
| `TC08` | Concurrent parallel capture requests behind async capture barrier block second caller (`409 Conflict`) and insert single ledger receipt | PASS |
| `TC09` | Captured amount or currency mismatch rejected (`422`), never marks target paid, saves `PayPalCaptureId` under `CompensationRefundRequired` | PASS |
| `TC10` | PayPal `PAYER_ACTION_REQUIRED` status never triggers `/capture` (`CaptureCallCount == 0`, returns `409 PAYPAL_APPROVAL_REQUIRED`) | PASS |
| `TC11` | Booking hold expired before capture aborts pre-capture (`409`, `CaptureCallCount == 0`) with `ExpiredBeforeCapture` | PASS |
| `TC12` | Booking hold expires during PayPal redirect/capture -> preserves capture evidence & receipt first, executes compensation refund outside SQL TX, posts offsetting `refund` row (`NetPaid = 0`, `PaymentStatus = "refunded"`, `RefundedAfterExpiry`) | PASS |
| `TC13` | Transient compensation refund failure retains capture evidence (`CompensationRefundRequired`) and completes cleanly on background `ReconcileStaleAttemptsAsync` sweep | PASS |
| `TC14` | Standalone Pro Shop order (`paymentMethod="paypal"`) creates & captures via guest `HttpOnly` cookie/token and posts order ledger (`paypal`) | PASS |
| `TC15` | Pro Shop order merged into active booking blocks direct `"order"` PayPal checkout (`409 ORDER_MERGED_TO_BOOKING`) and settles linked booking single combined balance cleanly without double charge | PASS |
| `TC16` | Wrong authenticated account or wrong/missing guest token blocked with `403 Forbidden` on both create and capture | PASS |
| `TC17` | Capture gateway timeout transitions cleanly via `AmbiguousTimeout` and reconciles `COMPLETED` via `GetOrderAsync` with zero extra `/capture` calls | PASS |
| `TC18` | `PayPalPaymentService.ReconcileStaleAttemptsAsync` resolves aged `AwaitingApproval` where buyer approved in PayPal | PASS |
| `TC19` | Admin `POST /api/admin/paypal/{attemptCode}/refund` executes refund and posts `-Amount` counter ledger row; manual cashier endpoint blocks `paymentMethod="paypal"` spoofing (`400`) | PASS |
| `TC20` | `POST /api/webhooks/paypal` rejects unverified signatures (`401`) and processes verified `CHECKOUT.ORDER.APPROVED` webhooks idempotently | PASS |
| Existing Suite | `38` pre-existing regression, V4 QR, Pro Shop V6, Shadcn Admin V7 parity, HTTP security, and SQL migration/concurrency tests | PASS |

---

## 2. Live Browser & Real PayPal Sandbox Verification (Chromium Playwright)

Executed against an isolated Release instance on `http://127.0.0.1:5399` wired to an isolated temporary SQL Server DB (`PickleballClub_PayPalE2E_*`, dropped post-run) with User Secrets pointing to `https://api-m.sandbox.paypal.com` and `PayPal:VndPerUsd = 26000`:

- **Responsive Viewports & Light/Dark Theme (`375px`, `768px`, `1024px`, `1440px`)**:
  - Verified `/Booking`, `/Product`, `/Payment/PayPalReturn`, `/Payment/PayPalCancel`, `/Account/Profile`, `/Admin/Home/Reports`: `0` horizontal overflow (`scrollWidth === clientWidth`), `0` broken images (`naturalWidth === 0`), `0` console/page errors on Spike OS pages.
- **Live PayPal Sandbox OAuth2 & Orders API v2 Round-Trip Verified**:
  - Booking checkout (`PB-757FDC`, `150,000 VND` → `$5.77 USD` @ `26,000`): Created real PayPal Sandbox Order `3MP348792Y8812832`, browser navigated to `https://www.sandbox.paypal.com/checkoutnow?token=3MP348792Y8812832` (title `"Đăng nhập vào tài khoản PayPal của bạn"`), clicked the PayPal Sandbox cancel link returning via `/Payment/PayPalCancel`, and persisted `LocalStatus = "Cancelled"`.
  - Pro Shop checkout (`ORD-FD85F7`, `26,000 VND` → `$1.00 USD` @ `26,000`): Created real PayPal Sandbox Order `6DA909617H9363843`, redirected Chromium to `https://www.sandbox.paypal.com/checkoutnow?token=6DA909617H9363843`, and persisted `LocalStatus = "AwaitingApproval"`.
- **Interactive Buyer Sign-In / Live Capture Pending User Action**:
  - Interactive Sandbox Buyer sign-in and manual **Approve & Capture** click on `www.sandbox.paypal.com` pauses at PayPal's login prompt (`Đăng nhập vào tài khoản PayPal của bạn`) because headless Playwright does not supply your interactive Sandbox Personal Buyer account credentials.
  - Database migration `V5_PayPalSandboxIntegration` was **not** applied to your local `PickleballClub` database pending your explicit approval and backup confirmation.
