/* ==========================================================================
   SPIKE OS — PAYPAL SANDBOX RETURN & CANCEL VERIFICATION CONTROLLER
   Never marks an order paid from URL parameters. Always executes CSRF-protected
   POST /api/payments/paypal/{capture|cancel|reconcile} and binds only from
   server-verified PayPal transaction records.
   ========================================================================== */

let _ppReturnBusy = false;

function formatCompactVnd(amountVnd) {
  const num = Math.max(0, Math.round(Number(amountVnd) || 0));
  return `${num.toLocaleString('vi-VN')}đ`;
}

const RECEIPT_SVG_PATHS = {
  'copy': '<rect width="14" height="14" x="8" y="8" rx="2" ry="2"/><path d="M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2"/>',
  'shopping-cart': '<circle cx="8" cy="21" r="1"/><circle cx="19" cy="21" r="1"/><path d="M2.05 2.05h2l2.66 12.42a2 2 0 0 0 2 1.58h9.78a2 2 0 0 0 1.95-1.57l1.65-7.43H5.12"/>',
  'ticket': '<path d="M2 9a3 3 0 0 1 0 6v2a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-2a3 3 0 0 1 0-6V7a2 2 0 0 0-2-2H4a2 2 0 0 0-2 2Z"/><path d="M13 5v2"/><path d="M13 17v2"/><path d="M13 11v2"/>',
  'credit-card': '<rect width="20" height="14" x="2" y="5" rx="2"/><line x1="2" x2="22" y1="10" y2="10"/>',
  'shield-check': '<path d="M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z"/><path d="m9 12 2 2 4-4"/>',
  'refresh-cw': '<path d="M3 12a9 9 0 0 1 9-9 9.75 9.75 0 0 1 6.74 2.74L21 8"/><path d="M21 3v5h-5"/><path d="M21 12a9 9 0 0 1-9 9 9.75 9.75 0 0 1-6.74-2.74L3 16"/><path d="M8 16H3v5"/>',
  'user': '<path d="M19 21v-2a4 4 0 0 0-4-4H9a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/>',
  'phone': '<path d="M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.5 19.5 0 0 1-6-6 19.79 19.79 0 0 1-3.07-8.67A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72 12.84 12.84 0 0 0 .7 2.81 2 2 0 0 1-.45 2.11L8.09 9.91a16 16 0 0 0 6 6l1.27-1.27a2 2 0 0 1 2.11-.45 12.84 12.84 0 0 0 2.81.7A2 2 0 0 1 22 16.92z"/>',
  'store': '<path d="m2 7 4.41-4.41A2 2 0 0 1 7.83 2h8.34a2 2 0 0 1 1.42.59L22 7"/><path d="M4 12v8a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-8"/><path d="M15 22v-4a2 2 0 0 0-2-2h-2a2 2 0 0 0-2 2v4"/><path d="M2 7h20"/><path d="M22 7v3a2 2 0 0 1-2 2a2.7 2.7 0 0 1-1.59-.63.7.7 0 0 0-.82 0A2.7 2.7 0 0 1 16 12a2.7 2.7 0 0 1-1.59-.63.7.7 0 0 0-.82 0A2.7 2.7 0 0 1 12 12a2.7 2.7 0 0 1-1.59-.63.7.7 0 0 0-.82 0A2.7 2.7 0 0 1 8 12a2.7 2.7 0 0 1-1.59-.63.7.7 0 0 0-.82 0A2.7 2.7 0 0 1 4 12a2 2 0 0 1-2-2V7"/>',
  'calendar-clock': '<path d="M21 7.5V6a2 2 0 0 0-2-2H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h3.5"/><path d="M16 2v4"/><path d="M8 2v4"/><path d="M3 10h5"/><path d="M17.5 17.5 16 16.3V14"/><circle cx="16" cy="16" r="6"/>',
  'loader-circle': '<path d="M21 12a9 9 0 1 1-6.219-8.56"/>',
  'x-circle': '<circle cx="12" cy="12" r="10"/><path d="m15 9-6 6"/><path d="m9 9 6 6"/>',
  'clock-alert': '<path d="M12 6v6l4 2"/><path d="M16 21.16a10 10 0 1 1 5-13.516"/><path d="M20 11.5v6"/><path d="M20 21.5h.01"/>',
  'rotate-ccw': '<path d="M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8"/><path d="M3 3v5h5"/>',
  'alert-octagon': '<path d="M12 16h.01"/><path d="M12 8v4"/><path d="M15.312 2a2 2 0 0 1 1.414.586l4.688 4.688A2 2 0 0 1 22 8.688v6.624a2 2 0 0 1-.586 1.414l-4.688 4.688a2 2 0 0 1-1.414.586H8.688a2 2 0 0 1-1.414-.586l-4.688-4.688A2 2 0 0 1 2 15.312V8.688a2 2 0 0 1 .586-1.414l4.688-4.688A2 2 0 0 1 8.688 2z"/>',
  'check-circle-2': '<circle cx="12" cy="12" r="10"/><path d="m9 12 2 2 4-4"/>',
  'clock': '<circle cx="12" cy="12" r="10"/><polyline points="12 6 12 12 16 14"/>',
  'home': '<path d="M15 21v-8a1 1 0 0 0-1-1h-4a1 1 0 0 0-1 1v8"/><path d="M3 10a2 2 0 0 1 .709-1.528l7-5.999a2 2 0 0 1 2.582 0l7 5.999A2 2 0 0 1 21 10v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/>',
  'arrow-right': '<path d="M5 12h14"/><path d="m12 5 7 7-7 7"/>',
  'printer': '<path d="M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2"/><path d="M6 9V3a1 1 0 0 1 1-1h10a1 1 0 0 1 1 1v6"/><rect x="6" y="14" width="12" height="8" rx="1"/>',
  'search': '<circle cx="11" cy="11" r="8"/><path d="m21 21-4.3-4.3"/>',
  'calendar': '<path d="M8 2v4"/><path d="M16 2v4"/><rect width="18" height="18" x="3" y="4" rx="2"/><path d="M3 10h18"/>',
  'calendar-plus': '<path d="M8 2v4"/><path d="M16 2v4"/><path d="M21 13V6a2 2 0 0 0-2-2H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h8"/><path d="M3 10h18"/><path d="M16 19h6"/><path d="M19 16v6"/>',
  'wallet': '<path d="M19 7V4a1 1 0 0 0-1-1H5a2 2 0 0 0 0 4h15a1 1 0 0 1 1 1v4h-3a2 2 0 0 0 0 4h3a1 1 0 0 0 1-1v-2a1 1 0 0 0-1-1"/><path d="M3 5v14a2 2 0 0 0 2 2h15a1 1 0 0 0 1-1v-4"/>'
};

