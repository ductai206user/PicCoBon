# SPIKE OS — Design System Specification (Soft Green Gradient Edition)

**Document Version:** 1.0  
**Design Language:** Spike OS — Modern Sports Management Platform  
**Aesthetic Philosophy:** *"Fresh botanical energy outside, calm operational clarity inside."*

---

## 1. Color System & Brand Identity

Spike OS completely replaces the legacy orange (`#FF5520`) identity with a refined **Soft Green Gradient** palette designed for high-readability SaaS dashboards and premium sports club experiences.

### 1.1 Core Brand & Surface Tokens (Light Mode — Default)

| CSS Custom Property | Value | Role & Usage | WCAG 2.2 AA Contrast |
|---|---|---|---|
| `--brand-primary` | `#2E8B57` | Primary brand accent, active indicators, links, focus borders, chart bars | `4.3:1` on `#FFFFFF` (large text / icons / borders) |
| `--brand-secondary` | `#6BCB77` | Secondary gradient stop, live pulse dots, decorative highlights | Used in gradient or with `#17251D` text (`8.6:1`) |
| `--brand-deep` | `#226B48` | Accessible deep green for primary button backgrounds, hover states, badge text | **`6.45:1` with `#FFFFFF`** (Passes AA Normal Text) |
| `--brand-tint` | `#EAF7EE` | Soft green surface tint for selected slots, active sidebar items, subtle badges | `12.8:1` with `#17251D` text |
| `--brand-tint-hover` | `#D7F0DF` | Hover state for tinted green surfaces and ghost buttons | `11.4:1` with `#17251D` text |
| `--brand-gradient` | `linear-gradient(135deg, #2E8B57 0%, #6BCB77 100%)` | Selective brand gradient for hero accents, badges, and decorative cards | — |
| `--brand-cta-gradient` | `linear-gradient(135deg, #226B48 0%, #2E8B57 100%)` | Primary CTA button background guaranteeing `>= 4.5:1` contrast with `#FFFFFF` text across the entire button | **`4.5:1–6.45:1` with `#FFFFFF`** |
| `--bg-canvas` | `#FFFFFF` | Primary page canvas, cards, tables, modals, drawers | `15.4:1` with `--text-primary` |
| `--bg-secondary` | `#F6F8F6` | Secondary workspace background, subtle section bands, table headers | `14.3:1` with `--text-primary` |
| `--bg-tertiary` | `#EDF2EE` | Input hover wells, skeleton bases, segmented control tracks | `13.1:1` with `--text-primary` |
| `--text-primary` | `#17251D` | Deep forest charcoal for headings, body copy, and data values | `15.4:1` on `#FFFFFF` |
| `--text-secondary` | `#4A5950` | Supporting copy, table labels, form labels | **`7.1:1` on `#FFFFFF`** (Passes AA & AAA) |
| `--text-muted` | `#627168` | Metadata, timestamps, captions, secondary hints | **`4.95:1` on `#FFFFFF`** (Passes AA Normal Text) |
| `--border-default` | `#DFE8E1` | `1px solid` structural card, table, and section dividers | — |
| `--border-strong` | `#C4D4C9` | Input borders, interactive control borders, hover borders | — |

### 1.2 Semantic Operational Status Tokens

Semantic statuses remain distinct so operators can distinguish warnings, errors, and peak-hour states at a glance without confusing them with brand green:

| Semantic State | Surface (`bg`) | Text (`fg`) | Border (`border`) | Domain Mapping |
|---|---|---|---|---|
| **Success / Available / Paid** | `#EAF7EE` | `#166534` (`7.1:1`) | `#B7E4C7` | Available slot, Approved, Paid (`paid`), In Stock |
| **Warning / Pending / Peak** | `#FFFBEB` | `#92400E` (`7.4:1`) | `#FDE68A` | Pending (`pending`), Partial Payment (`partial`), Peak Hour (`16:00–22:00`), Low Stock |
| **Danger / Conflict / Unpaid** | `#FEF2F2` | `#991B1B` (`7.8:1`) | `#FECACA` | Cancelled (`cancelled`), Unpaid Debt (`unpaid`), Slot Locked (`locked`), Out of Stock |
| **Info / Playing / In-Session** | `#EFF6FF` | `#1E40AF` (`8.1:1`) | `#BFDBFE` | Currently Playing (`playing`), Completed (`completed`), Transfer Reference |

