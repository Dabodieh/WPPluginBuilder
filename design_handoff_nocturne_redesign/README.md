# Handoff: ModuleMint — Nocturne Visual Redesign

## Overview
Visual redesign of ModuleMint's marketing + app shell (Home, Dashboard, Builder, My Plugins, Billing) onto the **Nocturne** design system. The current app (WPAIPlugin, ASP.NET Core) renders these as static HTML/CSS/JS in `src/WPAIPlugin.Api/wwwroot/`. This is a like-for-like restyle + light UX pass of those exact screens — same routes, same functionality, new look.

## About the design files
`designs/*.dc.html` are **design references** — open any of them directly in a browser to see the exact rendered result. Ignore the `.dc.html` extension and the `<helmet>` wrapper tag; everything inside renders as ordinary HTML/CSS. **Every element in these files uses literal inline `style="..."` attributes** — there is no hidden class-based CSS to reverse-engineer. This means the files themselves are the exact spec: any color, size, spacing, or radius value you need is sitting in that element's `style` attribute or in `nocturne-styles.css`. The written breakdown below is a navigation aid on top of that, not a replacement for reading the files.

**Do not ship the `.dc.html` files or their inline-style approach as-is.** Re-implement each screen inside the existing wwwroot file, using the existing app's CSS file structure (`site.css`, `app.css`, `billing.css`) with real classes/selectors, driven by the token values below. Preserve every existing `<script>` include, JS behavior, and API call — this is a restyle, not a rebuild.

## Fidelity
**High-fidelity.** Colors, spacing, type, and component treatment are final. Implement pixel-close. Copy shown is final unless it conflicts with live data (credit counts, plugin names, prices) — those cases are called out explicitly below.

## Screen → existing file map
| Design file | Existing wwwroot file(s) | Purpose |
|---|---|---|
| `ModuleMint - Home.dc.html` | `index.html` + `site.css` | Logged-out marketing home |
| `ModuleMint - Dashboard.dc.html` | `dashboard.html` + `dashboard.js` + `app.css` | Logged-in overview |
| `ModuleMint - Builder.dc.html` | `builder.html` | Plugin description → plan flow |
| `ModuleMint - My Plugins.dc.html` | `myplugins.html` + `myplugins.js` | Saved plugins list |
| `ModuleMint - Billing.dc.html` | `billing.html` + `billing.js` + `billing.css` | Credits + purchase history |
| (shared nav pattern, all 4 app screens) | `nav.js` | Top nav — brand, links, credits badge, account menu |

`login.html`, `register.html`, `forgot-password.html`, `reset-password.html`, `confirm-email.html`, `admin.html`, `plugin.html`, `privacy.html`, `terms.html`, `refunds.html`, `support.html`, `legal.css` were **not redesigned** — apply the same token swap for visual consistency (dark ground, blurple accent, Inter, outlined primary buttons), but no new layout was authored for them. Use the Dashboard/Billing patterns as the closest reference.

## Design tokens (Nocturne)
Full source of truth: `designs/nocturne-styles.css` — port these CSS variables into the app's existing stylesheets (or link the file directly and reference `var(--*)` everywhere).

