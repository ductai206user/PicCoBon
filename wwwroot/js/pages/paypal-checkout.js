/* ==========================================================================
   SPIKE OS — PAYPAL SANDBOX RETURN & CANCEL VERIFICATION CONTROLLER
   Never marks an order paid from URL parameters. Always executes CSRF-protected
   POST /api/payments/paypal/{capture|cancel|reconcile} and displays verified status.
   ========================================================================== */

let _ppReturnBusy = false;

function setPpViewState(mode, opts = {}) {
  const host = document.getElementById('paypalOutcomeCard');
  if (!host) return;

  const badgeEl = document.getElementById('ppStatusBadge');
  const iconBoxEl = document.getElementById('ppIconCircle');
  const iconEl = document.getElementById('ppMainIcon');
  const titleEl = document.getElementById('ppOutcomeTitle');
  const descEl = document.getElementById('ppOutcomeSubtitle');
  const metaBoxEl = document.getElementById('ppMetaSummary');
  const actionsEl = document.getElementById('ppActionRow');
  const ticketWrapEl = document.getElementById('ppCompletedTicketWrap');
  const ticketHostEl = document.getElementById('ppCompletedTicketHost');
  const orderReceiptEl = document.getElementById('ppOrderReceiptBox');

  if (ticketWrapEl) ticketWrapEl.style.display = 'none';
  if (orderReceiptEl) orderReceiptEl.style.display = 'none';

  const statusStyles = {
    loading: {
      badgeCls: 'badge-neutral',
      badgeText: 'ĐANG XÁC THỰC PAYPAL SANDBOX...',
      iconBg: 'var(--brand-primary-tint)',
      iconColor: 'var(--brand-primary)',
      iconName: 'lucide:loader-circle'
    },
    completed: {
      badgeCls: 'badge-success',
      badgeText: 'THANH TOÁN PAYPAL THÀNH CÔNG (SANDBOX)',
      iconBg: '#DCFCE7',
      iconColor: '#15803D',
      iconName: 'lucide:check-circle-2'
    },
    cancelled: {
      badgeCls: 'badge-warning',
      badgeText: 'ĐÃ HỦY THANH TOÁN PAYPAL',
      iconBg: '#FEF3C7',
      iconColor: '#B45309',
      iconName: 'lucide:x-circle'
    },
    pending: {
      badgeCls: 'badge-warning',
      badgeText: 'CẦN XÁC NHẬN / ĐANG ĐỐI SOÁT',
      iconBg: '#FEF3C7',
      iconColor: '#B45309',
      iconName: 'lucide:clock-alert'
    },
    refunded: {
      badgeCls: 'badge-danger',
      badgeText: 'ĐÃ HOÀN TIỀN TỰ ĐỘNG QUA PAYPAL',
      iconBg: '#FEE2E2',
      iconColor: '#B91C1C',
      iconName: 'lucide:rotate-ccw'
    },
    failed: {
      badgeCls: 'badge-danger',
      badgeText: 'KHÔNG THỂ HOÀN TẤT THANH TOÁN',
      iconBg: '#FEE2E2',
      iconColor: '#B91C1C',
      iconName: 'lucide:alert-octagon'
    }
  };

  const st = statusStyles[mode] || statusStyles.failed;
  if (badgeEl) {
    badgeEl.className = `badge ${st.badgeCls}`;
    badgeEl.textContent = st.badgeText;
  }
  if (iconBoxEl) {
    iconBoxEl.style.background = st.iconBg;
    iconBoxEl.style.color = st.iconColor;
  }
  if (iconEl) {
    iconEl.setAttribute('icon', st.iconName);
    iconEl.classList.toggle('pp-spin', mode === 'loading');
  }
  if (titleEl) titleEl.textContent = opts.title || '';
  if (descEl) descEl.textContent = opts.message || '';

  const d = opts.data || null;
  if (metaBoxEl) {
    if (d && (d.targetCode || d.payPalOrderId || d.amountVnd)) {
      const rateStr = Number(d.exchangeRateVndPerUsd) > 0
        ? `1 USD = ${Number(d.exchangeRateVndPerUsd).toLocaleString('vi-VN')} đ`
        : '--';
      metaBoxEl.style.display = 'block';
      metaBoxEl.innerHTML = `
        <div class="payment-line">
          <span class="text-muted">Đối tượng thanh toán</span>
          <span class="font-bold text-mono">${escapeHtml(d.targetType === 'order' ? 'Đơn Pro Shop' : 'Lịch đặt sân')} · ${escapeHtml(d.targetCode || '--')}</span>
        </div>
        <div class="payment-line">
          <span class="text-muted">Giá trị hóa đơn (VNĐ)</span>
          <span class="font-bold text-mono" style="color:var(--brand-primary);">${formatMoney(d.amountVnd || 0)}</span>
        </div>
        <div class="payment-line">
          <span class="text-muted">Quy đổi thanh toán (USD Sandbox)</span>
          <span class="font-bold text-mono">${window.SpikePayPal ? SpikePayPal.formatUsd(d.amountUsd) : `$${Number(d.amountUsd || 0).toFixed(2)} USD`}</span>
        </div>
        <div class="payment-line">
          <span class="text-muted">Tỷ giá áp dụng</span>
          <span class="text-mono text-xs">${escapeHtml(rateStr)}</span>
        </div>
        ${d.payPalOrderId ? `
        <div class="payment-line">
          <span class="text-muted">PayPal Order ID</span>
          <span class="text-mono text-xs font-semibold">${escapeHtml(d.payPalOrderId)}</span>
        </div>` : ''}
        ${d.payPalCaptureId ? `
        <div class="payment-line">
          <span class="text-muted">PayPal Capture ID</span>
          <span class="text-mono text-xs font-bold" style="color:var(--color-success);">${escapeHtml(d.payPalCaptureId)}</span>
        </div>` : ''}
        ${d.payPalRefundId ? `
        <div class="payment-line">
          <span class="text-muted">PayPal Refund ID</span>
          <span class="text-mono text-xs font-bold" style="color:var(--color-danger);">${escapeHtml(d.payPalRefundId)}</span>
        </div>` : ''}
        ${d.attemptCode ? `
        <div class="payment-line">
          <span class="text-muted">Mã phiên Spike OS</span>
          <span class="text-mono text-xs">${escapeHtml(d.attemptCode)}</span>
        </div>` : ''}
      `;
    } else {
      metaBoxEl.style.display = 'none';
      metaBoxEl.innerHTML = '';
    }
  }

  // Mount Booking Digital QR Pass on Verified Completed Booking
  if (mode === 'completed' && d && d.booking && ticketWrapEl && ticketHostEl) {
    ticketWrapEl.style.display = 'block';
    const tok = resolveStoredGuestTokenForCode(d.booking.id);
    if (window.SpikeTicket) {
      SpikeTicket.mountTicket('ppCompletedTicketHost', d.booking, {
        accessToken: tok,
        theme: 'light'
      });
    }
  }

  // Render Order summary card on Verified Completed Order
  if (mode === 'completed' && d && d.order && orderReceiptEl) {
    orderReceiptEl.style.display = 'block';
    orderReceiptEl.innerHTML = `
      <div class="sh-subtle-panel" style="padding:14px 16px;text-align:left;">
        <div style="display:flex;align-items:center;justify-content:space-between;gap:8px;margin-bottom:8px;">
          <strong class="text-mono" style="color:var(--brand-primary);font-size:1rem;">${escapeHtml(d.order.code)}</strong>
          <span class="badge badge-success">Đã thu đủ qua PayPal Sandbox</span>
        </div>
        <div class="text-sm" style="color:var(--text-secondary);line-height:1.6;">
          Khách nhận: <strong>${escapeHtml(d.order.customerName)}</strong> · ${escapeHtml(d.order.phone)}<br>
          Hình thức giao: ${escapeHtml(d.order.address || 'Nhận tại quầy Pro Shop')}
        </div>
      </div>
    `;
  }

  if (actionsEl) {
    actionsEl.innerHTML = opts.actionsHtml || `
      <a href="/Booking" class="btn btn-primary">Đặt sân mới</a>
      <a href="/Lookup" class="btn btn-secondary">Tra cứu vé &amp; Lịch đặt</a>
    `;
  }
}

