/* ==========================================================================
   SPIKE OS — MODERN SPORTS MANAGEMENT PLATFORM
   Member Profile & Athlete Passport Controller (/Account/Profile)
   ========================================================================== */

(function () {
  const MAX_AVATAR_BYTES = 2 * 1024 * 1024;
  const ALLOWED_AVATAR_MIME = new Set(['image/jpeg', 'image/png', 'image/webp']);
  const ALLOWED_AVATAR_EXT = /\.(jpe?g|png|webp)$/i;
  const DEFAULT_UPLOAD_HINT = 'Kéo thả ảnh vào đây hoặc chọn tệp JPG, JPEG, PNG, WEBP (tối đa 2 MB)';

  let activeObjectUrl = null;
  let pulseTimer = null;

  function getEls() {
    return {
      studio: document.getElementById('avatarStudio'),
      dropzone: document.getElementById('avatarDropzone'),
      fileInput: document.getElementById('profAvatarFile'),
      presetInput: document.getElementById('selectedPresetAvatar'),
      clearUploadBtn: document.getElementById('avatarClearUploadBtn'),
      uploadMeta: document.getElementById('avatarUploadMeta'),
      sourcePill: document.getElementById('avatarSourcePill'),
      errorBanner: document.getElementById('avatarUploadError'),
      errorText: document.getElementById('avatarUploadErrorText'),
      livePreview: document.getElementById('liveAvatarPreview'),
      liveFallback: document.getElementById('liveAvatarFallback'),
      inlinePreview: document.getElementById('inlineAvatarPreview'),
      inlineFallback: document.getElementById('inlineAvatarFallback'),
      passportWrap: document.querySelector('.member-avatar-wrap'),
      form: document.getElementById('profileUpdateForm'),
      submitBtn: document.getElementById('profileSaveSubmitBtn'),
      ajaxAlert: document.getElementById('profileAjaxAlert')
    };
  }

  function formatKb(bytes) {
    if (bytes >= 1024 * 1024) {
      return (bytes / (1024 * 1024)).toFixed(2) + ' MB';
    }
    return Math.max(1, Math.round(bytes / 1024)) + ' KB';
  }

  function revokeActiveObjectUrl() {
    if (activeObjectUrl) {
      try { URL.revokeObjectURL(activeObjectUrl); } catch (_) {}
      activeObjectUrl = null;
    }
  }

  function setError(message) {
    const { errorBanner, errorText } = getEls();
    if (!errorBanner || !errorText) return;
    if (message) {
      errorText.textContent = message;
      errorBanner.hidden = false;
      if (typeof window.showToast === 'function') {
        window.showToast('Ảnh đại diện chưa hợp lệ', message, 'error');
      }
    } else {
      errorText.textContent = '';
      errorBanner.hidden = true;
    }
  }

  function showProfileSaveAlert(isSuccess, message) {
    document.querySelectorAll('[data-server-profile-alert="true"]').forEach(el => {
      el.hidden = true;
    });
    const { ajaxAlert } = getEls();
    if (!ajaxAlert) return;
    ajaxAlert.className = isSuccess ? 'sh-alert-success' : 'sh-alert-danger';
    ajaxAlert.style.display = 'flex';
    ajaxAlert.style.alignItems = 'center';
    ajaxAlert.style.gap = '0.65rem';
    ajaxAlert.hidden = false;
    ajaxAlert.innerHTML = '';

    const icon = document.createElement('iconify-icon');
    icon.setAttribute('icon', isSuccess ? 'lucide:check-circle-2' : 'lucide:alert-circle');
    icon.setAttribute('width', '20');
    icon.style.color = isSuccess ? 'var(--color-success)' : 'var(--color-danger)';
    icon.style.flexShrink = '0';

    const span = document.createElement('span');
    span.textContent = message || (isSuccess ? 'Đã cập nhật hồ sơ thành công!' : 'Không thể cập nhật hồ sơ.');

    ajaxAlert.appendChild(icon);
    ajaxAlert.appendChild(span);
  }

  function pulseAvatarCard() {
    const { passportWrap } = getEls();
    if (!passportWrap) return;
    passportWrap.classList.remove('is-updating');
    void passportWrap.offsetWidth;
    passportWrap.classList.add('is-updating');
    clearTimeout(pulseTimer);
    pulseTimer = setTimeout(() => {
      passportWrap.classList.remove('is-updating');
    }, 360);
  }

  function updatePreviewPair(imgEl, fbEl, src) {
    if (!imgEl || !fbEl) return;
    if (src) {
      if (imgEl.getAttribute('src') !== src) {
        imgEl.src = src;
      }
      imgEl.hidden = false;
      fbEl.hidden = true;
    } else {
      imgEl.removeAttribute('src');
      imgEl.hidden = true;
      fbEl.hidden = false;
    }
  }

  function updatePreviews(src, withPulse) {
    const { livePreview, liveFallback, inlinePreview, inlineFallback } = getEls();
    updatePreviewPair(livePreview, liveFallback, src);
    updatePreviewPair(inlinePreview, inlineFallback, src);
    if (withPulse) {
      pulseAvatarCard();
    }
  }

  function updatePresetSelection(activeUrl, hasQueuedUpload) {
    const tiles = document.querySelectorAll('.spk-avatar-tile');
    let matchedName = null;
    const normalized = (activeUrl || '').trim();

    tiles.forEach(tile => {
      const tileUrl = tile.dataset.avatarUrl || '';
      const isMatch = !hasQueuedUpload && normalized !== '' && tileUrl === normalized;
      tile.classList.toggle('is-selected', isMatch);
      tile.setAttribute('aria-pressed', isMatch ? 'true' : 'false');
      if (isMatch) {
        matchedName = tile.dataset.avatarName || 'Spike OS';
      }
    });

    return matchedName;
  }

  function updateSourceState(hasQueuedUpload, file) {
    const { presetInput, sourcePill, dropzone, clearUploadBtn, uploadMeta } = getEls();
    const currentUrl = presetInput ? presetInput.value.trim() : '';

    if (hasQueuedUpload && file) {
      if (dropzone) dropzone.classList.add('has-upload');
      if (clearUploadBtn) clearUploadBtn.hidden = false;
      if (uploadMeta) {
        uploadMeta.textContent = `Sẵn sàng lưu: ${file.name} (${formatKb(file.size)}) — Nhấn "Lưu hồ sơ" để cập nhật`;
      }
      if (sourcePill) {
        sourcePill.textContent = 'Ảnh từ thiết bị (chờ lưu)';
        sourcePill.classList.add('is-active');
      }
      updatePresetSelection('', true);
      return;
    }

    if (dropzone) dropzone.classList.remove('has-upload');
    if (clearUploadBtn) clearUploadBtn.hidden = true;
    if (uploadMeta) uploadMeta.textContent = DEFAULT_UPLOAD_HINT;

    const matchedPreset = updatePresetSelection(currentUrl, false);
    if (sourcePill) {
      if (matchedPreset) {
        sourcePill.textContent = `Nhân vật: ${matchedPreset}`;
        sourcePill.classList.add('is-active');
      } else if (currentUrl.startsWith('/uploads/avatars/')) {
        sourcePill.textContent = 'Ảnh tải lên đã lưu';
        sourcePill.classList.add('is-active');
      } else if (currentUrl !== '') {
        sourcePill.textContent = 'Ảnh đại diện đã lưu';
        sourcePill.classList.add('is-active');
      } else {
        sourcePill.textContent = 'Chưa đặt ảnh đại diện';
        sourcePill.classList.remove('is-active');
      }
    }
  }

  function validateFile(file) {
    if (!file) return 'Không tìm thấy tệp ảnh đã chọn.';
    if (file.size <= 0) return 'Tệp ảnh rỗng, vui lòng chọn tệp khác.';
    if (!ALLOWED_AVATAR_EXT.test(file.name || '') || (file.type && !ALLOWED_AVATAR_MIME.has(file.type))) {
      return 'Định dạng không hỗ trợ. Vui lòng chọn tệp JPG, JPEG, PNG hoặc WEBP.';
    }
    if (file.size > MAX_AVATAR_BYTES) {
      return `Dung lượng ảnh vượt quá giới hạn 2 MB (tệp hiện tại: ${formatKb(file.size)}).`;
    }
    return null;
  }

  function applySelectedFile(file, assignToInput) {
    const { fileInput } = getEls();
    const validationError = validateFile(file);
    if (validationError) {
      if (fileInput) fileInput.value = '';
      setError(validationError);
      return;
    }

    if (assignToInput && fileInput) {
      try {
        const dt = new DataTransfer();
        dt.items.add(file);
        fileInput.files = dt.files;
      } catch (_) {
        setError('Trình duyệt không hỗ trợ kéo thả tệp trực tiếp, vui lòng dùng nút "Chọn ảnh từ máy".');
        return;
      }
    }

    setError(null);
    revokeActiveObjectUrl();
    activeObjectUrl = URL.createObjectURL(file);
    updatePreviews(activeObjectUrl, true);
    updateSourceState(true, file);
  }

  function clearQueuedUpload() {
    const { studio, fileInput, presetInput } = getEls();
    if (fileInput) fileInput.value = '';
    revokeActiveObjectUrl();
    setError(null);

    const savedUrl = (studio && studio.dataset.savedAvatar) || '';
    if (presetInput) presetInput.value = savedUrl;

    updatePreviews(savedUrl, true);
    updateSourceState(false, null);
  }

  async function handleProfileSubmit(e) {
    const { form, submitBtn, studio, fileInput, presetInput } = getEls();
    if (!form || typeof window.fetch !== 'function' || typeof window.FormData !== 'function') {
      return;
    }

    e.preventDefault();
    setError(null);

    if (submitBtn) {
      submitBtn.disabled = true;
      submitBtn.dataset.originalHtml = submitBtn.innerHTML;
      submitBtn.textContent = 'Đang lưu hồ sơ...';
    }

    try {
      const formData = new FormData(form);
      const res = await fetch(form.getAttribute('action') || '/Account/UpdateProfile', {
        method: 'POST',
        headers: {
          'X-Requested-With': 'XMLHttpRequest'
        },
        body: formData
      });

      const data = await res.json().catch(() => ({}));
      if (!res.ok || !data || !data.ok) {
        const errMsg = (data && data.error) || 'Không thể lưu hồ sơ. Vui lòng kiểm tra lại thông tin.';
        showProfileSaveAlert(false, errMsg);
        if (typeof window.showToast === 'function') {
          window.showToast('Lưu hồ sơ thất bại', errMsg, 'error');
        }
        return;
      }

      if (data.csrfToken) {
        const csrfInput = form.querySelector('input[name="__RequestVerificationToken"]');
        if (csrfInput) csrfInput.value = data.csrfToken;
        if (typeof window.setCsrfToken === 'function') {
          window.setCsrfToken(data.csrfToken);
        }
      }

      if (fileInput) fileInput.value = '';
      revokeActiveObjectUrl();

      const savedUrl = data.avatar || '';
      const savedName = data.fullName || '';
      const initials = data.initials || 'U';

      if (studio) studio.dataset.savedAvatar = savedUrl;
      if (presetInput) presetInput.value = savedUrl;

      const { liveFallback, inlineFallback } = getEls();
      if (liveFallback) liveFallback.textContent = initials;
      if (inlineFallback) inlineFallback.textContent = initials;

      updatePreviews(savedUrl, true);
      updateSourceState(false, null);

      const liveName = document.getElementById('liveNamePreview');
      if (liveName && savedName) liveName.textContent = savedName;

      if (window.SpikeAvatarSync) {
        window.SpikeAvatarSync.setGlobalAvatar(savedUrl, savedName);
      }

      const okMsg = data.message || 'Đã cập nhật Hồ sơ Hội viên & Vận động viên thành công!';
      showProfileSaveAlert(true, okMsg);
      if (typeof window.showToast === 'function') {
        window.showToast('Đã lưu hồ sơ', okMsg, 'success');
      }
    } catch (_) {
      const connErr = 'Lỗi kết nối máy chủ khi lưu hồ sơ. Vui lòng thử lại.';
      showProfileSaveAlert(false, connErr);
      if (typeof window.showToast === 'function') {
        window.showToast('Lỗi kết nối', connErr, 'error');
      }
    } finally {
      if (submitBtn) {
        submitBtn.disabled = false;
        if (submitBtn.dataset.originalHtml) {
          submitBtn.innerHTML = submitBtn.dataset.originalHtml;
        }
      }
    }
  }

  function initAvatarStudio() {
    const { dropzone, fileInput, clearUploadBtn, livePreview, inlinePreview, form } = getEls();
    if (!dropzone || !fileInput) return;

    const handlePreviewError = (imgEl) => {
      if (!imgEl) return;
      imgEl.addEventListener('error', () => {
        if (window.SpikeAvatarSync) {
          window.SpikeAvatarSync.handleImageError(imgEl);
        }
      });
    };
    handlePreviewError(livePreview);
    handlePreviewError(inlinePreview);

    fileInput.addEventListener('change', () => {
      const file = fileInput.files && fileInput.files[0];
      if (!file) {
        clearQueuedUpload();
        return;
      }
      applySelectedFile(file, false);
    });

    if (clearUploadBtn) {
      clearUploadBtn.addEventListener('click', (e) => {
        e.preventDefault();
        e.stopPropagation();
        clearQueuedUpload();
      });
    }

    dropzone.addEventListener('click', (e) => {
      if (e.target.closest('label[for="profAvatarFile"], button')) return;
      fileInput.click();
    });

    dropzone.addEventListener('dragenter', (e) => {
      e.preventDefault();
      dropzone.classList.add('is-dragover');
    });

    dropzone.addEventListener('dragover', (e) => {
      e.preventDefault();
      dropzone.classList.add('is-dragover');
    });

    dropzone.addEventListener('dragleave', (e) => {
      if (!dropzone.contains(e.relatedTarget)) {
        dropzone.classList.remove('is-dragover');
      }
    });

    dropzone.addEventListener('drop', (e) => {
      e.preventDefault();
      dropzone.classList.remove('is-dragover');
      const file = e.dataTransfer && e.dataTransfer.files && e.dataTransfer.files[0];
      if (file) {
        applySelectedFile(file, true);
      }
    });

    if (form) {
      form.addEventListener('submit', handleProfileSubmit);
    }

    updateSourceState(false, null);
  }

  window.switchProfileTab = function (tabKey, btnEl) {
    document.querySelectorAll('.profile-tab-btn').forEach(b => b.classList.remove('active'));
    document.querySelectorAll('.profile-tab-pane').forEach(p => p.classList.remove('active'));
    if (btnEl) btnEl.classList.add('active');
    const target = document.getElementById('tab-' + tabKey);
    if (target) target.classList.add('active');
  };

  window.selectPresetAvatar = function (url, btnEl) {
    const { fileInput, presetInput } = getEls();
    if (fileInput) fileInput.value = '';
    revokeActiveObjectUrl();
    setError(null);

    if (presetInput) presetInput.value = url;
    updatePreviews(url, true);
    updateSourceState(false, null);
    if (btnEl) btnEl.focus();
  };

  window.setPaddleBrand = function (brand) {
    const input = document.getElementById('profPaddle');
    const preview = document.getElementById('livePaddlePreview');
    if (input) input.value = brand;
    if (preview) preview.textContent = brand;
  };

  window.payProfileBookingPayPal = async function (bookingCode, btnEl) {
    if (!window.SpikePayPal || !bookingCode) return;
    if (btnEl) btnEl.disabled = true;
    try {
      const res = await SpikePayPal.createAndRedirect({
        targetType: 'booking',
        targetCode: bookingCode
      });
      if (!res || !res.redirecting) {
        showToast('Không thể thanh toán PayPal', (res && res.error) || 'Vui lòng thử lại sau.', 'error');
      }
    } finally {
      if (btnEl) btnEl.disabled = false;
    }
  };

  document.addEventListener('DOMContentLoaded', initAvatarStudio);
  window.addEventListener('beforeunload', revokeActiveObjectUrl);
})();