### Color
```css
--color-bg: #161826;            /* page ground — dark blue-grey, never pure black */
--color-surface: #232532;        /* card/input backgrounds */
--color-text: #e9e9ed;
--color-accent: #9184d9;         /* blurple — the ONLY accent hue in the system */
--color-accent-2: #a7a1db;       /* machine stand-in, reads identical to accent — don't treat as a second color */
--color-divider: color-mix(in srgb, #e9e9ed 16%, transparent);  /* soft line, not a hard border */

/* neutral ramp (100 lightest -> 900 darkest) */
--color-neutral-100:#f3f5fe; --color-neutral-200:#e4e7f5; --color-neutral-300:#cfd3e5;
--color-neutral-400:#b2b6ca; --color-neutral-500:#9397ab; --color-neutral-600:#75798c;
--color-neutral-700:#595d6c; --color-neutral-800:#3f424d; --color-neutral-900:#292b31;

/* accent ramp */
--color-accent-100:#f5f4ff; --color-accent-200:#e7e5fe; --color-accent-300:#d2cefd;
--color-accent-400:#b5abfc; --color-accent-500:#968ae0; --color-accent-600:#796cbf;
--color-accent-700:#5d5294; --color-accent-800:#423a6a; --color-accent-900:#2b2741;

/* section ground — saturated, RARE: used only for the Home "offer band" full-bleed strip */
--color-section:#262a60; --color-section-glow:#353b80; --color-section-ghost:#4c5397;
```
Usage rule: on this dark ground, use the **dark** ramp steps (700–900) for tinted fills/hovers/borders, 500 as a role's base, and **light** steps (100–300) for text on those tints. Never flood a large area with the accent — it's a line, border, or small tag fill only.

### Type
```css
--font-heading: "Inter", system-ui, sans-serif;
--font-heading-weight: 500;   /* NEVER go bolder than 500, even for h1 */
--font-body: "Inter", system-ui, sans-serif;
```
Base sizes used across the designs: h1 30–32px (Dashboard/Billing) up to a fluid `clamp(36px,5.5vw,60px)` for the Home hero; body copy 14–17px; small/meta text 12–13px; kicker/section-label text 13px uppercase with `letter-spacing:0.06em`, colored `var(--color-accent)`. Line-height 1.08–1.6 depending on role (tight for display headings, ~1.6 for paragraph copy).

### Spacing (compact, 0.7× density — this is a dense UI on purpose)
```css
--space-1:2.8px; --space-2:5.6px; --space-3:8.4px; --space-4:11.2px; --space-6:16.8px; --space-8:22.4px;
```

### Radius & shadow
```css
--radius-sm:4px; --radius-md:8px; --radius-lg:14px;
--shadow-sm: 0 0 0 1px #3f424d;
--shadow-md: 0 0 0 1px #595d6c, 0 6px 18px rgba(0,0,0,0.55);
--shadow-lg: 0 0 0 1px #9397ab, 0 16px 40px rgba(0,0,0,0.65);
```
Elevation on this dark theme = hairline edge + soft ambient darkness, never a heavy drop shadow.

### Core component rules
- **Primary button**: accent-colored **outline** (1px border, transparent fill), never a solid accent fill. Text color = accent. Hover: light accent-tint background wash. Padding `var(--space-2) calc(var(--space-3)*1.2)`, radius `var(--radius-md)`.
- **Secondary button**: filled with a neutral surface tint, no accent.
- **Ghost button**: text-only, accent-colored, minimal horizontal padding.
- **Tags** (`tag-accent` / `tag-outline` / `tag-neutral`): small pill-ish labels, `border-radius: calc(var(--radius-md)*0.75)`, 11px text, `padding:3px 10px`.
- **Cards**: `background: var(--color-surface)`, `border-radius: var(--radius-md)`, `padding: var(--space-3)` (or more for hero/content cards — see per-screen notes), no border by default; add `box-shadow: var(--shadow-sm/md)` for elevation, or `box-shadow: inset 0 0 0 1px var(--color-accent)` to highlight a card (used for "featured" states).
- **Inputs/textareas**: `background: var(--color-surface)`, `border: 1px solid var(--color-divider)`, `border-radius: var(--radius-md)`, `min-height: 36px` (inputs) / `90px+` (textarea), focus state = `border-color: var(--color-accent)`.
- **Nav**: flex row, `gap: var(--space-4)`, brand mark has `margin-right:auto` so brand sits left and everything else (links, badges, buttons) sits right. Links: 14px, `color:inherit`, hover/`aria-current="page"` → `color: var(--color-accent)`.
- **Icons**: Phosphor, inline SVG, `fill="currentColor"`, typically 14–16px.
- **Global micro-motion**: `transition: background 0.15s ease, box-shadow 0.15s ease, border-color 0.15s ease, transform 0.15s ease` on buttons/cards/tags/inputs; cards additionally `transform: translateY(-2px)` on `:hover`.