function resolveStoredGuestTokenForCode(code) {
  if (!code) return '';
  try {
    return window.sessionStorage.getItem(`pb_guest_token_${code}`) || '';
  } catch (_) {
    return '';
  }
}

function getPagePayPalBootstrap() {
  const root = document.getElementById('paypalCheckoutPageRoot');
  if (!root) return null;
  return {
    mode: root.getAttribute('data-pp-mode') || 'return',
    isValidState: root.getAttribute('data-pp-valid-state') === 'true',
    stateToken: root.getAttribute('data-pp-state') || '',
    payPalOrderId: root.getAttribute('data-pp-order-id') || '',
    attemptCode: root.getAttribute('data-pp-attempt') || '',
    targetType: root.getAttribute('data-pp-target-type') || 'booking',
    targetCode: root.getAttribute('data-pp-target-code') || ''
  };
}

async function runPayPalReturnCapture() {
  const cfg = getPagePayPalBootstrap();
  if (!cfg || _ppReturnBusy) return;

  if (!cfg.isValidState || !cfg.stateToken) {
    setPpViewState('failed', {
      title: 'Liên kết trả về từ PayPal không hợp lệ',
      message: 'Chữ ký bảo vệ phiên (state) bị thiếu hoặc đã hết hạn. Hệ thống không ghi nhận thanh toán nào từ URL.',
      actionsHtml: `
        <a href="/Lookup" class="btn btn-primary">Mở trang Tra cứu vé</a>
        <a href="/Booking" class="btn btn-secondary">Quay lại Đặt sân</a>
      `
    });
    return;
  }

  _ppReturnBusy = true;
  setPpViewState('loading', {
    title: 'Đang kiểm tra & chốt giao dịch với PayPal Sandbox...',
    message: 'Máy chủ đang gọi PayPal Orders API v2 để xác thực chữ ký phê duyệt, số tiền USD và ghi sổ cái an toàn.'
  });

  try {
    const storedGuestToken = resolveStoredGuestTokenForCode(cfg.targetCode);
    const headers = { 'Content-Type': 'application/json' };
    if (storedGuestToken) headers['X-Booking-Access-Token'] = storedGuestToken;

    const res = await apiFetch('/api/payments/paypal/capture', {
      method: 'POST',
      headers,
      body: JSON.stringify({
        state: cfg.stateToken,
        payPalOrderId: cfg.payPalOrderId || null,
        guestAccessToken: storedGuestToken || null
      })
    });

    const data = await res.json().catch(() => ({}));
    if (res.ok && data && data.ok) {
      const lifecycleNote = data.booking
        ? (data.booking.status === 'pending'
            ? 'Khoản thu đã được ghi nhận vào sổ cái. Trạng thái giữ sân vẫn giữ nguyên Chờ duyệt / Check-in đúng quy trình CLB.'
            : 'Khoản thu đã được ghi nhận an toàn. Xuất trình mã QR bên dưới khi đến sân.')
        : 'Đơn hàng Pro Shop của bạn đã được thanh toán hoàn tất qua PayPal Sandbox.';

      setPpViewState('completed', {
        title: data.deduplicated ? 'Giao dịch PayPal đã được xác nhận trước đó' : 'Thanh toán PayPal Sandbox hoàn tất!',
        message: lifecycleNote,
        data,
        actionsHtml: `
          <a href="${data.targetType === 'order' ? '/Product' : `/Lookup?code=${encodeURIComponent(data.targetCode || cfg.targetCode)}`}" class="btn btn-primary">
            ${data.targetType === 'order' ? 'Tiếp tục xem Pro Shop' : 'Quản lý vé tại Trang Tra cứu'}
          </a>
          <a href="/" class="btn btn-secondary">Về Trang chủ</a>
        `
      });
      return;
    }

    if (data && data.code === 'PAYPAL_CAPTURED_AND_REFUNDED') {
      setPpViewState('refunded', {
        title: 'Thời gian giữ chỗ hết hạn — PayPal đã được hoàn tiền tự động',
        message: data.error || 'Ca đặt sân đã hết hạn giữ chỗ trước khi hoàn tất, hệ thống đã hoàn lại tiền về tài khoản PayPal Sandbox của bạn.',
        data,
        actionsHtml: `
          <a href="/Booking" class="btn btn-primary">Đặt lại khung giờ mới</a>
          <a href="/Lookup" class="btn btn-secondary">Kiểm tra lịch sử vé</a>
        `
      });
      return;
    }

    if (res.status === 409 && (data.code === 'PAYPAL_APPROVAL_REQUIRED' || data.code === 'PAYPAL_CAPTURE_IN_PROGRESS' || data.needsReconciliation)) {
      setPpViewState('pending', {
        title: data.code === 'PAYPAL_APPROVAL_REQUIRED'
          ? 'Giao dịch PayPal chưa hoàn tất bước xác nhận'
          : 'Giao dịch đang chờ đối soát kết quả cuối cùng',
        message: data.error || 'Vui lòng nhấn Đối soát để kiểm tra trạng thái mới nhất từ PayPal Sandbox.',
        data,
        actionsHtml: `
          <button type="button" class="btn btn-primary" onclick="reconcilePayPalAttemptFromPage()">
            <iconify-icon icon="lucide:refresh-cw" width="16"></iconify-icon>
            <span>Kiểm tra &amp; Đối soát lại ngay</span>
          </button>
          <a href="/Lookup" class="btn btn-secondary">Mở trang Tra cứu vé</a>
        `
      });
      return;
    }

    setPpViewState('failed', {
      title: 'Không thể hoàn tất thu tiền PayPal Sandbox',
      message: (data && data.error) || `Máy chủ trả về mã lỗi ${res.status}.`,
      data,
      actionsHtml: `
        <button type="button" class="btn btn-primary" onclick="reconcilePayPalAttemptFromPage()">
          <iconify-icon icon="lucide:refresh-cw" width="16"></iconify-icon>
          <span>Đối soát giao dịch</span>
        </button>
        <a href="/Lookup" class="btn btn-secondary">Quay lại Tra cứu vé</a>
      `
    });
  } catch (_) {
    setPpViewState('pending', {
      title: 'Mất kết nối trong quá trình xác nhận thanh toán',
      message: 'Đừng lo — hệ thống sẽ đối soát tự động với PayPal Sandbox. Bạn có thể bấm nút bên dưới để kiểm tra lại.',
      actionsHtml: `
        <button type="button" class="btn btn-primary" onclick="reconcilePayPalAttemptFromPage()">Thử đối soát lại</button>
        <a href="/Lookup" class="btn btn-secondary">Quay lại Tra cứu</a>
      `
    });
  } finally {
    _ppReturnBusy = false;
  }
}

