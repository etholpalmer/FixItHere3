# Product

## Register

product

## Platform

ios

## Users

The customer — someone at home or at their car with something broken, who has
just asked a stranger to come fix it. They are not a power user and they are not
relaxed: a pipe is leaking, the car won't start, the furnace died in January.
They open the app to answer one question over and over — *is help actually
coming, and when?* — and they check it standing in a hallway, one-handed, while
also dealing with the problem. Between opening the app and the provider arriving
they will look at the tracking screen a dozen times. Everything else in the app
exists to get them to that screen and to reassure them once they are on it.

The secondary audience is an investor watching this run in a pitch. They never
booked a real job; they are judging whether this looks like a product a real
person would trust with a real emergency.

## Product Purpose

FixItHere connects a customer to a nearby provider who travels to them —
plumber, mechanic, HVAC tech — and lets the customer watch the whole thing
happen live: book, get matched, see the provider drive toward them, chat, and
pay. This app is the customer's half. Its job is to convert *anxiety* into
*confidence*: a clear price up front, a real arrival time, a moving car on a map,
and a way to reach the person on the way.

Success is a customer who never has to wonder what is happening — and never sees
anything that reads as a demo, a placeholder, or a number that escaped.

## Positioning

A consumer service app you'd trust in an emergency — closer to a ride-hail
arrival screen than to a form-heavy booking tool. The live map and the countdown
are the product; the booking flow is the shortest possible path to them.

## Brand Personality

Warm, credible, unfussy. The reassurance comes from *legibility and calm*, not
from cheerful illustration. One honey accent (`#9C6516`, already the map marker
and the console's accent, so all three surfaces read as one product), confident
spacing, and type you can read across a kitchen at arm's length. Never chirpy,
never corporate. The one moment of real emotional weight is the arrival
countdown, and the design should let that number carry it without decoration.

## Anti-references

- **Not a generic marketplace clone**: not a white page of uniform rounded cards
  with one friendly blue and a hero search bar. That is the category reflex.
- **Not a SaaS dashboard**: no metric tiles, no gradient stat cards, no
  KPI-by-numbers. A customer does not have a dashboard; they have one job in
  flight.
- **Not the warm cream/sand "editorial" surface** — that is the 2026 AI default.
  Warmth lives in the accent and the spacing, on a true white page, not in a
  beige-tinted background.
- Not a debug screen: no raw ISO timestamps, no enum names, no `$277.5`, no
  "My location". Every value passes through a formatter before it reaches glass.

## Design Principles

1. **The tracking screen is the hero.** Map, status, and countdown are the whole
   product while a job is live; everything else is a path to this screen and
   should recede. The only bright, moving thing is the data — the provider's car
   and the countdown.
2. **One question, always answered.** "Is help coming, and when?" must be
   readable in under a second, from across a room, one-handed. The countdown is
   headline-scale and its urgency is a colour, not a caption.
3. **HIG-native, not web-in-a-webview.** This is an iPhone app. Follow iOS
   conventions — large titles, the Dynamic Type ramp, 44pt touch targets, safe
   areas, native list and sheet affordances — rather than porting web layout.
   `reference/ios.md` governs.
4. **Coherence is the feature.** A single tell (a lake job, a leaked timestamp,
   a rating from the wrong person) costs more trust than a missing feature buys.
   Believability is the acceptance bar.
5. **Calm under load.** The furnace is dead; the app is the one steady thing on
   the screen. No spinners that thrash, no toasts that pile up, no motion that
   competes with the map.
