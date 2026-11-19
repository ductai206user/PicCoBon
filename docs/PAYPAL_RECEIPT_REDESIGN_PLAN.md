# SPIKE OS — PayPal Payment Success & Receipt Page Redesign Plan

**Document Status:** Implemented & Verified (Pass 1 → Pass 2 → Pass 3 Complete)  
**Target Branch:** `feat/paypal-sandbox`  
**Primary Route:** `GET /Payment/PayPalReturn` (+ shared shell alignment for `GET /Payment/PayPalCancel`)  
**Scope:** UI/UX Presentation Redesign + Historical Receipt Replay Safety — **Zero rewrites to payment, capture, verification, ledger, or database transaction logic.**

---

## 1. Existing Architecture Analysis

### 1.1 Current Flow & Endpoints (`PaymentController` + `PayPalPaymentService`)
1. Buyer approves checkout on PayPal Sandbox (`https://www.sandbox.paypal.com/checkoutnow?token=<PayPalOrderId>`).
2. PayPal redirects the buyer via `GET /Payment/PayPalReturn?state=<protectedState>&token=<payPalOrderId>&PayerID=<payerId>`:
   - `PaymentController.PayPalReturn(...)` is **strictly read-only** (zero DB writes on `GET`), generates antiforgery CSRF tokens, validates DataProtection-protected `state`, and passes safe bootstrap data (`ViewBag.StateToken`, `ViewBag.PayPalOrderId`, `ViewBag.AttemptCode`, `ViewBag.TargetType`, `ViewBag.TargetCode`, `ViewBag.IsValidState`, `ViewBag.VndPerUsd`) via `data-pp-*` attributes on `#paypalCheckoutPageRoot`.
3. Immediately on `DOMContentLoaded`, `wwwroot/js/pages/paypal-checkout.js` calls `apiFetch('/api/payments/paypal/capture', { method: 'POST', ... })` with the CSRF header, DataProtection-signed `state`, `payPalOrderId`, and caller ownership proof (`spike_auth` / `spike_guest_bk` `HttpOnly` cookie / `sessionStorage` booking token fallback).
4. `PaymentController.CapturePayPalOrderApi` delegates to `PayPalPaymentService.CaptureCheckoutAsync(...)`, which enforces strict DB/HTTP separation, queries `GET /v2/checkout/orders/{id}`, captures **only** when `APPROVED` (reconciling already-captured `COMPLETED` orders idempotently), posts `TbPaymentTransaction` receipt rows into `tb_PaymentTransaction` inside a short `Serializable` transaction, and returns `MapPayPalResultToActionResult(...)`:
   - `ok`, `code`, `error`, `deduplicated`, `needsReconciliation`
   - `attemptCode`, `targetType` (`"order"` | `"booking"`), `targetCode`
   - `localStatus` (`"Completed"`, `"Cancelled"`, `"RefundedAfterExpiry"`, `"AmbiguousTimeout"`, etc.)
   - `payPalOrderId`, `payPalCaptureId`, `payPalRefundId`, `payPalStatus`
   - `amountVnd`, `amountUsd`, `currencyCode`, `exchangeRateVndPerUsd`
   - `order` (`code`, `customerName`, `phone`, `address`, `totalAmount`, `paidAmount`, `paymentStatus`, `paymentMethod`)
   - `booking` (`id`, `courtName`, `date`, `time`, `duration`, `name`, `phone`, `amountDue`, `paidAmount`, `remainingDue`, `status`, `paymentStatus`, `qrSvg`, `qrSvgUrl`, etc.)

### 1.2 Database & Entity Dependencies Preserved
- **`tb_PayPalTransaction` (`TbPayPalTransaction`)**: Authoritative gateway attempt log (`PayPalOrderId`, `PayPalCaptureId`, `AmountVnd`, `AmountUsd`, `ExchangeRateVndPerUsd`, `LedgerTransactionId`).
- **`tb_PaymentTransaction` (`TbPaymentTransaction`)**: Financial ledger entries (`PaymentMethod = "paypal"`, `ReferenceCode = PayPalCaptureId`).
- **`tb_Order` (`TbOrder`) & `tb_Booking` (`TbBooking`)**: Updated solely inside `PayPalPaymentService` + `OrderService` / `BookingService` ledger helpers. No schema or migration changes are needed for this UI redesign.

