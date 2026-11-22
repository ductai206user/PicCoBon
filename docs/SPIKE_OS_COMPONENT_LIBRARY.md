# SPIKE OS — Reusable Component Library Specification

**Document Version:** 1.0  
**Stylesheet Location:** `wwwroot/css/components/primitives.css` & `wwwroot/css/components/qr-ticket.css`

---

## 1. Navigation Components

### 1.1 Public Topbar (`.public-header` / `.spk-navbar`)
- **Height**: `64px` sticky glassmorphic header (`background: rgba(255, 255, 255, 0.92)`, `backdrop-filter: blur(12px)`, `border-bottom: 1px solid var(--border-default)`).
- **Elements**:
  - Brand Mark (`.brand-mark` with Soft Green gradient badge + `SPIKE OS` wordmark).
  - Primary Nav Links (`/`, `/Booking`, `/Product`, `/Lookup`) with `2px` active `#2E8B57` bottom indicator.
  - Action Cluster: Pro Shop Cart trigger (`#headerCartBtn` with `#headerCartCount` green badge), Account Menu / Login CTA, Primary `Đặt sân ngay` CTA, and Mobile Hamburger button (`#mobileNavToggle`).

### 1.2 Admin App Shell (`.admin-shell`)
- **Sidebar (`.admin-sidebar`)**: `256px` fixed column on desktop, collapsible off-canvas drawer on `<1024px`. Grouped into **Vận hành (Operations)** (`/Admin`, `/Management`, `/Checkin`, `/Reports`) and **Quản trị (Catalog & System)** (`/Cms`, `/Admin/Bookings`, `/Admin/Courts`, `/Admin/Products`, `/Admin/Accounts`, `/Admin/Menus`). Active link highlighted with `background: var(--brand-tint)` (`#EAF7EE`), `color: var(--brand-deep)` (`#226B48`), and `3px` left accent bar (`#2E8B57`).
- **Topbar (`.admin-topbar`)**: `60px` sticky bar with mobile drawer toggle, breadcrumb trail, `Ctrl+K` quick-search trigger (`#btnOpenCmdPalette`), theme toggle (`#btnAdminThemeToggle`), live clock (`#adminLiveClock`), and role badge (`ADMIN` / `STAFF`).

---

## 2. Interactive Controls & Form Primitives

### 2.1 Buttons (`.btn`, `.btn-main`, `.btn-outline`, `.btn-ghost`, `.btn-danger`)
- **Primary CTA (`.btn-main`, `.btn-primary`)**:
  - Background: `var(--brand-cta-gradient)` (`linear-gradient(135deg, #226B48 0%, #2E8B57 100%)`)
  - Text: `#FFFFFF` (`font-weight: 600`, contrast `>= 4.5:1` across all gradient stops)
  - Hover: `linear-gradient(135deg, #1B573A 0%, #226B48 100%)` with subtle `translateY(-1px)` and `--shadow-brand`.
- **Secondary / Outline (`.btn-outline`, `.btn-secondary`)**:
  - Background: `#FFFFFF`, Border: `1px solid var(--border-strong)`, Text: `var(--text-primary)` (`#17251D`).
  - Hover: Background `var(--brand-tint)` (`#EAF7EE`), Border `var(--brand-primary)` (`#2E8B57`), Text `var(--brand-deep)`.
- **Ghost (`.btn-ghost`)**: Transparent background, hover `var(--bg-secondary)`.
- **Danger (`.btn-danger`)**: `#DC2626` background with `#FFFFFF` text (or subtle `#FEF2F2` surface with `#991B1B` text for table actions).

### 2.2 Inputs, Selects & Search Bars (`.form-control`, `.form-select`, `.input-field`)
- Height: `40px` (`36px` in compact admin toolbars), `padding: 0 12px`, `border-radius: 8px`, `border: 1px solid var(--border-strong)`, `background: #FFFFFF`, `color: var(--text-primary)`.
- Focus ring: `border-color: var(--brand-primary)`, `box-shadow: 0 0 0 3px rgba(46, 139, 87, 0.22)`.

---

## 3. Data Display & Domain Components

### 3.1 KPI Stat Cards (`.kpi-card`, `.stat-card`, `.report-kpi-card`)
- Clean `#FFFFFF` surface, `1px solid var(--border-default)`, `border-radius: 12px`, `padding: 20px`.
- Uppercase `12px` muted label, `24px–28px` `800`-weight tabular numeral in `#17251D`, and semantic/green delta badge.

### 3.2 Court × Time Slot Matrix (`.slot-matrix-table`, `.slot-cell`, `.tl-cell`)
- Sticky left court column (`min-width: 170px`) + horizontally scrollable time-slot columns (`06:00` to `21:30`).
- **Available Slot**: `#FFFFFF` surface, `#DFE8E1` border; hover `#EAF7EE` with `#2E8B57` border.
- **Peak Slot (`16:00–22:00`)**: Subtle warm top accent dot or border hint indicating peak pricing.
- **Selected Contiguous Range**: `var(--brand-cta-gradient)` fill with `#FFFFFF` bold time text and checkmark.
- **Booked / Locked / Past Slot**: Muted `#F1F5F2` or semantic status tint with `cursor: not-allowed`.

### 3.3 Data Tables (`.data-table`, `.admin-table`)
- Sticky uppercase header (`background: var(--bg-secondary)`, `font-size: 12px`, `color: var(--text-secondary)`).
- `1px solid var(--border-default)` row hairlines, hover highlight `rgba(234, 247, 238, 0.45)`, right-aligned tabular currency cells, and compact action button groups.

### 3.4 Modals, Slide-Over Drawers & Toasts
- **Modal (`.modal-backdrop`, `.modal-box`)**: Backdrop blur `4px`, centered card (`max-width: 560px`), `ESC` key and backdrop click dismissal.
- **Slide-Over Drawer (`.booking-drawer`)**: Right-anchored `480px` operational inspector for POS items, payment ledger collection, refunds, extensions, and cancellations.
- **Toast Stack (`#toastContainer`, `.toast-msg`)**: Top-right non-blocking notifications with semantic left border (`#2E8B57` success, `#D97706` warning, `#DC2626` error).

### 3.5 Protected Digital QR Pass (`.spk-ticket-card`, `.spk-ticket-qr-well`)
- Isolated in `wwwroot/css/components/qr-ticket.css`.
- `.spk-ticket-qr-well` enforces `background: #FFFFFF !important`, `padding: 14px`, `border: 2px solid #17251D`, and `transform: none !important` so camera and file-based `jsQR` decoders achieve 100% scan reliability in both light and dark modes.
