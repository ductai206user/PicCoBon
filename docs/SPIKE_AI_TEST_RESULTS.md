# SPIKE OS — GEMINI AI ASSISTANT TEST RESULTS & VERIFICATION

## 1. Automated Build & xUnit Test Suite

- **Release Build**:
  - Command: `dotnet build e:\pickleball\PickleballClub.slnx -c Release --nologo`
  - Result: **Build succeeded — 0 Warning(s), 0 Error(s)**.
- **Full xUnit Automated Test Suite**:
  - Command: `dotnet test e:\pickleball\PickleballClub.Tests\PickleballClub.Tests.csproj -c Release --nologo`
  - Result: **Passed! — Failed: 0, Passed: 75, Skipped: 0, Total: 75**.

### New AI Assistant Automated Tests (`PickleballClub.Tests/GeminiAssistantTests.cs`)

| Test ID | Test Method | Verified Behavior | Status |
| :--- | :--- | :--- | :--- |
| **AI01** | `AI01_PublicPages_MountFloatingMascotAndChatScript_AndStatusEndpointReportsReadyWithoutExposingSecrets` | Verifies `/`, `/Booking`, `/Product`, and `/Lookup` render `#spikeFloatingMascot`, `/js/mascot-assistant.js`, and `/css/components/mascot-assistant.css`; confirms `GET /api/assistant/status` returns `READY` and zero API key substrings appear in HTML or JSON. | **PASSED** |
| **AI02** | `AI02_MissingApiKey_OrUpstreamFailure_ReturnsFriendly503_WithoutCrashingWebsite` | Verifies missing `Gemini:ApiKey` returns `GEMINI_API_KEY_MISSING`, and upstream Gemini timeout/503 returns HTTP `503` with `pose: "thinking"`, friendly Vietnamese fallback text, and verified navigation links (`/Booking`, `/Product`, `/Lookup`). | **PASSED** |
| **AI03** | `AI03_CourtAvailability_AndPricingQuoteTools_UseLiveDatabaseBookingsLocksAndSegmentedPricing` | Verifies multi-round Gemini tool calling (`check_court_availability` + `get_pricing_quote`) respects active bookings, maintenance court locks (`TbCourtLock`), and `PricingService.CalculateQuote` peak/standard pricing (`150.000đ/h` peak). | **PASSED** |
| **AI04** | `AI04_ProShopProductSearch_AndWebsiteGuideTools_ReturnRealCatalogWithoutSlot_AndLabelPayPalSandbox` | Verifies `search_proshop_products` returns real `TbProducts` (`Vợt Joola Perseus Pro Carbon 16mm`, `4.890.000đ`), excludes internal `"Slot"` rows, and `get_website_guide` labels PayPal as **PayPal Sandbox (USD Test Mode)**. | **PASSED** |
| **AI05** | `AI05_MyBookingsTool_EnforcesStrictOwnership_PreventsCrossAccountLeakage_AndSupportsVerifiedGuestToken` | Verifies anonymous callers without a token receive `requiresAuthenticationOrLookup: true`; authenticated User A sees ONLY User A's bookings and receives `found: false` when querying User B's booking code; guest callers with a valid `BookingCode` + `GuestAccessToken` (`QrTokenService.VerifyGuestAccessToken`) can query their own booking. | **PASSED** |
| **AI06** | `AI06_PromptInjection_DisallowedToolExecution_MultiTurnMemoryIsolation_AndInputValidation` | Verifies prompt-injection attempts and non-allowlisted tool calls (`execute_raw_sql_drop_table`) are rejected (`Tool is not permitted.`); multi-turn memory in `IMemoryCache` is isolated across distinct sessions even with identical `conversationId`; `POST /api/assistant/reset` clears memory; messages `> 500` chars return HTTP `400 MESSAGE_TOO_LONG`. | **PASSED** |

---

## 2. Live Google Gemini API & Playwright Browser E2E Verification

Executed an end-to-end headless Chromium (`Playwright`) session against an isolated test instance (`http://127.0.0.1:5394`) connected to the live Google Gemini API (`gemini-3.5-flash-lite`) via `.NET User Secrets`:

- **Mascot Preload & Opening**:
  - `#spikeFloatingMascot` preloaded all 6 transparent PNG poses (`data-preloaded="true"`).
  - Clicking `#spikeFloatingMascot` opened `#spikeAssistantPanel` directly above the mascot and transitioned the mascot to `02_wave.png` (`wave`).
- **Live Multi-Tool Gemini Query**:
  - User prompt submitted in browser: `"Tối mai lúc 19:00 còn sân trống không và giá thuê 1 giờ là bao nhiêu?"`
  - Mascot transitioned immediately to `03_thinking.png` (`thinking`) with typing indicator visible.
  - Backend executed `check_court_availability` and `get_pricing_quote` and returned the live grounded response from Gemini:
    > *"Dạ, hiện tại khung giờ 19:00 tối mai (23/11/2026) đã kín lịch và không còn sân trống. Về giá thuê tham khảo cho khung giờ vàng (17:00–22:00) này: Giá ước tính: 150.000 VNĐ / 60 phút (khoảng $5.77 USD khi thanh toán qua PayPal Sandbox). Bạn có thể truy cập trang Đặt sân (/Booking) để kiểm tra các khung giờ lân cận hoặc chọn ngày khác nhé!"*
  - Mascot transitioned to `05_point.png` (`idea`) upon response arrival and rendered verified action pill `→ Đặt sân trực tuyến` (`/Booking`) and contextual follow-up chips.
- **Responsive & Console Cleanliness**:
  - Verified at Desktop (`1440×900`) and Mobile (`390×844`): `hasHorizontalOverflow: false`, `consoleErrorCount: 0`.
