# SPIKE OS — Complete Route, Role & Selector Matrix

**Document Version:** 1.0  
**Branch:** `feature/spike-os-redesign`

---

## 1. Public & Customer Routes

| URL Route | HTTP | Auth Policy | View File | Critical Protected DOM IDs & Selectors |
|---|---|---|---|---|
| `/` | `GET` | Anonymous | `Views/Home/Index.cshtml` | `#statTotalCourts`, `#statAvailableNow`, `#statBookedSlots`, `#statOpenHours`, `#quickBookCourt`, `#quickBookDate`, `#quickBookStart`, `#quickBookDuration`, `#quickBookQuotePrice`, `#btnQuickBookGo`, `#homeCourtsGrid`, `#homeDateFilter`, `#homeMatrixWrap`, `#homeProductsGrid` |
| `/Booking` | `GET` | Anonymous | `Views/Booking/Index.cshtml` | `#bookingCourtSelect`, `#bookingDateInput`, `#bookingDurationGroup`, `#bookingSlotMatrixWrap`, `#bookingTimelineBar`, `#bookingCustomerName`, `#bookingCustomerPhone`, `#bookingPlayerCount`, `#bookingNotes`, `#summaryCourtName`, `#summaryDateText`, `#summaryTimeRange`, `#summaryDurationText`, `#summaryBasePrice`, `#summaryPeakFee`, `#summaryVipFee`, `#summaryTotalAmount`, `#btnSubmitBooking`, `#bookingSuccessModal`, `#bookingDigitalTicketHost` |
| `/Product` | `GET` | Anonymous | `Views/Product/Index.cshtml` | `#shopCategoryTabs`, `#shopSearchInput`, `#shopSortSelect`, `#shopProductsGrid`, `#shopCartItems`, `#shopCartTotal`, `#shopCustomerName`, `#shopCustomerPhone`, `#shopBookingCode`, `#btnVerifyShopBooking`, `#btnSubmitShopOrder`, `#headerCartCount` |
| `/Lookup` | `GET` | Anonymous | `Views/Lookup/Index.cshtml` | `#lookupQueryInput`, `#btnLookupSearch`, `#lookupResultsWrap`, `#lookupQrModal`, `#lookupDigitalTicketHost` |
| `/Account/Login` | `GET, POST` | Anonymous | `Views/Account/Login.cshtml` | `#loginEmail`, `#loginPassword`, `#btnSubmitLogin`, `#regFullName`, `#regPhone`, `#regEmail`, `#regPassword`, `#regConfirmPassword`, `#forgotPasswordModal`, `#forgotAccountInput` |
| `/Account/Profile` | `GET` | `CustomerCookie` | `Views/Account/Profile.cshtml` | `#tabBookings`, `#tabOrders`, `#tabSettings`, `#profileFullName`, `#profilePhone`, `#profileEmail`, `#currentPassword`, `#newPassword`, `#confirmNewPassword` |

---

## 2. Admin & Staff Workspace Routes