### 1.3 Current Visual System & Assets Inspected
- **Shared Layout & Nav**: `Views/Shared/_Layout.cshtml` provides Spike OS header (`Trang chủ`, `Đặt sân`, `Pro Shop`, `Tra cứu vé`, user profile/logout, theme switcher, authorized `Quản trị` link, `Đặt sân ngay` CTA) and club footer.
- **CSS Pipeline**: `Views/Shared/_DesignSystemCss.cshtml` loads `tokens.css`, `reset.css`, `primitives.css`, `qr-ticket.css`, `public-shell.css`, `admin-shell.css`, `public-pages.css`, `admin-pages.css`.
- **Fonts & Icons**: `--font-display` (`Plus Jakarta Sans`), `--font-sans` (`Inter`), `--font-mono` (`JetBrains Mono`), and `Iconify` (`lucide:*`).
- **Existing PayPal Outcome Styles**: Currently minimal generic card styling in `Views/Payment/PayPalReturn.cshtml` (`max-width: 740px`) and bottom of `wwwroot/css/pages/public-pages.css` (lines 1887–1919).

### 1.4 Verification of Payment Safety, Monetary Accuracy & QR Compatibility
- **Server-Verified F5 Refresh & Idempotent Callbacks**: `PayPalPaymentService.CaptureOrReconcileAttemptInternalAsync` checks persisted `ppTx.LocalStatus == "Completed"` and `ppTx.LedgerTransactionId.HasValue` inside a short `Serializable` lock **before** calling PayPal APIs. Browser refresh (`F5`) or repeated return callbacks replay the persisted verified snapshot (`deduplicated = true`) without issuing a second PayPal capture and reject unauthorized callers with `403`.
- **Immutable Transaction-Time Monetary Snapshot**: `MapPayPalResultToActionResult` reads `amountVnd`, `amountUsd`, `currencyCode`, and `exchangeRateVndPerUsd` exclusively from the persisted `TbPayPalTransaction` database record captured at checkout creation/completion (`ppTx.AmountUsd`, `ppTx.ExchangeRateVndPerUsd`). Historical receipts are **never** recalculated against newer `PayPal:VndPerUsd` settings.
- **QR Ticket Preservation**: Booking checkouts mount `SpikeTicket.mountTicket('ppCompletedTicketHost', data.booking, ...)` styled cleanly by `wwwroot/css/components/qr-ticket.css`. All new receipt styles must be isolated under `.spk-receipt-*` so QR box dimensions, SVG sharpness, crisp white scan margin, and print visibility remain 100% intact.

---

## 2. Files Requiring Modification

1. **`Controllers/PaymentController.cs`** *(Minimal additive ViewBag only)*
   - Expose non-secret `ViewBag.PayPalEnvironment = _payPalOptions.CurrentValue.Environment ?? "Sandbox"` on `PayPalReturn` and `PayPalCancel` so Sandbox UI pills are strictly environment-aware (never mislabeled on non-Sandbox environments) and optionally include `environment` on checkout API JSON responses.