function receiptSvgIcon(name, size = 14, opts = {}) {
  const paths = RECEIPT_SVG_PATHS[name] || RECEIPT_SVG_PATHS['alert-octagon'];
  const clsAttr = opts.className ? ` class="${opts.className}"` : '';
  const idAttr = opts.id ? ` id="${opts.id}"` : '';
  const sw = opts.strokeWidth || 2;
  return `<svg${idAttr}${clsAttr} width="${size}" height="${size}" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="${sw}" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${paths}</svg>`;
}

function formatImmutableUsdBadge(amountUsd, currencyCode = 'USD', mode = 'completed') {
  const cur = String(currencyCode || 'USD').toUpperCase();
  const usd = Number(amountUsd);
  if (!Number.isFinite(usd) || usd <= 0) return '';
  const formatted = `${usd.toFixed(2)} ${cur}`;
  if (mode === 'completed') return `Thực thu PayPal: ${formatted}`;
  if (mode === 'refunded') return `Đã hoàn PayPal: ${formatted}`;
  return `Quy đổi PayPal: ≈ ${formatted}`;
}

function copyReceiptIdentifier(val, label = 'mã giao dịch') {
  if (!val) return;
  if (navigator.clipboard && navigator.clipboard.writeText) {
    navigator.clipboard.writeText(val).then(() => {
      if (typeof showToast === 'function') {
        showToast('Đã sao chép', `Đã sao chép ${label}: ${val}`, 'success');
      }
    }).catch(() => {});
  }
}

