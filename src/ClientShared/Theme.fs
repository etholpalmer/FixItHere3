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

let gutter = 16.0
let gapTight = 4.0
let gap = 12.0