2. **`Views/Payment/PayPalReturn.cshtml`** *(Primary Redesign Target)*
   - Preserve exact layout chrome (`_Layout.cshtml`) and existing JS bootstrap contract (`#paypalCheckoutPageRoot`, `#paypalOutcomeCard`, all `data-pp-*` attributes).
   - Introduce atmospheric soft mint background layer (`.spk-receipt-stage`), ambient curved green background waves & court hairline SVG layer, optional subtle corner pickleball vector/emblem accent (desktop only, hidden cleanly in print/mobile).
   - Rebuild the centered payment receipt container (`max-width: 860px`, `border-radius: 32px`, translucent white surface, soft emerald border & diffused shadow) containing:
     - Top centered environment-aware pill (`PAYPAL SANDBOX · USD TEST MODE`, hidden unless `Sandbox`).
     - Animated hero emblem (green gradient circle `#065F46 -> #15803D -> #22C55E`, animated SVG white checkmark stroke draw, soft radial halo, flanking laurel leaves & minimal confetti flecks) + bold Vietnamese headline + subtitle.
     - Highlighted Payment Amount Banner (`20px` radius, white-to-mint wave background, bold large `clamp()` emerald VND display e.g. `20.000đ`, frosted pill `~ 0.77 USD` — 100% bound to verified server response).
     - Structured 2-column `Chi tiết giao dịch` card with header badge (`Đã thu đủ qua PayPal Sandbox` only when verified `Completed`), left column (Order/Ticket ID, PayPal Order ID, Payment/Capture ID, Spike OS Attempt Code with safe click-to-copy helper), and right column (`Tỷ giá áp dụng`, `Khách nhận`, `SĐT`, `Hình thức giao` / Court schedule).
     - Booking Digital QR Pass container (`#ppCompletedTicketHost`) seamlessly slotted beneath the details card for booking checkouts.
     - Footer CTA bar: gradient primary button (`Tiếp tục xem Pro Shop →` for orders / `Quản lý vé tại Trang Tra cứu →` for bookings), crisp secondary button (`Về Trang chủ`), and optional print trigger (`In hóa đơn`).
3. **`Views/Payment/PayPalCancel.cshtml`** *(Visual Alignment)*
   - Align cancel shell with `.spk-receipt-stage` + `.payment-receipt` so cancel and return flows share a unified, native Spike OS look and feel across Light and Dark modes.
4. **`wwwroot/js/pages/paypal-checkout.js`** *(Dynamic Rendering + States + Copy/Print UX)*
   - Keep `runPayPalReturnCapture()`, `runPayPalCancelSync()`, `reconcilePayPalAttemptFromPage()`, API endpoints, CSRF headers, and guest token handling **100% unchanged in security/protocol**.
   - Rewrite `setPpViewState(mode, opts)` to dynamically bind every field from verified API payloads into the redesigned receipt DOM (`formatReceiptVnd`, `formatReceiptUsdApprox`, copy-to-clipboard buttons for long PayPal IDs, clean state styling for `loading`, `completed`, `pending`, `cancelled`, `refunded`, `failed`, and missing/tampered state).
5. **`wwwroot/css/pages/public-pages.css`** *(Dedicated Receipt Styles + Responsive + Dark Mode + Print CSS)*
   - Add scoped `.spk-receipt-*` / `.spk-pp-receipt*` styles implementing the palette (`#065F46`, `#15803D`, `#22C55E`, `#DCFCE7`, `#F0FDF4`, `#102A22`, `#64748B`), `145deg` atmospheric mint background, subtle court-line & wave layers, restrained GPU-friendly keyframes respecting `@media (prefers-reduced-motion: reduce)`, complete Dark Mode (`html[data-theme="dark"]`) harmony, responsive breakpoints (`>=1440px`, `1024px`, `768px`, `375px` / `320px`), and `@media print` receipt formatting.
   6. **`Services/PayPalPaymentService.cs`** *(Minimal Historical Receipt F5 Replay Fix)*
   - Check `IsPayPalSchemaAvailableAsync` at entry of `CaptureOrReconcileAttemptInternalAsync`, and allow already-`Completed` / `Refunded` / `RefundedAfterExpiry` transactions to replay their persisted server snapshot cleanly even if `PayPal:VndPerUsd` or checkout configuration is later changed or disabled.
7. **`PickleballClub.Tests/PayPalSandboxIntegrationTests.cs`** *(Automated Regression Test `TC22`)*
   - Verify SSR markup & environment attributes (`data-pp-env="Sandbox"`), `F5` idempotent repeat capture, immutable transaction-time `AmountUsd` / `ExchangeRateVndPerUsd` replay under changed/zeroed FX rate configs, and `403` protection against unauthorized users.

---

## 3. CSS Isolation, Component Architecture & Reference Alignment

