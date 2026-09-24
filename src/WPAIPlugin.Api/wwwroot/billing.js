const el = (id) => document.getElementById(id);

function formatGbp(minor) {
  return `£${(minor / 100).toFixed(2)}`;
}

function formatDateTime(iso) {
  try {
    return new Date(iso).toLocaleString();
  } catch {
    return iso;
  }
}

async function loadBalance() {
  try {
    const response = await apiFetch("/api/credits");
    if (!response.ok) throw new Error("Credits unavailable");
    const data = await response.json();
    el("currentBalance").textContent = `${data.balance.toLocaleString()} credits`;
  } catch {
    el("currentBalance").textContent = "Unavailable. Please refresh.";
  }
}

// Server-resolved only - this page never calculates a discount or bonus
// itself, and never decides whether a sale is active.
let appliedPromoCode = null;

// Presentation only: the configured "builder" pack is the design's featured
// card. Price/credits always come from the server response below.
const FEATURED_PACK_ID = "builder";

function renderPackCard(pack) {
  const featured = pack.packId === FEATURED_PACK_ID;
  const card = document.createElement("div");
  card.className = featured ? "card elev-md card-featured pack-card" : "card elev-sm pack-card";

  const head = document.createElement("div");
  head.className = "pack-head";
  const titleBlock = document.createElement("div");

  const heading = document.createElement("h3");
  heading.className = "card-kicker";
  heading.textContent = pack.displayName;
  titleBlock.appendChild(heading);

  const credits = document.createElement("p");
  credits.className = "pack-credits";
  credits.textContent = pack.bonusCredits
    ? `${pack.credits.toLocaleString()} credits + ${pack.bonusCredits.toLocaleString()} bonus`
    : `${pack.credits.toLocaleString()} credits`;
  titleBlock.appendChild(credits);
  head.appendChild(titleBlock);

  if (featured) {
    const popular = document.createElement("span");
    popular.className = "tag tag-accent";
    popular.textContent = "Most popular";
    head.appendChild(popular);
  }
  card.appendChild(head);

  const price = document.createElement("p");
  price.className = "pack-price";
  if (pack.discountedAmountMinor) {
    const original = document.createElement("span");
    original.className = "pack-price-original";
    original.textContent = formatGbp(pack.amountMinor);
    const discounted = document.createElement("span");
    discounted.textContent = ` ${formatGbp(pack.discountedAmountMinor)}`;
    price.appendChild(original);
    price.appendChild(discounted);
  } else {
    price.textContent = formatGbp(pack.amountMinor);
  }
  card.appendChild(price);

  if (pack.promotionName) {
    const badge = document.createElement("span");
    badge.className = "tag tag-outline pack-promotion";
    badge.textContent = pack.promotionName;
    card.appendChild(badge);
  }

  const button = document.createElement("button");
  button.type = "button";
  button.className = featured ? "btn btn-primary btn-block" : "btn btn-secondary btn-block";
  button.textContent = "Buy now";
  button.addEventListener("click", () => startCheckout(pack.packId, button));
  card.appendChild(button);

  return card;
}

async function loadPacks() {
  const container = el("packList");
  try {
    const response = await apiFetch("/api/payments/packs");
    if (!response.ok) throw new Error("Packs unavailable");
    const packs = await response.json();

    container.innerHTML = "";
    packs.forEach((pack) => container.appendChild(renderPackCard(pack)));
  } catch {
    container.innerHTML = "<p>Unavailable. Please refresh.</p>";
  }
}

async function applyPromoCode(code) {
  const status = el("promoStatus");
  status.hidden = false;
  status.textContent = "Checking code…";

  const container = el("packList");
  try {
    const packsResponse = await apiFetch("/api/payments/packs");
    if (!packsResponse.ok) throw new Error("Packs unavailable");
    const packs = await packsResponse.json();

    let appliedToAny = false;
    const resolved = [];
    for (const pack of packs) {
      try {
        const response = await apiFetch("/api/promotions/resolve-code", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ packId: pack.packId, code }),
        });
        if (response.ok) {
          resolved.push(await response.json());
          appliedToAny = true;
          continue;
        }
      } catch {
        // fall through to the unresolved-pack case below
      }
      resolved.push(pack);
    }

    if (!appliedToAny) {
      status.textContent = "Invalid or expired code.";
      appliedPromoCode = null;
      return;
    }

    appliedPromoCode = code;
    status.textContent = "Code applied.";
    container.innerHTML = "";
    resolved.forEach((pack) => container.appendChild(renderPackCard(pack)));
  } catch {
    status.textContent = "Could not check that code. Please try again.";
  }
}