---

## Screen-by-screen spec

### 1. Home (`index.html`)
**Purpose**: convert a logged-out visitor into a signup, lead with the free-build offer.

**Structure, top to bottom** (max-width 1200px, centered, side padding `clamp(20px,5vw,32px)`):
1. **Nav** — brand "ModuleMint" (links to itself/home), links: "How it works" (`#how`), "Features" (`#features`), "Dashboard"; trailing primary button "Start Building" with a forward-arrow SVG icon. `white-space:nowrap` on the button label (icon buttons must never wrap).
2. **Hero** — two-column grid (`1.1fr / 0.9fr`, 48px gap, vertically centered).
   - Left: kicker "From an idea to a WordPress plugin" (accent, uppercase, 13px). H1 "Build WordPress plugins with AI" (`clamp(36px,5.5vw,60px)`, weight 500, line-height 1.08). Subcopy (17px, ~82% text opacity, max 52ch): "Describe what you need. ModuleMint plans it, builds it, and hands you a ready-to-install ZIP — with optional WordPress validation before you download." CTA row: primary button "Start Building" (+ arrow icon) and ghost link "See how it works" (scrolls to `#how`), both `white-space:nowrap`.
   - Right: a `card elev-md` (padding 20px) mock of the plan-preview UI: kicker "Idea → plugin" + `tag-outline` "Example" top row; a labeled fake input showing the example description text "Create a staff directory with name, email, job title and a shortcode."; a centered "↓" glyph; a bordered sub-panel showing a small plugin-type icon swatch, "PLUGIN PLAN" label + "Staff Directory" title, a 3-line checklist (Custom Post Type / Custom Fields / Shortcode), and a footer row with a pulsing-dot "Ready to build" status + ".zip" label.
