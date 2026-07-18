
# FixItHere3

The marketplace context. Defines the language used to describe customers, providers, and the work that flows between them.

## Language

**Service**:
A category of mobile work the platform offers to customers (e.g. "Dog grooming", "Tire repair", "Oil change"). Abstract and slow-changing — exists in the catalogue independent of any provider or customer.
_Avoid_: Service type, service category, product (when referring to the catalogue entry)

**Provider**:
A person or business registered to perform one or more **Services** at customer locations. Short for "Service Provider"; use `Provider` everywhere in code and UI to avoid collision with **Service**.
_Avoid_: Service provider (in running text after first introduction), worker, vendor, contractor

**Customer**:
A person who books **Jobs** for **Services** at a location they specify.
_Avoid_: Client, user (User is the auth-layer term; Customer is the domain term)

**Offering**:
A specific **Provider**'s commitment to perform a specific **Service**, with that Provider's price and availability. Customers don't browse Offerings directly — they navigate the **Catalog** by Service category, see the Providers offering that Service near them, and select one; the (Service, Provider) tuple identifies an Offering. A single Provider with multiple Services has one Offering per Service and appears in each corresponding Catalog section. Each Offering has a `priceCommitment` of either `firm` (Customer can book directly at the listed price; no **Estimate** needed) or `pending` (Customer must open an **Inquiry** for an **Estimate** before a **Job** is created).
_Avoid_: Listing, service offering, package

**Catalog**:
The customer-facing index of all **Services** the platform offers, organised into sections (one section per Service). Customer enters from a Service section → sees the eligible **Providers** in that section, sorted by proximity → selects one → the resulting Offering identifies what the Customer is requesting. Service-first navigation is the only entry point — there is no Provider-first or Offering-first browse in v0.1.
_Avoid_: Marketplace, store, directory

**Price Commitment**:
The point in the lifecycle at which a dollar amount is firmly attached to a future **Job**. For an **Offering** with `priceCommitment = firm`, this happens at listing time; for `priceCommitment = pending`, it happens when the Customer accepts an **Estimate**. Every **Job** has exactly one Price Commitment by the time work begins.
_Avoid_: Quoted price, locked price

**Job**:
A single instance of work — one **Customer** booking one **Provider** to perform one **Service** at one location at one time. The lifecycle entity that flows through the dispatch state machine and accumulates the rating at the end. A **Job** is created either directly from an **Offering** (browse path) or from an accepted **Estimate** (inquiry path or Open Request claim).

Each Job has a `kind`:

- `service` — the Provider performs work for the Customer; the Customer pays; both parties rate at completion.
- `onSiteEstimate` — the Provider travels to the Customer's location, inspects, and issues a regular **Estimate**. Free up to the platform's standard estimate radius; beyond that, the Customer may optionally pay a **Travel Fee** to enable the visit. No work is performed and no rating is collected. If the Customer accepts the resulting Estimate, a *new* Job with `kind = service` is created (linked back via `inquiryId`).

A `kind = service` Job's **happy-path lifecycle** is a state machine over these states:

- `scheduled` — created with a future agreed start time. Provider has it in their **Schedule**. The Provider's **Active Job** begins here.
- `enRoute` — Provider has departed for the Customer's location. Geo-tracking active; Customer sees ETA.
- `arrived` — Provider is at the Customer's location. Auto-prompted when within 10m of Customer location (geo-fence); Provider taps to confirm.
- `inProgress` — work is underway. Hourly clock running if applicable.
- `paused` — a revised **Estimate** is awaiting the Customer's accept/decline; resumes to `inProgress` on accept, or terminates on decline.
- `completed` — Provider has marked the work done. **Active Job ends** (Provider returns to pre-Job Online state and is reachable for new Customers). Payment captures. Customer is notified and the rating window opens.
- `closed` — both ratings submitted, or the rating window has expired. Final state.

`kind = onSiteEstimate` Jobs use a subset of these states (`scheduled → enRoute → arrived → estimateIssued | estimateDeclined`) with no `inProgress`, `paused`, `completed`, or rating window.