async function startCheckout(packId, button) {
  button.disabled = true;
  try {
    const response = await apiFetch("/api/payments/checkout", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ packId, promoCode: appliedPromoCode || undefined }),
    });
    if (response.status === 503) {
      window.alert("Credit purchases are currently unavailable. Please try again later.");
      button.disabled = false;
      return;
    }
    if (!response.ok) {
      const body = await response.json().catch(() => null);
      window.alert(body?.error || "Could not start checkout. Please try again.");
      button.disabled = false;
      return;
    }
    const result = await response.json();
    // Stripe-hosted Checkout: the browser is only ever handed a URL to
    // redirect to. Credits are granted later, server-side, only once Stripe's
    // webhook confirms payment - never by this redirect.
    window.location.href = result.checkoutUrl;
  } catch {
    window.alert("Could not start checkout. Please try again.");
    button.disabled = false;
  }
}

async function loadHistory() {
  const container = el("purchaseHistory");
  try {
    const response = await apiFetch("/api/payments/history");
    if (!response.ok) throw new Error("History unavailable");
    const purchases = await response.json();
    if (purchases.length === 0) {
      container.innerHTML = '<div class="card history-empty"><p>No purchases yet.</p>'
        + '<p class="history-empty-sub">Buy a credit pack above and it\'ll show up here with its date, amount and status.</p></div>';
      return;
    }

    const table = document.createElement("table");
    table.className = "table";
    table.innerHTML = "<thead><tr><th>Date</th><th>Pack</th><th>Amount</th><th>Credits</th><th>Status</th></tr></thead>";
    const tbody = document.createElement("tbody");
    purchases.forEach((p) => {
      const tr = document.createElement("tr");
      const credits = p.bonusCredits
        ? `${p.creditsPurchased.toLocaleString()} + ${p.bonusCredits.toLocaleString()} bonus`
        : p.creditsPurchased.toLocaleString();
      const pack = p.promotionNameSnapshot ? `${p.packId} (${p.promotionNameSnapshot})` : p.packId;
      [formatDateTime(p.createdAtUtc), pack, formatGbp(p.amountMinor), credits, p.status].forEach((value) => {
        const td = document.createElement("td");
        td.textContent = value;
        tr.appendChild(td);
      });
      tbody.appendChild(tr);
    });
    table.appendChild(tbody);
    const card = document.createElement("div");
    card.className = "card history-card table-scroll";
    card.appendChild(table);
    container.innerHTML = "";
    container.appendChild(card);
  } catch {
    container.innerHTML = "<p>Unavailable. Please refresh.</p>";
  }
}

function showCheckoutNotice() {
  const params = new URLSearchParams(window.location.search);
  const checkout = params.get("checkout");
  if (!checkout) return;

  const notice = el("checkoutNotice");
  notice.hidden = false;
  if (checkout === "success") {
    notice.className = "notice ok";
    notice.textContent = "Payment received. Your balance below reflects the server's confirmed record - if credits are not yet visible, they will appear once Stripe's confirmation is processed (usually within seconds).";
  } else if (checkout === "cancelled") {
    notice.className = "notice warn";
    notice.textContent = "Checkout was cancelled. No credits were charged or granted.";
  } else {
    notice.hidden = true;
  }
}

(async () => {
  try {
    const response = await apiFetch("/api/account/me");
    if (!response.ok) {
      window.location.href = "login.html";
      return;
    }
    el("billing").hidden = false;
    showCheckoutNotice();
    // Always re-fetch the authoritative server balance - never trust or
    // optimistically apply anything from the success redirect itself.
    loadBalance();
    loadPacks();
    loadHistory();

    el("promoForm").addEventListener("submit", (event) => {
      event.preventDefault();
      const code = el("promoCode").value.trim();
      if (!code) return;
      applyPromoCode(code);
    });
  } catch {
    window.location.href = "login.html";
  }
})();
