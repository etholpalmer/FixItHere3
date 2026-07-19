# Product

## Register

product

## Platform

web

## Users

The person driving a live FixItHere demo — usually the founder, sometimes a
salesperson — standing in a client's meeting room with a laptop mirrored to a
wall TV. They are talking while operating, so every control has to be findable
without hunting, and the state of the system has to be readable from across the
room. A secondary audience watches over their shoulder: the prospective client
or investor the demo is being run for, who never touches the controls but does
form an impression of the product from what is on screen.

The job to be done: drive a scripted two-app scenario (book → accept → travel →
chat → arrive → work → pay → rate) without the tooling drawing attention to
itself or stalling the pitch.

## Product Purpose

FixItHere is a mobile-services marketplace connecting customers to providers who
travel to them. This repository is the prototype that proves the *experience* —
the live tracking, the realtime chat, the job state machine — ahead of building
the real product. The `/dev` console is the operator's instrument panel for that
prototype, and the in-app map is what the demo audience actually watches.

Success is a demo that runs start to finish without the operator apologising for
the tooling.

## Positioning

The demo tooling should feel like part of the product, not scaffolding around it.

## Brand Personality

Consumer-marketplace polish: warm, credible, and unfussy. Approachable rather
than corporate, precise rather than playful. The warmth lives in a single honey
accent and in generous, confident spacing — never in decorative flourish. The
operator surface is calm and instrument-like so the map and the live data are
the only things that move.

## Anti-references

Not a debug page: no unstyled default form controls, no wall of monospace, no
"it's only internal" concessions. Not an observability dashboard either — this
is not Grafana, and cold slate-and-cyan telemetry styling would misrepresent a
consumer marketplace. Avoid the generic marketplace clone look (white page,
uniform rounded cards, one friendly blue) and avoid the warm cream/sand
"editorial" surface; both are category reflexes rather than decisions.

## Design Principles

The map is the hero. Everything else is chrome and should recede — the only
bright, saturated, moving things on screen are the data.

Legible under a projector. Midtones disappear when a laptop is mirrored to a TV
in a lit room, so contrast is a functional requirement, not an accessibility
checkbox.

Operator speed outranks polish. If a treatment looks better but costs a beat of
hesitation while someone is mid-sentence, the treatment loses.

State is colour-coded because it is information. Job state drives a real
pipeline; colour carries that meaning and is never decorative.

Earned familiarity. Standard affordances, standard form controls, consistent
component vocabulary — the tool should disappear into the task.

## Accessibility & Inclusion

WCAG 2.2 AA. Body text ≥4.5:1 and large text ≥3:1 against its background;
visible `:focus-visible` rings on every interactive control; full keyboard
operability; `prefers-reduced-motion` honoured with a non-animated equivalent
rather than a removed one. The live event stream is an ARIA live region. Job
state is never encoded in colour alone — every state pill carries its label.
