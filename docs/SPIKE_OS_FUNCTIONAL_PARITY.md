# SPIKE OS — Functional Preservation & Parity Matrix

**Document Version:** 1.0  
**Branch:** `feature/spike-os-redesign`  
**Contract:** Complete frontend replacement with **zero alteration** to backend business logic, SQL Server concurrency locks, pricing formulas, ledger immutability, RBAC, or QR verification.

---

## 1. Zero-Touch Backend & Domain Verification

| Domain Layer | Verification Mechanism | Status |
|---|---|---|
| **Database Schema & Migrations** | Zero new or altered EF Core migrations (`V2`, `V2_Ledger`, `V4_HoldTtlIdempotencyAndOrderFulfillment` applied identically) | **PASS** |
| **Production Database Safety (`.\SQLEXPRESS` / `PickleballClub`)** | Verified untouched read-only counts (`4` courts, `11` products, `9` bookings, `0` orders, `2` accounts); all automated suites run on disposable `PickleballClub_Test_*` / `PickleballClub_E2E_*` / `PickleballClub_UI_*` DBs | **PASS** |
| **Court Concurrency (`sp_getapplock` + `Serializable`)** | `ParallelBookings_SameCourtOverlappingTime` & `ParallelBookingVsLock_And_ParallelExtendVsAdjacentBooking` verified | **PASS** |
| **Peak-Hour & VIP Segmented Pricing (`PricingService`)** | Server-authoritative `/api/bookings/quote` + `PricingVersion` + zero peak surcharge test + gold/silver member discount | **PASS** |
| **Immutable Financial Ledger (`tb_PaymentTransaction`)** | Separate `FinishSession` vs partial collect vs refund counter-entry; mandatory transfer reference enforcement | **PASS** |
| **Pro Shop Atomic Stock & Linked Cancellation** | `UPDLOCK` stock deduction, idempotency key replay, `tb_InventoryMovement`, and atomic linked order cancellation/restock | **PASS** |
| **QR Check-In (`SPK1.*` HMAC-SHA256)** | Vector SVG & raster PNG generation, quiet zone `.spk-ticket-qr-well` (`#FFFFFF !important`), independent ZXing `105/105` decode benchmark, tamper/expiry/replay rejection | **PASS** |
| **Authentication, RBAC, CSRF & IDOR Guards** | `CustomerCookie` vs `AdminCookie`, `Staff` blocked from `/AdminOnly` (`/Cms`, `/Admin/Courts`, `/Admin/Products`, `/Admin/Accounts`, `/Admin/Menus`), CSRF `ValidateAntiForgeryToken`, guest `bk_*` access token guard | **PASS** |

---

## 2. Frontend UI Selector & Contract Parity Checklist

Every DOM ID, form field, data attribute, and JavaScript controller binding tested by `ShadcnAdminV7ParityTests` and `SafeRemediationRegressionTests` has been preserved identically in the new **Spike OS (Soft Green Gradient)** Razor views and modular assets:

### 2.1 Public Layout & Pages
- **Shared Topbar & Theme Toggle**: `#siteThemeToggleBtn`, `#mobileNavToggle`, `#mobileNavDrawer`, responsive `375px` hamburger menu.
- **Home (`/`, `Views/Home/Index.cshtml`)**:
  - Real server-side counts: `Tổ hợp <span data-cms="courtCount">@initialCourtCount</span> sân`, `#pricePeakCard` real CMS peak pricing (`BasePrice + PeakSurcharge`), featured Pro Shop showcase, floating quick-booking form, live court directory, and interactive day availability preview.
- **Booking (`/Booking`, `Views/Booking/Index.cshtml` + `wwwroot/js/booking.js`)**:
  - `#bookingDateInput`, `#dashMatrixTable`, `#courtsGrid`, `#btnSubmitBooking`, `#modalQR`, `#bookingDigitalTicketHost`, server quote badge (`updateQuoteBadgeDom()` using semantic `.badge-*` classes), and mobile sticky summary bar (`#mobileBookingSummaryBar`).
- **Pro Shop (`/Product`, `Views/Product/Index.cshtml` + `wwwroot/js/menu.js`)**:
  - `#menuGrid`, `#shopCartBadge`, `#mobileShopCartBar`, category/search filters, cart stepper controls, active booking attach input, and order checkout flow.
- **Ticket Lookup (`/Lookup`, `Views/Lookup/Index.cshtml` + `wwwroot/js/lookup.js`)**:
  - `#lookupCode`, `#lookupAccessToken`, `#modalTicketQR`, `#lookupDigitalTicketHost`, member booking history table, and security policy banner.
- **Authentication (`/Account/Login`, `Views/Account/Login.cshtml` + `wwwroot/js/pages/auth.js`)**:
  - Preserved dual-tab Login/Register toggle, `#loginEmail`, `#loginPassword`, `#btnSubmitLogin`, real server `@activeCourtCount sân trực tiếp` stat, honest `503` forgot-password OTP modal, and anti-forgery tokens.
- **Member Profile (`/Account/Profile`, `Views/Account/Profile.cshtml` + `wwwroot/js/pages/profile.js`)**:
  - Athlete membership card, `3`-tab navigation (`info`, `bookings`, `security`), booking cancel actions, and password update flow without inline CSS/JS blocks.

### 2.2 Admin & Staff Backoffice Layout & Pages
- **App Shell (`Areas/Admin/Views/Shared/_Layout.cshtml`)**:
  - `#shAdminShell`, `#shAdminSidebar`, `#shAdminTopbar`, `#shSidebarToggleBtn`, `#shCmdTriggerBtn`, `#shThemeToggleBtn`, `#shCommandModal`, `.sh-nav-group-admin-only` RBAC group visibility.
- **Operational Pages (`/Admin`, `/Management`, `/Checkin`, `/Reports`, `/Cms`)**:
  - Full preservation of KPI telemetry bindings, timeline cell interactions, `#detailDrawer` (POS attach, ledger payment collection, refund, extend, cancel, session close), ZXing & `jsQR` dual check-in pipeline (`camera` + `image upload` + `manual`), financial reports date range filter + CSV export, and live CMS editor preview (`wwwroot/js/pages/cms.js`).
- **MVC CRUD & Auth Pages (`/Admin/Bookings`, `/Admin/Courts`, `/Admin/Products`, `/Admin/Accounts`, `/Admin/Menus`, `/Admin/Login`, `/Admin/Register`)**:
  - Preserved `.sh-auth-card` markers on `/Admin/Login` & `/Admin/Register`, anti-forgery form bindings, and `wwwroot/js/pages/admin-product-form.js` preview logic.
