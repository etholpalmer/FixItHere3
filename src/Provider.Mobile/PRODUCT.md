# Product

## Register

product

## Platform

ios

## Users

The provider — a working tradesperson (plumber, mechanic, HVAC tech) with the
phone in a van, a pocket, or a greasy hand between jobs. They are not browsing;
they are working. They glance at the app at a red light, on a doorstep, under a
hood — a few seconds at a time — to answer *what's my next job, where is it, and
what does the customer need from me right now?* They accept work, drive, tell the
customer when they're running late, arrive, do the job, and get paid. Speed and
glanceability matter more than delight: a mis-tap or a hunt for the right button
costs them a job or a rating.

The secondary audience is an investor: this app is the supply side of the
marketplace, the proof that the two-sided system is real and not one screen
pretending to be two.

## Product Purpose

FixItHere's provider app is the worker's console for the same live job the
customer is watching. Its job is to make the *next action* unmistakable at every
step — Accept, Depart, Running late, Arrived, Start work, Complete — and to show
the two numbers that run the job: the countdown to *when they must leave to
arrive on time*, and what they'll be paid. It is the other half of every event
the customer sees; when the provider taps, the customer's phone lights up.

Success is a provider who can run a job correctly with three-second glances and
never has to think about which button — and an audience that sees a real,
two-sided system react in real time.

## Positioning

A worker's tool, not a consumer app. Closer to a courier or ride-hail *driver*
app than to the customer side: denser, faster, action-first. It shares the brand
thread (the honey accent, the type ramp) but earns its keep through efficiency,
not reassurance.

## Brand Personality

Competent and calm. Same honey accent (`#9C6516`) as the customer app and the
console, but carried with more restraint — the provider surface is a working
instrument. Confident type, high contrast readable in a sunlit van, and one
obvious primary action per screen. Never cute; a tradesperson mid-job has no
patience for personality that slows them down.

## Anti-references

- **Not the customer app re-skinned.** The customer optimises for reassurance;
  the provider optimises for speed and correctness. Same tokens, different
  density and hierarchy.
- **Not a bristling dashboard of controls.** "One clear next action" is the
  principle the current build already gets right and must keep — a wall of
  equally-weighted buttons is the failure mode.
- **Not a debug surface.** No dev toggles, no simulated-GPS switches, no raw
  state strings on the worker's screen — those belong only in the `/dev` console.
- Not the cream/sand editorial default; true surfaces, warmth in the accent.

## Design Principles

1. **One clear next action.** Every job state resolves to a single, obvious,
   full-width primary button. Secondary affordances (chat, call, running-late)
   are visibly secondary. This is the app's spine — the redesign amplifies it,
   never dilutes it.
2. **Glanceable in three seconds.** Next job, its address, its countdown, and the
   payout must be legible at a glance in bad light. The depart-by countdown — the
   number that changes the provider's behaviour — is headline-scale with urgency
   as colour.
3. **HIG-native.** iPhone app, iOS conventions: large titles, Dynamic Type ramp,
   44pt targets, safe areas. `reference/ios.md` governs. Denser than the customer
   app, but never below the touch-target floor.
4. **Two-sided truth.** Every provider action maps to a customer-visible event.
   The design should make the provider *feel* that their tap reaches someone —
   confirmation is immediate, never ambiguous.
5. **Coherence is the feature.** Same acceptance bar as the customer app: one
   tell (a leaked timestamp, a bare trade name, an impossible price) reads as
   fake. Believability over completeness.
