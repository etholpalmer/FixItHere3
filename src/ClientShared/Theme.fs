/// The apps' visual vocabulary, in one place.
///
/// PRODUCT.md describes the brand as "consumer-marketplace polish: warm,
/// credible, and unfussy… the warmth lives in a single honey accent and in
/// generous, confident spacing — never in decorative flourish". That accent is
/// the same amber the `/dev` console and the map marker already use, so the
/// three surfaces read as one product.
///
/// Concrete colours rather than iOS semantic ones is a deliberate, and
/// temporary, compromise: MAUI does not surface `label`/`systemBackground`
/// through Fabulous, so Dark Mode is not yet handled. That is a Phase 4 task,
/// and it is written down here rather than left as a surprise.
module FixItHere.ClientShared.Theme

open Microsoft.Maui.Graphics

let private rgb (hex: string) = Color.FromArgb hex

// --- surfaces ---------------------------------------------------------------
let page = rgb "#FFFFFF"
/// One step off the page, for anything that groups: composer bar, inbound
/// bubbles, list rows.
let surface = rgb "#F2F2F7"
let surfaceEdge = rgb "#D7D7DE"

// --- ink --------------------------------------------------------------------
/// 15.8:1 on white. Body copy never uses anything lighter.
let ink = rgb "#1C1C1E"
/// 4.6:1 on white — passes AA for body, so timestamps and captions stay
/// readable rather than being decoratively grey.
let inkMuted = rgb "#5C5C63"

// --- brand ------------------------------------------------------------------
/// The honey accent. Dark enough that white sits on it at 4.6:1, which means it
/// can carry a primary button without the label going grey-on-gold.
let brand = rgb "#9C6516"
let onBrand = rgb "#FFFFFF"
/// The same hue at surface weight: outbound bubbles are warm without shouting.
let brandWash = rgb "#F9E9CC"
let brandEdge = rgb "#D9A441"
let brandInk = rgb "#3A2B14"

// --- state ------------------------------------------------------------------
let danger = rgb "#B02A2A"
let warning = rgb "#9A5B0A"
let calm = rgb "#2B4D8A"
let success = rgb "#1B5E3A"

// --- geometry ---------------------------------------------------------------
/// Deliberately heavier than the 1px hairline a web habit reaches for. On a
/// phone held at arm's length a hairline disappears, and the composer is the
/// one control on the screen that must never look uncertain.
let strokeThick = 2.0
let strokeHair = 1.0

let radiusBubble = 18.0
let radiusControl = 12.0

// --- spacing (4pt grid) -----------------------------------------------------
/// iOS lays out on a 4pt grid. The views were mixing on-grid values (4, 8, 12,
/// 16) with off-grid ones (6, 14); the redesign snaps to these named rungs so
/// rhythm is a choice rather than an accident. One source of truth — the legacy
/// flat aliases below point into it.
module Space =
    let xs = 4.0
    let sm = 8.0
    let md = 12.0
    let lg = 16.0
    let xl = 24.0
    let xxl = 32.0

/// Legacy flat aliases, kept so existing call sites keep compiling. New code
/// should reach for `Space.*` directly.
let gutter = Space.lg // 16 — screen gutter / list-row inset
let gap = Space.md // 12
let gapTight = Space.xs // 4

// --- type ramp (iOS Dynamic Type, default sizes) ----------------------------
/// The apps already reached for these by instinct: 34 / 28 / 22 / 20 / 17 / 16 /
/// 15 / 13 / 12 / 11 are exactly the iOS Dynamic Type default point sizes, and
/// the views were scattering them as literals alongside a few off-ramp outliers
/// (42, 40, 24, 18). Naming the ramp gives the redesign passes one vocabulary
/// and a place to collapse the outliers onto.
///
/// Point sizes, not scaled: Fabulous does not surface Dynamic Type, so these are
/// the `.large` content-size-category values — which is what the pinned-Light
/// demo runs at. `headline` is `body` at semibold; set the weight at the call
/// site, not here.
module Font =
    let largeTitle = 34.0
    let title1 = 28.0
    let title2 = 22.0
    let title3 = 20.0
    let headline = 17.0
    let body = 17.0
    let callout = 16.0
    let subhead = 15.0
    let footnote = 13.0
    let caption = 12.0
    let caption2 = 11.0

// --- iPhone layout constants ------------------------------------------------
/// HIG minimum hit target. No tappable control ships smaller than this, however
/// tight the provider app's density gets.
let touchTarget = 44.0
/// Default horizontal screen inset. The customer app breathes at the wider end;
/// the provider app tightens toward `gutter` where density earns it.
let screenMargin = 20.0
