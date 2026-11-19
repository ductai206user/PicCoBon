# SPIKE OS — Project Audit & Architecture Inventory

**Document Version:** 1.0  
**Platform:** Spike OS — Modern Sports Management Platform (ASP.NET Core 10 MVC + EF Core 10 + SQL Server)  
**Branch:** `feature/spike-os-redesign` (branched from `6007084`)  
**Brand Direction:** Soft Green Gradient (`#2E8B57` / `#6BCB77` / `#226B48` / `#EAF7EE`)

---

## 1. Executive Summary

Spike Pickleball Club is an ASP.NET Core 10 MVC application powering public court reservation, a Pro Shop e-commerce storefront, customer membership profiles, and an enterprise Staff/Admin backoffice (Live Timeline, Walk-in & POS Console, QR Check-in Scanner, Financial Ledger & Reports, and CMS).

While backend business logic, SQL Server concurrency locks (`sp_getapplock`), immutable payment ledgers (`tb_PaymentTransaction`), inventory movement tracking (`tb_InventoryMovement`), and HMAC-SHA256 QR tokens (`SPK1.*`) are production-hardened, the frontend suffered from **three competing visual eras**:
1. **Legacy Neon-Cyberpunk Stylesheet (`wwwroot/css/styles.css`)** — 4,841 lines (`114.4 KB`) loaded first on every public and admin page, forcing downstream CSS files to rely on `!important` specificity overrides.
2. **Unused Template & Library Bloat** — `wwwroot/admin/` (`2,948` files, `100.01 MB` SB Admin 2 / Bootstrap 4 template) and `wwwroot/lib/` (`60` files, `9.33 MB` Bootstrap 5 / jQuery / Validation) sitting on disk with zero Razor references.
3. **Inline View Monoliths & Orange (`#FF5520`) Brand Remnants** — `Views/Account/Login.cshtml` (`1,781` lines including ~1,000 lines of inline ACE Bot / neon curtain CSS and canvas mascot scripts), `Views/Account/Profile.cshtml` (`~240` lines of inline CSS + inline scripts), plus hardcoded `#FF5520` inline styles in `shared.js`, `management.js`, `reports.js`, `checkin.js`, and SVG favicons.

---

## 2. Route, Controller & View Inventory

### 2.1 Public Customer Routes

| Route | Controller / Action | Razor View & ViewComponents | Frontend JS Modules | Backing APIs |
|---|---|---|---|---|
| `/` | `HomeController.Index` | `Views/Home/Index.cshtml`, `Components/Court/Default.cshtml`, `Components/MenuTop/Default.cshtml` | `shared.js`, `dashboard.js` | `GET /api/bootstrap`, `POST /api/bookings/quote` |
| `/Booking` | `BookingController.Index` | `Views/Booking/Index.cshtml` | `shared.js`, `booking.js` | `GET /api/bootstrap`, `POST /api/bookings/quote`, `POST /api/bookings`, `GET /api/bookings/{code}`, `GET /api/bookings/{code}/qr.svg`, `GET /api/bookings/{code}/qr.png` |
| `/Product` | `ProductController.Index` | `Views/Product/Index.cshtml`, `Components/Product/Default.cshtml` | `shared.js`, `menu.js` | `GET /api/bootstrap`, `POST /api/orders/create`, `GET /api/bookings/lookup` |
| `/Lookup` | `LookupController.Index` | `Views/Lookup/Index.cshtml` | `shared.js`, `lookup.js` | `GET /api/bookings/lookup`, `GET /api/bookings/{code}`, `GET /api/bookings/{code}/qr.svg` |
| `/Account/Login` | `AccountController.Login` | `Views/Account/Login.cshtml` | `shared.js`, `pages/auth.js` | `POST /Account/Login`, `POST /Account/Register`, `POST /api/account/forgot-password/send-otp` |
| `/Account/Profile` | `AccountController.Profile` | `Views/Account/Profile.cshtml` | `shared.js`, `pages/profile.js` | `POST /Account/UpdateProfile`, `POST /Account/ChangePassword`, `POST /api/bookings/{code}/cancel`, `POST /api/orders/{code}/cancel` |

### 2.2 Staff & Admin Operational Routes