### 3.1 Strict CSS Namespacing & Regression Prevention
- Every new layout, surface, hero, amount card, detail grid, action bar, and print rule in `public-pages.css` MUST be prefixed with `.spk-receipt-*` (preserving `.spk-sandbox-pill` cleanly).
- Zero un-namespaced overrides to `.card`, `.btn`, `.badge`, `.payment-summary`, navbar (`.site-header`), footer (`.site-footer`), QR ticket primitives (`.st-card` / `qr-ticket.css`), Pro Shop, or Admin containers.
- Reuse existing CSS tokens (`--font-sans`, `--font-display`, `--font-mono`, `--text-primary`, `--brand-primary`, etc.) plus explicit Dark Mode (`html[data-theme="dark"] .spk-receipt-*`) overrides.

### 3.2 Visual Implementation Strategy & Token Map

### 3.1 Color & Surface System (Light Mode + Dark Mode Adaptation)
- **Light Mode Stage**: `linear-gradient(145deg, #F8FFFB 0%, #E9FAF0 55%, #F3FFF7 100%)`
- **Dark Mode Stage**: `#0C1410` with subtle top radial `rgba(34, 197, 94, 0.08)` glow (no harsh white washes).
- **Receipt Card**: `rgba(255, 255, 255, 0.96)`, border `1px solid rgba(22, 163, 74, 0.12)`, radius `32px` desktop / `22px` mobile, shadow `0 24px 70px rgba(15, 70, 40, 0.08), 0 4px 16px rgba(15, 70, 40, 0.03)`.
- **Hero Gradient & CTA Gradient**: `linear-gradient(135deg, #065F46 0%, #15803D 50%, #22C55E 100%)` (text contrast white `#FFFFFF` exceeds WCAG AA on emerald gradients and deep forest stops).
- **Amount Box Gradient**: `linear-gradient(135deg, #FFFFFF 0%, #EAFBF0 52%, #DCFCE7 100%)` with subtle translucent SVG wave layers and crisp `#065F46` / `#15803D` numeric value (`clamp(2.25rem, 5.5vw, 3.25rem)`).
- **Zero Orange Policy**: Semantic statuses use emerald greens for success, deep amber `#92400E`/`#FEF3C7` for pending/cancelled, and muted crimson `#B91C1C`/`#FEE2E2` for error/refunded states.

### 3.2 Restraint & Motion Choreography (`<= 500ms`)
- Single card fade-in & rise (`380ms cubic-bezier(0.16, 1, 0.3, 1)`).
- Success circle pop-in (`360ms`) followed by crisp SVG stroke checkmark draw (`420ms`).
- Staggered subtle reveal of Amount Box (`+70ms`) and Transaction Details card (`+130ms`).
- One-shot delicate laurel/leaf confetti drift (`0.5s`, stops completely — no endless loop!).
- Disabled cleanly under `@media (prefers-reduced-motion: reduce)`.

---

## 4. Component Structure (ASCII Wireframe)

```text
+-------------------------------------------------------------------------+
| [Header Navbar — Spike OS Existing Shell]                               |
+-------------------------------------------------------------------------+
|  ~ Light Mint Gradient Atmospheric Stage + Faint Court Hairlines ~      |
|                                                                         |
|    +---------------------------------------------------------------+    |
|    |           (🧪 PAYPAL SANDBOX · USD TEST MODE)                 |    |
|    |                                                               |    |
|    |                 ~ 🌿   (  ✓  )   🌿 ~                         |    |
|    |            Thanh toán PayPal Sandbox thành công               |    |
|    |   Đơn hàng Pro Shop của bạn đã được thanh toán hoàn tất...    |    |
|    |                                                               |    |
|    |   +-------------------------------------------------------+   |    |
|    |   |                  Giá trị thanh toán                   |   |    |
|    |   |                      20.000đ                          |   |    |
|    |   |                   ( ~ 0.77 USD )                      |   |    |
|    |   +-------------------------------------------------------+   |    |
|    |                                                               |    |
|    |   +-------------------------------------------------------+   |    |
|    |   | 🧾 Chi tiết giao dịch     [✔ Đã thu đủ qua PayPal...] |   |    |
|    |   |-------------------------------------------------------|   |    |
|    |   | 🛒 Mã đơn hàng:   ORD-XXXXXX | 💱 Tỷ giá: 1 USD = ... |   |    |
|    |   | 🅿 PayPal Order: 8MF16028... | 👤 Khách nhận: ...     |   |    |
|    |   | 💳 Payment ID:   6YH32075... | 📞 SĐT: ...            |   |    |
|    |   | 🔖 Mã phiên:     PP-XXXXXX   | 🏪 Hình thức: ...      |   |    |
|    |   +-------------------------------------------------------+   |    |
|    |                                                               |    |
|    |          [Booking Only: Digital QR Ticket Mount]              |    |
|    |                                                               |    |
|    |      [ 🏪 Tiếp tục xem Pro Shop → ]   [ 🏠 Về Trang chủ ]    |    |
|    |                     ( 🖨 In hóa đơn )                        |    |
|    +---------------------------------------------------------------+    |
|                                                         [subtle paddle] |
+-------------------------------------------------------------------------+
| [Footer — Spike OS Existing Shell]                                      |
+-------------------------------------------------------------------------+
```

