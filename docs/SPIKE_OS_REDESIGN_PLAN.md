# SPIKE OS — Complete Redesign & Frontend Replacement Plan

**Document Version:** 1.0  
**Branch:** `feature/spike-os-redesign`  
**Visual Identity:** Spike OS — Soft Green Gradient (`#2E8B57` / `#6BCB77` / `#226B48` / `#EAF7EE`)

---

## 1. Strategic Vision

Transform **Spike Pickleball Club** into **Spike OS — Modern Sports Management Platform**: a ground-up frontend replacement combining:
- **Refined Botanical & Court-Green Energy** (`#2E8B57` primary, `#6BCB77` secondary, `#226B48` deep accessible green, `#EAF7EE` surface tint) for public athlete experiences.
- **Calm, High-Density Operational Clarity** (`#FFFFFF` primary surfaces, `#F6F8F6` secondary canvas, `#17251D` crisp dark forest ink, `#DFE8E1` 1px structural hairlines) for Staff and Admin backoffice consoles.
- **Complete Legacy Elimination**: Zero dependency on `wwwroot/css/styles.css`, `wwwroot/admin/`, `wwwroot/lib/`, or inline view `<style>/<script>` blocks.

---

## 2. Seven-Milestone Execution Plan

### Milestone 1 — Project Audit, Test Stabilization & Architecture Plan
- Create recovery branch `feature/spike-os-redesign`.
- Stabilize time-of-day-sensitive test fixtures in `PickleballClub.Tests/V4ReleaseBlockersAndQrTests.cs` and `PickleballClub.Tests/SqlServerConcurrencyAndMigrationTests.cs` (`38/38` xUnit tests green at any hour).
- Preserve baseline screenshots in `docs/ui-spike-os/screenshots/before/`.
- Publish architecture, design system, component library, route matrix, and rollback documentation in `docs/`.

### Milestone 2 — Spike OS Design System & Core Architecture
- Build standalone modular CSS architecture in `wwwroot/css/`:
  - `core/tokens.css` — Soft Green Gradient palette, typography, spacing, elevation, semantic status tokens, and dark mode tokens.
  - `core/reset.css` & `core/utilities.css` — Modern CSS reset, focus-visible rings, tabular numbers, layout primitives, and print stylesheet.
  - `components/primitives.css` — Spike OS Buttons, Inputs, Cards, KPI StatCards, DataTables, Status Badges, Tabs, Modals, Slide-over Drawers, Toasts, Empty/Skeleton states.
  - `components/qr-ticket.css` — Isolated `.spk-ticket-*` QR pass styling with `#FFFFFF !important` quiet-zone well.
  - `layouts/public-shell.css` & `layouts/admin-shell.css` — Public 64px glassmorphic header/footer and Admin 256px sidebar/topbar/command palette.
  - `pages/public-pages.css` & `pages/admin-pages.css` — Dedicated page-level rules without legacy overrides.
- Extract all inline view scripts into `wwwroot/js/pages/{auth.js,profile.js,cms.js,admin-product-form.js}` and update `shared.js` tokens from orange to green.
- Point `Views/Shared/_DesignSystemCss.cshtml` exclusively to the new modular CSS files (disconnecting `styles.css`).

### Milestone 3 — Public Website Complete Replacement
- **Home (`/`)**: Editorial Soft Green hero, live club metrics strip, floating quick-booking bar, court directory showcase, interactive today availability matrix, real CMS pricing cards, featured Pro Shop grid, and 3-step booking guide.
- **Booking (`/Booking`)**: Interactive `Court × Time Slot Matrix` with green selection states, peak-hour indicators, duration pills (`60/90/120/180m`), sticky server-quote summary sidebar, mobile sticky bottom bar, and instant QR pass modal.
- **Ticket Lookup (`/Lookup`)**: Clean search console by booking code or phone (`09xxxxxxxx`), status badge cards, and one-click QR ticket modal.
- **Authentication (`/Account/Login`) & Profile (`/Account/Profile`)**:
  - Replace the legacy ACE Bot / neon curtain login page with a modern **Spike OS Soft Green Split-Screen Authentication** experience (brand showcase panel + clean login/register card, password strength bar, and honest `503` OTP notice modal).
  - Replace inline styles in `/Account/Profile` with a clean athlete membership header, KPI summary strip, bookings/orders history tabs, and profile/security settings forms.

### Milestone 4 — Pro Shop Complete Replacement
- **Storefront (`/Product`)**: Category filter pills, search & sort bar, uniform `1:1` product packshot cards with stock badges (`Còn N` / `Sắp hết` / `Hết hàng`), quantity steppers, and a sticky Spike OS Cart Drawer/Sidebar supporting both **Merge into Active Booking** (`#shopBookingCode`) and **Standalone Counter Order** checkout.

### Milestone 5 — Admin & Staff Backoffice Complete Replacement
- **Executive Dashboard (`/Admin`)**: 4 KPI StatCards, 7-day revenue chart in soft green (`#2E8B57`), court utilization progress bars, recent activity table, and Pending Approval queue.
- **Live Operations & POS (`/Management`)**: Date/filter toolbar, `Court × Hour` interactive timeline grid, Walk-In Booking modal, Slot Lock modal, and slide-over **Booking & POS Inspector Drawer** (Quick Add POS, Payment Ledger, Refund, Extend, Cancel).
- **QR Check-In (`/Checkin`)**: Dual-column scanner console (`jsQR` camera viewport + image upload dropzone + manual token/code verify + instant check-in receipt card).
- **Financial Reports (`/Reports`)**: Period filter bar (`today`, `7d`, `30d`, `all`), 5 financial KPI cards, daily revenue bars, court & Pro Shop leaderboards, and CSV export.
- **CMS (`/Cms`) & CRUD Modules (`/Admin/Bookings`, `/Admin/Courts`, `/Admin/Products`, `/Admin/Accounts`, `/Admin/Menus`, `/Admin/Login`, `/Admin/Register`)**: Unified Spike OS forms, tables, and detail cards with zero inline `<style>` or `<script>` blocks.

### Milestone 6 — Functional Verification & Controlled Legacy Cleanup
- Verify all unit, integration, and Playwright E2E flows run cleanly without `styles.css`.
- Delete `wwwroot/css/styles.css`, `wwwroot/css/{tokens,components,layouts,pages}.css`, `wwwroot/admin/` (`100.01 MB`), and `wwwroot/lib/` (`9.33 MB`).
- Document every deleted path in `docs/SPIKE_OS_LEGACY_CLEANUP.md`.

### Milestone 7 — Final Regression Testing, Visual QA & Release Gate
- Execute full build, `38/38` xUnit tests (`105/105` QR decodes), `node --check`, 4-timezone JS unit tests, 16-flow Chrome Playwright E2E suite, and 72-check 4-viewport (`375px`, `768px`, `1024px`, `1440px`) screenshot & layout overflow audit.
- Verify production `PickleballClub` database untouched and publish final release gate documentation.