async function runPayPalCancelSync() {
  const cfg = getPagePayPalBootstrap();
  if (!cfg || _ppReturnBusy) return;

  if (!cfg.isValidState || !cfg.stateToken) {
    setPpViewState('cancelled', {
      title: 'Bạn đã thoát khỏi cổng thanh toán PayPal',
      message: 'Không có giao dịch nào bị trừ tiền.',
      actionsHtml: `
        <a href="/Booking" class="btn btn-primary">Quay lại Đặt sân</a>
        <a href="/Lookup" class="btn btn-secondary">Tra cứu vé của tôi</a>
      `
    });
    return;
  }

  _ppReturnBusy = true;
  setPpViewState('loading', {
    title: 'Đang cập nhật trạng thái hủy thanh toán...',
    message: 'Đang kiểm tra đảm bảo không có khoản thu treo trên PayPal Sandbox.'
  });

  try {
    const storedGuestToken = resolveStoredGuestTokenForCode(cfg.targetCode);
    const headers = { 'Content-Type': 'application/json' };
    if (storedGuestToken) headers['X-Booking-Access-Token'] = storedGuestToken;

    const res = await apiFetch('/api/payments/paypal/cancel', {
      method: 'POST',
      headers,
      body: JSON.stringify({
        state: cfg.stateToken,
        payPalOrderId: cfg.payPalOrderId || null,
        guestAccessToken: storedGuestToken || null
      })
    });

    const data = await res.json().catch(() => ({}));
    if (res.ok && data && data.localStatus === 'Completed') {
      // Edge case: user completed payment in another tab
      setPpViewState('completed', {
        title: 'Giao dịch PayPal này thực tế đã hoàn tất!',
        message: 'Hệ thống phát hiện khoản thanh toán đã được xác nhận thành công trước đó.',
        data
      });
      return;
    }

    setPpViewState('cancelled', {
      title: 'Bạn đã hủy thanh toán qua PayPal Sandbox',
      message: data.targetType === 'order'
        ? 'Đơn Pro Shop vẫn được giữ ở trạng thái Chưa thanh toán. Bạn có thể thanh toán lại hoặc thanh toán tại quầy.'
        : 'Đơn đặt sân của bạn vẫn chưa thu tiền PayPal. Bạn có thể thử thanh toán lại trong Trang Tra cứu hoặc thanh toán trực tiếp tại quầy CLB.',
      data,
      actionsHtml: `
        ${data.targetType === 'booking' && data.targetCode
          ? `<button type="button" class="btn btn-primary" onclick="retryPayPalFromCancel('${escapeAttr(data.targetType)}','${escapeAttr(data.targetCode)}')">
               <iconify-icon icon="lucide:wallet" width="16"></iconify-icon>
               <span>Thanh toán PayPal lại</span>
             </button>`
          : ''}
        <a href="${cfg.targetType === 'order' ? '/Product' : '/Lookup'}" class="btn btn-secondary">
          ${cfg.targetType === 'order' ? 'Quay lại Pro Shop' : 'Mở trang Tra cứu vé'}
        </a>
      `
    });
  } catch (_) {
    setPpViewState('cancelled', {
      title: 'Đã hủy thanh toán PayPal Sandbox',
      message: 'Phiên thanh toán đã được dừng theo yêu cầu của bạn.'
    });
  } finally {
    _ppReturnBusy = false;
  }
}