---

## 5. Responsive Design Strategy

- **Desktop (`>= 1024px`)**: Centered `max-width: 860px` card, generous padding (`36px 40px`), balanced 2-column transaction details card with vertical hairline divider (`gap: 0`), horizontal CTA button row, optional bottom-right subtle pickleball paddle & ball accent (`pointer-events: none; aria-hidden="true"`).
- **Tablet (`768px – 1023px`)**: Card fluid width (`width: min(100%, 760px)`), padding `28px 24px`, 2-column grid with `word-break: break-word` for long PayPal capture hashes.
- **Mobile (`320px – 767px`)**: Full-width with `12px` side gutters (`border-radius: 22px`, padding `22px 16px`), smaller `64px` emblem, `clamp()` monetary figure preventing wrap, single-column stacked transaction rows (`1fr`), vertically stacked full-width action buttons (`100%` width, min-height `46px` touch targets), zero horizontal overflow (`scrollWidth <= clientWidth`).

---

## 6. Testing & Validation Checklist

1. **Syntax & Build Verification**:
   - `node --check wwwroot/js/pages/paypal-checkout.js`
   - `dotnet build e:\pickleball\PickleballClub.csproj -c Release --nologo` (`0 Warnings, 0 Errors`).
2. **Automated Backend + Integration Suite (`59 / 59`)**:
   - `dotnet test e:\pickleball\PickleballClub.Tests\PickleballClub.Tests.csproj -c Release --nologo` validating idempotency, F5 refresh replay, historical monetary immutability, authorization (`403`), capture rules, refunds, and schema readiness guard.
3. **Browser Visual, Print, QR & Reference-Fidelity Verification (Chromium Playwright on Isolated Server)**:
   - Mocked capture & cancel verification on `http://127.0.0.1:5399` across `375px`, `768px`, `1024px`, `1440px` in Light & Dark Mode:
     - **Completed Pro Shop Receipt**: Dynamic VND/USD binding (`20.000đ` · `≈ 0.77 USD` @ `1 USD = 26.000đ`, distinct VND vs captured USD labels), two-column details grid (`ORD-...`, `PayPal Order ID`, `Capture ID`), zero clipping/overflow.
     - **Completed Booking QR Receipt**: Digital QR pass rendered inside `#ppCompletedTicketHost`, natural/bounding box square ratio preserved, zero overlap with receipt buttons, plus separate `@media print` verification confirming navbar/footer/background hidden and receipt + QR pass intact.
     - **Payment Safety & F5 Reload Test**: Reload `/Payment/PayPalReturn` (`F5`) on a `Completed` transaction -> confirm zero additional `/capture` HTTP errors, same immutable capture ID & exchange rate displayed, and unauthorized anonymous session rejected (`403`).
     - **Edge States**: Loading state, Pending/Reconcile state, Cancelled (`/Payment/PayPalCancel`), RefundedAfterExpiry, Tampered/Invalid `state`.
   - **Mandatory Visual Refinement Loop (>= 2 Passes)**:
     - Capture **Before** and **After Pass 1 / Pass 2** screenshots at `1440px` & `375px`.
     - Explicitly audit proportions, hero laurel & badge alignment, gradient stops, highlight wave box, grid dividers, button hierarchy, contrast, and spacing against `media_1795097871331_735e2410.jpg`, executing at least two targeted CSS/layout refinement passes until fidelity, accessibility, and responsiveness align.

