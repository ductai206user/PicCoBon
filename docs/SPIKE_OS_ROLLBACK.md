# SPIKE OS — Recovery & Rollback Plan

**Document Version:** 1.0  
**Active Feature Branch:** `feature/spike-os-redesign`  
**Baseline Recovery Commit:** `6007084` (`feat/notion-redesign`)

---

## 1. Recovery Guarantees

1. **Zero Database Mutation**:
   - No EF Core migrations were added, modified, or removed.
   - The production SQL Express database (`.\SQLEXPRESS`, `PickleballClub`) is never modified by automated tests; all xUnit, Playwright E2E, and screenshot verification scripts run against isolated, disposable temporary SQL databases (`PickleballClub_E2E_*`, `PickleballClub_Test_*`) that are dropped upon completion.
2. **Isolated Feature Branch**:
   - All Spike OS frontend replacement and legacy cleanup changes are committed exclusively to `feature/spike-os-redesign`.
   - The pre-Spike-OS state remains intact on `feat/notion-redesign` (`6007084`).

---

## 2. Rollback Procedures

### Scenario A — Full Rollback to Pre-Spike-OS Baseline (`6007084`)
If an immediate full rollback to the pre-Spike-OS state is required:

```powershell
git checkout feat/notion-redesign
dotnet build PickleballClub.csproj -c Release
```

### Scenario B — Selective Restoration of Deleted Legacy Assets
If a specific deleted file from `wwwroot/css/styles.css`, `wwwroot/admin/`, or `wwwroot/lib/` needs to be inspected or restored on `feature/spike-os-redesign`:

```powershell
# Restore a single file from baseline commit 6007084:
git checkout 6007084 -- wwwroot/css/styles.css
```

### Scenario C — Post-Rollback Verification Checklist
After any rollback action, execute:
1. `dotnet build PickleballClub.csproj -c Release` (Verify `0` errors, `0` warnings).
2. `dotnet test PickleballClub.Tests/PickleballClub.Tests.csproj -c Release` (Verify all tests pass).
3. Inspect `SELECT COUNT(*) FROM tb_Court`, `tb_Product`, `tb_Booking`, `tb_Order` on `.\SQLEXPRESS` `PickleballClub` to confirm operational data integrity.