function renderReceiptRow({ iconHtml, label, value, isMono = false, copyLabel = '' }) {
  if (!value && value !== 0) return '';
  const strVal = String(value);
  const copyBtn = copyLabel
    ? `<button type="button" class="spk-receipt-copy-btn" title="Sao chép ${escapeAttr(copyLabel)}" aria-label="Sao chép ${escapeAttr(copyLabel)}" onclick="copyReceiptIdentifier('${escapeAttr(strVal)}', '${escapeAttr(copyLabel)}')">
         ${receiptSvgIcon('copy', 12)}
       </button>`
    : '';

  return `
    <div class="spk-receipt-row">
      <span class="spk-receipt-row-key">
        <span class="spk-receipt-row-ico">${iconHtml}</span>
        <span>${escapeHtml(label)}</span>
      </span>
      <span class="spk-receipt-row-val ${isMono ? 'is-mono' : ''}">
        <span>${escapeHtml(strVal)}</span>
        ${copyBtn}
      </span>
    </div>
  `;
}

function buildReceiptGridHtml(d, mode, pageBootstrap) {
  const isOrder = d.targetType === 'order' || Boolean(d.order);
  const targetLabel = isOrder ? 'Mã đơn hàng:' : 'Mã vé sân:';
  const captureValue = d.payPalCaptureId || d.payPalRefundId || (mode === 'cancelled' ? 'Chưa thu tiền' : '--');
  const captureLabel = d.payPalRefundId && !d.payPalCaptureId ? 'Refund ID:' : 'Payment ID:';

  const rateVal = Number(d.exchangeRateVndPerUsd) > 0
    ? `1 USD = ${formatCompactVnd(d.exchangeRateVndPerUsd)}`
    : '--';

  const ord = d.order || null;
  const bk = d.booking || null;
  const customerName = ord?.customerName || bk?.name || '';
  const customerPhone = ord?.phone || bk?.phone || '';
  const fulfillmentOrCourt = ord
    ? (ord.address || 'Nhận tại quầy Pro Shop')
    : (bk ? `${bk.courtName || `Sân #${bk.courtId}`} · ${bk.date || ''} ${bk.time || ''}` : '');

  const leftRows = [
    renderReceiptRow({
      iconHtml: receiptSvgIcon(isOrder ? 'shopping-cart' : 'ticket', 14),
      label: targetLabel,
      value: d.targetCode || ord?.code || bk?.id || '--',
      isMono: true
    }),
    renderReceiptRow({
      iconHtml: '<span class="spk-pp-monogram">P</span>',
      label: 'PayPal Order ID:',
      value: d.payPalOrderId || pageBootstrap?.payPalOrderId || '--',
      isMono: true,
      copyLabel: 'PayPal Order ID'
    }),
    renderReceiptRow({
      iconHtml: receiptSvgIcon('credit-card', 14),
      label: captureLabel,
      value: captureValue,
      isMono: true,
      copyLabel: d.payPalCaptureId ? 'Payment ID' : ''
    }),
    d.attemptCode ? renderReceiptRow({
      iconHtml: receiptSvgIcon('shield-check', 14),
      label: 'Mã phiên Spike OS:',
      value: d.attemptCode,
      isMono: true
    }) : ''
  ].join('');

  const rightRows = [
    renderReceiptRow({
      iconHtml: receiptSvgIcon('refresh-cw', 14),
      label: 'Tỷ giá áp dụng:',
      value: rateVal,
      isMono: true
    }),
    customerName ? renderReceiptRow({
      iconHtml: receiptSvgIcon('user', 14),
      label: isOrder ? 'Khách nhận:' : 'Khách hàng:',
      value: customerName
    }) : '',
    customerPhone ? renderReceiptRow({
      iconHtml: receiptSvgIcon('phone', 14),
      label: 'SĐT:',
      value: customerPhone,
      isMono: true
    }) : '',
    fulfillmentOrCourt ? renderReceiptRow({
      iconHtml: receiptSvgIcon(isOrder ? 'store' : 'calendar-clock', 14),
      label: isOrder ? 'Hình thức giao:' : 'Sân & Khung giờ:',
      value: fulfillmentOrCourt
    }) : ''
  ].join('');

  return `
    <div class="spk-receipt-col spk-receipt-col-left">${leftRows}</div>
    <div class="spk-receipt-col spk-receipt-col-right">${rightRows}</div>
  `;
}

function setPpViewState(mode, opts = {}) {
  const card = document.getElementById('paypalOutcomeCard');
  if (!card) return;

  const bootstrap = getPagePayPalBootstrap();
  const isSandbox = String((opts.data && opts.data.environment) || bootstrap?.environment || 'Sandbox').toLowerCase() === 'sandbox';

  // State classes drive hero emblem, animations, halo, laurels
  card.classList.remove(
    'is-state-loading',
    'is-state-completed',
    'is-state-cancelled',
    'is-state-pending',
    'is-state-refunded',
    'is-state-failed'
  );
  card.classList.add(`is-state-${mode}`);

  const envPill = document.getElementById('ppEnvBadge');
  if (envPill) {
    envPill.style.display = isSandbox ? 'inline-flex' : 'none';
  }

  const liveChip = document.getElementById('ppStatusBadge');
  if (liveChip) {
    const chipConfig = {
      loading: { show: false, cls: '', text: '' },
      completed: { show: false, cls: '', text: '' },
      cancelled: { show: true, cls: 'is-warning', text: 'ĐÃ HỦY THANH TOÁN PAYPAL' },
      pending: { show: true, cls: 'is-warning', text: 'CẦN ĐỐI SOÁT TRẠNG THÁI' },
      refunded: { show: true, cls: 'is-danger', text: 'ĐÃ HOÀN TIỀN TỰ ĐỘNG' },
      failed: { show: true, cls: 'is-danger', text: 'KHÔNG THỂ HOÀN TẤT' }
    }[mode] || { show: true, cls: 'is-danger', text: 'LỖI XỬ LÝ' };

    liveChip.style.display = chipConfig.show ? 'inline-flex' : 'none';
    liveChip.className = `spk-receipt-live-chip ${chipConfig.cls}`;
    liveChip.textContent = chipConfig.text;
  }

  const circleEl = document.getElementById('ppIconCircle');
  if (circleEl) {
    if (mode === 'completed') {
      circleEl.innerHTML = `
        <svg class="spk-receipt-check-svg" viewBox="0 0 36 36" fill="none" aria-hidden="true">
          <path class="spk-receipt-check-path" d="M8.5 18.5L15 25L28 11.5" stroke="#FFFFFF" stroke-width="3.6" stroke-linecap="round" stroke-linejoin="round" />
        </svg>`;
    } else {
      const iconMap = {
        loading: 'loader-circle',
        cancelled: 'x-circle',
        pending: 'clock-alert',
        refunded: 'rotate-ccw',
        failed: 'alert-octagon'
      };
      const iconName = iconMap[mode] || 'alert-octagon';
      circleEl.innerHTML = receiptSvgIcon(iconName, 34, {
        id: 'ppMainIcon',
        className: mode === 'loading' ? 'pp-spin' : '',
        strokeWidth: 2.2
      });
    }
  }

  const titleEl = document.getElementById('ppOutcomeTitle');
  if (titleEl) {
    if (opts.titleHtml) {
      titleEl.innerHTML = opts.titleHtml;
    } else {
      titleEl.textContent = opts.title || '';
    }
  }

  const descEl = document.getElementById('ppOutcomeSubtitle');
  if (descEl) descEl.textContent = opts.message || '';

  const d = opts.data || null;

  // 1. Monetary Highlight Box (immutable values straight from server TbPayPalTransaction)
  const amountBannerEl = document.getElementById('ppAmountBanner');
  const amountLabelEl = document.getElementById('ppAmountLabel');
  const amountVndEl = document.getElementById('ppAmountVndDisplay');
  const amountUsdEl = document.getElementById('ppAmountUsdDisplay');

  if (amountBannerEl && amountVndEl && amountUsdEl) {
    if (d && Number(d.amountVnd) > 0) {
      amountBannerEl.style.display = 'block';
      if (amountLabelEl) {
        amountLabelEl.textContent = mode === 'refunded'
          ? 'Giá trị đã hoàn trả (VNĐ)'
          : (mode === 'cancelled'
            ? 'Giá trị phiên đã hủy (VNĐ)'
            : (d && d.booking ? 'Giá trị đặt sân (VNĐ)' : 'Giá trị đơn hàng (VNĐ)'));
      }
      amountVndEl.textContent = formatCompactVnd(d.amountVnd);
      const usdText = formatImmutableUsdBadge(d.amountUsd, d.currencyCode, mode);
      if (usdText) {
        amountUsdEl.style.display = 'inline-flex';
        amountUsdEl.textContent = usdText;
      } else {
        amountUsdEl.style.display = 'none';
      }
    } else {
      amountBannerEl.style.display = 'none';
    }
  }

  // 2. Transaction Details Sub-Card
  const metaCardEl = document.getElementById('ppMetaSummary');
  const metaGridEl = document.getElementById('ppMetaGridHost');
  const detailsBadgeEl = document.getElementById('ppDetailsStatusBadge');

  if (metaCardEl && metaGridEl) {
    if (d && (d.targetCode || d.payPalOrderId || Number(d.amountVnd) > 0)) {
      metaCardEl.style.display = 'block';
      metaGridEl.innerHTML = buildReceiptGridHtml(d, mode, bootstrap);

      if (detailsBadgeEl) {
        if (mode === 'completed' && d.localStatus === 'Completed') {
          detailsBadgeEl.className = 'spk-receipt-verified-badge';
          detailsBadgeEl.innerHTML = `${receiptSvgIcon('check-circle-2', 14, { strokeWidth: 2.2 })}<span>${isSandbox ? 'Đã thu đủ qua PayPal Sandbox' : 'Đã thu đủ qua PayPal'}</span>`;
        } else if (mode === 'refunded') {
          detailsBadgeEl.className = 'spk-receipt-verified-badge is-danger';
          detailsBadgeEl.innerHTML = `${receiptSvgIcon('rotate-ccw', 14, { strokeWidth: 2.2 })}<span>Đã hoàn tiền</span>`;
        } else if (mode === 'cancelled') {
          detailsBadgeEl.className = 'spk-receipt-verified-badge is-neutral';
          detailsBadgeEl.innerHTML = `${receiptSvgIcon('x-circle', 14, { strokeWidth: 2.2 })}<span>Đã hủy phiên</span>`;
        } else {
          detailsBadgeEl.className = 'spk-receipt-verified-badge is-pending';
          detailsBadgeEl.innerHTML = `${receiptSvgIcon('clock', 14, { strokeWidth: 2.2 })}<span>${escapeHtml(d.localStatus || 'Chờ xác nhận')}</span>`;
        }
      }
    } else {
      metaCardEl.style.display = 'none';
      metaGridEl.innerHTML = '';
    }
  }

  // 3. Preserved Booking Digital QR Ticket Mount
  const ticketWrapEl = document.getElementById('ppCompletedTicketWrap');
  const ticketHostEl = document.getElementById('ppCompletedTicketHost');
  if (ticketWrapEl && ticketHostEl) {
    if (mode === 'completed' && d && d.booking) {
      ticketWrapEl.style.display = 'block';
      const tok = resolveStoredGuestTokenForCode(d.booking.id);
      const isDarkTheme = document.documentElement.getAttribute('data-theme') === 'dark';
      if (window.SpikeTicket) {
        SpikeTicket.mountTicket('ppCompletedTicketHost', d.booking, {
          accessToken: tok,
          theme: isDarkTheme ? 'dark' : 'light'
        });
      }
    } else {
      ticketWrapEl.style.display = 'none';
      ticketHostEl.innerHTML = '';
    }
  }

  // 4. Footer Actions (+ Print Receipt on Verified Completed State)
  const actionsEl = document.getElementById('ppActionRow');
  if (actionsEl) {
    const baseActions = opts.actionsHtml || `
      <a href="/Product" class="spk-receipt-btn-primary">
        ${receiptSvgIcon('store', 18)}
        <span>Tiếp tục xem Pro Shop</span>
        ${receiptSvgIcon('arrow-right', 17, { className: 'spk-receipt-btn-arrow' })}
      </a>
      <a href="/" class="spk-receipt-btn-secondary">
        ${receiptSvgIcon('home', 17)}
        <span>Về Trang chủ</span>
      </a>
    `;

    const printBtn = (mode === 'completed' && d)
      ? `<div class="spk-receipt-print-wrap">
           <button type="button" class="spk-receipt-btn-print" onclick="window.print()">
             ${receiptSvgIcon('printer', 14)}
             <span>In hóa đơn</span>
           </button>
         </div>`
      : '';

    actionsEl.innerHTML = `${baseActions}${printBtn}`;
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
    targetCode: root.getAttribute('data-pp-target-code') || '',
    environment: root.getAttribute('data-pp-env') || 'Sandbox'
  };
}

function buildCompletedActionsHtml(data, fallbackTargetCode) {
  const isOrder = data.targetType === 'order' || Boolean(data.order);
  if (isOrder) {
    return `
      <a href="/Product" class="spk-receipt-btn-primary">
        ${receiptSvgIcon('store', 18)}
        <span>Tiếp tục xem Pro Shop</span>
        ${receiptSvgIcon('arrow-right', 17, { className: 'spk-receipt-btn-arrow' })}
      </a>
      <a href="/" class="spk-receipt-btn-secondary">
        ${receiptSvgIcon('home', 17)}
        <span>Về Trang chủ</span>
      </a>
    `;
  }

  const bookingCode = data.targetCode || (data.booking && data.booking.id) || fallbackTargetCode || '';
  const lookupHref = bookingCode ? `/Lookup?code=${encodeURIComponent(bookingCode)}` : '/Lookup';
  return `
    <a href="${lookupHref}" class="spk-receipt-btn-primary">
      ${receiptSvgIcon('ticket', 18)}
      <span>Quản lý vé tại Trang Tra cứu</span>
      ${receiptSvgIcon('arrow-right', 17, { className: 'spk-receipt-btn-arrow' })}
    </a>
    <a href="/" class="spk-receipt-btn-secondary">
      ${receiptSvgIcon('home', 17)}
      <span>Về Trang chủ</span>
    </a>
  `;
}

function buildCompletedHeadlineHtml(data, isSandbox) {
  const gatewayLabel = isSandbox ? 'PayPal Sandbox' : 'PayPal';
  if (data.deduplicated) {
    return `Thanh toán ${gatewayLabel} <span class="spk-receipt-title-accent">đã xác nhận</span>`;
  }
  return `Thanh toán ${gatewayLabel} <span class="spk-receipt-title-accent">thành công</span>`;
}

function buildCompletedSubtitle(data, isSandbox) {
  const gatewayLabel = isSandbox ? 'PayPal Sandbox' : 'PayPal';
  if (data.booking) {
    if (data.booking.status === 'pending') {
      return `Khoản thu đã được ghi nhận vào sổ cái qua ${gatewayLabel}. Ca đặt sân vẫn giữ trạng thái Chờ duyệt / Check-in theo quy trình CLB.`;
    }
    return `Ca đặt sân của bạn đã được thanh toán hoàn tất qua ${gatewayLabel}. Xuất trình mã QR bên dưới khi đến sân.`;
  }
  return `Đơn hàng Pro Shop của bạn đã được thanh toán hoàn tất qua ${gatewayLabel}.`;
}

async function runPayPalReturnCapture() {
  const cfg = getPagePayPalBootstrap();
  if (!cfg || _ppReturnBusy) return;

  if (!cfg.isValidState || !cfg.stateToken) {
    setPpViewState('failed', {
      title: 'Liên kết trả về từ PayPal không hợp lệ',
      message: 'Chữ ký bảo vệ phiên (state) bị thiếu hoặc đã hết hạn. Hệ thống không ghi nhận thanh toán nào từ URL.',
      actionsHtml: `
        <a href="/Lookup" class="spk-receipt-btn-primary">
          ${receiptSvgIcon('search', 17)}
          <span>Mở trang Tra cứu vé</span>
          ${receiptSvgIcon('arrow-right', 17, { className: 'spk-receipt-btn-arrow' })}
        </a>
        <a href="/Booking" class="spk-receipt-btn-secondary">
          ${receiptSvgIcon('calendar', 17)}
          <span>Quay lại Đặt sân</span>
        </a>
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
    const isSandbox = String((data && data.environment) || cfg.environment || 'Sandbox').toLowerCase() === 'sandbox';

    if (res.ok && data && data.ok && data.localStatus === 'Completed') {
      setPpViewState('completed', {
        titleHtml: buildCompletedHeadlineHtml(data, isSandbox),
        message: buildCompletedSubtitle(data, isSandbox),
        data,
        actionsHtml: buildCompletedActionsHtml(data, cfg.targetCode)
      });
      return;
    }

    if (data && (data.code === 'PAYPAL_CAPTURED_AND_REFUNDED' || data.localStatus === 'RefundedAfterExpiry' || data.localStatus === 'Refunded')) {
      setPpViewState('refunded', {
        title: 'Thời gian giữ chỗ hết hạn — PayPal đã được hoàn tiền tự động',
        message: data.error || 'Ca đặt sân đã hết hạn giữ chỗ trước khi hoàn tất, hệ thống đã hoàn lại tiền về tài khoản PayPal Sandbox của bạn.',
        data,
        actionsHtml: `
          <a href="/Booking" class="spk-receipt-btn-primary">
            ${receiptSvgIcon('calendar-plus', 17)}
            <span>Đặt lại khung giờ mới</span>
            ${receiptSvgIcon('arrow-right', 17, { className: 'spk-receipt-btn-arrow' })}
          </a>
          <a href="/" class="spk-receipt-btn-secondary">
            ${receiptSvgIcon('home', 17)}
            <span>Về Trang chủ</span>
          </a>
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
          <button type="button" class="spk-receipt-btn-primary" onclick="reconcilePayPalAttemptFromPage()">
            ${receiptSvgIcon('refresh-cw', 17)}
            <span>Kiểm tra &amp; Đối soát lại ngay</span>
          </button>
          <a href="/Lookup" class="spk-receipt-btn-secondary">
            ${receiptSvgIcon('search', 17)}
            <span>Mở trang Tra cứu vé</span>
          </a>
        `
      });
      return;
    }

    setPpViewState('failed', {
      title: res.status === 403 ? 'Không có quyền truy cập biên lai này' : 'Không thể hoàn tất thu tiền PayPal Sandbox',
      message: (data && data.error) || `Máy chủ trả về mã lỗi ${res.status}.`,
      data,
      actionsHtml: `
        ${res.status === 403 ? '' : `
          <button type="button" class="spk-receipt-btn-primary" onclick="reconcilePayPalAttemptFromPage()">
            ${receiptSvgIcon('refresh-cw', 17)}
            <span>Đối soát giao dịch</span>
          </button>`}
        <a href="/Lookup" class="spk-receipt-btn-secondary">
          ${receiptSvgIcon('search', 17)}
          <span>Quay lại Tra cứu vé</span>
        </a>
      `
    });
  } catch (_) {
    setPpViewState('pending', {
      title: 'Mất kết nối trong quá trình xác nhận thanh toán',
      message: 'Đừng lo — hệ thống sẽ đối soát tự động với PayPal Sandbox. Bạn có thể bấm nút bên dưới để kiểm tra lại.',
      actionsHtml: `
        <button type="button" class="spk-receipt-btn-primary" onclick="reconcilePayPalAttemptFromPage()">
          ${receiptSvgIcon('refresh-cw', 17)}
          <span>Thử đối soát lại</span>
        </button>
        <a href="/Lookup" class="spk-receipt-btn-secondary">
          ${receiptSvgIcon('search', 17)}
          <span>Quay lại Tra cứu</span>
        </a>
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
        <a href="/Booking" class="spk-receipt-btn-primary">
          ${receiptSvgIcon('calendar', 17)}
          <span>Quay lại Đặt sân</span>
        </a>
        <a href="/" class="spk-receipt-btn-secondary">
          ${receiptSvgIcon('home', 17)}
          <span>Về Trang chủ</span>
        </a>
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
    const isSandbox = String((data && data.environment) || cfg.environment || 'Sandbox').toLowerCase() === 'sandbox';

    if (res.ok && data && data.localStatus === 'Completed') {
      setPpViewState('completed', {
        titleHtml: buildCompletedHeadlineHtml({ ...data, deduplicated: true }, isSandbox),
        message: buildCompletedSubtitle(data, isSandbox),
        data,
        actionsHtml: buildCompletedActionsHtml(data, cfg.targetCode)
      });
      return;
    }

    const canRetryBooking = data.targetType === 'booking' && data.targetCode;
    setPpViewState('cancelled', {
      title: 'Bạn đã hủy thanh toán qua PayPal Sandbox',
      message: data.targetType === 'order'
        ? 'Đơn Pro Shop vẫn được giữ ở trạng thái Chưa thanh toán. Bạn có thể thanh toán trực tiếp tại quầy.'
        : 'Đơn đặt sân của bạn vẫn chưa thu tiền PayPal. Bạn có thể thử thanh toán lại hoặc trả tại quầy CLB.',
      data,
      actionsHtml: `
        ${canRetryBooking
          ? `<button type="button" class="spk-receipt-btn-primary" onclick="retryPayPalFromCancel('${escapeAttr(data.targetType)}','${escapeAttr(data.targetCode)}')">
               ${receiptSvgIcon('wallet', 17)}
               <span>Thanh toán PayPal lại</span>
             </button>`
          : `<a href="${cfg.targetType === 'order' ? '/Product' : '/Booking'}" class="spk-receipt-btn-primary">
               ${receiptSvgIcon(cfg.targetType === 'order' ? 'store' : 'calendar', 17)}
               <span>${cfg.targetType === 'order' ? 'Quay lại Pro Shop' : 'Quay lại Đặt sân'}</span>
               ${receiptSvgIcon('arrow-right', 17, { className: 'spk-receipt-btn-arrow' })}
             </a>`}
        <a href="/" class="spk-receipt-btn-secondary">
          ${receiptSvgIcon('home', 17)}
          <span>Về Trang chủ</span>
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
    const isSandbox = String((data && data.environment) || cfg.environment || 'Sandbox').toLowerCase() === 'sandbox';

    if (res.ok && data && data.ok && data.localStatus === 'Completed') {
      setPpViewState('completed', {
        titleHtml: buildCompletedHeadlineHtml({ ...data, deduplicated: true }, isSandbox),
        message: buildCompletedSubtitle(data, isSandbox),
        data,
        actionsHtml: buildCompletedActionsHtml(data, cfg.targetCode)
      });
      return;
    }

    setPpViewState(data.needsReconciliation ? 'pending' : 'failed', {
      title: 'Kết quả đối soát PayPal Sandbox',
      message: data.error || `Trạng thái hiện tại: ${data.localStatus || 'Chưa hoàn tất'}`,
      data,
      actionsHtml: `
        <button type="button" class="spk-receipt-btn-primary" onclick="reconcilePayPalAttemptFromPage()">
          ${receiptSvgIcon('refresh-cw', 17)}
          <span>Thử đối soát lại</span>
        </button>
        <a href="/Lookup" class="spk-receipt-btn-secondary">
          ${receiptSvgIcon('search', 17)}
          <span>Mở trang Tra cứu vé</span>
        </a>
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