---

## 7. Rollback Considerations

- Scope is isolated cleanly to 4 frontend files + 1 minor non-breaking `ViewBag` addition (`PaymentController.cs`).
- No database migrations, no entity modifications, and no changes to payment verification/capture/refund services.
- Can be reverted cleanly via `git checkout -- Views/Payment wwwroot/js/pages/paypal-checkout.js wwwroot/css/pages/public-pages.css Controllers/PaymentController.cs`.

---

## 8. Post-Implementation Outcomes *(Verified)*

### 8.1 Visual Refinement Audit Log
- **Pass 1 (`pass1-order-*`, `pass1-booking-qr-*`)**:
  - *Found*: Below-the-fold `iconify-icon` elements in mobile `375px` row icons rendered blank until scrolled into view; Dark Mode upper wave SVG in `.spk-receipt-amount-waves` rendered overly opaque white (`#FFFFFF`); primary CTA buttons sat slightly below the `1440×900` initial fold; 17-character PayPal IDs wrapped awkwardly on `375px`.
  - *Fixed in Pass 2*: Replaced all receipt icons (`PayPalReturn.cshtml`, `PayPalCancel.cshtml`, `paypal-checkout.js`) with inline Lucide-style `<svg>` (`receiptSvgIcon` helper); added CSS-themed `.spk-receipt-wave-top` / `.spk-receipt-wave-bottom` for Light & Dark Mode harmony; tightened desktop spacing; tuned `@media (max-width: 440px)` typography so long IDs fit on one row; clarified VND vs captured PayPal USD labels (`Giá trị đơn hàng (VNĐ)` vs `Thực thu PayPal: ... USD`).
- **Pass 2 → Pass 3 Refinements**:
  - *Found in Pass 2 inspection*: Single orphan word `"Sandbox."` wrapped to a second line in `.spk-receipt-subtitle` on `1440px`; `In hóa đơn` sat partially clipped at the bottom edge of `1440×900`; booking receipts still displayed `"Giá trị đơn hàng (VNĐ)"`.
  - *Fixed in Pass 3*: Widened `.spk-receipt-subtitle` `max-width` (`540px -> 640px`), tightened vertical rhythm (`ctaBottom: 825px` within `900px` viewport), and switched booking monetary label to `"Giá trị đặt sân (VNĐ)"`.

### 8.2 Build, Test & Browser Verification Results
- `node --check wwwroot/js/pages/paypal-checkout.js`: **Passed** (`exit 0`).
- `dotnet build PickleballClub.csproj -c Release --nologo`: **Passed** (`0 Warnings, 0 Errors`).
- `dotnet test PickleballClub.Tests/PickleballClub.Tests.csproj -c Release --nologo`: **Passed (`60 / 60` passed, `0` failed, `0` skipped; includes new `TC22`)**.
- **Isolated Chromium Playwright Verification (`pass3`)**:
  - Responsive & Light/Dark audit (`320px`, `375px`, `768px`, `1024px`, `1440px`): **10 / 10** passed (`overflowX: false`, `brokenImgCount: 0`, `zeroIconCount: 0`).
  - Booking Digital QR pass (`375px`, `1024px`, `1440px`, Light & Dark): **6 / 6** passed (`180×180px`, `squareRatio: 1`, `insideWrap: true`).
  - Browser `F5` Reload Replay test: **Passed** (`callCount: 2`, headline transitioned idempotently to `Thanh toán PayPal Sandbox đã xác nhận`).
  - Print Layout (`@media print`) test: **Passed** (`headerHidden`, `footerHidden`, `actionsHidden`, `receiptVisible`, `qrVisibleInPrint` all `true`).
  - Runtime browser errors: **`0` console errors, `0` page errors**.