| Route | Access Policy | Controller / Action | Razor View | Frontend JS Modules |
|---|---|---|---|---|
| `/Admin` (`/Admin/Home/Index`) | `Admin, Staff` | `Areas.Admin.HomeController.Index` | `Areas/Admin/Views/Home/Index.cshtml` | `shared.js`, `dashboard.js` |
| `/Management` (`/Admin/Home/Management`) | `Admin, Staff` | `Areas.Admin.HomeController.Management` | `Areas/Admin/Views/Home/Management.cshtml` | `shared.js`, `management.js` |
| `/Checkin` (`/Admin/Home/Checkin`) | `Admin, Staff` | `Areas.Admin.HomeController.Checkin` | `Areas/Admin/Views/Home/Checkin.cshtml` | `shared.js`, `checkin.js`, `vendor/jsqr.min.js` |
| `/Reports` (`/Admin/Home/Reports`) | `Admin, Staff` | `Areas.Admin.HomeController.Reports` | `Areas/Admin/Views/Home/Reports.cshtml` | `shared.js`, `reports.js` |
| `/Cms` (`/Admin/Home/Cms`) | `AdminOnly` | `Areas.Admin.HomeController.Cms` | `Areas/Admin/Views/Home/Cms.cshtml` | `shared.js`, `pages/cms.js` |
| `/Admin/Bookings/*` | `Admin, Staff` | `Areas.Admin.BookingsController` | `Index`, `Details`, `Create`, `Edit`, `Delete` | Server-rendered MVC + `shared.js` |
| `/Admin/Courts/*` | `AdminOnly` | `Areas.Admin.CourtsController` | `Index`, `Details`, `Create`, `Edit`, `Delete` | Server-rendered MVC + `shared.js` |
| `/Admin/Products/*` | `AdminOnly` | `Areas.Admin.ProductsController` | `Index`, `Details`, `Create`, `Edit`, `Delete` | Server-rendered MVC + `pages/admin-product-form.js` |
| `/Admin/Accounts/*` | `AdminOnly` | `Areas.Admin.AccountsController` | `Index`, `Details`, `Create`, `Edit`, `Delete` | Server-rendered MVC + `shared.js` |
| `/Admin/Menus/*` | `AdminOnly` | `Areas.Admin.MenusController` | `Index`, `Details`, `Create`, `Edit`, `Delete` | Server-rendered MVC + `shared.js` |
| `/Admin/Login` | `Anonymous` | `Areas.Admin.LoginController.Index` | `Areas/Admin/Views/Login/Index.cshtml` | Server-rendered MVC |
| `/Admin/Register` | `AdminOnly` | `Areas.Admin.RegisterController.Index` | `Areas/Admin/Views/Register/Index.cshtml` | Server-rendered MVC |

---

## 3. Protected Business Logic & Invariants (Zero-Touch Contract)

The following domain services, database entities, and security contracts are strictly preserved without behavioral modification:

1. **Pricing & Peak-Hour Segmentation (`Services/PricingService.cs`)**:
   - Minute-accurate overlap split across Normal (`120,000đ/60m`) and Peak (`16:00–22:00`, `+20,000đ/60m` surcharge) windows plus VIP/Indoor surcharge (`+30,000đ/60m`).
   - Authoritative server quote endpoint `POST /api/bookings/quote` and `pricingVersion` concurrency token.
2. **Booking Concurrency & Lifecycle (`Services/BookingService.cs`)**:
   - `Serializable` transaction + `sp_getapplock` per `(CourtId, BookingDate)` preventing double-booking, slot-lock overlap, and extension collisions.
   - Strict status state machine (`pending` → `approved` → `playing` → `completed` / `cancelled` / `no_show`).
   - `FinishSession` (`POST /api/bookings/{code}/finish`) closes court play without fabricating payment ledger rows.
3. **Immutable Financial Ledger (`tb_PaymentTransaction`)**:
   - Every collection (`payment`) and counter-entry (`refund`) writes an immutable row with `Method` (`cash` / `transfer`), `ReferenceCode` (mandatory for bank transfers), `IdempotencyKey`, and actor audit trail.
4. **Pro Shop Stock & Fulfillment (`Services/OrderService.cs`)**:
   - Atomic stock decrement across `Quantity` and `UnitInStock` with `tb_InventoryMovement` audit entries.
   - Linked-booking cancellation guard (`409 LINKED_ORDER_DELIVERED` if linked order was already delivered).
5. **Cryptographic QR Check-In (`Services/QrTokenService.cs` & `SpikeTicket` in `wwwroot/js/shared.js`)**:
   - Token format `SPK1.{Base64UrlPayload}.{Base64UrlHmacSha256}`.
   - Scannable SVG/PNG rendering in `.spk-ticket-qr-well` (`#FFFFFF !important` background, quiet zone, ECC Medium, zero CSS transforms).
6. **Authentication, RBAC & CSRF (`Program.cs`, `AccountController.cs`)**:
   - Dual cookie schemes (`CustomerCookie`, `AdminCookie`), `/api/security/csrf` anti-forgery header `X-CSRF-TOKEN`, and `MustChangePassword` enforcement filter.

---

## 4. Legacy Bloat & Technical Debt Identified for Removal

| Path | Size / Lines | Diagnosis | Target Action |
|---|---|---|---|
| `wwwroot/admin/` | `100.01 MB` (`2,948` files) | Unused SB Admin 2 / Bootstrap 4 template | Delete in Milestone 6 |
| `wwwroot/lib/` | `9.33 MB` (`60` files) | Unused Bootstrap 5 / jQuery bundles | Delete in Milestone 6 |
| `wwwroot/css/styles.css` | `114.4 KB` (`4,841` lines) | Legacy neon/orange stylesheet | Disconnect in Milestone 2; Delete in Milestone 6 |
| `wwwroot/css/{tokens,components,layouts,pages}.css` | `46.5 KB` (`1,923` lines) | Transitional override stylesheets with `#FF5520` tokens | Replace with `wwwroot/css/{core,components,layouts,pages}/`; Delete in Milestone 6 |
| Inline `<style>` & `<script>` in `Views/` | `~1,850` lines across 7 views | Inline ACE Bot CSS/JS in `Login.cshtml`, `Profile.cshtml`, `Cms.cshtml`, `Products/{Create,Edit}.cshtml`, `Admin/{Login,Register}` | Extract into modular Spike OS CSS & JS files |