async function retryPayPalFromCancel(targetType, targetCode) {
  if (!window.SpikePayPal) return;
  const tok = resolveStoredGuestTokenForCode(targetCode);
  const res = await SpikePayPal.createAndRedirect({ targetType, targetCode, guestAccessToken: tok });
  if (res && !res.ok && res.error) {
    showToast('Không thể mở lại PayPal', res.error, 'error');
  }
}

async function reconcilePayPalAttemptFromPage() {
  const cfg = getPagePayPalBootstrap();
  if (!cfg || _ppReturnBusy) return;
  _ppReturnBusy = true;

  setPpViewState('loading', {
    title: 'Đang đối soát trạng thái trực tiếp từ PayPal Sandbox...',
    message: 'Vui lòng chờ giây lát.'
  });

  try {
    const storedGuestToken = resolveStoredGuestTokenForCode(cfg.targetCode);
    const headers = { 'Content-Type': 'application/json' };
    if (storedGuestToken) headers['X-Booking-Access-Token'] = storedGuestToken;

    const res = await apiFetch('/api/payments/paypal/reconcile', {
      method: 'POST',
      headers,
      body: JSON.stringify({
        attemptCode: cfg.attemptCode || null,
        state: cfg.stateToken || null,
        guestAccessToken: storedGuestToken || null
      })
    });

    const data = await res.json().catch(() => ({}));
    if (res.ok && data && data.ok && data.localStatus === 'Completed') {
      setPpViewState('completed', {
        title: 'Đối soát thành công — Thanh toán đã hoàn tất!',
        message: 'Bút toán sổ cái và trạng thái giao dịch đã được đồng bộ chính xác.',
        data
      });
      return;
    }

    setPpViewState(data.needsReconciliation ? 'pending' : 'failed', {
      title: 'Kết quả đối soát PayPal Sandbox',
      message: data.error || `Trạng thái hiện tại: ${data.localStatus || 'Chưa hoàn tất'}`,
      data,
      actionsHtml: `
        <button type="button" class="btn btn-primary" onclick="reconcilePayPalAttemptFromPage()">Thử đối soát lại</button>
        <a href="/Lookup" class="btn btn-secondary">Mở trang Tra cứu vé</a>
      `
    });
  } finally {
    _ppReturnBusy = false;
  }
}

document.addEventListener('DOMContentLoaded', () => {
  const cfg = getPagePayPalBootstrap();
  if (!cfg) return;
  if (cfg.mode === 'cancel') {
    runPayPalCancelSync();
  } else {
    runPayPalReturnCapture();
  }
});