### 1.3 Dark Mode Tokens (`[data-theme="dark"]`)

- `--bg-canvas`: `#0C1410`
- `--bg-secondary`: `#121E18`
- `--bg-tertiary`: `#1A2B22`
- `--text-primary`: `#ECFDF3`
- `--text-secondary`: `#A3B8AC`
- `--text-muted`: `#7A9083`
- `--border-default`: `#21352B`
- `--border-strong`: `#2F4B3D`
- `--brand-primary`: `#4ADE80`
- `--brand-tint`: `rgba(46, 139, 87, 0.22)`

---

## 2. Typography Scale & Font Stack

- **Display / Headings (`--font-display`)**: `'Plus Jakarta Sans', 'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif`
- **Body / UI Controls (`--font-sans`)**: `'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif`
- **Monospace / Codes / Prices (`--font-mono`)**: `'JetBrains Mono', ui-monospace, SFMono-Regular, Consolas, monospace` with `font-variant-numeric: tabular-nums`

| Token | Size | Line Height | Weight | Tracking | Typical Usage |
|---|---|---|---|---|---|
| `display-xl` | `clamp(2.25rem, 4vw, 3.25rem)` | `1.08` | `800` | `-0.03em` | Public hero headline |
| `h1` | `clamp(1.75rem, 2.5vw, 2.25rem)` | `1.15` | `700` | `-0.025em` | Page titles |
| `h2` | `clamp(1.35rem, 2vw, 1.65rem)` | `1.22` | `700` | `-0.02em` | Section headers |
| `h3` | `1.125rem` (`18px`) | `1.35` | `600` | `-0.015em` | Card titles, modal headers |
| `body-lg` | `1rem` (`16px`) | `1.6` | `400` | `-0.005em` | Lead paragraphs |
| `body` | `0.9375rem` (`15px`) | `1.55` | `400` | `0` | Standard UI copy |
| `body-sm` | `0.8125rem` (`13px`) | `1.45` | `500` | `0` | Table cells, form labels, secondary controls |
| `caption` | `0.75rem` (`12px`) | `1.35` | `600` | `+0.04em` | Uppercase kickers, status badges, slot timestamps |

---

## 3. Spacing, Grid, Radius & Elevation

- **8px Spatial Rhythm**:
  - `--space-1: 4px`, `--space-2: 8px`, `--space-3: 12px`, `--space-4: 16px`, `--space-5: 20px`, `--space-6: 24px`, `--space-8: 32px`, `--space-10: 40px`, `--space-12: 48px`, `--space-16: 64px`.
- **Container Widths**:
  - Public container: `max-width: 1280px`, horizontal padding `clamp(16px, 4vw, 32px)`.
  - Admin workspace container: `max-width: 1600px`, fluid 12-column grid.
- **Border Radius**:
  - `--radius-xs: 6px` (badges, slot cells)
  - `--radius-sm: 8px` (buttons, inputs, selects)
  - `--radius-md: 12px` (cards, tables, dropdowns)
  - `--radius-lg: 16px` (modals, hero showcase cards, auth container)
  - `--radius-pill: 9999px` (filter pills, status chips)
- **Shadow & Elevation**:
  - `--shadow-xs`: `0 1px 2px rgba(23, 37, 29, 0.04)`
  - `--shadow-sm`: `0 2px 8px -2px rgba(23, 37, 29, 0.06), 0 1px 3px rgba(23, 37, 29, 0.04)`
  - `--shadow-md`: `0 8px 24px -4px rgba(23, 37, 29, 0.08), 0 2px 6px -1px rgba(23, 37, 29, 0.04)`
  - `--shadow-lg`: `0 20px 40px -12px rgba(23, 37, 29, 0.14)`
  - `--shadow-brand`: `0 6px 18px -4px rgba(46, 139, 87, 0.28)`