**Payment timing** (Stripe Connect Express): authorize at Job creation (when the Estimate is accepted, or at booking for a `firm` Offering), capture at `completed`, re-authorize when a revised Estimate is accepted at a different amount, cancel the authorization on a terminal failure path.

**Terminal failure states** (a `kind = service` Job ends in one of these instead of `closed`):

- `cancelledByCustomer` — Customer cancelled in any state from `scheduled` through `paused`. Customer pays per a time-graded tier (free >24h before start; 50% from 2h–24h before; 100% <2h before, after `enRoute`, or `arrived`; mid-`inProgress` per Q6 form rules). No ratings collected.
- `cancelledByProvider` — Provider cancelled. Customer always receives a full refund plus a rebook credit graded by lateness; Provider is recorded a **Strike**. No ratings collected.
- `customerNoShow` — Provider reached `arrived`, Customer was not present after a 15-minute wait window with attempted contact. Customer pays 100% of agreed amount; Provider is freed (Active Job ends).
- `providerNoShow` — Job was still in `scheduled` 30 minutes past the agreed start time with no `enRoute` transition; Customer (or system) marked no-show. Customer receives full refund and an emergency-rebook flow; Provider is recorded a major Strike.
- `terminatedRevisionDeclined` — Customer declined a revised **Estimate** in `paused`. Cost-on-decline rules from Q6 apply. Proceeds to `closed` after payment settlement; no ratings collected.

`disputed` is a transient (non-terminal) state during a Customer-raised dispute in the rating window. Resolves to `closed` or to one of the cancellation states based on adjudication. Dispute mechanics are pending Q12.

The numeric thresholds (24h / 2h cancellation cutoffs, 50% / 100% capture percentages, 15-min / 30-min no-show triggers, rebook-credit percentages, Strike rolling-window rules) are platform-configurable defaults in v0.1 and belong in an ADR rather than the glossary. Per-Service overrides are deferred to v0.2.
_Avoid_: Booking, request, trip, visit, appointment, ticket, order, gig

**Strike**:
A recorded negative incident against a **Provider**, principally a `cancelledByProvider` or `providerNoShow` event. Strikes accumulate in a rolling window and feed into pattern detection: thresholds trigger required actions (coaching prompts, temporary rate-limits on new bookings, manual admin review, suspension). Strikes are also a soft input to ranking on the **Catalog** — Providers with recent Strikes rank lower among otherwise-equivalent matches. Strikes are visible to the Provider in their own dashboard but not publicly displayed.
_Avoid_: Penalty, demerit, infraction, ding

**Rating**:
A 5-star score (1 = worst, 5 = best) with an optional free-text comment, submitted by either party at the end of a `kind = service` **Job**. Bidirectional and asymmetric in *prompts*: the Customer is asked about quality, professionalism, and timeliness; the Provider is asked about location accuracy, scope honesty, punctuality, and access. Both parties use the same 5-star scale.

Submission is blind: each party rates without seeing the other's. Ratings are revealed to both parties simultaneously when (a) both have submitted, or (b) the 7-day rating window closes (in which case whichever side submitted is then revealed). Once submitted, a Rating is immutable; corrections require a dispute.

A Customer's average Rating is shown to a **Provider** only when the Provider is considering an **Inquiry** or **Open Request** from that Customer (aggregate only — never per-comment text). A Provider's average Rating and per-comment text are publicly visible on the Provider's profile, with the Customer's first name + last initial.

Ratings affect Provider ranking in the **Catalog** as a tiebreaker after proximity. New Providers with fewer than 5 Ratings receive a "New Provider" badge and rank at the median position rather than the bottom. Ratings do **not** affect Provider payouts (worker-classification posture).

