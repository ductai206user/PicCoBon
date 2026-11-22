# SPIKE OS — Regression & Automated Verification Suite Report

**Document Version:** 1.0  
**Branch:** `feature/spike-os-redesign`  
**Execution Date:** `2026-11-19`  
**Isolated DB Policy:** Every SQL test and Playwright run executed against ephemeral isolated SQL Express databases (`PickleballClub_Test_*`, `PickleballClub_E2E_*`, `PickleballClub_UI_*`) and cleaned up on exit; production `PickleballClub` remained untouched.

---

## 1. Build Verification

| Command | Result | Details |
|---|---|---|
| `dotnet build PickleballClub.csproj -c Release --nologo` | **PASS** | `0 Warning(s)`, `0 Error(s)` |

---

## 2. xUnit Automated Test Suite (`38 / 38 PASS`)

Executed via `dotnet test PickleballClub.Tests/PickleballClub.Tests.csproj -c Release --nologo --verbosity normal` (`Total time: 51.9738s`).

| Test Class | Count | Status | Scope Verified |
|---|---:|---|---|
| `HttpSecurityAndApiIntegrationTests` | `7` | **PASS** | Anonymity PII strip, ETag `304`, ZXing QR image upload check-in (`409` -> `200` -> replay `409`), `503` OTP guard, strict IDOR protection (`bk_*`), CSRF validation, RBAC role separation, security headers |
| `ProShopV6ImageAndApiTests` | `5` | **PASS** | Product image upload (`PNG`/`JPG`/`WEBP` resize & alpha), MIME/magic-byte rejection, edit lifecycle cleanup, ETag invalidation, admin product auth/CSRF |
| `SafeRemediationRegressionTests` | `3` | **PASS** | Pro Shop attach/fulfill same-day & cancelled conflict guards (`FIX-03/04`), negative net revenue bar preservation (`FIX-07`), CMS `peakSurcharge=0` SSR + quote parity (`FIX-08/18/20`) |
| `ShadcnAdminV7ParityTests` | `4` | **PASS** | Public vs Admin sidebar isolation, `#shAdmin*` shell parity, `#siteThemeToggleBtn`, `/Admin/Login` & `/Admin/Register` auth card + CSRF markers, Staff RBAC navigation & route blocking |
| `SqlServerConcurrencyAndMigrationTests` | `6` | **PASS** | Fresh & legacy schema migrations, parallel court booking mutual exclusion (`409`), lock/extend collision mutual exclusion, atomic last-stock Pro Shop decrement + restock, immutable payment ledger vs session close, full-day lock & image normalization |
| `UnitAndQrVerificationTests` | `7` | **PASS** | Segmented peak pricing & tier discounts, PBKDF2/MD5 password upgrade, isolated auth cookie check, icon XSS sanitizer, constant-time `bk_*` verification, QR fail-fast key validation, `QrTokenService` PNG roundtrip & tamper/expiry rejection |
| `V4ReleaseBlockersAndQrTests` | `6` | **PASS** | Pending hold TTL & per-actor cap, `FinishSession` overdue lifecycle, strict `IdempotencyKey` replay/mismatch `409`, atomic linked order cancellation/restock, CSRF/recovery/MustChangePassword/ETag contracts, **`105/105` ZXing QR benchmark** |
| **Total** | **`38`** | **`38 / 38 PASS`** | `0 Failed`, `0 Skipped` |

### 2.1 QR Scannability Benchmark (`docs/qa/qr/qr-benchmark-report.json`)
- **Total Samples Tested**: `105` (`standard_512_png: 35`, `logo_512_png: 30`, `mobile_220_downscaled: 15`, `jpeg_compressed_q80: 15`, `highres_1024_png: 10`)
- **Total Samples Decoded (ZXing)**: `105` (**`100%` pass rate**)

---

## 3. JavaScript Syntax & Multi-Timezone Unit Tests

| Check | Coverage | Result |
|---|---|---|
| `node --check` | All **`12`** files in `wwwroot/js/` and `wwwroot/js/pages/` | **PASS** (`12 / 12`, `0` syntax errors) |
| `js_unit_tests.mjs` | Vietnam `UTC+7` date/time & timezone helpers across `UTC`, `America/New_York`, `Asia/Ho_Chi_Minh`, `Asia/Tokyo` | **PASS** (`4 / 4` timezones) |

---

## 4. Real-Browser Playwright Chrome E2E Verification (`PASS`)

Executed via isolated Release server + SQL Express migration & realistic seed (`PickleballClub_E2E_*`) in Chromium across multiple responsive viewports (`375px`, `768px`, `1024px`, `1440px`) and explicit browser timezone emulation (`America/New_York` / `UTC` / `Asia/Ho_Chi_Minh`):

- **Public & Customer Workflows**:
  1. Landing `/` SSR court count (`2`), real CMS pricing sync, and court card grid rendering.
  2. `/Booking` under `America/New_York` browser timezone resolves VN today (`UTC+7`), syncs `?duration=90` URL query, maps server quote DTO cleanly (never `0 đ`), enforces honest `Chờ Duyệt` pending modal copy, renders `SPK1.*` QR SVG inside `#bookingDigitalTicketHost`, and closes `#modalQR` cleanly on backdrop click (`aria-hidden="true"`).
  3. `/Lookup` blocks anonymous phone-only enumeration (`400`) and unlocks ticket detail + SVG/PNG QR via valid `PB-*` + `bk_*` token.
  4. `/Account/Login` & `/Account/Profile` preserve keyboard-accessible `.paddle-chip` `<button>` elements and Member Portal workflows without granting anonymous access to `/Admin`.
  5. `/Product` renders clean images/SVG fallbacks, hides ineligible future/ended bookings from `#shopOrderBooking`, retains eligible active today bookings, and submits real orders via `POST /api/orders/create` returning `ORD-*`.
- **Admin & Staff Workflows**:
  6. `/Reports` accurately preserves negative net revenue bars (`data-value="-100000"`) when refunds exceed collections.
  7. `/Management` filters out expired pending holds before bulk approval, loads paginated standalone Pro Shop orders (`>25` with `#btnLoadMoreShopOrders`), prevents duplicate `/payments` calls on partial close failure, and syncs full-day maintenance locks (`06:00`).
  8. `/Cms` updates live pricing (`peakSurcharge=0` -> `120.000 đ`) reflected immediately in SSR & quotes.
  9. `/Checkin` decodes uploaded QR PNG via server ZXing (`SPK1.*`) and populates verified check-in receipt.
  10. `Staff` role accesses `/Management` (`200`) while blocked (`403`/`302` redirect) from `/Admin/Courts` and `/Cms`.
- **Telemetry**: `0` console errors, `0` uncaught page errors.

---

## 5. Static Hygiene & Production DB Safety Checks

- Inline `<style>` blocks remaining in `Views/**` and `Areas/**`: **`0`**
- Legacy brand orange hexes (`#FF5520`, `#E94A16`, `#FFF1EB`, `#F97316`) across `Views/**`, `Areas/**`, `wwwroot/css/**`, `wwwroot/js/**`: **`0`**
- `wwwroot/` clean footprint: **`42` files (`3.565 MB`)**, CSS module bundle: **`8` files (`0.123 MB`)**
- SQL Server `.\SQLEXPRESS` `sys.databases` check: Only **`PickleballClub`** present (`Courts = 4`, `Products = 11`, `Bookings = 9`, `Orders = 0`, `Accounts = 2`); all temporary test databases cleaned up.