3. **Offer band** — full-bleed section, background = radial glow (`--color-section-glow`) over `--color-section` flat fill (the ONE place saturated color is allowed to flood). Padding 40px vertical. Flex row (wraps), space-between: left = H2 "New here? Your first 2 plugins are on us." (26px) + two lines of body copy: "No credit card required. Build, validate, and download — see the whole flow before you ever pay for a credit." then a smaller (13px, 62% opacity) line "Then from £4.99 for 25 credits — pay only if you keep building." Right = a primary button styled with `border-color:var(--color-text); color:var(--color-text)` (white outline, since it sits on the saturated section ground where the normal accent-outline would lose contrast) reading "Claim your free builds".
4. **How it works** (`id="how"`, `scroll-margin-top:88px`) — kicker "A straightforward workflow", H2 "How it works" (`clamp(28px,3.5vw,38px)`), subcopy "Know what you're building before you build it." Below a `border-top` divider, a 3-column responsive grid (`auto-fit, minmax(220px,1fr)`, 32px gap): each column = accent "01"/"02"/"03" numeral (13px), H3 title (18px: "Describe it" / "Review it" / "Build & download"), 14.5px body copy per column (matches original product copy — see file for exact text).
5. **"Built, not just generated"** — a single `card elev-sm` (padding 32px) two-column split (equal `1fr`/`1fr`, 40px gap): left = kicker "A plan you can inspect", H2 "Built, not just generated" (28px), two paragraphs of body copy about deterministic templates and optional Build & Validate. Right = a 6-row checklist, each row `border-bottom` divider except the last, some rows carry a trailing 12px muted label "With Build & Validate" (PHP syntax validation / WordPress installation test / WordPress activation test rows only).
6. **Features grid** (`id="features"`, `scroll-margin-top:88px`) — kicker "What can you build?", H2 "Practical building blocks for your WordPress site". Responsive card grid (`auto-fit, minmax(220px,1fr)`, 16px gap), 5 `card` elements, each with `card-kicker` (category label: Shortcodes / Custom Post Types / Custom Fields / Settings Pages / Scheduled Tasks), `card-title` (a short benefit title), `card-body` (one-sentence description) — exact copy in the file.
7. **"Your plugins stay with you"** — two-column (`1fr` / `0.8fr`, 40px gap, centered): left = H2 + one paragraph. Right = a `card elev-md` "My Plugins" preview: header row (label + `tag-outline` "Example"), a plugin row (avatar-letter swatch "W", title "Staff Directory", meta "v1.0.0 · Revision 1", trailing `tag-accent` "✓ Validated"), and a footer row (meta text "WordPress plugin · ZIP" + secondary "Download" button with a rotated arrow icon).
8. **`<hr>`** divider (the system's fading-rule `.hr` class/pattern).
9. **Closing CTA** — H2 "Ready to build your first plugin?" (`clamp(28px,3.8vw,42px)`), subcopy "Describe it. Review it. Download it.", then a row: primary "Start Building" button (+ arrow icon) and a `tag-accent` "First 2 plugins free" reinforcing the offer one final time (this is intentionally the ONLY other place the offer is restated — it was originally in 3 spots and trimmed to 2 to avoid redundancy).
10. **Footer** — divider top border, flex row (wraps) space-between: left = "ModuleMint" brand mark + "From your idea to your WordPress site."; right = link row (Privacy, Terms, Refunds, Support, Dashboard).

### 2. Dashboard (`dashboard.html`)
**Purpose**: logged-in home base; surface account state and the primary "start a plugin" action.

1. **Nav** (app variant, reused on all 4 app screens) — brand links home; 4 nav links (Dashboard current via `aria-current="page"`, Builder, My Plugins, Billing); trailing `tag-outline` showing live credit count ("5 credits"); trailing ghost button "Account ▾" (non-functional placeholder — wire to a real dropdown).
2. H1 "Dashboard" (32px) + subcopy "Build WordPress plugins from a structured AI-generated plan."
3. **Verification banner** — a `card` with `box-shadow: inset 0 0 0 1px var(--color-accent)` (accent-outlined, not the old amber/warning color), padding 16/20px, flex row space-between (wraps): left = small accent label "VERIFY TO DOWNLOAD" + body line "You can start planning right away — verify your email before you build and download." (intentionally NOT blocking — see Interactions below); right = primary button "Resend verification email". **Only show this banner if the account is actually unverified** — conditional on real state.
4. Primary button "New Plugin" (+ plus-icon), links to Builder. Full anchor-as-button, not a bare `<button>`, so it's directly navigable.
5. **Stat cards row** — responsive grid (`auto-fit, minmax(240px,1fr)`, 16px gap), two `card elev-sm` (padding 20px): "Credits" (`card-kicker`) + big number "5 remaining"; "Free builds" + "2 remaining". **Bind both numbers to live account/credit state.**
6. **"Your plugins" card** — `card elev-sm`, kicker "Your plugins", big text "0 saved plugins" (bind to real count), secondary button/link "View My Plugins" → myplugins.html.
7. **"How it works" card** — `card elev-sm`, kicker "How it works", one line: "Plan → Build → Validate → Download".

### 3. Builder (`builder.html`)
**Purpose**: the plugin-description → plan-creation flow. Max-width 760px (narrower, form-focused), centered.

1. **Nav** — same app nav, Builder marked current.
2. H1 "Build a WordPress Plugin" (30px) + subcopy: "Describe what you want to build. We'll turn the request into a structured plugin plan before generating files."
3. **Step 1 card** — `card elev-md` (padding 24px): header row = `tag-accent` "1" + uppercase 13px label "Describe your plugin". A `.field` block: label "What do you want your WordPress plugin to do?" + `textarea.input` (min-height 120px) with placeholder "Create a staff directory with staff members, job titles and a shortcode." Full-width primary button "Create Plan — free" (+ arrow icon). Below it a `tag-outline` "Uses 1 free build — 2 remaining" (**bind to live free-build counter**).
4. **Step 2 & 3 preview** — a `flex column, gap:12px` block at 45% opacity (visually de-emphasized, NOT interactive yet) containing two plain `card`s (no elevation): Step 2 = `tag-neutral` "2" + label "Review plan" + one line explaining what happens; Step 3 = `tag-neutral` "3" + label "Build & validate" + one line. **These become live/interactive once the plan step actually returns data** — swap opacity to 1 and wire real content in as the flow advances (plan JSON display for step 2, build/validate progress + download for step 3).

### 4. My Plugins (`myplugins.html`)
**Purpose**: list of the user's saved plugin builds; currently spec'd for the empty state (no builds yet) — a populated-list layout is NOT yet designed and should follow the Home page's "My Plugins preview card" pattern (avatar-letter swatch, title, version/revision meta, validated tag, download button) repeated as a list.

1. **Nav** — My Plugins marked current.
2. Header row: H1 "My Plugins" (30px) + primary button "+ New Plugin" → Builder.
3. **Empty state** — a `card`, centered content, generous padding (56px/24px): body line "You haven't built any plugins yet." + a smaller (13.5px, 55% opacity) supporting line "Every plugin you build is saved here with its version history, ready to re-download anytime." + primary button "Build your first plugin" → Builder.
4. **When populated** (not built yet — implement using the Home page's plugin-row pattern as the template): each plugin as its own row/card with avatar-letter swatch, name, "v{version} · Revision {n}" meta, a status tag (`tag-accent` "✓ Validated" / consider a neutral tag for "Not validated" / a warning-styled tag for "Build failed" — no failed-state color was specified, derive from the accent ramp's darker step or ask design before shipping), and a "Download" secondary button. Consider adding a version-history expand or a "Rebuild" action — not specified, flag as an open question.

### 5. Billing (`billing.html`)
**Purpose**: show current credit balance, sell credit packs, show purchase history.

1. **Nav** — Billing marked current.
2. H1 "Billing" (30px) + subcopy "Buy credits and view your purchase history."
3. **Current credits card** — `card elev-sm`, max-width 320px, kicker "Current credits" + big number "5 credits" (**bind to live balance**).
4. **Promo code** — H2 "Promo code" (18px) + flex row (max-width 420px): text input (placeholder "WELCOME25") + secondary button "Apply". Wire to real promo/redemption endpoint (`PromotionsController`).
5. **Buy credits** — H2 "Buy credits" + responsive 3-card grid (`auto-fit, minmax(220px,1fr)`, 16px gap):
   - **Starter**: plain `card elev-sm`, kicker "Starter", "25 credits", price "£4.99" (24px), full-width secondary "Buy now" button.
   - **Builder** (featured): `card elev-md` with `box-shadow: inset 0 0 0 1px var(--color-accent)` (accent-outlined to stand out), header row includes a `tag-accent` "Most popular" badge, "75 credits", price "£9.99", full-width **primary** "Buy now" button (the only one of the three that's primary-styled).
   - **Pro**: plain `card elev-sm`, kicker "Pro", "200 credits", price "£19.99", full-width secondary "Buy now" button.
   - **Bind all prices/credit amounts to `CreditPackOptions`/`PaymentsController` — do not hardcode.**
6. **Purchase history** — H2 "Purchase history" + a `card` empty state: "No purchases yet." + supporting line "Buy a credit pack above and it'll show up here with the date and plugin it went toward." **When populated**, no table layout was designed yet — recommend using the system's `.table` component (see `nocturne-readme.md`'s Components table) with columns: Date / Pack / Amount / Status.
7. **Footer** — divider top, small link row: "Terms of Service · Refund Policy".

---

## Interactions & behavior (cross-cutting)
- **Nav active state**: every app screen's own nav link gets `aria-current="page"`; Home's brand mark links back to itself as a home-anchor (no `aria-current` needed there since there's no "Home" nav item to mark, the brand mark IS that link).
- **Scroll anchors**: `#how` and `#features` on Home need `scroll-margin-top: 88px` so the sticky/fixed-height nav doesn't crop the section heading when jumped to.
- **Hover/press states**: use the system's built-in states (don't hand-roll) — accent-ramp tints on buttons, 2px accent `:focus-visible` ring on all interactive elements, `::selection` accent tint, disabled = 45% opacity.
- **Dashboard verification gating**: copy was deliberately changed from "you must verify before doing anything" to "verify before you build/download" — confirm with backend whether `PluginsController`/`CreditsController` currently blocks plan creation pre-verification; if so, this is a product/API change, not just a copy change.
- **Builder steps 2/3**: currently non-interactive previews (opacity 0.45). Do NOT ship them permanently inert — they need to light up and populate with real content as the user progresses through the actual plan → build → validate → download flow. Loading states (spinner/skeleton while a plan or build request is in flight) and error states (invalid description, build failure, validation failure) are **not designed** — flag these as open scope before implementation, or default to a simple inline `tag`-based error message (e.g. a red/warning-toned tag near the failed step) using an ad-hoc semantic color if the system doesn't define one (Nocturne has no error/danger token — recommend proposing one, e.g. a desaturated warm hue at the same lightness step as the accent, rather than inventing an off-system red).

## State management
No new state beyond what the existing JS already tracks. Redesign changes presentation only, except:
- Home offer band price ("£4.99 for 25 credits") — pull from the cheapest live pack, don't hardcode.
- Builder free-build counter ("2 remaining") — bind to live `BuildEntitlementService` state.
- Dashboard stat cards, "Your plugins" count, Billing current-credit balance, purchase history — all must bind to real data; the designs show representative/example values only.

## Assets
No image/photo assets on these 5 screens. All icons are inline Phosphor SVGs already embedded directly in the design files — copy the `<svg>` markup verbatim (they're sized 14–16px, `fill="currentColor"` so they inherit text color automatically).

## Open questions / unspecified states (flag before or during implementation)
1. No error/danger color token exists in Nocturne — needed for build failures, validation failures, invalid input. Recommend proposing one rather than improvising off-system red.
2. My Plugins populated-list layout, and Purchase History populated-table layout, are not fully designed — follow the patterns called out above but confirm before pixel-locking.
3. Builder steps 2/3 populated content (what the plan review UI and the build/validate progress UI actually look like mid-flow) is not designed — only the collapsed/preview state is.
4. "Account ▾" nav menu is a non-functional placeholder in every app screen — needs its own dropdown design (profile, logout, settings?) before implementation.

## Files in this bundle
```
designs/
  ModuleMint - Home.dc.html          — open directly in a browser to view (source = exact spec, inline styles)
  ModuleMint - Dashboard.dc.html
  ModuleMint - Builder.dc.html
  ModuleMint - My Plugins.dc.html
  ModuleMint - Billing.dc.html
  nocturne-styles.css                 — design system token + component stylesheet (source of truth for values above)
  nocturne-readme.md                  — full Nocturne design system guide (do/don't rules, component catalogue)
```

## Implementation order suggestion
1. Port design tokens into the app's CSS (new CSS variables block in `site.css`/`app.css`/`billing.css`, or a new shared `tokens.css` included by all).
2. Restyle Home (`index.html`) first — it has zero dependency on live account state, purely presentational.
3. Restyle Dashboard + nav pattern (shared across all 4 app screens) — get the nav right once, reuse.
4. Restyle Builder — wire steps 2/3 as functionality allows.
5. Restyle My Plugins + Billing — these need the populated-state layouts designed (see open questions) before final polish, but the empty states can ship from this spec directly.
6. Sweep remaining auth/legal/admin pages for the same token swap.