Eligibility on terminal states: `closed` Jobs allow both directions. `customerNoShow` allows Provider→Customer rating only (Customer wasn't present to assess Provider). All other terminal failure states (`cancelledByCustomer`, `cancelledByProvider`, `providerNoShow`, `terminatedRevisionDeclined`) collect no Ratings.

Multi-dimensional Ratings (separate scores for timeliness, quality, communication) are deferred to v0.2.
_Avoid_: Review (may apply specifically to the comment text), score, feedback

**Favorite**:
A **Customer**'s saved reference to a **Provider** for quick rebooking. Surfaces in the Customer's app for one-tap re-engagement: tapping a Favorite navigates to the Provider's profile or initiates a new **Inquiry** with the **Service** the Customer most recently booked with that Provider. Any Provider may be favorited (a prior completed **Job** is not required). The relationship is private to the Customer except for an optional one-time notification to the Provider when first favorited.
_Avoid_: Bookmark, follow, save, friend

**Dispute**:
A challenge raised by either the **Customer** or the **Provider** against a **Job** that has reached `completed`, within the 7-day rating window. Eligible Customer reasons include work quality, scope mismatch, unauthorized charge, property damage, or safety concern; eligible Provider reasons include unfair **Rating**, abusive Customer, equipment damage. Disputes cannot be raised on the cancellation, no-show, or `terminatedRevisionDeclined` terminal states in v0.1.

A Dispute moves the Job into the transient `disputed` state. Provider payout is held during the dispute. The Job exits `disputed` to `closed` with the resolution's financial and rating adjustments applied. Disputes flow through two phases:

1. **Direct Resolution** — a 3-day in-app structured chat where the parties try to settle. Either side can propose terms (refund amount, rating adjustment, redo-at-no-charge) and either side can mark resolved. Direct Resolution that succeeds closes the Dispute without admin involvement.
2. **Admin Review** — invoked when Direct Resolution times out (3 days) or either party escalates. Platform admin reviews evidence (chat logs, geo-traces, state-machine timeline, Estimate history, payment log) and issues a decision within a 5–7 business-day target SLA. Decisions can include refunds (full/partial/none), **Strikes**, **Rating** expunge, account suspension, or a "redo at no charge" outcome (which spawns a new `kind = service` **Job** with `redoOf = <originalJobId>`, priced at $0 to the Customer).
   _Avoid_: Complaint, claim, ticket, grievance

**Appeal**:
A one-shot review of a Dispute's Admin Review decision, available only for **severe outcomes**: account suspension, or a **Strike** that crosses a suspension threshold, or a contested financial impact above a platform-set value threshold. Either party may invoke at most one Appeal per Dispute. The Appeal is reviewed by a different (senior) admin within its own target SLA. The Appeal decision is final in v0.1.
_Avoid_: Reconsideration, second review, escalation

**Inquiry**:
A pre-**Job** conversation between a **Customer** and exactly one **Provider**. The Provider is identified one of two ways: (1) the **Customer** picks the Provider from the **Catalog** directly, or (2) the Provider **Claims** an **Open Request** the Customer posted to the **Request Board**. Once the Inquiry is established, it is 1:1 and carries the Customer's description, media (photos/videos), and an in-app message thread. Resolves into an accepted **Estimate** (which becomes a **Job**), a withdrawn inquiry, or a declined estimate.
_Avoid_: Lead, request, ticket, enquiry (the British spelling — pick one)

**Open Request**:
A Customer's service request that did not match an immediately-available **Provider** (either no nearby Provider was online, or the Customer chose a future scheduled time). Posted to the **Request Board** for any eligible **Provider** in the area to **Claim**. Carries the **Service**, location, scheduled time (or window), description, and optional photos/videos. Has a state of `green` (available to claim), `red` (a Provider has Claimed; Inquiry in progress; no other Provider can act), `removed` (Inquiry succeeded — **Job** created), or `expired` (scheduled time passed without a successful Inquiry; Customer notified).

v0.1 Open Requests are **negotiation-only** — pricing is determined through the Inquiry → Estimate flow that follows a Claim. v0.2 will add an optional Customer-set **Fixed Price** field on Open Requests: when set, the Customer specifies a willing-to-pay amount upfront, and a Provider's Claim is take-it-or-leave-it at that amount (no Estimate negotiation). The v0.2 design carries forward Q3's deferred Posting concept; document the v0.2 spec separately when scoping that release.
_Avoid_: Open offer, public posting, tender, gig, listing

**Request Board**:
The per-Provider-filtered view of currently-`green` **Open Requests** that match a Provider's eligibility (Provider has a published **Offering** for the requested Service, location is within Provider's serviceable distance, etc.). Items show in green if available; red items appear visible-but-locked while another Provider is in negotiation. Selecting a green item is a **Claim** and transitions the Open Request from green → red, locking it to that Provider until the Inquiry resolves. Distinct from a Provider's personal **Schedule**.
_Avoid_: Calendar (overloaded with Provider's personal schedule), feed, marketplace, queue

**Claim**:
A Provider's act of taking exclusive negotiation rights on an **Open Request** by selecting it from their **Request Board**. The Claim transitions the Open Request `green → red` and creates an **Inquiry** between the claiming Provider and the Customer. While the Open Request is red, no other Provider can contact the Customer about that request. The lock returns `red → green` if the Inquiry is withdrawn or the Estimate declined by either party, or if the Provider abandons (timeout — value pending). The lock is `removed` once the Customer accepts an Estimate (a **Job** is created).
_Avoid_: Pick, take, accept (Accept is reserved for Estimate acceptance)

**Schedule**:
A **Provider**'s personal calendar of accepted **Jobs** — the work they have committed to perform, including `kind = onSiteEstimate` visits. Distinct from the **Request Board** (which is the public pool of unclaimed Open Requests) and from **Working Hours** (the recurring template).
_Avoid_: Calendar (overloaded), agenda, diary

**Working Hours**:
A **Provider**'s declared weekly-recurring availability template (e.g. Tue–Sat 09:00–17:00). Visible to Customers on the Provider's profile as the times the Provider normally accepts bookings. Drives default values for **Online** auto-transitions but does not by itself make a Provider Online — bookings *outside* Working Hours are still possible by mutual agreement; **Online** *outside* Working Hours is also allowed if the Provider sets it manually.
_Avoid_: Hours of operation, business hours, opening hours

**Online**:
A **Provider** state controlling whether the Provider appears in Customer instant-match results in the **Catalog** (Online → green dot, listed as available now; Offline → greyed-out indicator, listed but not "available right now"). A Provider who is Offline can still be selected by a Customer; the Inquiry just sits until the Provider next becomes reachable. Online is a single boolean per Provider with two normal control modes:

1. **Manual toggle** — Provider explicitly sets Online on or off. May be set inside *or* outside Working Hours. The Provider may attach an **Online Duration** (e.g. "online for the next 3 hours") which expires to Offline automatically.
2. **Auto-off at end of Working Hours** — opt-in: when Working Hours end for the day, Online auto-flips to Offline if it was on.

In addition, a **system-forced Offline** applies whenever the Provider has an **Active Job**. This is not a Provider choice. Online is restored automatically when the Active Job ends (completion, or termination via declined revised Estimate).

The legitimate Offline triggers, then, are exactly: manual toggle off, Online Duration expiry, Working-Hours-end auto-off, and Active Job (system). A Provider never goes Offline silently or for any other reason.
_Avoid_: Available, away, status, presence

**Online Duration**:
An optional timer the Provider attaches when manually toggling Online ("online for the next N hours"). When the duration elapses, the Provider's Online state auto-flips to Offline. Distinct from Working Hours, which are a recurring weekly template.

**Active Job**:
A **Job** for which the **Provider** has accepted the **Estimate** (or completed direct booking) and which has not yet reached completion or termination. A Provider has at most one Active Job at a time. While they have one, they are forced Offline; existing **Inquiries** with other Customers continue (the Provider can still message), but no new instant-match Customers can pick this Provider until the Active Job ends.

**Estimate**:
A **Provider**'s priced commitment, issued at one of two moments:

1. **Initial Estimate** — a response to an **Inquiry**; when the **Customer** accepts it, the Inquiry converts into a **Job**.
2. **Revised Estimate** — issued during an in-progress **Job** when scope or price needs to change (Provider-discovered scope expansion, Customer-requested additions, or scope reduction). When the Customer accepts it, it becomes the Job's new active **Price Commitment**.

Every Estimate takes one of three forms: **Hourly**, **Fixed-price**, or **On-site**. A Job accumulates a chain of accepted Estimates over its life; the most recent one is authoritative.
_Avoid_: Quote, bid, proposal, offer (Offer is reserved for the dispatch fan-out concept; not yet defined)

**Hourly Estimate**:
An **Estimate** form: a Provider's hourly rate × the Provider's estimate of how many hours the work will take. The **Job** is billed on actual time spent. Customer must see the running clock during the **Job**.

**Fixed-price Estimate**:
An **Estimate** form: a single firm dollar amount the **Customer** will pay regardless of actual time, plus an estimated duration. Provider absorbs over-runs; under-runs do not reduce the bill. Out-of-scope work requires a revised **Estimate** before continuing.

**On-site Estimate**:
An **Estimate** form used when the **Provider** can't price the work from photos/videos alone. The Provider travels to the Customer's location, inspects, then issues an **Hourly** or **Fixed-price Estimate** which (if accepted) becomes a `kind = service` **Job**. The visit itself is tracked as a `kind = onSiteEstimate` **Job**. Free to the Customer up to the platform's standard estimate radius; beyond that, the Customer may optionally pay a **Travel Fee** to enable the visit.

**Travel Fee**:
An optional fixed dollar amount the **Customer** pays the **Provider** to enable an `onSiteEstimate` **Job** when the Provider's location exceeds the platform's standard estimate radius from the Customer's location. The fee schedule is platform-set (not Provider-set) to keep estimates as close to free as possible while letting the Customer extend the radius when they need a specific Provider. Distinct from any pricing inside a `service` **Job**.
_Avoid_: Mileage charge, callout fee, dispatch fee, trip fee

## Relationships

- A **Provider** publishes one or more **Offerings**
- An **Offering** is a (**Provider**, **Service**) pair with price and availability
- A **Customer** enters from the **Catalog** (Service-first); selecting a Provider in a Service section identifies an Offering
- A **Customer** creates a **Job** in one of three ways:
  1. **Direct booking** — books a `firm`-`priceCommitment` **Offering** at a chosen time (no **Estimate** required); produces a **Job** immediately if the Provider is available at that time
  2. **Inquiry path** — opens an **Inquiry** with a Provider they picked from the Catalog (instant if Provider is online; deferred if not) → accepts an **Estimate** → **Job**
  3. **Open Request path** — when no nearby Provider is available at the chosen time, posts an **Open Request** to the **Request Board** → a Provider **Claims** it → **Inquiry** is created with the claiming Provider → standard Estimate flow → **Job**
- An **Inquiry** belongs to exactly one **Customer** and is addressed to exactly one **Provider** (the Customer chooses)
- An **Inquiry** produces zero or one **Estimate** (the chosen **Provider** either responds or doesn't)
- A **Job** belongs to exactly one **Customer** and exactly one **Provider** (after acceptance)
- A **Job** carries a chain of one or more accepted **Estimates** over its life; the most recently accepted Estimate is the active **Price Commitment**
- A revised **Estimate** issued mid-Job pauses the Job until the Customer accepts (Job resumes at the new commitment) or declines (Job ends, with cost-of-work-done-so-far rules applying based on Estimate form)
- A **Job** with `kind = service` ends with one rating from the **Customer** about the **Provider**, and one rating from the **Provider** about the **Customer**. A **Job** with `kind = onSiteEstimate` does not collect ratings.

## Example dialogue

> **Dev:** "When a **Customer** picks an **Offering**, do we create the **Job** before the **Provider** accepts?"
> **Domain expert:** "Yes — the **Job** is created in a `requested` state immediately. The dispatch service then offers it to the matching **Provider**. If they decline or time out, we may re-offer to a different **Provider**, but it's still the same **Job**."

## Flagged ambiguities

- "Service" was originally used to mean three things (category, listing, instance) — resolved into **Service**, **Offering**, and **Job** respectively.
- "Service Provider" shortens to **Provider** everywhere; the long form is reserved for marketing copy and external-facing legal text.
- **Inquiry routing**: resolved — every **Inquiry** is 1:1 between exactly one Customer and one Provider. The Provider is identified either by Customer pick (Catalog → Provider) or by Provider **Claim** (from the **Request Board**). Customer-set-price broadcast (the v0.2 Fixed Price option on **Open Requests**) is deferred — but the v0.1 model already includes broadcast through Open Requests; only the Customer-set-price variant is held back.
- **Travel Fee schedule on widened search**: when the Customer widens the search radius and accepts a Travel Fee, what does the fee schedule look like — flat per-tier, per-km, max-cap? Pending decision.
- **Abandonment timeout on red-state Open Requests**: how long can a Provider hold the lock without making negotiation progress before the lock auto-returns to green? Pending decision (suggested starting point: 10 minutes of no message activity).
- **"Online and available" semantics for Providers**: resolved — Provider has declared **Working Hours** plus a manual **Online** toggle (with optional **Online Duration**); Working Hours can auto-flip Online to Offline at day-end. While in an **Active Job**, the Provider is system-forced Offline. The four legitimate Offline triggers are: manual toggle off, Online Duration expiry, Working-Hours-end auto-off, and Active Job.
- **Inquiry expiration timeout**: how long does a directly-picked Provider have to respond to an Inquiry before it expires and the Customer is offered to convert it to an Open Request? Pending decision.
- **Open Request eligibility filters on the Request Board**: a Provider must have a published **Offering** for the Service to see related Open Requests, but additional filters (rating threshold, distance cap, capacity) are pending decision.
- **Job failure-path states**: resolved — five terminal failure states (`cancelledByCustomer`, `cancelledByProvider`, `customerNoShow`, `providerNoShow`, `terminatedRevisionDeclined`) plus a transient `disputed` state. Numeric thresholds (cancellation timing, no-show wait windows, capture percentages, Strike rolling-window) are platform-configurable defaults belonging in an ADR.
- **Reschedule path** (move a scheduled time without triggering cancellation penalties) — deferred to v0.2.
- **Per-Service cancellation policy overrides** (different windows for emergency Services vs scheduled work) — deferred to v0.2.
- **Rating mechanics**: resolved — 5-star single-overall, optional comment, asymmetric prompts, 7-day blind+simultaneous reveal, immutable, aggregate-only Customer visibility to Providers, ranking tiebreaker after proximity, no payout linkage. Multi-dimensional Ratings deferred to v0.2.
- **Dispute resolution mechanics**: resolved — `disputed` transient state with 3-day Direct Resolution then 5–7 business-day Admin Review; Provider payout held during; outcomes catalogue per **Dispute** glossary entry. Severe outcomes are eligible for a one-shot **Appeal** (reviewed by a senior admin; final). The numeric value threshold qualifying as "severe financial impact" is platform-configurable and belongs in an ADR.
- **Browse path and Estimate**: resolved — each **Offering** has a `priceCommitment` of `firm` (book directly, no **Estimate**) or `pending` (must go through **Inquiry → Estimate** to create a **Job**).
- **On-site Estimate as a Job**: resolved — the on-site visit is a **Job** with `kind = onSiteEstimate`. If the resulting Estimate is accepted, a separate **Job** with `kind = service` is created. Both link back to the same **Inquiry**.
- **Standard estimate radius and Travel Fee schedule**: the numeric values (radius threshold, fee per kilometre or fee tiers) are unset — pending decision when v0.1 pricing is finalised.
- **Proximity-first matching**: a stated design principle — browse and dispatch should prefer **Providers** physically close to the **Customer**. The exact distance-weighting in ranking is unset.
