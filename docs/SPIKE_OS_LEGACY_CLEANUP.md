# SPIKE OS — Legacy UI Cleanup & Asset Audit Report

**Document Version:** 1.0  
**Branch:** `feature/spike-os-redesign`  
**Recovery Baseline Commit:** `6007084` (`feat/notion-redesign`)

---

## 1. Executive Summary

Following complete frontend replacement with the modular **Spike OS — Soft Green Gradient** design system (`wwwroot/css/{core,components,layouts,pages}/*.css` and `wwwroot/js/pages/*.js`), all legacy stylesheets, unused template directories, unused third-party bundles, and inline view `<style>/<script>` monoliths were permanently removed.

- **Total Legacy Files Deleted from `wwwroot/`**: **`3,013` files**
- **Total Disk Footprint Removed from `wwwroot/`**: **`109.50 MB`** (`114,819,200` bytes)
- **Inline `<style>` & `<script>` Lines Removed from Razor Views**: **`~1,850` lines** across 7 views

---

## 2. Deleted Files & Directories Inventory

| Deleted Path | File Count | Size Removed | Reason for Removal | Spike OS Replacement |
|---|---:|---:|---|---|
| `wwwroot/admin/**` | `2,948` files | `100.01 MB` | Unused legacy SB Admin 2 / Bootstrap 4 / FontAwesome / Chart.js / DataTables template bundle (`0` Razor references) | `Areas/Admin/Views/Shared/_Layout.cshtml` + `wwwroot/css/layouts/admin-shell.css` + `wwwroot/css/pages/admin-pages.css` |
| `wwwroot/lib/**` | `60` files | `9.33 MB` | Unused default ASP.NET Core template libraries (`bootstrap`, `jquery`, `jquery-validation`, `jquery-validation-unobtrusive`; `0` Razor references) | Native Vanilla ES6+ modules (`wwwroot/js/*.js`, `wwwroot/js/pages/*.js`) + Spike OS CSS |
| `wwwroot/css/styles.css` | `1` file (`4,841` lines) | `114.4 KB` | Legacy multi-era neon-cyberpunk / `#FF5520` orange monolith that forced downstream specificity wars | `wwwroot/css/core/{tokens,reset}.css`, `wwwroot/css/components/{primitives,qr-ticket}.css`, `wwwroot/css/layouts/{public-shell,admin-shell}.css`, `wwwroot/css/pages/{public-pages,admin-pages}.css` |
| `wwwroot/css/tokens.css` | `1` file (`199` lines) | `6.1 KB` | Transitional `#FF5520` orange override token sheet | `wwwroot/css/core/tokens.css` (Soft Green Gradient `#2E8B57` / `#6BCB77` / `#226B48` / `#EAF7EE`) |
| `wwwroot/css/components.css` | `1` file (`599` lines) | `13.8 KB` | Transitional component override sheet | `wwwroot/css/components/primitives.css` & `wwwroot/css/components/qr-ticket.css` |
| `wwwroot/css/layouts.css` | `1` file (`548` lines) | `12.1 KB` | Transitional layout override sheet | `wwwroot/css/layouts/public-shell.css` & `wwwroot/css/layouts/admin-shell.css` |
| `wwwroot/css/pages.css` | `1` file (`577` lines) | `14.5 KB` | Transitional page override sheet | `wwwroot/css/pages/public-pages.css` & `wwwroot/css/pages/admin-pages.css` |

---

## 3. Inline View `<style>` & `<script>` Extraction Log

| View File | Removed Inline Code | Extracted Destination |
|---|---|---|
| `Views/Account/Login.cshtml` | `~1,000` lines of inline ACE Bot / neon curtain `<style>` + `~400` lines of inline mascot/canvas `<script>` | `wwwroot/css/pages/public-pages.css` (`.spk-auth-*`) + `wwwroot/js/pages/auth.js` |
| `Views/Account/Profile.cshtml` | `239` lines of inline `<style>` + `25` lines of inline `<script>` | `wwwroot/css/pages/public-pages.css` (`.profile-*`, `.member-passport*`) + `wwwroot/js/pages/profile.js` |
| `Areas/Admin/Views/Home/Cms.cshtml` | `69` lines of inline `<script>` | `wwwroot/js/pages/cms.js` |
| `Areas/Admin/Views/Products/Create.cshtml` | `98` lines of inline `<script>` | `wwwroot/js/pages/admin-product-form.js` |
| `Areas/Admin/Views/Products/Edit.cshtml` | `131` lines of inline `<script>` | `wwwroot/js/pages/admin-product-form.js` |
| `Areas/Admin/Views/Login/Index.cshtml` | `41` lines of inline `<style>` | `wwwroot/css/pages/admin-pages.css` (`.sh-auth-*`) |
| `Areas/Admin/Views/Register/Index.cshtml` | `40` lines of inline `<style>` | `wwwroot/css/pages/admin-pages.css` (`.sh-auth-*`) |

---

## 4. Retained Production Assets in `wwwroot/`

- `wwwroot/css/core/{tokens,reset}.css`
- `wwwroot/css/components/{primitives,qr-ticket}.css`
- `wwwroot/css/layouts/{public-shell,admin-shell}.css`
- `wwwroot/css/pages/{public-pages,admin-pages}.css`
- `wwwroot/js/{shared,dashboard,booking,menu,lookup,management,checkin,reports}.js`
- `wwwroot/js/pages/{auth,profile,cms,admin-product-form}.js`
- `wwwroot/js/vendor/jsqr.min.js` (Offline QR image/camera decoder for `/Checkin`)
- `wwwroot/assets/*` & `wwwroot/uploads/products/*` (Active court, hero, and Pro Shop product images)
