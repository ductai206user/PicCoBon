/* ==========================================================================
   SPIKE OS — MODERN SPORTS MANAGEMENT PLATFORM
   Member Profile & Athlete Passport Controller (/Account/Profile)
   ========================================================================== */

function switchProfileTab(tabKey, btnEl) {
  document.querySelectorAll('.profile-tab-btn').forEach(b => b.classList.remove('active'));
  document.querySelectorAll('.profile-tab-pane').forEach(p => p.classList.remove('active'));
  if (btnEl) btnEl.classList.add('active');
  const target = document.getElementById('tab-' + tabKey);
  if (target) target.classList.add('active');
}

function selectPresetAvatar(url, btnEl) {
  const input = document.getElementById('profAvatar');
  const preview = document.getElementById('liveAvatarPreview');
  if (input) input.value = url;
  if (preview) preview.src = url;
  document.querySelectorAll('.avatar-preset-item').forEach(b => b.classList.remove('selected'));
  if (btnEl) btnEl.classList.add('selected');
}

function setPaddleBrand(brand) {
  const input = document.getElementById('profPaddle');
  const preview = document.getElementById('livePaddlePreview');
  if (input) input.value = brand;
  if (preview) preview.textContent = brand;
}