| URL Route | HTTP | Auth Policy | View File | Critical Protected DOM IDs & Selectors |
|---|---|---|---|---|
| `/Admin` | `GET` | `Admin, Staff` | `Areas/Admin/Views/Home/Index.cshtml` | `#adminKpiRevenue`, `#adminKpiBookings`, `#adminKpiOccupancy`, `#adminKpiPending`, `#adminPendingQueue`, `#adminRecentBookingsTable`, `#adminRevenueChart`, `#adminCourtUtilList` |
| `/Management` | `GET` | `Admin, Staff` | `Areas/Admin/Views/Home/Management.cshtml` | `#mgmtDateInput`, `#mgmtCourtFilter`, `#mgmtStatusFilter`, `#mgmtSearchInput`, `#mgmtTimelineGrid`, `#mgmtBookingDrawer`, `#drawerPosList`, `#drawerPaymentLedger`, `#walkinModal`, `#slotLockModal` |
| `/Checkin` | `GET` | `Admin, Staff` | `Areas/Admin/Views/Home/Checkin.cshtml` | `#btnToggleCamera`, `#qrVideo`, `#qrFileInput`, `#qrManualInput`, `#btnVerifyManualQr`, `#checkinResult`, `#btnConfirmCheckin` |
| `/Reports` | `GET` | `Admin, Staff` | `Areas/Admin/Views/Home/Reports.cshtml` | `#reportPeriodFilter`, `#btnExportReportsCsv`, `#repTotalRevenue`, `#repCollectedAmount`, `#repOutstandingDebt`, `#repBookingRevenue`, `#repPosRevenue`, `#repDailyChart`, `#repCourtBreakdown`, `#repProductBreakdown` |
| `/Cms` | `GET` | `AdminOnly` | `Areas/Admin/Views/Home/Cms.cshtml` | `#cmsClubName`, `#cmsHeroTitle`, `#cmsHeroSubtitle`, `#cmsOpenHour`, `#cmsCloseHour`, `#cmsBasePrice`, `#cmsPeakSurcharge`, `#cmsVipSurcharge`, `#btnSaveCms` |
| `/Admin/Bookings` | `GET, POST` | `Admin, Staff` | `Areas/Admin/Views/Bookings/*.cshtml` | `.admin-table`, `.badge`, `.btn-main`, `.btn-outline`, anti-forgery tokens |
| `/Admin/Courts` | `GET, POST` | `AdminOnly` | `Areas/Admin/Views/Courts/*.cshtml` | `.admin-table`, `CourtCode`, `Title`, `CourtType`, `Capacity`, `Image`, `IsActive` |
| `/Admin/Products` | `GET, POST` | `AdminOnly` | `Areas/Admin/Views/Products/*.cshtml` | `#ImageUrlInput`, `#ImageFileInput`, `#PreviewImg`, `ProductName`, `CategoryId`, `Price`, `Quantity` |
| `/Admin/Accounts` | `GET, POST` | `AdminOnly` | `Areas/Admin/Views/Accounts/*.cshtml` | `Username`, `FullName`, `Email`, `Phone`, `RoleId`, `IsActive`, reset-password form |
| `/Admin/Menus` | `GET, POST` | `AdminOnly` | `Areas/Admin/Views/Menus/*.cshtml` | `Title`, `Alias`, `Url`, `Levels`, `ParentId`, `Position`, `IsActive` |
| `/Admin/Login` | `GET, POST` | Anonymous | `Areas/Admin/Views/Login/Index.cshtml` | `username`, `password`, `@Html.AntiForgeryToken()` |
| `/Admin/Register` | `GET, POST` | `AdminOnly` | `Areas/Admin/Views/Register/Index.cshtml` | `FullName`, `Username`, `Email`, `Phone`, `Password`, `RoleId` |

---

## 3. JSON API Endpoints (Unchanged Contracts)

| Endpoint | Method | Auth | Purpose |
|---|---|---|---|
| `/api/bootstrap` | `GET` | Public | Returns courts, products, categories, bookings, orders, slotLocks, clubSettings, and pricing config |
| `/api/security/csrf` | `GET` | Public | Issues fresh anti-forgery cookie & JSON `{ token, csrfToken }` |
| `/api/bookings/quote` | `POST` | Public | Server-authoritative Normal/Peak/VIP pricing breakdown & `pricingVersion` |
| `/api/bookings` | `POST` | Public / Staff | Creates booking with `Serializable` + `sp_getapplock` concurrency protection |
| `/api/bookings/lookup` | `GET` | Public | Searches bookings by code or 10-digit phone |
| `/api/bookings/{code}` | `GET` | Public / Owner / Staff | Retrieves booking detail and QR metadata |
| `/api/bookings/{code}/qr.svg` & `/qr.png` | `GET` | Public / Owner / Staff | Generates signed `SPK1.*` QR vector/raster image |
| `/api/bookings/{code}/approve` | `POST` | `StaffOrAdmin` | Approves pending booking |
| `/api/bookings/{code}/cancel` | `POST` | Owner / `StaffOrAdmin` | Cancels booking & restocks unpaid POS/linked order items |
| `/api/bookings/{code}/finish` | `POST` | `StaffOrAdmin` | Closes playing session without faking payment |
| `/api/bookings/{code}/extend` | `POST` | `StaffOrAdmin` | Extends duration (`+30m`/`+60m`) with adjacent-slot conflict detection |
| `/api/admin/bookings/{code}/pos-item` | `POST` | `StaffOrAdmin` | Adds/removes POS items on an active booking |
| `/api/admin/bookings/{code}/payments` | `POST` | `StaffOrAdmin` | Records cash/transfer payment in `tb_PaymentTransaction` |
| `/api/admin/bookings/{code}/refunds` | `POST` | `StaffOrAdmin` | Records counter-entry refund in `tb_PaymentTransaction` |
| `/api/checkin/verify` & `/api/checkin/confirm` | `POST` | `StaffOrAdmin` | Verifies `SPK1.*` token / booking code and transitions `approved -> playing` |
| `/api/orders/create` | `POST` | Public / Staff | Creates Pro Shop order (standalone or linked to booking) |
| `/api/admin/orders/{code}/fulfill` | `POST` | `StaffOrAdmin` | Updates order fulfillment status (`preparing`, `delivered`, `cancelled`) |
| `/api/admin/orders/{code}/payments` | `POST` | `StaffOrAdmin` | Records Pro Shop order payment |
| `/api/slot-locks` | `POST, DELETE` | `StaffOrAdmin` | Creates or removes court maintenance lock |
| `/api/admin/cms` | `POST` | `AdminOnly` | Updates club settings & pricing configuration |
