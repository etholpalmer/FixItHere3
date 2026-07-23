# Lessons Learned

> Engineering journal capturing what was learned, what went wrong, and what
> tradeoffs were made during development of this project.

## Environment

- **OS:** macOS (Darwin 25.5.0)
- **Tooling:** Claude Code CLI (agentic session), no traditional IDE
- **SDK:** .NET 10.0.302 (single SDK installed; no MAUI workload installed as of this writing)
- **Key Tools:** F# 9 (ships with .NET 10 SDK), EF Core 10.0.10, xUnit 2.9.3, FsCheck.Xunit 2.16.6 (pinned), SignalR, SQLite, MAUI workload 10.0.20/10.0.100 (installed mid-project; see Archive)
- **CI:** GitHub Actions ([.github/workflows/ci.yml](.github/workflows/ci.yml)) — tests on ubuntu-latest, advisory Mac Catalyst build on macos-latest
- **Last Updated:** 2026-07-23 (all 19 plan tasks complete; post-plan fixes from live use: map markers, countdown legibility, provider availability, error-bar lifetime, reseed resync, the demo pair that shared no job, and cold Cmd helpers. CI re-enabled and green; five stale gaps archived)

---

## Lessons

### 2026-07-23 — Half of an invariant was designed and commented; the other half was arithmetic ("half-specified invariant")

**Insight:** The whole product is two phones showing two sides of **one** job, and the seed gave the two prefilled demo accounts no live job in common. The reason is the interesting part: the pairing has two halves, and only one was ever specified. `mkJob` chose the customer from an explicit index — deliberate, with a comment saying "the soonest belongs to John Reyes — the demo login" — and chose the *provider* from `(i * 3) % provs.Length`, an expression written to spread work across a roster. Half the invariant was designed; the other half was whatever the arithmetic produced, and it produced GearHeads Mobile.

**Discovery:** Not by testing — by the user stating it flatly: "John Reyes did not request anything from Mike's Plumbing. Therefore they will remain disconnected." Every seeded screen looked right in isolation, which is exactly why it survived a full acceptance walkthrough: the customer's Home was correct, the provider's Home was correct, and nothing on either screen refers to the other.

**Design intent:** `(i * 3) % 20` is good code for what it was written for — 3 is coprime with 20, so finished jobs distribute evenly across the roster and no provider's review list repeats a customer. It was a *distribution* rule that silently also became an *identity* rule for the one job the demo opens on.

**Impact:** The general form, and the thing to hunt for: **an invariant stated over a pair where only one side is pinned.** The comment is the tell — a comment asserting "this belongs to X" next to a computed Y is a claim about half a relationship. Whenever seed or fixture data must connect two named actors, assert the *join*, not each end: the test that now guards this reads the soonest scheduled job and checks both its customer and its provider, which is a claim no arithmetic can accidentally satisfy.

**Active mitigation:** `the two prefilled logins share the soonest job` in [SeedTests.fs](tests/Backend.Api.Tests/SeedTests.fs) — mutation-checked (reverting the pin turns it red).

**Related:** [Mistake: the two demo logins shared no live job](#2026-07-23--the-two-demo-logins-shared-no-live-job); [Test fixtures that dodge the production shape pass forever](#2026-07-19--test-fixtures-that-dodge-the-production-shape-pass-forever)

---

### 2026-07-23 — A fix that makes something transient can outrun the tool that verifies it

**Insight:** This project leans on `xcrun simctl io … screenshot` as its answer to "rendering defects are invisible to a green suite". That technique has a floor: it cannot observe anything shorter-lived than a simulator round-trip. Fixing the persistent error bar gave it a ~7-second life, and three attempts to photograph it failed — each tap-then-screenshot cycle through the automation took longer than the bar now survives. The bug was easy to screenshot precisely *because* it was broken; the fix is not.

**Discovery:** Trying to produce the same before/after evidence used for every other visual fix this session, and running out of clock each time.

**Impact:** Two things follow. (1) When a change makes behaviour transient, decide the verification mechanism *before* implementing — a test that asserts the timer and its generation token is the right instrument, and a screenshot is not. (2) Report the difference honestly rather than letting "verified" cover both: in the commit that shipped this, the reset-resync half was verified live and the error-bar half by test only, and saying so is the difference between a journal that can be trusted and one that cannot. Absence of a bar in a late screenshot is not evidence it dismissed itself.

**Related:** [A screenshot verifies the *installed* binary, not the source](#2026-07-22--a-screenshot-verifies-the-installed-binary-not-the-source); [Mistake: the error bar outlived the screen that produced it](#2026-07-23--the-error-bar-outlived-the-screen-that-produced-it)

---

### 2026-07-23 — The restore path keeps forgetting what the login path does ("restore-path divergence")

**Insight:** This codebase has two ways to reach an authenticated Home — `LoggedIn` (fresh sign-in) and `SplashDone` → `RestoreSession` — and the second has now silently omitted work the first does **twice**: first the SignalR hub, then the provider's shift flag. Both omissions were invisible to every test project and to any walkthrough that begins with a fresh sign-in, which is the walkthrough an author naturally runs.

**Discovery:** The second occurrence surfaced while verifying an unrelated feature. After relaunching the provider app, Home read "Offline" with an empty job list, while `GET /providers/1` returned `online: true`. The login arm fetches the provider *precisely* to avoid that and carries a comment saying so; the restore arm, fifteen lines away in the same `match`, never did.

**Design intent:** Restore was added (task 0b) so a backgrounded app would not come back to a sign-in screen, which reads as an expired session. It was written as a *navigation* shortcut — get the user to Home — and the navigation is correct. What was never enumerated is the set of **startup side effects**; those had accreted on the login arm one at a time, so each new one had to be remembered twice by whoever added it.

**Impact:** The class generalises past this app: whenever a second route into a state is added, every side effect wired to the first route is a candidate omission, and the author is structurally unlikely to notice because they test the route they just built. Where the effect can be keyed on the *state* rather than the message, that is the durable fix — the hub is keyed on `m.Session` now for exactly this reason. Where it must be a one-shot fetch, the two arms should share one function.

**Active mitigation:** None yet, and that is the honest state. The concrete one is a shared `startupCmds (s: Session) : Cmd<Msg> list` called by both arms in each app, so a new startup effect can only be added in one place — roughly twenty minutes' work, not done. Until then the rule is manual: **grep both arms whenever either changes.**

**Related:** [Mistake: a restored provider session was shown Offline with no jobs](#2026-07-23--a-restored-provider-session-was-shown-offline-with-no-jobs); [Mistake: a restored session connected to no live hub](#2026-07-22--a-restored-session-connected-to-no-live-hub)

---

### 2026-07-23 — A task-based Cmd can fire before it is dispatched

**Insight:** `apiCmd` *was* `Cmd.ofTaskMsg (task { … })` ([Update.fs](src/Provider.Mobile/Update.fs)), and `ofTaskMsg` takes an **already started** task — F#'s `task { }` is hot, beginning execution when the expression is evaluated. The HTTP call therefore happened while `update` was *constructing* the `Cmd`, not when Fabulous later dispatched the returned sub, so a `Cmd` built and then discarded had already performed its effect.

**Discovery:** By a mutation test that failed to fail. To prove a new Cmd-draining test could go red, the effect was removed from the batch (`Cmd.batch [ …; (ignore backOnline; Cmd.none) ]`) — and the test still passed. The `let backOnline = apiCmd …` binding above it had already fired the call, so draining the Cmd was never what made the assertion true. Mutating the **guard** instead (`when not m.Online` → `when false`), so the Cmd is never constructed at all, turned it red as expected.

**Impact:** Two consequences, and the first is the uncomfortable one. (1) A test that drains a `Cmd` may be passing because *construction* fired the effect rather than because dispatch did — so "I drained the Cmd" is weaker evidence than it looks, and a mutation that only removes the Cmd from the returned batch cannot distinguish the two. Mutate the construction, not the return. (2) In product code, building a Cmd speculatively and choosing not to return it is a live API call.

**Resolution (2026-07-23):** `bb4a5d8` made `apiCmd` and `delayCmd` cold in both apps — the task is now built inside `Cmd.ofSub`, so nothing runs until dispatch — and added `apiCmd is cold` / `delayCmd is cold` to both suites, mutation-checked (restoring `Cmd.ofTaskMsg` turns the customer test red). The audit that ran first found **no** Cmd constructed and dropped anywhere, so consequence (2) never became a live defect. The *reasoning here stays permanently active*: F#'s `task { }` is hot, `Cmd.ofTaskMsg` takes an already-started task, and the rule "mutate the construction, not the presence in the returned batch" applies to every Cmd helper anyone writes next. Only the specific hot `apiCmd` retired.

**Active mitigation:** `apiCmd is cold: no call until the Cmd is dispatched` and `delayCmd is cold: the clock starts on dispatch`, in both `tests/Customer.Mobile.Tests/UpdateTests.fs` and `tests/Provider.Mobile.Tests/UpdateTests.fs`. Without them the helper reverts silently the first time it is rewritten.

**Related:** [MVU test helpers that discard the returned `Cmd<Msg>`…](#2026-07-18--mvu-test-helpers-that-discard-the-returned-cmdmsg-silently-hide-untested-guard-logic); [Mutation is the only honest proof that a test can fail](#2026-07-19--mutation-is-the-only-honest-proof-that-a-test-can-fail) — this is that lesson applied to itself: the mutation's job was to test the test, and what it actually caught was a bad mutation; [Gap: effects fire at Cmd construction (archived, closed)](#2026-07-23--effects-fire-at-cmd-construction-and-the-blast-radius-is-unaudited)

---

### 2026-07-23 — A clamp that protects one half of its domain breaks the other ("guard with a second face")

**Insight:** `Travel.minMinutes = 1.0` floors the ETA so that a provider visibly crossing the map never shows "ETA 0 min". The justification is written into the code and is correct — *for a provider who is moving*. The clamp also applies to a provider who has **stopped**, and there it produces a number that can never change: the customer's screen held "Arriving in 1:00" for as long as the provider stood at the door, and a countdown that never reaches zero reads as a frozen app rather than an imminent doorbell.

**Design intent:** The floor exists to prevent a false *impression* — 0 looks like a broken readout, not an arrival. That reasoning was applied to exactly one region of the input domain and then shipped as a rule over all of it.

**Impact:** The general form: a floor, ceiling, clamp or default is a statement about the **whole** domain but is almost always argued from one region of it. When adding one, name the region the argument covers and ask what the value means outside that region. Here the answer was that outside the moving case the number has stopped being an estimate at all — so the fix was not a lower floor but to stop counting and change the words ("Arriving now").

**Related:** [Mistake: "Arriving in 1:00" never reached zero](#2026-07-23--arriving-in-100-never-reached-zero)

---

### 2026-07-23 — Derive state that another change can invalidate, never store it

**Insight:** Third application of the same principle in this codebase, and the first time it was reached for deliberately rather than after a bug. The demo clock pushes an affine *map* so no client holds a timer (a moved deadline cannot strand a callback). Provider availability now follows it: "can this provider be offered work" is computed from the jobs list every frame (`Domain.availability`) rather than kept as a second flag beside `Online`. Storing it would mean every path that changes a job's state also has to remember to change the flag — the exact shape of the stale-timer bugs this project has already paid for twice.

**Discovery:** Not from a failure. Writing the "provider on a job is off the market" rule, the obvious implementation was a `PausedForJob: bool` on the model, and the reason not to was recognised from the clock's design rather than from a new incident.

**Impact:** The test for whether to derive or store is not "how often is it read" but **"how many places can invalidate it".** Availability can be invalidated by every job transition, a hub push, a reload and a shift toggle — five writers to one flag, or zero writers and one function. It also made the feature's hardest edge disappear: nothing has to "turn availability back on" when a job completes, because the job leaving the in-flight set *is* the change.

**Related:** [Compromise: sessions in plain `Preferences`](#2026-07-22--sessions-are-stored-in-plain-preferences-not-securestorage) (the opposite call — stored deliberately, with the condition to revisit written down)

---

### 2026-07-23 — A defect can be the only thing making a feature work ("accidental mechanism")

**Insight:** Before it was fixed, the tracking map reloaded its WebView about four times a second — the "flashing" bug. Every reload re-ran the page, and the page fetches the provider's position on load, so the marker *appeared* to track the drive. Memoising the source object stopped the reloads and, with them, the only mechanism that had ever moved that marker. The intended live path — the page's own SignalR subscription — had never worked once in the project's history.

**Discovery:** The user reported that the two dots never meet. Chasing it showed the provider position advancing server-side and the ETA text counting down (11 km → 0 km) while the map marker sat still: the F# app's typed hub client was receiving positions, the map's own client was not.

**Design intent:** the flashing fix was correct and remains correct — Fabulous re-set an attribute whose value was a new object each render. Nothing about it was wrong. It simply deleted an accident that had been standing in for a feature.

**Impact — name the pattern: *accidental mechanism*.** When a defect incidentally performs a feature's job, repairing the defect regresses the feature, and the regression reads as the fix's fault. Hunt for it whenever you remove **repeated work** — a reload, a re-render, a poll, a retry, a redundant refresh. Ask what that repetition was *also* doing. Checklist item: after killing a redundant refresh, prove the thing it refreshed still updates by its *intended* path, not merely that the screen looks right at one instant.

**Related:** [Mistake: memoised the map's HTML string but rebuilt its source object every render](#2026-07-22--memoised-the-maps-html-string-but-rebuilt-its-source-object-every-render) · [Mistake: the map never tracked live](#2026-07-23--the-map-never-tracked-live-signalr-credentials-and-a-casing-mismatch) · [Mistake: no CORS policy](#2026-07-22--the-in-app-map-had-never-received-a-live-position-because-the-backend-had-no-cors-policy)

---

### 2026-07-23 — The scripted demo did work the product itself did not ("demo-path divergence")

**Insight:** Driving the provider from its origin to the customer existed **only** inside the `/dev` scripted timeline's own interpolation loop. The real in-app **Depart** applied `DepartEnRoute` and nothing else. So "the provider drives to the customer, and the map closes in as they converge" — the centre of the tracking experience — looked implemented for the whole project's history, while the path a real user takes never moved the car at all.

**Discovery:** Only surfaced by running the **two-app walkthrough manually** (tapping Depart in the provider app) instead of triggering the canned `/dev/demo/start`. The scripted demo had always been the thing that got run, so it had always looked fine.

**Design intent:** task 0a deliberately pushed demo scaffolding off the product surface and into `/dev`. That was the right call. But the *behaviour* moved with the *controls* — the console ended up owning something the domain needed.

**Impact — name the pattern: *demo-path divergence*.** When a scripted path owns behaviour the real path needs, the script becomes the only place the product works, and every rehearsal reinforces the illusion. When moving scaffolding out of a product, separate **controls** (belong in the operator console) from **behaviour** (belongs in the domain, fired by the real action). Rehearse at least once through the real user path, never only the canned one.

**Related:** [Mistake: departing flipped the status but never drove the provider](#2026-07-23--departing-flipped-the-status-but-never-drove-the-provider)

---

### 2026-07-22 — A screenshot verifies the *installed* binary, not the source

**Insight:** `dotnet build -f net10.0-ios -t:Compile` proves the *source* compiles; it does **not** produce an installable `.app`. A screenshot taken after only a compile-gate shows whatever binary was last *installed* — which can be several edits stale. "It compiles", "it packages", and "it is the binary on the device" are three different claims.

**Discovery:** Twice this session (reviewing the redesigned customer and provider Payment screens) I drove a flow to a screen and screenshotted the *previous* design. The compile gate was green and the new code was correct; the installed `.app` simply predated the redesign, because between the review's `-t:Compile` gate and the screenshot I never ran a full `-c Debug` build + reinstall. Caught by the `.app` binary's mtime predating the edits.

**Impact:** This is a third rung under the [rendering-defects lesson](#2026-07-22--rendering-defects-are-structurally-invisible-to-a-green-test-suite): it is not enough to *look* — you must confirm you are looking at *this* code. Before any screenshot review, rebuild `-c Debug` and reinstall, or check the `.app` binary mtime against your last edit.

**Updated 2026-07-23 — it happened a third time, and the obvious verification is a trap.** Chasing the map bug, I edited `ClientShared/MapHtml.fs`, ran the compile gate, screenshotted, and again saw stale behaviour. Worse, the check I reached for to *prove* the fix was in the binary was useless: `grep` and `strings` over `Customer.Mobile.dll` found neither the new code nor the old. .NET keeps string literals in the metadata `#US` heap as UTF-16, so an ASCII `grep` cannot see a substring of a large format string, and `strings -e l` did not find it either. Both a false negative and a false positive are possible, so **binary grepping is not a verification mechanism here**. The only reliable checks are (a) `rm -rf obj/Debug/net10.0-ios bin/Debug/net10.0-ios`, rebuild `-c Debug`, reinstall, and (b) exercise the behaviour. Verify by *running*, not by inspecting.

**Related:** [Rendering defects are structurally invisible...](#2026-07-22--rendering-defects-are-structurally-invisible-to-a-green-test-suite) · Mistake: [A restored session connected to no live hub](#2026-07-22--a-restored-session-connected-to-no-live-hub) · Mistake: [The map never tracked live](#2026-07-23--the-map-never-tracked-live-signalr-credentials-and-a-casing-mismatch)

---

### 2026-07-22 — Rendering defects are structurally invisible to a green test suite

**Insight:** Across one session, every defect found after booting a simulator had been invisible to a passing suite — at the time, 179 to 194 tests, both apps compiling at 0 errors / 0 warnings. In each case the value was correct, correctly typed, and correctly plumbed; it was simply never passed through the function that exists to present it, or two individually-correct halves were composed into a wrong whole. No unit test can see that, because each half passes its own assertion.

**Discovery:** The user insisted on a simulator screenshot before building the next feature on top of the countdown. The first screenshot found a bug; so did nearly every screenshot after it.

**Design intent:** The plan's `-t:Compile` gate was adopted because the test projects compile only `Domain.fs`, `Api.fs` and `Update.fs` — never `Views/*.fs`. That gate is sound and does what it claims: it catches a view referencing a `Msg` that no longer exists. The error was treating "the code compiles and the logic is tested" as evidence about *the product*, and reporting both with the same confidence.

**Bug categorisation** — what would have caught each most cheaply:

| Defect | Cheapest catcher |
|---|---|
| `"Arriving in 3:06 late"` (sign-blind label + "late" suffix) | **Screenshot** — each half correct alone |
| Countdown 00:23 vs proposal 7:23 PM (local-time conversion) | **Screenshot** — internally consistent either side |
| `Scheduled for: 2026-01-01T00:31:31.5622679+00:00` | **Screenshot** — or a lint rule banning raw DTO date fields in views |
| `Price: $277.5` | **Screenshot** — or the same lint rule for money |
| Map flashing 4×/second | **Screenshot** — no metric available to the suite |
| Chat entry collapsed to zero width | **Screenshot** — layout has no test surface |
| Provider rows truncating off-screen | **Screenshot at device width** |
| `Nav.resetTo` skipping the position fetch | **Cmd-draining unit test** — was cheap and simply not written |
| Cancel with no confirmation | **Design review** — not a defect, an omission |

Only one of nine was recoverable by a cheaper test. The rest genuinely required looking.

**Impact:** "Tests pass and it compiles" and "I have seen it" are different claims and must be reported as such. Phase 4 is a full visual redesign; it runs screenshot-first, not screenshot-at-the-end.

**Active mitigation:** `xcrun simctl io <udid> screenshot` works without the MCP integration and needs no Xcode selection, so a screenshot is always available even when the panel is not. The iOS 26.5 simulator runtime is now installed, so a full build is ~2.5 min and a screenshot is one command.

**Resolution:** The companion gap "the in-app WebView map redesign has never been looked at" was archived 2026-07-22 once the simulator runtime landed and the map was screenshotted. The *reasoning* in this lesson stays active permanently — it is a claim about what test suites can and cannot observe, not about one screen.

**Related:** [Mistake: memoised the map's HTML string but rebuilt its source object](#2026-07-22--memoised-the-maps-html-string-but-rebuilt-its-source-object-every-render), [Mistake: a correct, mutation-tested fix caused a regression in what it stopped doing](#2026-07-22--a-correct-mutation-tested-fix-caused-a-regression-in-what-it-stopped-doing)

---

### 2026-07-22 — A metric that cannot observe the failure it is being used to rule out

**Insight:** I claimed the tracking map was not reloading because the backend logged zero SignalR reconnections across 25 seconds. The metric was incapable of detecting the failure: the WebView reloaded and re-established its connection faster than a dropped connection took to surface server-side. A screenshot that looked washed out *was* the real signal, and I explained it away because the metric I had chosen disagreed with it.

**Discovery:** The user reported the map visibly flashing, hours after I had declared it stable.

**Impact:** Before using an absence-of-signal as proof, state what the signal's latency and threshold are, and whether the failure mode would clear them. "Zero X observed" is only evidence if X would have appeared within the observation window. The generalisable name is **metric-blindness to the observed failure**: the check ran, returned clean, and could not have returned anything else.

**Related:** [Mistake: memoised the map's HTML string but rebuilt its source object](#2026-07-22--memoised-the-maps-html-string-but-rebuilt-its-source-object-every-render)

---

### 2026-07-22 — Composition defects: two correct halves, one wrong whole

**Insight:** Three separate user-visible defects this session had the same shape — each component was correct in isolation and the composition was wrong. `Countdown` returned a sign-blind label ("Arriving in") while `Format.countdown` appended "late" to the value, producing "Arriving in 3:06 late". `Format.clockTime` converted to local time (correct for a real timestamp) while the countdown ran on demo time (correct for a fictional one), producing a headline and a body four hours apart. The map memoised its HTML string (correct) inside a freshly-constructed source object (also correct in isolation).

**Impact:** Unit tests are per-component by construction, so this class is invisible to them by construction too. The catch is either a screenshot, or a test that asserts on the *composed* output — `Countdown.oneLine` now exists partly so a property test can assert a rendered line never contains both directions at once.

**Active mitigation:** `Countdown.oneLine` plus the property test `no countdown ever reads as both directions at once` ([tests/Shared.Tests/ContractTests.fs](tests/Shared.Tests/ContractTests.fs)). The pattern generalises: when two pure functions will be concatenated for display, test the concatenation.

**Related:** [Lesson: rendering defects are structurally invisible to a green test suite](#2026-07-22--rendering-defects-are-structurally-invisible-to-a-green-test-suite)

---

### 2026-07-22 — Delete unreachable code rather than repairing it

**Insight:** The plan listed "StartDemo hardcodes customer 1" as tell 25. Task 0a had already removed every UI entry point, so the code was unreachable — repairing the id would have been motion, not progress, and would have kept a demo-scaffolding dependency (`StartDemo: int -> int -> Task<...>`) in both shipping apps' `ApiDeps`.

**Discovery:** Grepping the views for `StartDemo` before starting the fix returned zero dispatches in either app.

**Impact:** Before fixing a defect inherited from a plan, confirm the defective path is still reachable. A plan written before earlier tasks landed can describe a world that no longer exists. Deleting removed one `Msg` case, one `DemoStarted` case, one dep and one Api implementation per app.

**Related:** [Compromise: Provider.Mobile's in-app "Start Demo" button hardcodes customer id 1](#2026-07-18--providermobiles-in-app-start-demo-button-hardcodes-customer-id-1) (archived)

---

### 2026-07-18 — F# record auto-properties don't populate EF Core `DbSet<T>` the way C# does

**Insight:** The idiomatic F# pattern `member val Services: DbSet<Service> = Unchecked.defaultof<_> with get, set` looks like a C# auto-property but does **not** get populated by EF Core's DbSet discovery/initialization. Every DbSet access threw `NullReferenceException` the moment the seeder touched `db.Services`.

**Discovery:** `Seed.run` failed at its very first line (`db.Services.AddRange services`) with a bare NRE. Traced to `AppDb`'s DbSet properties never being assigned — `Unchecked.defaultof<_>` stays null because F#'s `member val` backing field isn't the same shape EF Core's context-initialization scan expects.

**Design intent:** The plan followed standard EF Core advice (auto-property DbSets, C#-idiom) on the assumption that "DbSet auto-init just works" — true in C#, but the assumption doesn't hold one layer down once the property is F#'s `member val` instead of a genuine CLR auto-property with EF Core's expected initialization hook.

**Impact:** Fixed by making DbSets **computed** members backed by `this.Set<T>()`, and registering every entity explicitly in an overridden `OnModelCreating` rather than relying on auto-discovery:

```fsharp
override _.OnModelCreating(modelBuilder: ModelBuilder) =
    modelBuilder.Entity<Service>() |> ignore
    // ... one per entity

member this.Services: DbSet<Service> = this.Set<Service>()
```

This generalizes to any F# + EF Core project: never use `member val ... with get, set` for a DbSet; always compute it via `Set<T>()`.

**Related:** [Mistake: NullReferenceException on first DbSet access](#2026-07-18--nullreferenceexception-on-first-dbset-access)

---

### 2026-07-18 — F# 9's stricter indentation breaks the one-line `[<Attr>] type X = { ... }` idiom

**Insight:** F# 9 (default with the .NET 10 SDK) enforces stricter offside-rule indentation than F# 7/8. The common one-liner `[<CLIMutable>] type Foo = { A: int; B: string }` compiles fine for single-line records, but as soon as the record body spans multiple lines, putting the attribute on the same line as `type` produces `FS0058`/`FS0547`/`FS0010` errors demanding the body be indented further than the compiler will accept from that starting column.

**Discovery:** Building `Dtos.fs` and `Db.fs` — both authored with `[<CLIMutable>] type X = \n  { ... }` — failed immediately with a wall of `FS0058: Unexpected syntax or possible incorrect indentation` errors, one per multi-line record.

**Impact:** Fix is mechanical but must be applied everywhere: put `[<CLIMutable>]` (or any attribute) on its own line above `type X =` whenever the record body itself is multi-line. Single-line records (`type Foo = { A: int }`) are unaffected. This is a version-drift trap — code snippets copied from F# 7/8-era examples (including this project's own design spec, written before the fix) will fail to compile verbatim under F# 9 defaults.

**Active mitigation:** None automated yet — see [Gap: no fantomas/format-on-save hook wired up](#2026-07-18--no-fantomasformat-on-save-hook-configured). A pre-commit `dotnet build` catches it immediately, but doesn't prevent the first-draft error.

**Related:** [Mistake: multi-line CLIMutable records fail to compile](#2026-07-18--multi-line-climutable-records-fail-to-compile-under-f-9)

---

### 2026-07-18 — Minimal-API delegate parameter names are a silent contract with the route

**Insight:** ASP.NET Core Minimal APIs bind route/query parameters to a handler delegate by matching **parameter name**, via reflection over the delegate signature — not by position or explicit attribution. In F#, `Func<AppDb, int, IResult>(fun db jobId -> ...)` binds `jobId` to a route segment or query string key named `jobId` purely because that's the lambda parameter's name. Rename the F# lambda parameter and the binding breaks silently (requests that used to bind now 400, with no compile-time signal).

**Discovery:** Not directly hit as a bug in this project (the plan's Executor Notes flagged it preemptively), but confirmed as real risk while wiring `/providers?serviceId=&lat=&lng=` and `/jobs/{id}/{path}` — F# lambda parameter names had to be kept in lockstep with the plan's route templates by convention, with no compiler enforcement tying the two together.

**Impact:** Any future endpoint change must keep lambda parameter names and route/query template names in sync by hand. If binding mysteriously fails (200 becomes 400 with no error detail), suspect a renamed lambda parameter first, before suspecting the client.

**Related:** [Lesson: Nullable query params require a bound local before use in LINQ predicates](#2026-07-18--nullable-value-types-cant-be-captured-by-address-inside-linq-expression-trees)

---

### 2026-07-18 — Nullable value types can't be captured by address inside LINQ expression trees

**Insight:** F# closures over a `Nullable<'T>`'s `.Value` member, when the closure becomes a LINQ `Expression<Func<...>>` (as EF Core's `Where` requires for query translation), fail to compile with `FS3155: A quotation may not involve an assignment to or taking the address of a captured local variable`. This is because `.Value` on a struct captured directly requires taking its address, which quotations/expression trees forbid.

**Discovery:** `db.Providers.Where(fun p -> p.ServiceId = serviceId.Value)` (where `serviceId: Nullable<int>` is an optional query parameter) failed to compile with FS3155 at three call sites in `Endpoints.fs`.

**Impact:** Fix: extract the `.Value` into an immutable `let` binding *before* the LINQ lambda, so the closure captures a plain `int`/`float`, not a struct member access:

```fsharp
let sid = serviceId.Value
db.Providers.Where(fun p -> p.ServiceId = sid)
```

This is a recurring pattern anywhere an optional (`Nullable<'T>`) minimal-API query parameter feeds an EF Core LINQ predicate — expect to need this extraction every time.

**Related:** [Lesson: Minimal-API delegate parameter names are a silent contract with the route](#2026-07-18--minimal-api-delegate-parameter-names-are-a-silent-contract-with-the-route)

---

### 2026-07-18 — `WebApplicationFactory<T>` boots in Production, and env vars must be set before the SUT's module loads

**Insight:** `WebApplicationFactory<TEntryPoint>` does **not** default to the Development environment — it boots Production unless told otherwise. For a minimal-hosting F# app (`Program.fs` with module-level `let builder = ...`, `let app = ...`, no `[<EntryPoint>]`), the environment is read once, at **module initialization**, which happens on the *first* access to anything in that module — well before `WebApplicationFactory.ConfigureWebHost` runs. Setting `ASPNETCORE_ENVIRONMENT` inside `ConfigureWebHost`, or even at F# module-level `do` in the test file, is too late or unreliable — module-level `do` in a test file only runs when something forces that module's static initializer, which isn't guaranteed before the factory builds its host.

**Discovery:** All Development-gated endpoints (`/dev/*`, `/dev/index.html`) 404'd under `WebApplicationFactory` even though the same build served them at 200 when run directly with `ASPNETCORE_ENVIRONMENT=Development dotnet run`. A throwaway diagnostic test (see technique note below) confirmed the factory was booting `"Production"`.

**Design intent:** The plan assumed environment-gating (`if app.Environment.IsDevelopment() then ...`) would "just work" under `WebApplicationFactory` the same way it does in `dotnet run`, since that's the standard integration-testing story for ASP.NET Core. The minimal-hosting-with-module-level-code shape breaks that assumption because there's no `Main` method boundary to hook into — the host is built as a side effect of module load.

**Impact:** Fix: set `Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development")` inside the `Factory` type's **constructor** (`type Factory() as this = ... do Environment.SetEnvironmentVariable(...)`), guaranteeing it runs on `new Factory()` — before `CreateClient()` triggers the host build — and definitely before the `Program` module's own static init fires (since `Factory` inherits `WebApplicationFactory<Program>`, touching `Program` forces its module load only *after* the constructor body runs). The general rule: for minimal-hosting F# apps under test, any environment/config value the `Program` module reads at load time must be set via environment variable in the test factory's constructor, not via `ConfigureWebHost`.

**Active mitigation:** Executor Notes in the Plan 1 implementation plan now document this pattern explicitly for future F#/minimal-hosting projects.

**Related:** [Mistake: dev endpoints 404 under WebApplicationFactory](#2026-07-18--dev-endpoints-and-dev-console-404-under-webapplicationfactory), [Lesson: diagnostic-via-failing-assertion technique](#2026-07-18--diagnostic-technique-force-runtime-state-into-an-assertion-failure-message)

---

### 2026-07-18 — `WebApplicationFactory`'s content root doesn't default to the SUT project directory

**Insight:** `WebApplicationFactory<T>` resolves its content root (used by `UseStaticFiles()` to find `wwwroot`) relative to the **test assembly's** output/base directory by default, not the system-under-test project's directory. Static files that work perfectly under `dotnet run` (content root = the project folder) 404 under the factory unless the content root is explicitly repointed.

**Discovery:** After fixing the Development-environment issue above, `/dev/index.html` still 404'd. `ConfigureWebHost`'s `builder.UseContentRoot(...)` (and even `UseSolutionRelativeContentRoot`, which additionally isn't available on plain `IWebHostBuilder` without the TestHost-specific extension) both landed too late for the same reason as the environment var — `UseStaticFiles()` had already captured its file provider by the time `ConfigureWebHost` ran.

**Impact:** Fix: set `ASPNETCORE_CONTENTROOT` via `Environment.SetEnvironmentVariable` in the same constructor as the environment fix, computed via F#'s `__SOURCE_DIRECTORY__` compile-time constant walked relative to the test file's known location (`Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "src", "Backend.Api")`) rather than trying to walk up from `AppContext.BaseDirectory` at runtime (which pointed somewhere unpredictable and caused a regression — see Mistake log).

**Related:** [Mistake: content-root resolution regressed 9 passing tests](#2026-07-18--content-root-fix-attempt-regressed-9-previously-passing-tests)

---

### 2026-07-18 — Diagnostic technique: force runtime state into an assertion-failure message

**Insight:** When debugging an ASP.NET Core integration test where `Console.WriteLine`/logging output isn't reliably surfaced by `dotnet test`, the fastest way to inspect actual runtime state (e.g., "what environment name did the factory actually boot with?") is to add a **throwaway test that asserts the expected value against the actual value**, and read the runtime value out of the assertion failure's "Expected/Actual" diff — not `failwithf` (which needs a generic return type workaround and produced its own unrelated compile error here) but a plain `Assert.Equal(expected, actual)`.

**Discovery:** Used twice in this session: once to confirm `WebApplicationFactory` boots `"Production"` not `"Development"`, and implicitly again validating the content-root fix. Both resolved a "why is this 404ing when the real app returns 200" mystery in one test run instead of several rounds of blind guessing.

**Impact:** This generalizes to any integration-test debugging session: add a `[<Fact>]` that intentionally asserts a *deliberately wrong* expected value against the thing you want to inspect, run once, read the diff, delete the test. Faster and more reliable than adding logging plumbing for a one-off question.

**Related:** [Lesson: WebApplicationFactory boots in Production](#2026-07-18--webapplicationfactoryt-boots-in-production-and-env-vars-must-be-set-before-the-suts-module-loads)

---

### 2026-07-18 — Shared file-backed SQLite fixtures need `DisableTestParallelization`

**Insight:** xUnit parallelizes test *classes* by default. This project's `WebApplicationFactory`-based tests all boot against the same on-disk file (`fixithere-demo.db`), and startup does `EnsureDeleted()` → `EnsureCreated()` → reseed every time. Under default parallelization, two test classes booting factories concurrently would race on that file (delete-while-another-reads, etc.).

**Discovery:** Anticipated proactively (not hit as a live bug) based on the plan's Global Constraint that every startup resets the DB — recognized during Task 7 wiring that this constraint plus shared-file storage plus default xUnit parallelism is a race condition waiting to happen.

**Impact:** Added `[<assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)>]` in a dedicated `AssemblyInfo.fs`, compiled first. Generalizes to any test suite where multiple integration-test classes share one mutable on-disk fixture (file DB, single log file, shared port): disable parallelization at the assembly level rather than hoping test ordering happens to avoid the race.

**Related:** none

---

### 2026-07-18 — Rebuilding both Debug and Release for `net10.0-maccatalyst` across many verification cycles exhausts local disk without warning, and disk-full breaks the agent's own tool execution before it breaks the build

**Insight:** Mac Catalyst builds carry heavy native-codegen intermediate artifacts (AOT/linker prep), and Release is dramatically larger than Debug for the same TFM: measured on this repo, `Provider.Mobile` alone is 102M (`obj/Debug/net10.0-maccatalyst`) + 794M (`obj/Release/net10.0-maccatalyst`) + 213M (`bin/Debug`) + 252M (`bin/Release`) ≈ 1.36GB; `Customer.Mobile` is ≈ 1.75GB by the same breakdown. The other declared TFMs (`net10.0-android`, `net10.0-ios`) sat at 0B–140K — they were never actually built, only restored — so the bloat is entirely a Debug+Release×maccatalyst effect, not a multi-TFM effect. Across Plan 3's ~12 tasks, each ending in a "build + test" checkpoint, this repeated Catalyst rebuild pattern (plus a 5.2GB `~/.nuget/packages` cache) ran the host's already-thin free-disk headroom down to zero.

**Discovery:** `df` showed free space collapsing (179Mi observed at one point) then hitting 0, at which point **Bash tool calls themselves started failing** — not the `dotnet build`/`dotnet test` commands under them, but the tool harness's own mechanism for capturing command output, which needs to write a small temp file. This is a distinctive failure mode worth recognizing on sight: when a Bash call errors with no command-specific error text (nothing about the command that was supposedly run), suspect disk exhaustion in the sandbox before suspecting the command or the harness.

**Impact:** For any MAUI (or similarly native-AOT-heavy) project built repeatedly across many task/verification cycles in one long agentic session, disk headroom should be checked periodically (`df -h /`), not assumed. A single stuck agent turn can otherwise escalate from "build is slow" to "no tool in this session can execute" with very little warning, and recovery requires the user to free space out-of-band since the agent's own tools are what's blocked.

**Active mitigation:** None automated yet. A cheap one: run `dotnet clean` (or delete `bin/`+`obj/` for the mobile apps) at natural task/phase boundaries rather than only at the very end, and/or build only the configuration actually needed for manual verification (`-c Debug`) instead of letting both configurations accumulate. See the related Gap for what a real fix looks like.

**Related:** [Gap: no disk-headroom check before/during long MAUI build-heavy sessions](#2026-07-18--no-disk-headroom-check-beforeduring-long-maui-build-heavy-agentic-sessions)

---

### 2026-07-18 — MVU test helpers that discard the returned `Cmd<Msg>` silently hide untested guard logic

**Insight:** Both `Provider.Mobile.Tests` and `Customer.Mobile.Tests` define `let up msg model = Update.update stubDeps msg model |> fst` — a convenience helper that keeps the model half of `update`'s `(Model * Cmd<Msg>)` return and throws the `Cmd<Msg>` away. Any test written purely against `up` can only ever assert on model-field changes; it cannot detect whether the code *decided* to schedule a command (an auto-reply, a hub call, a demo-start request) unless that decision also mutates the model directly. A guard that's expressed purely as "should I emit this Cmd or not" is invisible to `up`-only tests even when the test's name claims to cover it.

**Discovery:** In Provider.Mobile, the auto-reply regression tests all went through `up`, so they exercised `AutoReplyDue`'s counter/cycling behavior and message de-duplication, but never actually exercised the `isMine`/`AutoReply`/job-ownership guard inside `HubMessageReceived` that decides *whether* to schedule an auto-reply Cmd in the first place — despite the tests' names implying that coverage. Caught only because a later reviewer pass in this branch actually traced what `up` discards.

**Impact:** The fix generalizes: any guard whose entire job is "emit this Cmd or don't" needs either (a) a pure predicate extracted out of the `update` arm and unit-tested directly (what was done here — `shouldAutoReply` was pulled into `Domain.fs`), or (b) a test that calls `Update.update` directly and drains the returned `Cmd<Msg>` by invoking each `Sub` with a capturing dispatch function (`Customer.Mobile.Tests`' `` `start demo errors when not logged in` `` test does this correctly). Prefer (a) when the guard logic is nontrivial enough to name — it's independently reusable and the test reads as documentation of the rule, not just its symptom.

**Active mitigation:** Both suites now have a `runWith` helper that drains the returned `Cmd<Msg>` against recording stubs, so the typing-throttle and seen-gating criteria are asserted by tests that can actually fail.

**Resolution (2026-07-19):** `b36ef79` shipped Cmd-executing tests in both apps and proved them by mutation — removing the cooldown guard from `Update.fs` turned the new throttle test red (`Failed: 1, Passed: 30`), which the previous `up`-based test would have survived. The *reasoning* here stays active permanently: any MVU helper that discards the Cmd can only observe model fields, and guards that live entirely in the Cmd remain invisible to it. Only the specific untested-guard instances retired.

**Related:** [Mistake: auto-reply guard was never actually exercised by its own regression tests](#2026-07-18--auto-reply-guard-was-never-actually-exercised-by-its-own-regression-tests)

---

### 2026-07-19 — Cross-entity id-space collision: independent sequences that both start at 1

**Insight:** `Customer` and `Provider` are separate tables with independent identity columns, so the seeder produces customers 1–20 *and* providers 1–20. Any code that compares a bare `SenderId` to a bare `UserId` is therefore asking "same number?" when it means "same actor?". For the documented demo pair — John (customer **1**) and Mike's Plumbing (provider **1**) — those questions have opposite answers.

**Discovery:** Found by reading [Seed.fs:22-46](src/Backend.Api/Seed.fs) after a manual backend walk showed a message posted as provider 1 coming back with `senderName: "John"`. [Endpoints.fs](src/Backend.Api/Endpoints.fs) resolved sender names Customers-first, so *no* provider message could ever resolve correctly.

**Design intent:** `MessageDto.SenderId` was modelled as a plain int because within either table an id does identify its row. The assumption that held one layer up — "an id identifies a sender" — silently stopped holding the moment two id spaces met in one field. Nothing in the type system flagged it because both spaces are `int`.

**Impact:** Four user-visible features were broken simultaneously and silently, all from one root cause: message ownership (the peer's messages rendered as your own), auto-reply (`me <> Some msg.SenderId` was false, so it never fired), the typing indicator, and the seen receipt. Fixed in `1f3b309` by carrying role wherever identity crosses a boundary — `SenderRole` on the DTOs and the entity, role on the `Typing`/`Seen` hub events, `Role` on the client `Session`, and one `isSelf session id role` helper replacing every bare-id comparison.

**Meta-pattern to hunt for — "cross-entity id-space collision":** whenever two entity types with independent identity columns can appear in the same field, comparing ids alone is a latent bug that only manifests when the numbers happen to coincide. Grep for equality on `*Id` fields where the operand could come from either space.

**Active mitigation:** the `isSelf` helper is the only sanctioned comparison in both apps, and three regression tests use the *real* colliding shape (provider 1 + customer 1) rather than ids that dodge it. A side effect worth knowing: `Session` and `LoginResponse` now share a field set, so record literals of that shape need explicit type annotations to pin F# inference.

**Related:** [Mistake: the /dev console was left behind by the identity change](#2026-07-19--the-dev-console-was-left-behind-when-identity-was-namespaced), [Lesson: fixtures that dodge the production shape](#2026-07-19--test-fixtures-that-dodge-the-production-shape-pass-forever)

---

### 2026-07-19 — Test fixtures that dodge the production shape pass forever

**Insight:** A test can only fail on conditions its fixture actually constructs. Three separate tests here were green solely because their fixtures used id combinations that production data never produces.

**Discovery:** Surfaced during the Opus review pass and confirmed by reading fixtures against [Seed.fs](src/Backend.Api/Seed.fs). The auto-reply guard test used `me = Some 4` with sender `1` — non-colliding, so the guard "passed". The Customer fixture declared `ProviderId = 2; ProviderName = "Mike's Plumbing"`, but the seeder makes Mike's Plumbing provider **1**; the fixture was factually wrong about the seed *and* that wrongness is precisely what kept `SenderId ≠ UserId` and made the typing/seen tests pass.

**Impact:** The suite reported 100% green across the exact features that were broken in the demo. Coverage numbers said nothing because the fixtures had quietly excluded the failing case.

**Meta-pattern to hunt for — "fixture-avoided condition":** when a test's fixture picks specific ids, dates, or enum values, ask what the *seeder or production* produces. If the fixture's values are more convenient than reality's, the test is testing convenience.

**Active mitigation:** regression tests now assert the colliding shape explicitly, and the incorrect Customer fixture was corrected to provider 1 with a comment stating why the collision is deliberate.

**Related:** [Lesson: cross-entity id-space collision](#2026-07-19--cross-entity-id-space-collision-independent-sequences-that-both-start-at-1), [Lesson: mutation as the honesty check](#2026-07-19--mutation-is-the-only-honest-proof-that-a-test-can-fail)

---

### 2026-07-19 — Mutation is the only honest proof that a test can fail

**Insight:** A green test proves nothing about whether it *could* go red. The cheap check is to break the production code deliberately and confirm the test catches it — seconds of work, and the only evidence that distinguishes a real assertion from a decorative one.

**Discovery:** After writing Cmd-executing tests for the typing throttle, the guard was removed from [Update.fs](src/Provider.Mobile/Update.fs) (`| Chat jobId, Some s when not model.TypingCooldown ->` → `| Chat jobId, Some s ->`) and the suite re-run. The new test failed as intended (`Failed: 1, Passed: 30`); the file was then restored and re-verified green. The *previous* `up`-based test would have stayed green through that same mutation.

**Impact:** This is the technique that separates the two preceding lessons from wishful thinking. Any test written specifically to close a coverage gap should be mutation-checked once, at the moment it is written, while the mutation is obvious.

**Related:** [Lesson: MVU test helpers that discard Cmd](#2026-07-18--mvu-test-helpers-that-discard-the-returned-cmdmsg-silently-hide-untested-guard-logic), [Lesson: fixtures that dodge the production shape](#2026-07-19--test-fixtures-that-dodge-the-production-shape-pass-forever)

---

### 2026-07-19 — Verify an error's own premises before acting on it

**Insight:** Tooling errors can be stale or replayed. A message that names concrete state — a PID, a file, a port — can be checked against reality in one command, and acting on a stale one can destroy the thing it is asking you to create.

**Discovery:** A "Port 5162 is in use by Backend.Api (PID 6218)" error arrived twice. The second time, PID 6218 had already been stopped and the port was held by the preview-managed server started in response to the *first* error. The instruction — free the port and retry — would have killed a healthy server, reset the demo database, and started a replacement for no gain. `ps -p 6218` plus `preview_list` settled it in one call.

**Impact:** The reflex "port in use → kill the holder → retry" is usually right and was exactly wrong here, because the port holder *was* the intended outcome. Generalises to any imperative tool output: confirm the named state still exists before obeying.

**Related:** [Mistake: preview_start with a URL displaced the running dev server](#2026-07-19--preview_start-with-a-url-displaced-the-running-dev-server)

---

### 2026-07-19 — `launchSettings.json` outranks `ASPNETCORE_URLS`, and macOS owns port 5000

**Insight:** Two independent facts that compounded into total client/server disconnection. `dotnet run` applies the `applicationUrl` from `launchSettings.json`, which **overrides** the `ASPNETCORE_URLS` environment variable — so setting the env var appears to do nothing. Separately, on macOS port 5000 is held by the AirPlay Receiver (ControlCenter), which answers HTTP **403**, so a client gets a plausible-looking HTTP response from something that is not the API.

**Discovery:** The apps hardcoded `http://localhost:5000` while [launchSettings.json:8](src/Backend.Api/Properties/launchSettings.json) pinned 5162 and [README.md](README.md) claimed 5000 was the default. `curl localhost:5000` returned 403 with the backend stopped, which is what identified AirPlay as the occupant.

**Impact:** Neither mobile app could reach the backend at all on a default Mac — the exact scenario the prototype exists to demonstrate. The 403 is the nastiest part: a connection-refused would have been diagnosed in seconds, whereas a 403 reads like an auth problem in your own API. Fixed in `d884e66` by aligning `Config.baseUrl`, both Android overrides, and the README on 5162.

**Active mitigation:** [README.md](README.md) now states that launchSettings takes precedence and that 5000 must not be used on macOS. [.claude/launch.json](.claude/launch.json) pins `"autoPort": false` so tooling cannot reassign the port the apps hardcode.

**Related:** [Compromise: port 5162 is load-bearing](#2026-07-19--the-backend-port-is-hardcoded-in-three-places)

---

### 2026-07-20 — Environment-dependent failures cannot be gated by pre-flight probes; build, then classify

**Insight:** When a failure is a property of the *caller's environment* rather than of the artifact being checked, no pre-flight probe can predict it — the probe necessarily runs in a different context than the real call. The durable design is to run the real operation and classify its outcome afterwards: known environmental signatures skip loudly with exit 0, anything else gates.

**Discovery:** Four successive gating strategies for the Mac Catalyst CI job failed, each refuted by its own run:

| Attempt | Strategy | Refuting evidence |
|---|---|---|
| 1 | pick newest Xcode by version | 26.6 selected, build errors — version isn't the axis |
| 2 | require `MacOSX.sdk` directory to exist | directory exists, xcodebuild still refuses — existence ≠ registration |
| 3 | probe via `xcrun --sdk macosx --find actool` | name resolution false-passes on a half-registered install |
| 4 | probe via the **exact** explicit-path xcodebuild call | **all fifteen installs pass in a plain step; the identical call fails under MSBuild** |

Attempt 4 (run 29717135645) is the decisive one: same command, same shell session, same selected Xcode — succeeds standalone, dies inside `dotnet build` with `SDK "…MacOSX.sdk" cannot be located` (actool exit 72). The env-var hypothesis was then also refuted by evidence: `SDKROOT`/`DEVELOPER_DIR`/`MD_APPLE_*` are all unset in the failing build step, so the mechanism is something subtler in MSBuild's invocation context. It remains unidentified — acceptably, because the final design no longer depends on knowing it.

**Design intent:** each probe was an attempt to keep the job honest — skip when the environment can't build, gate when it can. The intent was right; the assumption that usability is a checkable property of the Xcode install was wrong one layer down.

**Impact:** generalises to any CI gate wrapping a toolchain it doesn't control: probing "will X work?" duplicates X's own resolution logic and will eventually diverge from it. Running X and pattern-matching the failure is simpler and cannot drift.

**Active mitigation:** [.github/workflows/ci.yml](.github/workflows/ci.yml) — the build step tees logs, greps for the three known signatures (`SDK "…" cannot be located` / `requires Xcode` / `unable to find utility "actool"`), emits a `::notice::` annotation and exits 0 on match, exits 1 otherwise. `continue-on-error` is gone, so a real view-code failure gates again. Verified live: run 29717326198 is green with the skip annotation while its underlying build failed with the environment signature.

**Related:** [Mistake: an unverified probe justified removing the safety net](#2026-07-20--an-unverified-probe-justified-removing-continue-on-error-and-turned-green-runs-red), [Gap: the Mac Catalyst CI job cannot be a required check](#2026-07-19--the-mac-catalyst-ci-job-cannot-be-a-required-check)

---

### 2026-07-20 — Adversarial multi-lens audit finds the class of defect the author is structurally blind to

**Insight:** Three independent audits of one implementation plan, each given a *different lens*, scored it 53 / 64 / 45 out of 100. The scores mattered far less than the fact that each lens found a **disjoint class** of defect, and the lowest-scoring lens found the most important one. Reviewing your own work — or asking three reviewers the same question — cannot produce this, because the blind spot is definitional: you cannot audit for a category you did not know was a category.

**Discovery:** A plan to make this prototype demo-believable was audited by (a) an investor/believability lens, (b) an F# technical-soundness lens, (c) a delivery-risk lens.

| Lens | Score | The class it owned |
|---|---|---|
| Investor | 53 | **Surface** tells — the plan had catalogued fourteen *data* tells and almost zero *product-surface* tells |
| F# technical | 64 | Factual undercounts — job state has five string dependants, not the three the plan asserted |
| Delivery risk | 45 | Process — two-thirds of tasks touch code the test suite cannot compile; six journal lessons the plan reactivated were missing from its own executor notes |

The investor lens found that **"Developer Settings" is a button on both apps' Home screens** ([Customer/Views/Home.fs:19](src/Customer.Mobile/Views/Home.fs), [Provider/Views/Home.fs:28](src/Provider.Mobile/Views/Home.fs)), that neither app has a login (a picker over five hardcoded first names), and that completing one demo loop silently mutates a provider's public star rating. None of these are subtle. All were invisible to an author who had spent the session reasoning about coordinates, timestamps and state machines — the analysis had been *pointed at the data layer*, so it enumerated data defects exhaustively and surface defects not at all.

**Design intent:** the original enumeration was not lazy. It was built by grepping and querying the running system, which is precisely why it is strong on values (`"100 Demo Street"`, `Price = 85.00m`, `ScheduledFor = "Now"`) and blind to arrangement (which buttons exist on which screen). The method chose the category.

**Impact:** meta-pattern worth hunting for — **"lens-shaped blindness"**. When commissioning review, vary the *question*, not the reviewer count. Three reviewers asked "is this plan good?" converge; three asked "would an investor spot a mock?", "will this compile here?", "can a fresh agent execute this?" do not. All three convergent findings (they agreed on exactly one item — a `SeedTests` compile contradiction) are cheap to find; the divergent findings are where the value is.

**Active mitigation:** none automated. The practical rule: before commissioning review of any artefact, name the *categories* of failure it could have, and assign one reviewer per category rather than N reviewers to the whole.

**Related:** [Gap: verified defects found by audit and not yet fixed](#2026-07-20--verified-live-defects-surfaced-by-plan-audit-none-yet-fixed)

---

### 2026-07-20 — One id collision, four separate discoveries: convention-namespaced identity does not hold

**Insight:** Customer and Provider ids are independent sequences that both start at 1. That single fact has now produced four distinct user-visible defects, each found separately, each after shipping, each fixed by threading `(id, role)` through one more place:

| # | Surface | Symptom | Fixed in |
|---|---|---|---|
| 1 | Chat messages | provider's messages rendered as the customer's own; sender name resolved Customers-first | `1f3b309` |
| 2 | Typing / seen indicators | never appeared for the documented demo pair (customer 1 + provider 1) | `1f3b309` |
| 3 | Ratings | provider rating a customer moved that *provider's* public average — measured 3.3333 → 2.7500 | `71d610e` |
| 4 | SignalR group keys | job traffic broadcast to everyone; strangers' jobs appeared in each customer's list | `637a1e3` |

**Discovery:** never by a test. #1 came from a manual API walk, #2 from an Opus review pass, #3 and #4 from an adversarial plan audit. The test suite was green throughout — see the companion lesson on fixtures that dodge the production shape.

**Design intent:** ids were modelled as plain `int` because *within either table* an id does identify its row. The assumption held one layer up and silently stopped holding wherever two id spaces met in one field. Nothing in the type system objected, because both spaces are `int`.

**Impact — the generalisable point:** the fix each time was correct but local. Four local fixes for one root cause is the signal that the abstraction is wrong, not that the callers were careless. **When the same class of defect recurs in unrelated surfaces, stop fixing call sites and change the type.** A `Actor = Customer of int | Provider of int` would have made every one of these four a compile error rather than a demo-day surprise.

**Active mitigation:** partial only. `isSelf session id role` is now the single sanctioned comparison in both apps, and hub groups are keyed `(role, id)`. But the underlying representation is still a bare `int` namespaced by convention, so a fifth surface can repeat the pattern. Raised with the user; the natural moment to close it is the plan's contracts-first task, before Phase 2's domain work spreads identity further.

**Related:** [Lesson: cross-entity id-space collision](#2026-07-19--cross-entity-id-space-collision-independent-sequences-that-both-start-at-1), [Lesson: fixtures that dodge the production shape](#2026-07-19--test-fixtures-that-dodge-the-production-shape-pass-forever)

---

### 2026-07-20 — Removing scaffolding is a code change; the comments explaining the removal are a second, unverified claim

**Insight:** When a capability is withdrawn from the UI but its code retained, the comment left behind asserts something about the *world* — "this is now driven from X" — and that claim can be false the moment it is written. It reads as authoritative and no compiler checks it.

**Discovery:** A subagent removed the DevSettings screen from both apps and annotated the retained `Msg` cases "Operator-driven from /dev". Grepping the console for the controls those comments named returned **zero** matches for auto-reply, Real GPS and Teleport. Only the route walk existed, and even that bypasses the app's `Msg` entirely by calling `PUT /location` directly. A reader would have gone hunting for wiring that does not exist.

**Impact:** rewritten to state plainly that the cases are unreachable, *why* they are retained (the auto-reply handlers carry the id-collision regression tests), and that no console control replaces them. The general rule: **a comment that describes behaviour elsewhere in the system is a claim to verify, not prose to write.** Grep the thing you are about to name.

**Related:** [Mistake: a subagent's self-report is not verification](#2026-07-20--a-subagents-self-report-is-not-verification-review-found-a-real-bug-under-a-clean-summary)

---

## Mistakes & Fixes

### 2026-07-23 — The two demo logins shared no live job

**Symptom:** Reported directly: "Customer John Reyes did not request anything from Mike's Plumbing. Therefore they will remain disconnected." Confirmed against the running seed — John Reyes's only imminent job belonged to GearHeads Mobile; Mike's Plumbing's only upcoming job belonged to Jack O'Brien; their sole shared row was job 1, `Closed`, dated 2025-12-31.

**Attempted:** No diagnosis needed once stated, but it explains a whole session of confusion beforehand: every attempt to drive a two-app scenario needed a *different* job on each phone, and that friction was repeatedly read as a test-setup annoyance rather than as the defect it was.

**Root Cause:** `mkJob` pinned the customer by explicit index (deliberate, commented) and derived the provider from `(i * 3) % provs.Length` (a distribution rule, not an identity one). Half the pairing was specified; the other half was arithmetic — see the lesson below.

**Fix:** `mkJob` takes the provider index explicitly; the soonest pending job pins it to provider 0. [Seed.fs](src/Backend.Api/Seed.fs). `cd1e4e5`. Verified live: both phones now open on the same job — customer *"Plumbing — Mike's Plumbing · Arriving in 7:39"*, provider *"Plumbing — John Reyes · Leave now — due in 7:31"*.

**Prevention:** Assert the **join**, not each end. The new test reads the soonest scheduled job and checks both its customer and its provider against the two named demo logins.

**Active mitigation:** `the two prefilled logins share the soonest job` in [SeedTests.fs](tests/Backend.Api.Tests/SeedTests.fs), mutation-checked. It also uses the colliding id shape the fixtures lesson asks for (customer 1 + provider 1).

**Time Lost:** ~20 min to fix; considerably more spent earlier working around the symptom without recognising it.

**Severity:** **High** — the two-sided moment is the thing the build exists to demonstrate, and as shipped it could not be reached without switching an account first. Invisible to every test and to a walkthrough of either app alone.

**Related:** [Lesson: half-specified invariant](#2026-07-23--half-of-an-invariant-was-designed-and-commented-the-other-half-was-arithmetic-half-specified-invariant)

---

### 2026-07-23 — A reseed left every open app holding jobs that no longer existed

**Symptom:** "Job 81 not found" on the customer's screen, after the two phones "began to diverge" — each describing a different world.

**Attempted:** Checked the id against the running database first (`GET /jobs/81` → 404, and customer 1's list held ids 1, 40, 51, 71). That confirmed the client, not the server, was wrong.

**Root Cause:** `/dev/reset` calls `EnsureDeleted()` + `EnsureCreated()` + reseed, which restarts the id sequences. Job 81 was a *booked* job; the fresh seed only creates 1–80. Every connected app kept its pre-reset job list, so acting on one 404s. The reset already broadcast `ClockUpdated` — the clock was resynced and the data was not, which is the whole bug in one line.

**Fix:** New `IBroadcaster.DataReset`, broadcast from `/dev/reset` beside the clock signal; both apps drop Jobs/Messages/PaymentResult/Notices, land on Home via `Nav.resetTo`, and refetch. `25e3ddb`. Verified live: an app parked on a job's tracking screen returned to a freshly loaded Home on reset.

**Prevention:** When an operator action changes what the server *is*, ask what every connected client now believes. The clock had that reasoning applied and the data did not, in the same function.

**Time Lost:** ~40 min including the protocol change across nine files.

**Severity:** Medium — operator-only trigger, but it produces two phones telling different stories mid-demo, which is the one thing this build cannot afford.

**Related:** [Mistake: the error bar outlived the screen that produced it](#2026-07-23--the-error-bar-outlived-the-screen-that-produced-it) (the same report — this was the cause, that was the symptom that would not go away)

---

### 2026-07-23 — The error bar outlived the screen that produced it

**Symptom:** "⚠ Job 81 not found" pinned across the app — "it's persistent across other screens".

**Attempted:** Read the view first and found the bar *was* already tappable (`TapGestureRecognizer(DismissError)`), which is why it had never been reported as un-dismissable — only as inescapable.

**Root Cause:** `Error: string option` was set by `ApiError` and cleared by exactly one thing: a tap on the bar itself. Nothing cleared it on navigation and nothing expired it. The notice queue built later in the project got auto-expiry and bottom placement; the older error bar never had that treatment applied to it.

**Fix:** Clearing moved *into* `Nav.push/back/resetTo` so no call site can forget it, plus a ~7s real-time self-dismissal carrying an `ErrorToken` generation counter so an older error's timer cannot wipe the error that replaced it. Both apps. `25e3ddb`.

**Prevention:** When a second, better mechanism for the same job is introduced (the notice queue), audit the first one rather than leaving two conventions in the codebase — the older one will keep behaving the way it always did, and its behaviour is now the odd one out.

**Verification, stated honestly:** by test only — three tests, one mutation-checked (removing `Error = None` from `Nav.push` turns it red). Three attempts to photograph the bar failed because its new lifetime is shorter than a simulator round-trip; see the lesson on transient fixes outrunning screenshot verification.

**Time Lost:** ~30 min, most of it on the failed attempts to capture it visually.

**Severity:** Medium — cosmetic in isolation, but it plants a red error banner across an unrelated screen during a demo and nothing removes it.

**Related:** [Lesson: a fix that makes something transient can outrun the tool that verifies it](#2026-07-23--a-fix-that-makes-something-transient-can-outrun-the-tool-that-verifies-it); [Mistake: a reseed left every open app holding jobs that no longer existed](#2026-07-23--a-reseed-left-every-open-app-holding-jobs-that-no-longer-existed)

---

### 2026-07-23 — A restored provider session was shown Offline with no jobs

**Symptom:** Relaunching the provider app produced Home with the shift toggle off, "You're offline", and no available jobs — while the server had that provider online and the customer side could still see them.

**Attempted:** Nothing, in the sense that matters: it was seen immediately on the first relaunch during verification of an unrelated feature. Caught by *running* it, the same technique as the [hub omission](#2026-07-22--a-restored-session-connected-to-no-live-hub) and the [rendering-defects lesson](#2026-07-22--rendering-defects-are-structurally-invisible-to-a-green-test-suite).

**Root Cause:** `Online` is server-owned (the seed marks providers online). The `LoggedIn` arm hydrates it via `GetProvider` → `ProviderHydrated`, with a comment recording that assuming the local default of `false` "otherwise hid the available-jobs list until 'Go Online' was pressed". The `SplashDone` → `RestoreSession` arm fetched jobs and the clock but not the provider, so it reintroduced exactly the condition that comment describes — for every returning user, which is the normal case once stay-signed-in ships.

**Fix:** Add the same `apiCmd (fun () -> deps.GetProvider s.UserId) ProviderHydrated` to the restore batch. `e4d6e9c`, [Update.fs](src/Provider.Mobile/Update.fs). Covered by a Cmd-draining test that asserts `GetProvider` is called for the restored user id.

**Prevention:** Treat startup effects as belonging to the authenticated *state*, not to the message that produced it — see the named class below. This would also have shipped undetected without the availability work that made it visible: a provider stuck Offline looks like a toggle they forgot, not a bug.

**Time Lost:** ~10 min (obvious once seen; the cost was that it had been latent since task 0b).

**Severity:** Medium — cosmetic-looking but it disables the provider's entire job list on every relaunch, and it silently undermined the availability feature shipped in the same session.

**Related:** [Lesson: restore-path divergence](#2026-07-23--the-restore-path-keeps-forgetting-what-the-login-path-does-restore-path-divergence)

---

### 2026-07-23 — "Arriving in 1:00" never reached zero

**Symptom:** Reported from a screenshot: the customer's tracking screen read "Arriving in 1:00" beside "0.0 km away · ETA 1m", and stayed there. The provider had arrived; the countdown had not.

**Attempted:** No dead ends — the contradiction between "0.0 km away" and "ETA 1m" on the same card pointed straight at the ETA formula.

**Root Cause:** `Travel.minutesFor` floors at `minMinutes = 1.0`, which is correct while someone is driving and wrong once they have stopped: the estimate cannot fall further, so the countdown parks. The state is legitimate and can last indefinitely — a job stays `EnRoute` until the provider taps **Arrived**.

**Fix:** `Travel.isImminent` (the estimate has bottomed out) drives both readouts: `Countdown.forCustomer` returns `Label = "Arriving"; Value = "now"` instead of counting, and `Travel.describe` drops the contradictory "ETA 1m". `oneLine` renders "Arriving now", so Home agrees with Tracking. `e2dede5`. Test asserts both the imminent and still-driving cases.

**Prevention:** See the clamp lesson below — name the region a clamp's justification covers, and check what the clamped value means outside it.

**Time Lost:** ~25 min including the live drive to reproduce.

**Severity:** Medium — not wrong data, but a frozen number on the demo's centrepiece screen at the exact moment the audience is watching for arrival.

**Related:** [Lesson: a clamp that protects one half of its domain](#2026-07-23--a-clamp-that-protects-one-half-of-its-domain-breaks-the-other-guard-with-a-second-face)

---

### 2026-07-23 — The shared map told the provider their customer's doorstep was "You"

**Symptom:** Tapping the destination pin on the provider's active-job map popped up "You" — over the customer's address.

**Attempted:** None; noticed while changing the marker artwork and confirmed by tapping the pin on both apps.

**Root Cause:** `MapHtml.fs` is one page linked into both apps, and its popup text was a literal `"You"`. Correct on the customer's tracking screen, false on the provider's. A component shared by two roles carried one role's viewpoint baked in.

**Fix:** `destLabel` is now a parameter — `"You"` from Tracking, `job.CustomerName` from ActiveJob. `8c1e2be`. The `MapCache` key gained the label so a changed name cannot serve a stale page.

**The second half, which was the actual hazard:** the label is baked into the page as a **JS string literal that Leaflet then renders as HTML** — two escaping boundaries in one value. The seed's own "Jack O'Brien" carries an apostrophe that would have terminated the literal and left a page that never draws, i.e. a blank map with no error anywhere. `popupText` escapes to HTML entities, which settles both boundaries at once (the result contains no quote, backslash or angle bracket for either parser). Verified by rendering that exact name.

**Prevention:** When a value crosses two parsers, escape for the *inner-most* one in a way the outer one cannot see — HTML entities inside a JS literal, here. And when a shared component contains user-facing copy, check every role that mounts it; the copy is a parameter even when it looks like a constant.

**Time Lost:** ~20 min.

**Severity:** Medium — the popup is one tap away on the provider's centrepiece screen, and the un-escaped-apostrophe variant would have been a silent blank map.

**Related:** [MapHtml is a format string / accidental mechanism](#2026-07-23--a-defect-can-be-the-only-thing-making-a-feature-work-accidental-mechanism)

---

### 2026-07-23 — The provider's countdown row rendered as one clipped line of red

**Symptom:** Reported from a screenshot: under "Available jobs", the row showed `ate — reportable as a no-show in 43:46` — cut off at both ends, with the trade, the customer and the payout pushed off-screen entirely.

**Attempted:** None needed; the grid definition explains it once the string is known.

**Root Cause:** The countdown was rendered as one interpolated string, `sprintf "%s %s" c.Label c.Value`, in the grid's `Auto` column. Countdown labels range from "Leave in" (8 chars) to "Late — reportable as a no-show in" (33); the long one made the `Auto` column claim the full width and starved the `Star` column holding everything else. The same string is set at title and large-title scale on both status cards, where it is simply wider than a phone. The labels grew when the no-show flow landed (task 11); the layouts that render them were never re-checked against the longest one.

**Fix:** Split the caption from the clock — prose wraps in the column that has room, the clock keeps the narrow right-hand slot it always fits, and the scale contrast lands where it belongs (small words, large number). `875d307`, five views across both apps, plus `WordWrap` on the remaining single-line sites.

**Prevention:** A shared copy type with a variable-length field needs its **longest** member tried against every layout that renders it — the copy and the layout live in different files and no test connects them. `Countdown.oneLine` exists for one-line contexts and is fine there; the defect was using the same shape in a constrained column.

**Time Lost:** ~30 min including the live reproduction at the exact clock position that produces the long label.

**Severity:** High — the provider's primary screen, unreadable, in the state the demo's headline beat ("running late") is designed to reach.

**Related:** [Rendering defects are structurally invisible to a green test suite](#2026-07-22--rendering-defects-are-structurally-invisible-to-a-green-test-suite)

---

### 2026-07-23 — Departing flipped the status but never drove the provider

**Symptom:** In the two-app walkthrough, tapping **Depart** moved the job to `EnRoute` but the provider's dot sat at its origin indefinitely. The markers never met, the map never closed in, and the customer's ETA never counted down — a normally-progressing job eventually read "Late by …", because the promised time passed while the ETA stood still.

**Attempted:** First suspected the map. Traced the position source instead: `hub.LocationUpdated` is the only thing that moves a provider, and its only caller was the `/dev` scripted timeline's own `moveTo` loop. The provider app's GPS loop is gated on `UseRealGps`, which task 0a removed from the UI and which defaults false — so nothing in the real flow moved anyone. Caught by running the *real* path rather than the canned demo — see [Lesson: demo-path divergence](#2026-07-23--the-scripted-demo-did-work-the-product-itself-did-not-demo-path-divergence).

**Root Cause:** Travel was implemented as a step of the *script*, not as a consequence of *departing*.

**Fix:** Extract one shared interpolator — [src/Backend.Api/Movement.fs](src/Backend.Api/Movement.fs) `driveEnRoute` — and fire it when `DepartEnRoute` is applied: from the real `PUT /jobs/{id}/enroute` (fire-and-forget) and from `runTimeline` (awaited). It walks origin → customer over the ETA's worth of *demo* time, pushes `LocationUpdated` each step, and bails the moment the job leaves `EnRoute`. `runTimeline` now calls it instead of its own loop, so there is one interpolator rather than a fourth (executor note 10). Commit `6c76adb`.

**Prevention:** State transitions that have a physical consequence should trigger that consequence in the domain, not rely on a caller to also simulate it. If a demo script is the only producer of some effect, the product does not have that effect.

**Time Lost:** ~50 min including the shared-interpolator refactor.

**Severity:** High — the tracking screen is the demo's centrepiece and its core motion existed only inside the script.

**Related:** [Lesson: demo-path divergence](#2026-07-23--the-scripted-demo-did-work-the-product-itself-did-not-demo-path-divergence) · [Mistake: the map never tracked live](#2026-07-23--the-map-never-tracked-live-signalr-credentials-and-a-casing-mismatch)

---

### 2026-07-23 — The map never tracked live: SignalR credentials and a casing mismatch

**Symptom:** With the provider genuinely driving, the status card's ETA counted down (22 km → 0.0 km) while the map's amber marker stayed frozen at the origin and the view never zoomed in.

**Attempted:** Assumed the earlier CORS fix had already made the page's SignalR work — it had not (see that entry's 2026-07-23 update). Briefly considered pushing positions into the WebView from F# instead. Loaded `/dev` in a browser and saw "Realtime connected", which isolated the fault to the null-origin page rather than the hub or the broadcast. A first fix for the casing alone did **not** help, which is what exposed the second cause.

**Root Cause:** Two independent blockers, either one fatal on its own:
1. The page is loaded as an HTML **string**, so its document origin is `null` and every hub request is cross-origin. SignalR's JS client defaults `withCredentials` to **true**, and the server's CORS is `AllowAnyOrigin` (no credentials) — a pairing browsers refuse. The negotiate was blocked and the socket never opened. The plain `fetch` on the same page worked because `fetch` sends no credentials cross-origin by default — which is precisely what **masked** the failure: the car was placed once and looked right.
2. The handler read `l.lat` / `l.providerId` (camelCase) while the JSON hub protocol serialises the DTO PascalCase (`Lat` / `ProviderId`, matching the F# record the typed clients decode). Every push failed the id guard and was silently dropped.

**Fix:** `withUrl(..., { withCredentials: false })` plus reading either casing, in [src/ClientShared/MapHtml.fs](src/ClientShared/MapHtml.fs). Commit `7b4ec6b`. Verified by driving a job end to end: the marker tracks the car and `fitBounds` closes the view from a regional view to street level as the dots converge.

**Prevention:** A working `fetch` is **no evidence** of a working socket to the same origin — they differ on credentials, so one can succeed while the other is blocked. Test the streaming path explicitly. And when two clients decode the same event, confirm they agree on casing: a *typed* client succeeding says nothing about a hand-written one.

**Time Lost:** ~70 min, including one dead end (casing-only fix) and one wasted stale-binary cycle.

**Severity:** High — the live tracking map is the product's signature screen and had never worked by its intended path.

**Related:** [Lesson: accidental mechanism](#2026-07-23--a-defect-can-be-the-only-thing-making-a-feature-work-accidental-mechanism) · [Mistake: no CORS policy](#2026-07-22--the-in-app-map-had-never-received-a-live-position-because-the-backend-had-no-cors-policy) · [Lesson: a screenshot verifies the installed binary](#2026-07-22--a-screenshot-verifies-the-installed-binary-not-the-source)

---

### 2026-07-23 — Notices expired on the demo clock, so at 1x they never cleared

**Symptom:** Green "Payment Complete" / "Thanks!" banners sat over each screen's **title** and stayed there — at one point three stacked on the provider's Home, covering "Mike's Plumbing".

**Attempted:** Reported by the user from a screenshot. An earlier pass had already restyled them as floating rounded cards, which made them read as notifications but did nothing about placement or lifetime.

**Root Cause:** Two things. They were pinned to the top of the page, directly over every screen's header. And their expiry rode the **demo** clock (`Notify.lifetime` = 3 demo-minutes) — a deliberate earlier choice so that pausing the clock mid-sentence also pauses dismissal. That reasoning is right for *beats*, but it means a piece of chrome sits on screen for three **real** minutes at 1x.

**Fix:** Move the stack to the bottom (`verticalOptions End` + bottom margin) and give each non-`Ask` notice a real-time `delayCmd 7000 (DismissNotice id)` at creation. `notify` now returns `Model * Cmd<Msg>` and its call sites batch the timer; `Ask` still waits for its answer. Both apps. Commit `df7f943`.

**Prevention:** Anything the **user** perceives in real seconds — toast dismissal, spinners, debounce — must be timed in real seconds. Only things that model the simulated world should ride the demo clock. A variable-rate clock is the wrong timebase for chrome.

**Time Lost:** ~35 min.

**Severity:** Medium — cosmetic, but it obscured screen titles throughout the walkthrough.

**Related:** [Lesson: rendering defects are structurally invisible to a green test suite](#2026-07-22--rendering-defects-are-structurally-invisible-to-a-green-test-suite) — found by looking at a screenshot, invisible to every test.

---

### 2026-07-22 — A restored session connected to no live hub

**Symptom:** A job driven through its states server-side was invisible to the customer app parked on its tracking screen — the status never moved, no chat, no reschedule prompt. Only the client-side countdown pump was alive.

**Attempted:** First suspected SignalR group membership, then checked whether the transitions broadcast at all. Caught by *running* it — the parked app simply not reacting — the same "seeing beats a green suite" technique as the [rendering-defects lesson](#2026-07-22--rendering-defects-are-structurally-invisible-to-a-green-test-suite). A/B proof: identical drive delivered live after a *fresh login* but not after a *restored session*.

**Root Cause:** Both apps started the hub only on the `LoggedIn` message (`MauiProgram.fs` `updateWithHub`, `hubStarted` latch). Task 0b's "stay signed in" restores a returning user straight to Home via `SplashDone` → `RestoreSession`, which never emits `LoggedIn`. So every returning user — the *normal* case once stay-signed-in shipped — held a session but never opened the realtime connection. The demo's centrepiece "the customer's phone lights up while they watch" would have silently failed for any already-signed-in device.

**Fix:** Key hub-start on the post-update **model's `Session`** (present after both login and restore), not the message, with a synchronous `hubStarting` guard so the burst of messages after a restore starts it exactly once. `ac65493`. Both apps.

**Prevention:** When a feature adds a second way to reach an authenticated state (restore alongside login), audit every side effect that was wired to the *first* way. A capability keyed on a message misses states reached by other messages; key it on the state.

**Time Lost:** ~40 min (diagnosis was fast once driven live; the value was catching it at all).

**Severity:** High — demo-critical, both apps, invisible to every test project (none compile `MauiProgram.fs`) and to a fresh-login walkthrough.

**Updated 2026-07-23:** this recurred. The restore arm was found to omit the provider's shift-flag hydration as well, so the same divergence has now produced two separate demo-affecting defects; it is recorded as a named class in [restore-path divergence](#2026-07-23--the-restore-path-keeps-forgetting-what-the-login-path-does-restore-path-divergence), which also carries the mitigation neither fix has yet applied.

**Related:** [Rendering defects...](#2026-07-22--rendering-defects-are-structurally-invisible-to-a-green-test-suite); Compromise: [Sessions in plain `Preferences`](#2026-07-22--sessions-are-stored-in-plain-preferences-not-securestorage); [Mistake: a restored provider session was shown Offline](#2026-07-23--a-restored-provider-session-was-shown-offline-with-no-jobs)

---

### 2026-07-22 — Reviewed a stale `.app`: screenshots showed the pre-redesign screen

**Symptom:** After redesigning the customer (then provider) Payment screen, driving to it showed the *old* flat layout — no bordered receipt card, plain-blue button.

**Attempted:** Briefly suspected the Border/background not rendering. Then checked the installed `.app` binary mtime: it predated the redesign edits.

**Root Cause:** Between the review's `-t:Compile` gate and the screenshot I never ran a full `-c Debug` build + reinstall. `-t:Compile` produces no `.app`, so the sim kept running the previous build. Recurred once (task 15, then task 17) despite knowing it.

**Fix:** Rebuild `-c Debug` + `simctl install` before screenshotting; verify by mtime when in doubt.

**Prevention:** Promoted to the Lesson [A screenshot verifies the installed binary, not the source](#2026-07-22--a-screenshot-verifies-the-installed-binary-not-the-source).

**Time Lost:** ~20 min across both occurrences.

**Severity:** Medium — wasted a review cycle each time; no shipped defect.

---

### 2026-07-22 — Every provider's reviews were authored by one customer ("John R.")

**Symptom:** On every provider profile, all reviews showed the same reviewer — Mike's Plumbing's three all read "John R.".

**Attempted:** Traced from the profile query back into `Seed.fs`.

**Root Cause:** Finished jobs assigned the provider by `(i*3) % 20` (step 3, coprime with 20 providers) and the customer by `i % 20` (step 1). A provider recurs every 20 jobs; at exactly those `i` the customer index lands on the same value every lap → one customer per provider. Reviews seed from a provider's own closed jobs, so a provider's whole review history came from one customer.

**Fix:** Add a lap offset `(i + i / provs.Length)` to the finished jobs' customer index → three consecutive, distinct customers per provider. `53a2315`. **Cascade:** the fix moved a *`Completed`* (non-terminal) seeded job onto the demo login, exposing a latent tell — half the seeded history was `Completed`, which lingers on Home as "settling up" forever. Made all historical jobs `Closed`; two endpoint tests that arbitrarily picked a `Completed` job now pick a `Closed` one. `3fda1cb`. Seed determinism (run-to-run snapshot equality) preserved; 196 tests pass.

**Prevention:** When two indices with different strides address parallel arrays, a shared period makes them alias — vary the stride or add a lap term. And: a state that is *transient in the live flow* (`Completed` → settling → `Closed`, minutes) must not appear in *seeded history*, where it becomes permanent.

**Time Lost:** ~35 min (including the cascade + test fix).

**Severity:** Medium — a data tell visible on the second profile anyone opened.

**Related:** [Rendering defects...](#2026-07-22--rendering-defects-are-structurally-invisible-to-a-green-test-suite)

---

### 2026-07-22 — A MAUI native `Switch` does not flip on a synthetic tap

**Symptom:** Tapping the provider online/offline `Switch` (a real MAUI `Switch` widget, added for HIG correctness) at its centre did nothing across repeated attempts.

**Root Cause:** The simulator's synthetic `tap` does not drive `UISwitch`'s toggle; it wants the drag gesture.

**Fix:** Drive it with a **swipe** across the track (control `swipe` x 332→368, y 158). Worked first time.

**Prevention:** For native toggle controls in the sim, reach for swipe, not tap. Noted in the walkthrough handoff so it doesn't recur.

**Time Lost:** ~5 min.

**Severity:** Low — automation quirk, not an app defect.

---

### 2026-07-22 — Memoised the map's HTML string but rebuilt its source object every render

**Symptom:** The in-app tracking map visibly flashed several times a second. Reported by the user after I had declared it stable.

**Attempted:** Caught by the user looking at the running app — see [Lesson: rendering defects are structurally invisible to a green test suite](#2026-07-22--rendering-defects-are-structurally-invisible-to-a-green-test-suite). My earlier "verification" measured backend SignalR reconnections and found none; that metric could not observe this failure — see [Lesson: a metric that cannot observe the failure it is being used to rule out](#2026-07-22--a-metric-that-cannot-observe-the-failure-it-is-being-used-to-rule-out).

**Root Cause:** Executor note 8 warned that a 250 ms countdown tick would re-run the view function and could reload the WebView. I memoised `MapHtml.render`'s output string — necessary but not sufficient, because the view then wrapped it in `HtmlWebViewSource(Html = ...)`, constructing a **new object** every render. Fabulous diffs the attribute by reference and re-set it four times a second.

**Fix:** Memoise the `HtmlWebViewSource` *instance*, keyed on `(lat, lng, providerId)` — [src/Customer.Mobile/Views/Tracking.fs](src/Customer.Mobile/Views/Tracking.fs) and [src/Provider.Mobile/Views/ActiveJob.fs](src/Provider.Mobile/Views/ActiveJob.fs).

**Prevention:** When memoising to defeat a diff, memoise the value the diff actually compares — not an input to it.

**Updated 2026-07-23 — this correct fix removed the accident that was standing in for live tracking.** Each of those four-a-second reloads re-ran the page, which fetches the provider's position on load, so the marker had *appeared* to follow the car. Stopping the reloads revealed that the page's own SignalR subscription had never worked. Nothing here was wrong — but it is the canonical example of [accidental mechanism](#2026-07-23--a-defect-can-be-the-only-thing-making-a-feature-work-accidental-mechanism): after killing repeated work, verify what that repetition was *also* doing.

**Time Lost:** ~40 min, plus hours shipped in a "fixed" state.

**Severity:** High — the tracking map is the demo's centrepiece.

**Related:** [Mistake: the in-app map had never received a live position because the backend had no CORS policy](#2026-07-22--the-in-app-map-had-never-received-a-live-position-because-the-backend-had-no-cors-policy)

---

### 2026-07-22 — The in-app map had never received a live position because the backend had no CORS policy

**Symptom:** The customer's tracking map showed one dot, not two. Adding an initial-position `fetch` did not help.

**Attempted:** First assumed the provider marker was mis-styled; then found it was *initialised at the job's coordinate*, so both markers occupied the same point and amber painted over blue. Fixing that exposed the deeper problem: the fetch returned nothing.

**Root Cause:** `MapHtml` is an HTML **string** loaded into a WebView, so its document origin is `null` and every request it makes is cross-origin. The backend had no CORS policy at all. The initial fetch was blocked — and so was SignalR's `negotiate`, meaning the in-app map had most likely never received a live position in the project's history. The `/dev` console works because it is served same-origin from `wwwroot`, which is exactly why nobody noticed.

**Fix:** `AddCors`/`UseCors` with `AllowAnyOrigin` (no `AllowCredentials`) in [src/Backend.Api/Program.fs](src/Backend.Api/Program.fs), scoped and justified in a comment as correct for a local demo backend holding no credentials.

**Prevention:** A WebView fed an HTML string is a null-origin client of your own API. Treat it as a third party.

**Updated 2026-07-23 — this fix was necessary but *not sufficient*, and I recorded it as though it were complete.** Adding CORS unblocked the initial `fetch`, so the marker appeared and the entry was written as if live positions now flowed. They did not: SignalR's negotiate was still blocked, because its JS client sends credentials by default and `AllowAnyOrigin` cannot be paired with credentials — and even once connected, the handler read the wrong casing. The live path stayed dead for another day. The `fetch` succeeding is what made the incomplete fix look complete. See [Mistake: the map never tracked live](#2026-07-23--the-map-never-tracked-live-signalr-credentials-and-a-casing-mismatch). **The general error: verifying a two-path feature (fetch + socket) by exercising only the easier path.**

**Time Lost:** ~30 min then; ~70 min more on 2026-07-23 to finish it.

**Severity:** High — a core feature that had never worked, presented as working.

---

### 2026-07-22 — `Info.plist` edits silently did not reach the built app

**Symptom:** Added `UIUserInterfaceStyle = Light` to both apps' `Info.plist`, rebuilt, relaunched against a dark simulator — the app still rendered dark. `plutil -lint` said the source file was valid and `plutil -extract` read the key back from it.

**Attempted:** Re-read the source plist (correct), checked XML comment placement (valid), suspected MAUI stripped comments.

**Root Cause:** MAUI caches the plist-processing step. An incremental build reused the previously-packaged bundle, so the key was in the source and absent from `Customer.Mobile.app/Info.plist`. The difference is only visible by extracting from the **built** bundle.

**Fix:** `rm -rf obj/Debug/net10.0-ios bin/Debug/net10.0-ios` and rebuild. Provider's intermediates cleared preemptively.

**Prevention:** After changing any file consumed by the Apple asset/plist pipeline, verify against the built `.app`, never the source. Same pipeline (`actool`) that caused the earlier CI failures.

**Time Lost:** ~25 min.

**Severity:** Medium — and it invalidated a "one line of cheap insurance" claim made to the user.

---

### 2026-07-22 — A correct, mutation-tested fix caused a regression in what it stopped doing

**Symptom:** During the two-app walkthrough, the customer's tracking screen sat on "Locating provider…" for the entire wait, even though the map itself found the provider.

**Attempted:** Caught by the walkthrough — see [Lesson: rendering defects are structurally invisible to a green test suite](#2026-07-22--rendering-defects-are-structurally-invisible-to-a-green-test-suite).

**Root Cause:** Task 12 fixed back-re-books by changing `JobCreated` from `Nav.push` to `Nav.resetTo`. That fix was correct and mutation-tested. But `Nav.resetTo` does not pass through the `Navigate` handler, and the `Navigate (Tracking _)` branch was where `GetLocation` was dispatched — so arriving at Tracking *by booking* silently stopped seeding the provider's position.

**Fix:** Dispatch `GetLocation` from the `JobCreated` handler too — [src/Customer.Mobile/Update.fs](src/Customer.Mobile/Update.fs).

**Prevention:** When changing *how* a screen is reached, enumerate what the old route did on the way. The defect lives in the omission, not the change, so neither a test of the old behaviour nor of the new one sees it.

**Time Lost:** ~15 min.

**Severity:** Medium.

---

### 2026-07-22 — Scripted regex edits corrupted layout-sensitive F#, twice

**Symptom:** A Python script wrapping ten view files in `ScrollView` broke four of them with `FS0010`/`FS0193`. Earlier the same day, a scripted record-field insertion mis-aligned continuation lines and broke two more.

**Attempted:** First occurrence fixed by hand; second occurrence repeated the same approach at larger scale before reverting with `git checkout`.

**Root Cause:** F# is indentation-significant and its layout rules interact with record-field alignment and CE nesting. Regex patterns that match structure ("the outer `(VStack ... ).padding(N)`") do not survive the per-file variation (`.centerVertical().padding()`, a `match` arm wrapper, differing indentation).

**Fix:** Reverted, then used exact-literal replacements with `assert` on uniqueness, one file at a time, compiling after each batch.

**Prevention:** For F#, script only exact-literal substitutions with a uniqueness assertion. Never pattern-match layout.

**Time Lost:** ~35 min across both occurrences.

**Severity:** Medium — no shipped defect, pure rework.

---

### 2026-07-22 — Fabulous CE rejects nested layout containers (FS0792), hit three times

**Symptom:** `error FS0792: This construct is ambiguous as part of a computation expression. Nested expression...` when adding a `VStack` inside a `for` in a CE, an `HStack` inside a `VStack`, and a per-row `VStack` in a list.

**Attempted:** Each time, restructured to a flat sequence of siblings.

**Root Cause:** Fabulous 2.4's CE builder does not accept a nested container in these positions. Already recorded once (the login form), and walked into three more times because the constraint was in the journal rather than in the code.

**Fix:** Flat siblings, with the reason written as a comment at each site rather than only in this file.

**Prevention:** The comment at the call site is the mitigation; the journal entry alone demonstrably did not prevent recurrence.

**Time Lost:** ~20 min total.

**Severity:** Low.

---

### 2026-07-22 — 686 NuGet packages missing `.nuspec` blocked any clean clone

**Symptom:** A fresh `git clone` of the repo failed to restore with `NU5037: The package is missing the required nuspec file`. The working copy built fine.

**Attempted:** Repaired the first blocking package, hit the next, and looped — 55 packages for this solution alone.

**Root Cause:** Residue of the earlier disk-full event. Extracted package directories lost their `.nuspec` while retaining an intact `.nupkg`. The working copy never noticed because `obj/` was already populated and restore did not re-resolve.

**Fix:** Re-extract each `.nuspec` from the package's own `.nupkg` — non-destructive, restores a file that belongs there. Swept all 510 recoverable packages; 91 have no `.nupkg` and need re-download (all belong to other projects).

**Prevention:** After a disk-full event, verify with a clean clone, not the working copy. `git clone` + `dotnet test` is a two-minute check that a green working tree cannot substitute for.

**Time Lost:** ~25 min.

**Severity:** Medium — invisible locally, total for anyone else.

---

### 2026-07-20 — A subagent's self-report is not verification: review found a real bug under a clean summary

**Symptom:** A subagent completed plan task 0a and reported success with verbatim-looking evidence — both apps compiling at 0/0, 97 tests passing, and a clean grep. All of that was true.

**Attempted:** Nothing was wrong with the report. The defects were found by reviewing the diff anyway, on the standing convention that implementation is reviewed before commit.

**Root Cause:** two independent problems the subagent had no way to see:
1. Its new `/dev` route control was commented "Mirrors `Slider.position`" but interpolated from the provider's **current** position rather than latching an origin the way `Provider.Update` latches `SliderStart`. That makes the percentages *relative*: pressing 50% then 25% moves the provider further along instead of back, and repeated presses converge on the target without arriving. Verified numerically after fixing — 25% following 100% now returns to the 25% mark.
2. The comments on the retained `Msg` cases claimed `/dev` wiring that does not exist (see the companion lesson).

**Fix:** origin latched per provider+job; comments rewritten to match reality. Both in `8bdf06d`.

**Prevention:** the value of the review is highest exactly where the subagent's report is most confident — a passing build and a green suite say nothing about semantics a test does not cover. Read the diff for *claims*, not just for compile errors: "mirrors X" and "driven from Y" are both assertions that can be checked in one grep each.

**Time Lost:** ~15 minutes, all of it review rather than debugging.

**Severity:** Medium — neither defect broke the build, and both would have surfaced first during a live demo, which is the worst possible place.

**Related:** [Lesson: removing scaffolding leaves unverified claims](#2026-07-20--removing-scaffolding-is-a-code-change-the-comments-explaining-the-removal-are-a-second-unverified-claim), [Lesson: mutation is the only honest proof](#2026-07-19--mutation-is-the-only-honest-proof-that-a-test-can-fail)

---

### 2026-07-20 — The backend's root URL served a zero-byte 404, which renders as a solid black page

**Symptom:** Reported as "Blank screen — Backend was a black screen." Reproduced exactly: a screenshot of `http://localhost:5162/` in the preview pane was a literally solid black image.

**Attempted:** No dead ends. `curl -i http://localhost:5162/` returned `HTTP/1.1 404`, `Content-Length: 0` in one call, and `curl .../dev/index.html` returned the console markup — which located the fault immediately.

**Root Cause:** `GET /` was never mapped. `Program.fs` mapped `/dev` → redirect and `UseStaticFiles`, but nothing at the root. An empty 404 body renders as an empty page, and an empty page in a dark-mode browser is black. Nothing was broken; the console had been serving correctly the whole time one path over.

**Fix:** In Development, `/` now 302s to `/dev/index.html` exactly as `/dev` already did ([Program.fs](src/Backend.Api/Program.fs), commit `611d203`). Test-first: the new `DevConsoleTests` case failed against the old behaviour (404 vs 200-after-redirect) and passed after; all 18 backend tests green; verified in-browser that the previously-black tab renders the console.

**Prevention:** the root URL is every natural entry point — typing `host:port`, a preview tool's default tab, a shared link. If it is not mapped, it is a black screen for whoever arrives that way, regardless of how healthy the app is. Map `/` to *something* in any app with a browsable surface.

**Time Lost:** ~10 minutes, almost all of it reproduction rather than diagnosis.

**Severity:** Medium — cosmetic in mechanism, but it presents as "the backend is dead" and cost a walkthrough attempt.

**Related:** [Lesson: verify an error's own premises before acting on it](#2026-07-19--verify-an-errors-own-premises-before-acting-on-it)

---

### 2026-07-20 — An unverified probe justified removing continue-on-error, and turned green runs red

**Symptom:** After shipping the probe-gated design (`acadb57`), run 29716960025 went **red at the run level** — worse than the status quo, where the advisory job failed inside a green run.

**Attempted:** The probe (`xcrun --sdk macosx --find actool`) had been validated only on a machine where builds already worked. On the runner it false-passed a half-registered Xcode, so the build ran, failed as always — and with `continue-on-error` removed on the strength of that probe, the failure now failed the whole run. The false pass also meant the `runFirstLaunch` repair path never executed at all.

**Root Cause:** two layers. Immediate: the probe resolved the SDK by *name* while the build resolves it by *explicit path* — a fidelity gap. Structural: `continue-on-error` was removed based on a prediction that had never been observed correct in the failing environment. The safety net came off before the new mechanism had ever caught anything.

**Fix:** the exact-call probe (`ab32dee`) closed the fidelity gap — and was then itself refuted (every install passes standalone, the same call fails under MSBuild), which forced the real fix: build-then-classify (`e328b88`). See the companion lesson.

**Prevention:** never remove a safety mechanism in the same change that introduces its replacement, when the replacement has only been validated where the failure doesn't occur. Land the new mechanism, observe it working in the hostile environment once, then remove the net.

**Time Lost:** three CI round-trips (~40 minutes wall clock).

**Severity:** Medium — no code was wrong, but the branch's CI signal regressed from "green with a confusing red job" to "red", which is precisely the misleading state the work set out to eliminate.

**Related:** [Lesson: environment-dependent failures cannot be gated by pre-flight probes](#2026-07-20--environment-dependent-failures-cannot-be-gated-by-pre-flight-probes-build-then-classify)

---

### 2026-07-19 — The /dev console was left behind when identity was namespaced

**Symptom:** No error. The console's "as provider" button posted chat messages that came back attributed to the customer — `senderName: "John"` for a message sent as Mike's Plumbing.

**Attempted:** Found by re-reading the console as an API *client* while preparing to redesign it, not by any test or failure. The design spec calls the `/dev` console "a real integration harness, not a parallel path"; that framing is what prompted checking whether it had been updated alongside the apps.

**Root Cause:** `1f3b309` added a required `SenderRole` to `SendMessageRequest` and updated Customer.Mobile and Provider.Mobile — but not the console, which is equally a client of `POST /messages`. The backend defaults an absent role to `"Customer"` ([Endpoints.fs:150](src/Backend.Api/Endpoints.fs)), so the omission failed silently rather than erroring.

**Fix:** The console now sends `senderRole` explicitly ([index.html](src/Backend.Api/wwwroot/dev/index.html), `injectMsg`). Verified against the running API before and after: `"John"` → `"Mike's Plumbing"`, then re-confirmed through the console's own UI.

**Prevention:** When a shared DTO gains a required field, enumerate *every* client of that endpoint — here that is three, not two. A default-on-absent value makes the omission invisible, which is the trade for backwards compatibility.

**Time Lost:** ~10 minutes, all of it verification rather than diagnosis.

**Severity:** Medium — cosmetic in the console itself, but it silently misattributes messages in the surface used to demo chat.

**Related:** [Lesson: cross-entity id-space collision](#2026-07-19--cross-entity-id-space-collision-independent-sequences-that-both-start-at-1)

---

### 2026-07-19 — CI went red on its first run because the test suite was never re-run after the redesign

**Symptom:** The first real CI run failed: `Assert.Contains() Failure: Sub-string not found. Not found: "FixItHere Demo Control Panel"` in `DevConsoleTests`.

**Attempted:** Nothing — this was not diagnosed locally, because locally it was never observed. After the console redesign, both MAUI apps were rebuilt (0 warnings / 0 errors) and that was treated as sufficient verification.

**Root Cause:** Two compounding errors. The redesign changed the page `<title>` from `"FixItHere Demo Control Panel"` to `"FixItHere · Demo control"`, and [DevConsoleTests.fs:14](tests/Backend.Api.Tests/DevConsoleTests.fs) asserted that exact string. More importantly, *the wrong layer was verified*: rebuilding the apps proves nothing about a backend integration test that reads `wwwroot`.

**Fix:** The assertion now checks the structural anchors the console's own script binds to (`id="map"`, `id="jobs"`, `id="log"`) instead of heading copy, so a visual change cannot fail a test that guards environment gating (`a6f2acf`).

**Prevention:** Editing any file a test reads means re-running that test project, regardless of whether the change "looks like" code. `wwwroot` assets are test inputs.

**Active mitigation:** this is precisely what CI now exists for, and it earned its place on the first run — the one failure it caught was one a human had already talked themselves out of checking.

**Time Lost:** ~10 minutes, entirely after the fact.

**Severity:** Low as a defect, High as a process signal — the claim "verified" was made about a layer that could not have caught it.

**Related:** [Gap: Mac Catalyst CI cannot gate](#2026-07-19--the-mac-catalyst-ci-job-cannot-be-a-required-check)

---

### 2026-07-19 — The contrast-measurement script parsed `oklch()` as RGB and reported nonsense

**Symptom:** An in-browser WCAG check returned 1.13 for nearly every text pair and 1.0 for the primary button — implying the entire page was invisible, which the screenshot plainly contradicted.

**Attempted:** The first script read `getComputedStyle(el).color` and extracted the first three numbers with a regex. For `oklch(0.97 0.006 85)` that yields `[0.97, 0.006, 85]`, which was then fed to an sRGB luminance formula as if it were `rgb()`.

**Root Cause:** Modern browsers return `oklch()` verbatim from `getComputedStyle` when that is the authored value. The measurement tool, not the design, was broken.

**Fix:** Rasterise instead of parse — paint each computed colour into a 1×1 canvas and read the actual sRGB bytes back via `getImageData`. This works for any CSS colour the browser understands, including `color-mix()`.

**Prevention:** A measurement that returns implausible values is a broken measurement until proven otherwise. Here the implausibility was obvious; the danger is the inverse case, where a broken tool reports a comfortable *pass*.

**Time Lost:** ~10 minutes.

**Severity:** Medium — had the numbers landed plausibly instead of absurdly, two real AA failures (`--faint` at 4.14) would have shipped with a "verified WCAG AA" claim attached.

**Related:** [Lesson: mutation is the only honest proof](#2026-07-19--mutation-is-the-only-honest-proof-that-a-test-can-fail)

---

### 2026-07-19 — `preview_start` with a URL displaced the running dev server

**Symptom:** Mid-verification, `curl` to the API began returning nothing; the backend had vanished. The subsequent `preview_list` showed a single `Browser` entry where the `Backend.Api` server had been.

**Attempted:** Initially misread as a crash, and the log was checked for an exception. There was none.

**Root Cause:** Calling `preview_start` with `{url}` to open a browser tab replaced the session's existing `{name}`-started dev server rather than opening alongside it.

**Fix:** Restarted with `preview_start {name: "Backend.Api"}` and used `navigate` on the returned tab id for subsequent page loads.

**Prevention:** `preview_start {url}` and `preview_start {name}` occupy the same slot. Start the dev server first, then drive its tab with `navigate`.

**Time Lost:** ~5 minutes.

**Severity:** Low — self-inflicted, recovered immediately, but it silently invalidated an in-flight verification run.

**Related:** [Lesson: verify an error's own premises](#2026-07-19--verify-an-errors-own-premises-before-acting-on-it)

---

### 2026-07-18 — NullReferenceException on first DbSet access

**Symptom:** `Seed.run` threw `System.NullReferenceException: Object reference not set to an instance of an object.` at `db.Services.AddRange services`, and the same for every other DbSet.

**Attempted:** Initially assumed the seeder logic itself was wrong (e.g., calling `.AddRange` before `SaveChanges` was valid); re-read the seeder for logic bugs before checking whether `db.Services` itself was null.

**Root Cause:** `AppDb`'s DbSet properties were declared as F# `member val Services: DbSet<Service> = Unchecked.defaultof<_> with get, set` — a pattern that looks like a C# auto-property but never gets assigned by EF Core's context initialization.

**Fix:** Replaced with computed `member this.Services: DbSet<Service> = this.Set<Service>()`, plus explicit `modelBuilder.Entity<T>()` registration for all 6 entities in an overridden `OnModelCreating`. See [`src/Backend.Api/Db.fs`](src/Backend.Api/Db.fs).

**Prevention:** In any F# + EF Core `DbContext` subclass, never use `member val ... with get, set` for a `DbSet<T>` — always use a computed `this.Set<T>()` member.

**Time Lost:** ~1 test-run cycle (~10 minutes) to isolate once the NRE stack trace pointed at `Seed.run`'s first line.

**Severity:** High — blocked all 15 Backend.Api tests and the app itself from booting at all.

**Related:** [Lesson: F# record auto-properties don't populate EF Core DbSet](#2026-07-18--f-record-auto-properties-dont-populate-ef-core-dbsett-the-way-c-does)

---

### 2026-07-18 — Multi-line CLIMutable records fail to compile under F# 9

**Symptom:** `dotnet test tests/Shared.Tests` failed with a cascade of `FS0058`/`FS0547`/`FS0010` errors, all pointing at `[<CLIMutable>] type X = \n { ... }`-style declarations in `Dtos.fs`.

**Attempted:** Read the errors literally first (tried re-indenting the record body further, per the compiler's own suggested action `--strict-indentation-`); recognized that fighting indentation was the wrong axis and the attribute placement was the actual issue.

**Root Cause:** F# 9 (default on .NET 10 SDK) is stricter about the offside rule than the F# 7/8 assumptions baked into the original plan's code samples. `[<Attr>] type X =` followed by a body on subsequent lines needs the attribute on its own line.

**Fix:** Moved every multi-line record's `[<CLIMutable>]` attribute to its own line above `type X =`. Applied across `Dtos.fs` and `Db.fs`.

**Prevention:** When authoring F# record types with attributes under .NET 10 / F# 9, default to attribute-on-its-own-line for any record whose body doesn't fit on one line — don't rely on the single-line convenience form.

**Time Lost:** ~5 minutes (one clear error message set, one clean fix).

**Severity:** Medium — blocked compilation but was quick to diagnose once misread as an indentation problem rather than an attribute-placement problem.

**Related:** [Lesson: F# 9's stricter indentation breaks the one-line idiom](#2026-07-18--f-9s-stricter-indentation-breaks-the-one-line-attr-type-x-----idiom)

---

### 2026-07-18 — Dev endpoints and `/dev` console 404 under `WebApplicationFactory`

**Symptom:** `POST /dev/reset`, `POST /dev/demo/start`, and `GET /dev/index.html` all returned 404 under the xUnit `WebApplicationFactory`-based tests, despite the exact same build serving 200s under `dotnet run --project src/Backend.Api`.

**Attempted:** First fix attempt overrode `ConfigureWebHost` to call `builder.UseEnvironment("Development")` — did not work. Added a throwaway diagnostic test (see the diagnostic-technique lesson) to confirm the actual environment name at runtime, which revealed `"Production"`.

**Root Cause:** The F# `Program.fs` uses minimal-hosting module-level code (`let builder = WebApplication.CreateBuilder()` at module scope, no `[<EntryPoint>]`). The environment is captured once when `CreateBuilder()` runs, which happens the moment anything forces `Program` module's static initializer — earlier than `WebApplicationFactory.ConfigureWebHost` executes.

**Fix:** Set `ASPNETCORE_ENVIRONMENT` via `Environment.SetEnvironmentVariable` inside the `Factory` type's primary constructor (`type Factory() as this = ... do ...`), guaranteeing it runs before `CreateClient()`/`Program` module load. See [`tests/Backend.Api.Tests/AppFactory.fs`](tests/Backend.Api.Tests/AppFactory.fs).

**Prevention:** For F# minimal-hosting apps under `WebApplicationFactory`, always set environment-affecting variables in the factory constructor, never in `ConfigureWebHost`.

**Time Lost:** ~20 minutes across three attempts (ConfigureWebHost → module-level `do` → constructor).

**Severity:** High — blocked 3 of the plan's dev-endpoint tests and, transitively, verifying the entire `/dev` console tracer bullet.

**Related:** [Lesson: WebApplicationFactory boots in Production](#2026-07-18--webapplicationfactoryt-boots-in-production-and-env-vars-must-be-set-before-the-suts-module-loads)

---

### 2026-07-18 — Content-root fix attempt regressed 9 previously-passing tests

**Symptom:** After fixing the Development-environment 404s, `/dev/index.html` still 404'd (content root pointing at the wrong directory). A fix attempt using `ConfigureWebHost` + a `findRoot` helper that walked up from `AppContext.BaseDirectory` looking for `FixItHere.Demo.sln` caused **9 previously-passing tests** to fail with `Could not locate FixItHere.Demo.sln above the test output directory`.

**Attempted:** First tried `builder.UseSolutionRelativeContentRoot("src/Backend.Api")` inside `ConfigureWebHost` — compile error, that API isn't on plain `IWebHostBuilder` in this package set. Then tried walking up from `AppContext.BaseDirectory` at runtime to find the `.sln` — this ran in `ConfigureWebHost`, which (per the environment lesson) is too late, AND the base directory in the actual test-runner sandbox didn't contain the `.sln` anywhere in its ancestry, so the walk always failed.

**Root Cause:** Two compounding issues: (1) `ConfigureWebHost` timing, same root cause as the environment bug; (2) `AppContext.BaseDirectory` at test-run time is not reliably inside the repo tree in this sandboxed environment.

**Fix:** Replaced the runtime directory walk with a **compile-time** resolution using F#'s `__SOURCE_DIRECTORY__` (the `AppFactory.fs` file's own known location under `tests/Backend.Api.Tests/`), combined with `Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "src", "Backend.Api")`, and set via `ASPNETCORE_CONTENTROOT` env var in the constructor (not `ConfigureWebHost`).

**Prevention:** Never resolve a fixed, project-relative path at runtime via `AppContext.BaseDirectory` walk-up when a compile-time-known relative path (`__SOURCE_DIRECTORY__` in F#, `[CallerFilePath]` in C#) is available and the file's location relative to the target is fixed.

**Time Lost:** ~15 minutes (one regression cycle: fix attempt → 9 new failures → root-cause the base-directory assumption → switch to `__SOURCE_DIRECTORY__`).

**Severity:** Medium — self-inflicted regression, caught immediately by the full-suite test run before it could ship.

**Related:** [Lesson: WebApplicationFactory's content root doesn't default to the SUT project directory](#2026-07-18--webapplicationfactorys-content-root-doesnt-default-to-the-sut-project-directory)

---

### 2026-07-18 — Proximity-sort test failed on a correct implementation (Euclidean vs. haversine mismatch)

**Symptom:** `providers are sorted by proximity to query point` failed: the endpoint's returned order didn't match the test's expected sort order, even though eyeballing the data suggested both were "sorted by distance."

**Attempted:** Initially suspicious of the endpoint's `haversineKm` implementation — re-derived the haversine formula by hand to check for a sign/unit error before considering the test itself.

**Root Cause:** The test computed a **naive Euclidean** distance in raw lat/lng degree-space (`dLat² + dLng²`) as its comparison metric, while the endpoint correctly sorts by **haversine** (great-circle) distance. Near 43°N, one degree of longitude covers noticeably less ground distance than one degree of latitude (longitude degrees shrink by `cos(latitude)`), so the two metrics produce different orderings for points that are close together — the endpoint was correct; the test's oracle was wrong.

**Fix:** Rewrote the test's distance function to use the same haversine formula as the endpoint (duplicated inline in the test, matching `Endpoints.fs`'s `haversineKm`). See [`tests/Backend.Api.Tests/EndpointTests.fs`](tests/Backend.Api.Tests/EndpointTests.fs).

**Prevention:** When a test's "expected" value is derived from a different formula than the implementation under test, they will diverge exactly where the two formulas disagree — for geographic distance, that's anywhere longitude and latitude scales differ meaningfully (i.e., away from the equator). Test oracles for geo-distance sorting must use the same metric as the code being tested, not a simplified stand-in.

**Time Lost:** ~5 minutes.

**Severity:** Low — caught immediately by the failing assertion's printed diff, no debugging required beyond recognizing the metric mismatch.

**Related:** none

---

### 2026-07-18 — Generated SQLite file committed to git despite an earlier `.gitignore` update

**Symptom:** During the `finishing-a-development-branch` merge step, `git status`/the merge diff showed `src/Backend.Api/fixithere-demo.db` — a 40KB SQLite binary regenerated by every app startup — had been committed in the `feat/backend-and-dev-console` branch.

**Attempted:** Checked `.gitignore` expecting `*.db` to already be present (an earlier command in the session was intended to append `bin/\nobj/\n*.db\n`), but the file on disk only contained `.DS_Store`, `bin/`, and `obj/` — no `*.db` line.

**Root Cause:** Not fully diagnosed. The command run earlier in the session was `grep -q '^bin/' .gitignore || printf 'bin/\nobj/\n*.db\n' >> .gitignore`; the guard and printf are correct in isolation, but the resulting file is missing the third line. Most likely explanation: the db file was created and `git add -A`'d in a commit that landed **before** this .gitignore line was appended, so the file was already tracked and later shell/tooling state wasn't re-verified against the actual `.gitignore` contents before committing again. Root cause not conclusively isolated.

**Fix:** `git rm --cached src/Backend.Api/fixithere-demo.db`, then `printf '*.db\n' >> .gitignore` as its own explicit follow-up (verified with `cat .gitignore` before committing this time), committed separately as `chore: untrack generated SQLite db file, fix .gitignore`.

**Prevention:** After any `.gitignore` edit intended to exclude a category of generated file, run `git status --short` and `cat .gitignore` immediately afterward to positively confirm the file no longer appears as trackable — don't trust that an append command succeeded silently, especially once a matching file may already be tracked (adding a pattern to `.gitignore` does not untrack already-committed files).

**Active mitigation:** None automated. A pre-commit hook that greps `git status --short` for common generated-artifact extensions (`.db`, `.sqlite`, `.log`) not covered by `.gitignore` would catch this class of mistake going forward — not yet implemented (see Solution Gaps).

**Time Lost:** ~5 minutes (caught during the standard finishing-a-development-branch verification, not by an automated check).

**Severity:** Low — cosmetic (a regenerated file, not a secret or meaningful data), but is exactly the kind of file that silently bloats a repo if it recurs.

**Related:** [Gap: no pre-commit check for untracked generated-file patterns](#2026-07-18--no-pre-commit-check-for-generated-file-patterns)

---

### 2026-07-18 — `GpsTick` pushed a one-tick-stale location to the server

**Symptom:** No user-visible error or test failure — caught by code inspection during Provider.Mobile Task 7 (real-GPS streaming while EnRoute). The server-visible provider location always lagged the device's actual GPS reading by one 3-second tick.

**Attempted:** N/A — this wasn't chased from a failing assertion; it was found by reading `GpsTick`'s handler against the model-update lifecycle and noticing the push used `model.MyLocation` (the value already in the model *before* this tick's fetch) rather than the value the tick had just fetched.

**Root Cause:** `GpsTick`'s handler read a fresh GPS coordinate and updated `model.MyLocation` with it, but the same handler's server-push Cmd was built from `model` — the pre-update model captured in the `update` function's argument — not from the freshly-fetched value. In an MVU update function, everything derived from `model` inside one arm sees the *old* model; a fetched value that needs to both update the model AND be pushed in the same step must flow through as an explicit value, not be re-read from a `model` binding after conceptually "updating" it.

**Fix:** Split into two messages: `GpsTick` performs the fetch and dispatches a new `GpsFetched(jobId, lat, lng)` message; `GpsFetched`'s handler both sets `model.MyLocation` and builds the push-to-server Cmd from the same `lat`/`lng` arguments in one step, so there's no window where "the value in the model" and "the value that gets pushed" can diverge. See [`src/Provider.Mobile/Update.fs`](src/Provider.Mobile/Update.fs).

**Prevention:** In any MVU `update` arm that both (a) receives or fetches a new value and (b) needs to push/use that same value elsewhere in the same logical step, thread the value through function arguments/a follow-up message — never reconstruct it by reading back from `model` later in the same or a subsequent arm, since `model` there is the pre-update snapshot.

**Time Lost:** Not tracked precisely (found via inspection, not a debugging session) — low, given it was resolved in the same commit as the discovery.

**Severity:** Medium — silent, one-tick location lag wouldn't crash anything but would make the customer-side tracking car visibly lag behind the provider's real position, undermining the demo's core "live GPS tracking" feature.

**Related:** none

---

### 2026-07-18 — Auto-reply guard was never actually exercised by its own regression tests

**Symptom:** No failure — the two existing auto-reply tests passed. The gap was that they passed for reasons unrelated to what their names implied.

**Attempted:** N/A — found by tracing what the shared `up` test helper (`Update.update stubDeps msg model |> fst`) actually exercises versus what the test names claimed to cover.

**Root Cause:** `up` discards the `Cmd<Msg>` half of `update`'s return value. The `isMine`/`AutoReply`/job-ownership guard inside `HubMessageReceived` that decides whether to *schedule* an auto-reply lives entirely in the Cmd it returns — nothing about that decision touches the model directly — so no `up`-based test could ever observe whether the guard fired correctly or not.

**Fix:** Extracted the guard into a pure `shouldAutoReply` predicate in [`src/Provider.Mobile/Domain.fs`](src/Provider.Mobile/Domain.fs) that `HubMessageReceived` now calls, and added a test that exercises `shouldAutoReply` directly (no Cmd-draining needed since it's a pure function). Renamed the two pre-existing tests to describe what they actually assert (`AutoReplyDue`'s counter/cycling behavior, and message de-duplication) instead of implying Cmd-scheduling coverage they never had.

**Prevention:** See [Lesson: MVU test helpers that discard Cmd<Msg> hide untested guard logic](#2026-07-18--mvu-test-helpers-that-discard-the-returned-cmdmsg-silently-hide-untested-guard-logic) — extract Cmd-gating guards into pure predicates and test those directly, rather than relying on a model-only test helper to somehow surface them.

**Time Lost:** Bundled into the same commit as the GpsTick fix; not separately tracked.

**Severity:** Medium — not a live bug (the guard's actual logic was correct), but a false sense of test coverage that could have let a real regression in the guard ship undetected.

**Related:** [Lesson: MVU test helpers that discard the returned Cmd<Msg> silently hide untested guard logic](#2026-07-18--mvu-test-helpers-that-discard-the-returned-cmdmsg-silently-hide-untested-guard-logic)

---

## Solution Gaps

### 2026-07-23 — The iOS CI job reports success without building anything

**Current State:** CI is live and green since 2026-07-23 (`push:` + `pull_request:` restored in `ee7333e`; first run passed both jobs). But the `Build apps (iOS)` job carries its own annotation: *"xcodebuild cannot resolve the macOS SDK when invoked under MSBuild on this runner image, though the same call succeeds standalone. Known image/toolchain defect, not a code failure — this job gates again automatically once the image works."* It reports ✓ in 2m14s having proven nothing about whether either app packages.

**Limitation:** A green tick that means "skipped" is the same hazard as a red one that means "billing" — it reads as coverage that does not exist. Nothing in CI compiles view code: the `Tests` job cannot (no test project includes `Views/*.fs` or `MauiProgram.fs`), and the job that could, doesn't. **The local `-f net10.0-ios -t:Compile` gate remains the only thing checking view code anywhere**, and it depends on a human remembering to run it.

**Closing this gap requires:**
1. Add `-f net10.0-ios -t:Compile` for both apps to the **Tests** job — it needs no Xcode (the F# compiler's reference assemblies come from the workload pack, and `Compile` stops before the asset pipeline), so it should survive `ubuntu-latest` if the iOS pack restores there — ~30 min, and the first run tells you — pending
2. If the pack does not restore on ubuntu, move it to a small `macos-latest` job instead — ~30 min — pending
3. Leave `Build apps (iOS)` advisory; it self-describes and re-gates automatically — no action

**Priority:** High — this is the project's largest standing verification gap, and it predates the believability rebuild. Two-thirds of that rebuild touched code no CI job compiles.

**Related:** [Mitigating the view-code verification gap](#2026-07-22--rendering-defects-are-structurally-invisible-to-a-green-test-suite); [Archived: the Mac Catalyst CI job cannot be a required check](#2026-07-19--the-mac-catalyst-ci-job-cannot-be-a-required-check)

---

### 2026-07-23 — Two build warnings that local verification has been filtering out

**Current State:** CI's annotations surfaced a warning nobody here had seen: `FS3391` at [DevEndpoints.fs:172](src/Backend.Api/DevEndpoints.fs) (implicit `int` → `Nullable<int>` conversion). Re-running the build locally without a filter also shows `NU1903`: `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 has a **known high-severity vulnerability** ([GHSA-2m69-gcr7-jv3q](https://github.com/advisories/GHSA-2m69-gcr7-jv3q)).

**Limitation:** Both were invisible during this session because every build in it was piped through `grep -E "error|Build succeeded"` — a filter that shows failures and successes and hides everything in between. The plan's reviewer checklist asks for "0 warnings / 0 errors **shown**, not assumed"; the filter quietly turned that into "no errors shown". The `NU1903` one matters beyond tidiness: it is a transitive dependency with a published advisory, in the package that backs the demo database.

**Closing this gap requires:**
1. Stop filtering warnings out of build output — use `grep -E "error|warning|Build succeeded"` or read the tail — ~0 min, a habit — pending
2. Bump `SQLitePCLRaw` past the advisory and confirm the suite still passes — ✅ **shipped `00a8f49`**: a direct `SQLitePCLRaw.bundle_e_sqlite3` 2.1.12 pin in `Backend.Api.fsproj` overrides the 2.1.11 EF Core 10.0.10 pulls in. `dotnet nuget why` confirms the whole trio resolves to 2.1.12 across every path including the test project (the pin propagates through the `ProjectReference`, so no second pin was needed); `NU1903` is gone from both projects; 48 backend tests still pass.
3. Make the `Nullable` conversion explicit at `DevEndpoints.fs:172` rather than suppressing `FS3391` — ~10 min — pending

**Priority:** Medium — the advisory (item 2) is resolved; what remains is the `FS3391` tidy-up and the durable habit of not filtering warnings, which is the finding that actually matters here.

---

### 2026-07-23 — An accepted job is indistinguishable from an unclaimed one

**Current State:** A provider's Home lists their `Scheduled` jobs under "Available jobs". Tapping **Accept** does not change the job's state — the machine's own `Scheduled, Accepted -> Ok Scheduled` is state-preserving by design — and the DTO carries no acceptance marker, so the job reappears in the same list looking untaken. The provider can tap into it and be offered "Accept Job" a second time.

**Limitation:** It reads as the app having ignored the tap. It also leaves a hole in the availability rule shipped in `5f9295d`: a provider goes off-market at **Depart**, not at **Accept**, so between the two they can still accept a second job.

**Ideal Solution:** An acceptance fact on the job — `AcceptedAt: DateTimeOffset option` or an `Accepted` sub-status alongside the reschedule fields — surfaced in the DTO so both apps can distinguish "assigned to me" from "committed to by me". A committed job then renders as its own section, and availability keys on commitment rather than on in-flight.

**Closing this gap requires:**
1. Column + DTO field + `toJobDto` mapping, set by the `Accepted` transition — ~1 hour — pending
2. `/dev` console updated for the new DTO field (it is a first-class client — executor note 19) — ~15 min — pending
3. Provider Home: separate "Accepted — ready to depart" from "Available" — ~30 min — pending
4. Decide whether `availability` keys on accepted-or-in-flight rather than in-flight, and say so in the copy — ~15 min — pending

**Priority:** Medium — visible on the provider's first screen, but only to someone who accepts and then goes back, which the scripted demo does not do.

**Related:** [Lesson: derive state that another change can invalidate](#2026-07-23--derive-state-that-another-change-can-invalidate-never-store-it)

---

### 2026-07-23 — Seeded jobs are promised ~8 minutes out, so any accelerated run reads as late

**Current State:** The seed places the first upcoming jobs at `Epoch + 8 / 25 / 55` minutes so a countdown is already ticking when the app opens (deliberate — see the demo-clock work). The customer's en-route countdown is reconciled against the *promised* arrival, so it turns red and reads "Late by …" once demo-now passes the promise.

**Limitation:** Those two facts fight each other the moment the operator accelerates. At 30× the clock crosses an 8-minute promise in ~16 real seconds, so a job that is progressing perfectly normally — provider driving, ETA falling — shows a red, behind-promise countdown for the rest of the run. The signal is *honest* (the provider genuinely will arrive later than promised) but it misreads on stage as something being broken.

**Ideal Solution:** Either stage the demo job further out than the rate will chew through, or have the operator accept the reschedule (which retargets the promise and clears the red), or make the en-route countdown target the live ETA and show behind-promise as a separate, quieter mark rather than recolouring the headline.

**Closing this gap requires:**
1. Decide the intended reading — is red-while-driving informative or alarming? (a conversation, not code) — pending
2. If staging: give the demo-tracked job a promise proportional to the intended rate (~an hour) — half an hour
3. If UI: split "time to arrival" from "behind promise" in `Countdown`, so the headline counts down and lateness is a subordinate badge — half a day, and it touches both apps' status cards

**Active mitigation:** run the pitch at 1× through the arrival beat, or answer the running-late proposal, which retargets the promise and returns the countdown to calm.

**Priority:** Medium — nothing is wrong, but the most-watched number on the demo's hero screen can read alarming during a normal run.

**Related:** [Mistake: departing flipped the status but never drove the provider](#2026-07-23--departing-flipped-the-status-but-never-drove-the-provider) — until that was fixed the ETA never fell at all, which made this look like the same bug.

---

### 2026-07-22 — The provider reaches Payout only by tapping Complete itself

**Current State:** The customer auto-advances to Payment on the `HubJobUpdated` "Completed" event while parked on Tracking. The provider's equivalent nav to Payout lives in the `JobActioned` (own-action) branch, not `HubJobUpdated`.

**Limitation:** Driving a job to `Completed` via external `PUT /jobs/{id}/complete` updates the provider's *status* ("Work complete — awaiting payment") but does **not** navigate it to Payout — only the provider tapping **Complete in-app** does. This is arguably correct (the provider settles up through its own action), but it means the two provider screens Payout and Rate-customer cannot be screenshotted by server-side driving; they need the in-app tap, i.e. the real two-app walkthrough.

**Closing this gap requires:** nothing to *fix* — it is a reachability property, documented so the walkthrough drives the final Complete through the UI. If a future automated check wants Payout, it must tap, not curl.

**Priority:** Low — behavioural note, captured in the walkthrough handoff.

**Related:** Compromise: [Both apps pinned to Light](#2026-07-22--both-apps-are-pinned-to-light-appearance) (also verified via the two-app flow)

---

### 2026-07-22 — CC0 image sourcing is noisy and needs per-image curation

**Current State:** Sample trade photos were pulled from Openverse filtered to `license=cc0`, downloaded via `curl`, and loaded into the sim library with `simctl addmedia`; the on-target set plus attribution lives under `demo/sample-photos/`.

**Limitation:** A CC0 keyword search returns tangential results — "car engine repair" surfaced a dealership building and a locomotive; "electrical panel" surfaced an analog computer; "cleaning supplies" surfaced a geology image. Roughly a third had to be viewed and rejected by hand. There is no way to get a clean per-trade set without looking at each image.

**Current workaround:** Curated by eye down to ten genuinely on-target photos; off-target ones dropped from both the committed set and the About-page credits. One file arrived as WebP mislabelled `.jpg` (broke `sips -Z`); converted with `sips -s format jpeg`.

**Closing this gap requires:** a hand-picked set if higher quality is wanted (a couple of hours), or accepting that the operator simply won't attach the weaker ones.

**Priority:** Low — the demo only needs one good photo per trade to attach in chat.

---

### 2026-07-22 — Two simulators are at the edge of this machine's headroom

**Current State:** Running the customer and provider apps on two booted simulators worked, but the customer simulator shut itself down once mid-walkthrough and needed rebooting, and two `tap` calls failed with "the simulator likely rebooted" before succeeding on retry.

**Limitation:** The two-app walkthrough — the plan's acceptance mechanism — is the most resource-intensive thing this project does. Disk fell from 43 GB to 24 GB across the session (iOS 26.5 runtime ~8 GB, plus two debug app builds).

**Ideal Solution:** Check `df -h /` before starting, `dotnet clean` at phase boundaries, and expect to retry gestures rather than treating a timeout as a failure.

**Priority:** Medium — it does not block, but it makes the acceptance step flaky.

**Related:** [Gap: no disk-headroom check before/during long MAUI build-heavy agentic sessions](#2026-07-18--no-disk-headroom-check-beforeduring-long-maui-build-heavy-agentic-sessions)

---

### 2026-07-18 — No disk-headroom check before/during long MAUI build-heavy agentic sessions

**Current State:** Nothing in this session's tooling checked `df` before or during a long run of `dotnet build`/`dotnet test` cycles against two MAUI apps. Debug+Release Mac Catalyst builds for both apps together reached ≈3.1GB of `bin`/`obj`, and combined with a pre-existing 5.2GB `~/.nuget/packages` cache, this ran the host's free disk to zero mid-session, which in turn broke the Bash tool's own output-capture mechanism (see [Lesson: rebuilding Debug+Release for maccatalyst exhausts disk](#2026-07-18--rebuilding-both-debug-and-release-for-net100-maccatalyst-across-many-verification-cycles-exhausts-local-disk-without-warning-and-disk-full-breaks-the-agents-own-tool-execution-before-it-breaks-the-build)).

**Limitation:** There's no early warning between "build succeeded" and "the agent's tools stop working entirely" — disk exhaustion isn't surfaced as a build error or test failure, it silently degrades free space until the whole session's tool execution breaks with error text that doesn't mention disk at all.

**Recommended Improvement:** A lightweight check at natural checkpoints (e.g., after every few tasks in a long implementation plan, or via a Stop/PostToolUse hook) that runs `df -h /` and warns/fails loudly below some threshold (e.g., 2GB free), before the condition becomes unrecoverable from inside the session.

**Closing this gap requires:**
1. A small shell one-liner (`df` parse + threshold check) wired as a PostToolUse hook after `Bash` calls matching `dotnet build`/`dotnet test`, or as a periodic Stop-hook check — a couple of hours — pending
2. A documented cleanup step (`dotnet clean` for the mobile apps, or scoping builds to `-c Debug` only during iterative task work) added to this project's MAUI-specific executor notes so future implementation plans budget for it explicitly — pending
3. Optionally, prune `~/.nuget/packages` on a schedule independent of this repo, since it's a machine-wide cache that isn't specific to this project — pending, outside this repo's control

**Active mitigation:** None yet — the fastest interim step is simply running `df -h /` manually before kicking off a build-heavy task batch in this project.

**Priority:** Medium — didn't block correctness of any shipped code, but did block the agent's own ability to finish verifying Task 12 within this session (see the related Compromise/Gap on Task 12's incomplete final verification).

**Related:** [Lesson: rebuilding Debug+Release for maccatalyst exhausts disk](#2026-07-18--rebuilding-both-debug-and-release-for-net100-maccatalyst-across-many-verification-cycles-exhausts-local-disk-without-warning-and-disk-full-breaks-the-agents-own-tool-execution-before-it-breaks-the-build), [Gap: Task 12's manual acceptance walk and final review were not completed before disk exhaustion](#2026-07-18--task-12s-two-app-manual-acceptance-walk-and-final-per-task-review-were-not-completed-before-disk-exhaustion-interrupted-the-session)

---

### 2026-07-18 — Typing/Seen hub relay has no automated test; verified manually only

**Current State:** The SignalR `Typing`/`Seen` hub relay (provider ↔ customer typing indicator and read-receipt) added in Provider.Mobile Tasks 7/9 and mirrored into Customer.Mobile Task 11 has no automated backend hub test — `grep` across `tests/Backend.Api.Tests` for `Typing`/`Seen` returns nothing. The plan's own self-review notes call this out explicitly: "hub relay (Typing/Seen) is manually verified (no automated hub-method test) — accepted for the demo, noted in the spec's testing posture."

**Limitation:** A regression in the hub relay (e.g., a typo in the SignalR method/group name, or a broken de-dup/throttle on the client) would not be caught by `dotnet test` — only by a human manually watching both apps' chat screens simultaneously.

**Recommended Improvement:** Add a `Microsoft.AspNetCore.SignalR.Client.TestHost`-based (or in-memory hub context) test that connects two fake clients to the hub and asserts a `Typing`/`Seen` message sent by one is relayed to the other for the same job, mirroring the pattern already used for the REST endpoints in `WebApplicationFactory`.

**Closing this gap requires:**
1. Stand up an in-process SignalR test harness (two `HubConnection`s against the `WebApplicationFactory`'s `TestServer`) — roughly half a day given no existing precedent in this repo for hub-level testing — pending
2. Write the actual relay-assertion tests for `Typing` and `Seen` — an hour once the harness exists — pending

**Updated 2026-07-23 — this now carries the one item the two-app walkthrough never reached.** Chat *crossing* two devices has not only gone untested automatically, it has never been **watched live** either: every other beat of the demo has been driven on two simulators and screenshotted, but the typing indicator and seen receipts appearing on the opposite phone have not. So the relay currently has neither of its two possible forms of evidence. Watching it once on two simulators is ~15 minutes and worth doing before the harness work, because it is the cheaper of the two and answers "does it work at all" rather than "will it keep working".

**Priority:** Low for the prototype's current scope (explicitly accepted in the plan's self-review), but the first thing to add if this project graduates past demo status. The **live watch** above is a separate, higher priority — it is the last unverified beat in the demo.

**Related:** [Compromise: net10.0 instead of the plan's net8.0](#2026-07-18--targeted-net100-instead-of-the-designed-net80)

---

### 2026-07-18 — Fire-and-forget Demo Orchestrator task has no error surfacing

**Current State:** `DevEndpoints.mapAll`'s `/dev/demo/start` handler calls `runTimeline sp dto.Id |> ignore` — a genuinely fire-and-forget `Task`. If any step in the ~20-second scripted timeline throws (e.g., a job was manually transitioned out of band by someone clicking `/dev` console buttons mid-script, making a later `svc.Apply` call return `Error` unexpectedly, or an unhandled exception), the exception is silently swallowed; the HTTP response for `/dev/demo/start` has already returned 200 with the created job.

**Limitation:** A demo presenter clicking "Start Demo" then also fumbling with manual transition buttons on the same job could silently desync the timeline with no visible error — the UI would just stop updating with no explanation. **Updated 2026-07-18:** both Provider.Mobile and Customer.Mobile now also trigger this same path from in-app "Start Demo" buttons (Tasks 10/11), not just the `/dev` console, so the blast radius of an unnoticed desync is now two mobile apps' worth of demo presenters instead of one console operator.

**Recommended Improvement:** Either (a) log unhandled exceptions from `runTimeline` to console/ILogger so a desync is at least diagnosable from server logs, or (b) push a `Notification` hub event on timeline failure so the `/dev` console surfaces "Demo script failed: {reason}" instead of going silently quiet.

**Closing this gap requires:**
1. Wrap `runTimeline`'s body in a try/catch that pushes a `hub.Notify` on failure — half a day's work including a test — pending
2. Decide whether a failed script should also revert/cancel the in-flight job or leave it as-is for manual recovery — needs a product decision, not just code — pending

**Priority:** Low for a proof-of-concept demo tool (the intended usage — don't touch the job while Start Demo is running — is documented in the console's own UI flow), but worth a one-line code comment noting the constraint if not fixed.

**Related:** [Compromise: rating auto-closes a Completed job single-sidedly](#2026-07-18--rating-a-completed-job-auto-closes-it-single-sidedly)

---

### 2026-07-18 — No pre-commit check for generated-file patterns

**Current State:** `.gitignore` currently excludes `bin/`, `obj/`, `.DS_Store`, and `*.db`, added reactively after a generated SQLite file was accidentally committed (see Mistake log).

**Limitation:** Nothing currently verifies, before a commit, that `git status --short` is free of newly-generated files matching common runtime-artifact patterns not yet covered by `.gitignore`. The next generated-file category (e.g., a log file, a `.tmp` cache) could recur the same way.

**Recommended Improvement:** A lightweight pre-commit hook (or a `dotnet husky`/plain git hook) that fails the commit if `git status --short` shows any file matching a small denylist of common generated extensions not already `.gitignore`d.

**Closing this gap requires:**
1. Write a ~15-line shell pre-commit hook checking `git diff --cached --name-only` against a denylist (`*.db`, `*.log`, `*.tmp`, `*.user`) — a couple of hours including testing — pending
2. Decide whether to wire it via `.git/hooks/pre-commit` directly or a shared hooks-manager (husky.NET is already common in F#/.NET repos) — pending

**Priority:** Low — the concrete instance was cosmetic and already fixed; this is prevention for the *next* occurrence, not a live issue.

**Related:** [Mistake: generated SQLite file committed to git](#2026-07-18--generated-sqlite-file-committed-to-git-despite-an-earlier-gitignore-update)

---

### 2026-07-18 — No fantomas/format-on-save hook configured

**Current State:** The F#-specific hooks guidance (`~/.claude/rules/ecc/fsharp/hooks.md`) recommends a PostToolUse hook running `fantomas` on edited F# files, plus a `dotnet build`/`dotnet test --no-build` verification hook. None of these are currently configured for this repository — all builds and tests in this session were run manually via Bash.

**Limitation:** Formatting drift (e.g., the multi-line-record indentation issue above) is only caught at `dotnet build` time, not proactively on save. There's also no automatic re-test-on-edit loop; verification happened at task boundaries by explicit choice, which worked for this session's pace but doesn't scale to faster edit-test cycles.

**Recommended Improvement:** Wire the three hooks documented in `fsharp/hooks.md` (fantomas format, `dotnet build` verify, `dotnet test --no-build` targeted re-run) into this repo's `.claude/settings.json` before Plan 2/3 implementation, where edit velocity will be higher across more files (Fabulous views, MVU update functions).

**Closing this gap requires:**
1. Add a `.claude/settings.json` (project-local) with the three PostToolUse hooks from `fsharp/hooks.md` — under an hour — pending
2. Confirm `fantomas` is available/installable in this environment (`dotnet tool install fantomas`) — not yet verified — pending

**Priority:** Medium — would have caught the F# 9 indentation issue and the FS3155 Nullable-capture issue at edit time instead of at the next full build.

**Related:** [Lesson: F# 9's stricter indentation breaks the one-line idiom](#2026-07-18--f-9s-stricter-indentation-breaks-the-one-line-attr-type-x-----idiom)

---

## Compromises

### 2026-07-22 — Both apps are pinned to Light appearance

**Tradeoff:** `UIUserInterfaceStyle = Light` in both `Info.plist` files, so the apps ignore the device's Dark Mode preference.

**Reason:** `ClientShared/Theme.fs` uses concrete hex because MAUI does not surface iOS semantic colours (`label`, `systemBackground`) through Fabulous. On a dark device the result was dark system chrome around light-only content — white bubbles against a black status bar. A demo has no opportunity to recover from that.

**Impact:** Anyone who prefers Dark Mode gets Light. For a pitch demo that is invisible; for a shipped product it would not be.

**Prevention going forward:** The rationale is written **in the plist, at the point someone would delete the key** — not only here. Lifting the pin is gated on a real dark palette existing in `Theme.fs`, which is Phase 4 work.

**Updated 2026-07-22 (redesign session):** Deferral affirmed with the user after the full redesign, once the cost was clear. In Fabulous MVU this is **not** a palette swap: `Theme.*` are static values bound at module load, so real dark mode means adding an `Appearance` to the Model, subscribing to the system appearance-changed event, and threading it through **~80 `Theme.*` call sites** across every view (plus unpinning both plists and a screenshot pass in *both* appearances). That is a broad, whole-app refactor — its own scoped task, deliberately not attempted at the tail of a long session. Accepted recurrence: the Light pin ships for the demo.

**Revisit When:** `Theme.fs` gains a dark ramp, or Fabulous exposes MAUI's `AppThemeBinding`.

**Related:** [Mistake: `Info.plist` edits silently did not reach the built app](#2026-07-22--infoplist-edits-silently-did-not-reach-the-built-app)

---

### 2026-07-22 — Sessions are stored in plain `Preferences`, not `SecureStorage`

**Tradeoff:** The signed-in session (token, user id, role, display name) is written to unencrypted `Preferences`.

**Reason:** The token is literally `"fake-customer-1"` (see [src/Backend.Api/Auth.fs](src/Backend.Api/Auth.fs)). Putting it behind the keychain would dress a demo credential up as a real one — the same reasoning that keeps the demo password unhashed rather than pretending to a credential store this prototype does not have.

**Impact:** None while the token is fake. The moment real auth arrives, this must move.

**Prevention going forward:** Stored as four flat keys under a `fixithere.session.` prefix with the rationale in a comment at the store; restore requires *all* fields, so a partial write yields "not signed in" rather than an actor with no id. Real auth must change the storage in the same commit that changes the token.

**Revisit When:** Tokens stop being `fake-*`.

---

### 2026-07-19 — The demo console is dark, against the stated consumer-marketplace direction

**Tradeoff:** Asked what FixItHere should feel like, the answer was "consumer-marketplace polish — warm and approachable, like Uber or Thumbtack". The `/dev` console shipped as a dark, true-neutral instrument surround instead. The in-app WebView map stayed light and consumer-facing.

**Reason:** The console is the operator's back-of-house surface, and its usage scene decides its treatment: a laptop mirrored to a TV in a lit room, where midtones wash out under projection and the audience is watching the map. Dark chrome makes the bright map the only thing competing for attention. Following the stated direction literally would have produced the reflexive white-page-with-friendly-blue marketplace clone, which is also the first thing the design guidance warns against.

**Impact:** The two web surfaces do not look alike. That is intentional — they share the OKLCH token vocabulary and the honey accent, and differ in surface role — but anyone opening both expecting one visual system will be briefly surprised.

**Prevention going forward:** the reasoning is captured in [PRODUCT.md](PRODUCT.md) under Design Principles ("the map is the hero", "legible under a projector"), so the next contributor inherits the rationale rather than re-deriving it from the screenshot. Reverting is a token change, not a rewrite — the surfaces are already fully tokenised.

**Revisit When:** the console stops being projected, or if a stakeholder reads the dark chrome as "unfinished tooling" rather than "instrument panel" — the failure mode this choice is betting against.

**Related:** [Gap: the in-app WebView map redesign has never been looked at](#2026-07-19--the-in-app-webview-map-redesign-has-never-been-looked-at)

---

### 2026-07-19 — The backend port is hardcoded in three places

**Tradeoff:** `Config.baseUrl` is a mutable module-level string pinned to `http://localhost:5162`, matched by hand in both `MauiProgram.fs` Android overrides, the README, and [.claude/launch.json](.claude/launch.json). There is no single source of truth and no configuration mechanism.

**Reason:** The apps have no config file or environment plumbing, and adding one to a prototype whose backend always runs locally is scope the demo does not need. 5162 was chosen because it is what `launchSettings.json` already pinned, and because 5000 — the conventional default — is unusable on macOS.

**Impact:** Changing the port means editing four files, and missing one produces the silent connectivity failure this branch already fixed once. `"autoPort": false` is set in launch.json specifically so tooling cannot reassign it.

**Prevention going forward:** accepted recurrence, with a guard rather than a fix — the README's Notes section now states the port, states that `launchSettings.json` outranks `ASPNETCORE_URLS`, and states that 5000 must not be used on macOS. That is the mechanism: the next person to change the port reads why before they do.

**Revisit When:** the backend needs to run anywhere other than a developer's localhost, at which point `Config.baseUrl` should read from configuration and the hardcoded copies collapse into one.

**Related:** [Lesson: launchSettings outranks ASPNETCORE_URLS](#2026-07-19--launchsettingsjson-outranks-aspnetcore_urls-and-macos-owns-port-5000)

---

### 2026-07-19 — Two ruleset rules were removed to make the repository pushable

**Tradeoff:** The repository ruleset "Test" (id 19160476) targeted `~ALL` branches with `deletion`, `non_fast_forward`, `update`, and `creation`, and an empty `bypass_actors` list. `update` and `creation` were removed.

**Reason:** With those two rules active and no bypass actors, `current_user_can_bypass` was `"never"` — nobody, including the repository owner, could push a commit or create a branch anywhere. Six commits were stranded locally. The ruleset had been created and edited 31 seconds apart, which reads as an experiment that overshot rather than an intended posture.

**Impact:** The repository is public and now accepts pushes normally. `deletion` and `non_fast_forward` remain active, so branches still cannot be deleted or force-pushed — the protections worth having survived.

**Prevention going forward:** the surgical edit *is* the mechanism. Deleting the ruleset or disabling enforcement would have removed the force-push and deletion guards too; editing the rule list kept the ruleset as a live object to build on. Any future tightening should add rules back individually and verify a push still succeeds before walking away.

**Revisit When:** collaborators are added — at that point `update` becomes worth re-adding with a pull-request requirement and the owner as a bypass actor, rather than as a blanket block.

**Related:** none

---

### 2026-07-18 — Targeted net10.0 instead of the designed net8.0

**Tradeoff:** The Prototype-LLM.md design doc and the Plan 1 implementation plan both specify `net8.0` for `Shared` and `Backend.Api`. The actual build targets `net10.0` throughout.

**Why:** Only the .NET 10 SDK (`10.0.302`) is installed in this environment; no net8 runtime/SDK is present, and installing an additional major SDK version wasn't in scope for getting the prototype running.

**Impact:** All code is net10.0-only. This is invisible to app behavior (no net8-specific API was used) but means the solution cannot be built in an environment that only has net8 installed, and any net8-specific NuGet package version pins elsewhere would need reconciling if this project is later merged with net8-targeted code.

**Prevention going forward:** Accepted recurrence — this was a deliberate, surfaced tradeoff (flagged explicitly to the user at the start of Plan 1 execution), not a silent drift. The mechanism for staying consistent is the README's "Notes" section, which explicitly documents "net10 is what the toolchain here provides — same code" so future contributors aren't surprised by the mismatch against the design docs.

**Revisit When:** If this project needs to run in a CI environment or a teammate's machine pinned to net8 SDKs. **Updated 2026-07-18:** the other trigger condition — "MAUI's net10 workload manifests turn out to be unavailable/unstable" — is now resolved in the other direction: `dotnet workload install maui` succeeded (workload `10.0.20/10.0.100`) and both Customer.Mobile and Provider.Mobile build/run cleanly for `net10.0-maccatalyst`, so that fallback was never needed. The net10-vs-net8 compromise itself remains active/accepted; only the workload-availability uncertainty is gone. See [Archive: MAUI workload gap resolved](#archive).

**Related:** none

---

### 2026-07-18 — Rating a Completed job auto-closes it single-sidedly

**Tradeoff:** The domain glossary ([PlanTheApp.md](docs/PlanTheApp.md)) specifies **bidirectional, blind, simultaneous-reveal** ratings (both customer and provider rate; reveal happens when both submit or after a 7-day window). The implemented `POST /ratings` endpoint instead closes the job (`RateAndClose`) the moment **any single** rating is posted against a `Completed` job.

**Why:** The prototype's explicit mandate (Prototype-LLM.md) is to "prove the experience, not the business rules," and the demo flow only ever exercises one side rating (the customer, from the mobile app) — implementing the full blind/bidirectional/7-day-window machinery would add meaningful complexity with no demo-visible payoff.

**Impact:** The `/dev` console and any future Provider.Mobile rating screen will each independently trigger a close on first submission, rather than waiting for both sides — acceptable for a proof-of-concept but a real product misrepresentation if anyone reads this code as reference for the production rating rules.

**Prevention going forward:** This simplification is already called out explicitly in the Plan 1 implementation plan's self-review notes ("Known simplifications (deliberate)") and now here — the convention going forward is that every deliberate glossary-deviation gets logged in both places so it's discoverable from either the plan or this journal.

**Revisit When:** If this prototype ever needs to demonstrate the bidirectional-blind-reveal rating mechanic specifically (e.g., a stakeholder asks "show me how ratings stay hidden until both sides submit"), or if it graduates toward a production build.

**Related:** [Gap: fire-and-forget Demo Orchestrator task has no error surfacing](#2026-07-18--fire-and-forget-demo-orchestrator-task-has-no-error-surfacing)

---

### 2026-07-18 — Payment "Authorized → Transferred" two-phase animation is client-side only

**Tradeoff:** The design spec describes a two-phase fake-payment UX: "Payment Authorized" (immediate) → loading → "Transferred to Provider $X" (after a delay). The actual `POST /payment/simulate` endpoint returns the final `"Transferred"` status in one synchronous call — there's no server-side "Authorized" intermediate state or delay.

**Why:** The two-phase *feel* is purely a presentation-layer concern (timing and copy), and the backend's job was only ever to prove the state machine and contract, not simulate realistic payment-gateway latency. Building the phase transition into the client (Customer.Mobile Payment screen, per its design spec) is simpler than adding a second endpoint or a server-side delay/polling mechanism.

**Impact:** Any client consuming `/payment/simulate` must implement the "Authorized" phase itself as a local UI state before calling the endpoint, rather than being able to poll or subscribe for an intermediate status. This is already reflected in the Customer.Mobile design spec's Payment screen description ("Phase 1 'Payment Authorized' (on entry) → 2s loading → call the endpoint").

**Prevention going forward:** Documented in both the Plan 1 self-review notes and the Customer.Mobile design spec so Plan 3 (Provider.Mobile) implements the same client-side pattern rather than reinventing it or expecting a different contract.

**Revisit When:** Not planned to be revisited within this prototype's scope — would only matter if a future phase needs the backend to expose real intermediate payment states (e.g., to test disconnect/reconnect handling mid-payment).

**Related:** [Compromise: rating auto-closes a Completed job single-sidedly](#2026-07-18--rating-a-completed-job-auto-closes-it-single-sidedly)

---

## Archive

> Entries moved here when the underlying condition no longer applies.
> Kept for historical context.

### 2026-07-23 — CI is re-enabled but GitHub Actions is blocked on billing

**Current State:** `.github/workflows/ci.yml` was restored to `push:` + `pull_request:` on 2026-07-23, after being paused since 2026-07-20 for the believability rebuild's DTO churn. The suite it runs is green — 212 tests across the four projects. But Actions is blocked by an account billing issue, so runs fail or refuse to start for reasons unrelated to this repository.

**Limitation:** The gate exists and does not gate. Worse than not having it: a *red* CI badge that means "billing", not "broken", trains everyone to ignore red — which is exactly the state a required check is supposed to prevent. The re-enable was made with that known and accepted; the workflow's header comment says so, so a contributor meeting a failure knows to reproduce locally before believing it.

**Closing this gap requires:**
1. Resolve the GitHub Actions billing issue on the account — not a code change, and not something this repo can do — pending
2. Confirm the first real run is green on `ubuntu-latest` (tests) — ~10 min once (1) clears — pending
3. Decide whether the iOS `-t:Compile` gate belongs in the same job or a small macOS one, and make the Tests job a required check on `main` — ~30 min — pending

**Active mitigation:** The four `dotnet test` commands and the two `-f net10.0-ios -t:Compile` builds are the real gate meanwhile, run locally before every commit this session. Never `dotnet test` the `.slnx` — it pulls in the mobile TFMs and fails for environment reasons.

**Priority:** Medium — nothing is unverified today because the local gate is being run, but that depends entirely on discipline, which is what CI exists to replace.

**Related:** [Environment-dependent failures cannot be gated by pre-flight probes](#2026-07-20--environment-dependent-failures-cannot-be-gated-by-pre-flight-probes-build-then-classify); [Mistake: an unverified probe justified removing continue-on-error](#2026-07-20--an-unverified-probe-justified-removing-continue-on-error-and-turned-green-runs-red)

**Archived 2026-07-23 — this entry was wrong, and is kept as a correction rather than deleted.** It was written on a reported billing constraint *before* the re-enabled workflow had run once. The first two runs (push `30028398304`, pull_request `30028402397`) both completed **success** — Tests in 1m10s, Build apps (iOS) in 2m14s — so Actions was never blocked. The lesson is the ordinary one: a constraint that has not been observed is a hypothesis, and writing it into the journal as a fact costs more than waiting ninety seconds for the run. What *is* real from that first run is recorded separately below.

---

### 2026-07-18 — Task 12's two-app manual acceptance walk and final per-task review were not completed before disk exhaustion interrupted the session

**Current State:** Commit `cec1e7c` ("docs: Provider.Mobile run instructions; prototype acceptance complete") landed with all 78 automated tests passing and both mobile apps building cleanly for `net10.0-maccatalyst`. However, per this project's standing execution/review split (Sonnet 5 implements, Opus 4.8 reviews each task's diff — see the plan's "Execution profile"/"Reviewer checklist" sections in [docs/superpowers/plans/2026-07-18-provider-mobile.md](docs/superpowers/plans/2026-07-18-provider-mobile.md)), the final task's Opus review had not yet been dispatched, and the plan's Task 12 Step 2 (a manual two-app-side-by-side click-through: online toggle, accept, live GPS tracking, chat typing/seen, auto-reply, fake payment, ratings, both apps' in-app Start Demo buttons, `/dev` Reset Demo) had not been independently re-driven interactively in this session — the disk-exhaustion incident interrupted verification before either step ran.

**Limitation:** There is also no tool in this environment analogous to Playwright for web apps that can drive a running native Mac Catalyst window — a MAUI/Fabulous UI can't currently be click-tested by the agent itself the way a browser-rendered page can via the Browser pane tools. Even absent the disk incident, Task 12 Step 2 would have needed either the user to drive it manually or a native UI-automation tool this project doesn't have wired up.

**Recommended Improvement:** Now that disk headroom is restored (~5.2GB free as of this writing), dispatch the deferred Opus review pass over the Task 12 diff, and explicitly ask the user to perform (or confirm they already performed) the two-app manual walkthrough from the plan's Step 2 — don't infer it happened just because the commit message says "acceptance complete."

**Updated 2026-07-19:** the review shipped and its findings are remediated, but the walkthrough itself is still outstanding — and it now matters *more*, not less. The review returned **BLOCK with 15 findings**, four of them user-visible defects on the primary demo path that no test caught. Every one was found by reading code or driving the API, never by running the apps. Both apps now launch and reach the backend (the port bug is fixed), so the walkthrough is finally possible; it has simply not been done.

**Closing this gap requires:**
1. Dispatch the Opus 4.8 review pass for the final task's diff per the standing execution-profile — ✅ shipped 2026-07-19; returned BLOCK, findings remediated across `1f3b309`, `6927691`, `b36ef79`
2. Get explicit user confirmation of the two-app manual acceptance walk (Step 2 of Task 12), or perform it together interactively — pending; both apps launch cleanly and the demo pair (John + Mike's Plumbing) now works after the identity fix
3. Longer-term: evaluate whether any native macOS UI-automation tool (e.g., XCUITest driven headlessly, or `cliclick`/AppleScript against the built `.app`) could give the agent a Playwright-equivalent for MAUI/Mac Catalyst windows — not started, speculative

**Priority:** High — still the last open item, and the review demonstrated empirically that static analysis alone missed four defects a single walkthrough would have surfaced.

**Related:** [Gap: no disk-headroom check before/during long MAUI build-heavy agentic sessions](#2026-07-18--no-disk-headroom-check-beforeduring-long-maui-build-heavy-agentic-sessions)

**Archived 2026-07-23** — superseded twice over. It refers to Task 12 of the *previous* plan (`docs/superpowers/plans/2026-07-18-provider-mobile.md`) and to a Mac Catalyst target that no longer exists. The believability plan replaced that acceptance walk with its own, which has now been run end to end on two iOS simulators, and the agent *can* drive a native UI here — `xcrun simctl` plus the simulator MCP tools, which the entry assumed were unavailable.

---

### 2026-07-19 — The Mac Catalyst CI job cannot be a required check

**Current State:** [.github/workflows/ci.yml](.github/workflows/ci.yml) builds both apps for `net10.0-maccatalyst` on `macos-latest`, marked `continue-on-error: true`. The Tests job on ubuntu is the real gate and passes (96 tests, run 29680507369).

**Limitation:** The job cannot currently succeed on the hosted image, and the reason is a genuine bind rather than a misconfiguration. `Microsoft.MacCatalyst.Sdk` 26.5.10301 requires **Xcode 26.6**, and while the image's 26.6 passes every standalone check — same Build 17F113 as a working local install, SDK present, exact-call probe green — the identical xcodebuild invocation fails with `SDK cannot be located` **when run under MSBuild**. **Updated 2026-07-20:** the earlier "missing platform SDK" diagnosis was an approximation; the failure is context-dependent, not install-dependent, which is why every install-inspection strategy failed (see the build-then-classify lesson).

**Why it still earns its place:** the test projects compile `Domain`/`Update`/`Api` but never `Views/*.fs` or `MauiProgram.fs`. This job is the only thing that would catch a broken view, and views were edited repeatedly during this work.

**Closing this gap requires:**
1. Wait for the image/toolchain combination to work — the build-then-classify design detects this automatically: the first run whose builds succeed simply gates, with no workflow change needed — pending, zero work ✅ (mechanism shipped in `e328b88`)
2. Or pin the MacCatalyst workload to a version matching a working Xcode on the image — an hour, and re-pins on every SDK bump — pending
3. Or self-host a macOS runner with a known-good Xcode — half a day plus ongoing maintenance — pending

**Active mitigation:** `continue-on-error` is gone. The job builds and classifies: environment-signature failures annotate and exit 0 (verified live — run 29717326198 is green with the skip notice over a failed underlying build); any other failure gates, so a broken view blocks the merge the moment the image starts working.

**Priority:** Medium — the gap is real coverage, but the local `dotnet build -f net10.0-maccatalyst` still catches the same class of breakage on a developer machine.

**Related:** [Mistake: CI went red on its first run](#2026-07-19--ci-went-red-on-its-first-run-because-the-test-suite-was-never-re-run-after-the-redesign)

**Archived 2026-07-23** — moot. Mac Catalyst was removed entirely in plan task 0e; `grep -rn maccatalyst` over `src/` and `.github/` returns nothing. The iOS `-t:Compile` gate replaced it, and CI was re-enabled on 2026-07-23.

---

### 2026-07-20 — Verified live defects surfaced by plan audit; none yet fixed

**Current State:** A three-lens audit of the believability plan surfaced defects in the *shipped* code that no prior review, test or walkthrough had caught. Every one below was verified against source or the running system.

**Updated 2026-07-20 — Phase 0 closed items 1, 2, 3, 4 and 5.** Remaining: 6 (scripted demo's ephemeral chat), 7 (double-rating), 8 (back re-books), 9 (five state-string dependants — documented, not yet consolidated), 10 (unbounded `VStack`s, which an iPhone viewport will clip). Items 6–8 are plan task 7; item 10 is task 12.

| # | Defect | Evidence | Severity |
|---|---|---|---|
| 1 | **"Developer Settings" is a button on both apps' Home screens**, leading to Teleport / Simulated-GPS / route-percentage / Start-Demo controls. Provider Chat ships a labelled Auto-Reply `Switch` | `Customer/Views/Home.fs:19`, `Provider/Views/Home.fs:28`, `Provider/Views/Chat.fs:36-38` | **Fatal for a demo** |
| 2 | **Neither app has a login** — `Login.fs` is *"Who's booking today?"* over five hardcoded first names | both `Views/Login.fs` | **Fatal for a demo** |
| 3 | **Ratings collide across id spaces.** `Rating` has no role column; provider→customer rating writes `RateeId = job.CustomerId`, while the public query filters `RateeId = providerId`. Both sequences run 1–20, so **each completed demo loop mutates a provider's public star average** | `Db.fs:29-30`, `Provider/Update.fs:180`, `Endpoints.fs:30,174` | **High** — same class as the message-identity bug already fixed |
| 4 | **Every customer's Home accumulates strangers' jobs.** `JobUpdated` broadcasts to `Clients.All` and `Customer/Update.fs:167` appends any unseen job | `Hub.fs:20`, `Customer/Update.fs:163-174` | **High** — cross-tenant leakage on the first screen |
| 5 | Live-booked jobs render `Address = "My location"` | `Customer/Update.fs:69` → `JobDetail.fs:15` | Medium |
| 6 | Scripted demo injects chat with `Id = 0` that is never persisted: the second is deduped away, the customer-role one renders as **"You: Hi!"** in the customer's own app, and both vanish on navigation | `DevEndpoints.fs:52-62` | Medium |
| 7 | Scripted demo double-rates — `runTimeline` applies its own 5-star "Great demo!" *and* `RateAndClose` while the customer app is already on Rating | `DevEndpoints.fs` | Medium |
| 8 | Back-navigation re-books: `JobCreated` pushes Tracking onto Booking, so back-back-tap creates a duplicate job | `Customer/Update.fs:71-73` | Medium |
| 9 | Job state has **five** string dependants, not the three previously documented — the extra two are `Provider/Domain.fs:160` (`inFlight` list) and `DevEndpoints.fs:32-75` (`runTimeline`'s hardcoded happy path) | — | Medium — corrects an earlier entry |
| 10 | Only `Chat.fs` has a `ScrollView`; Home, Catalog, ProviderList, JobDetail are unbounded `VStack`s | both `Views/` | Medium — bites the moment any narrower layout ships |

**Closing this gap requires:**
1. Strip demo scaffolding from both apps' shipping surface; move route control to `/dev` — ✅ shipped `8bdf06d`
2. Replace the name-picker login with a real-looking sign-in — ✅ shipped `2d888f4`
3. Add a role column to `Rating` and scope the public query — ✅ shipped `71d610e`
4. Job-scoped SignalR groups replacing `Clients.All` — ✅ shipped `637a1e3`
5. `Address = "My location"` on booked jobs — pending, folded into Phase 1 task 1 (geography)
6. Items 6–10 — roughly a day — pending, Phase 2 task 7 and Phase 3 task 12

**Priority:** High — items 1 and 2 are visible within the first four seconds of any demo, before any feature has a chance to argue otherwise.

**Related:** [Lesson: adversarial multi-lens audit](#2026-07-20--adversarial-multi-lens-audit-finds-the-class-of-defect-the-author-is-structurally-blind-to)

**Archived 2026-07-23** — every row is closed. Items 1–5 shipped in Phase 0 and Phase 1 (commit refs in the checklist above). Items 6, 7 and 8 shipped in plan task 7 — `DevEndpoints.fs` now persists scripted chat and no longer double-rates (both carry comments naming the old behaviour), and `Customer/Update.fs` uses `Nav.resetTo` so back-navigation cannot re-book. Item 10 shipped in task 12: all five previously-unbounded screens now open with a `ScrollView` (`grep -c ScrollView` returns 1 for each). Item 9 was a *correction to documentation*, not a defect — the five state-string dependants are recorded in the plan's executor note 4.

---

### 2026-07-22 — Four presentation defects found by the walkthrough, deferred to Phase 4

**Current State:** Catalog shows bare trade names; `ProviderList` shows `★0.0 (0)` for unrated providers; all of a provider's reviews are attributed to one customer; `JobDetail`'s countdown is not urgency-coloured.

**Limitation:** Each is a visible tell. None is behavioural.

**Ideal Solution:** Fix with the redesign of the screen each lives on, rather than twice.

**Closing this gap requires:**
1. Render `ServiceDto.FromPrice` in Catalog ("Plumbing · from $277") — pending, plan task 14
2. `★0.0 (0)` → "New" in ProviderList — pending, plan task 14
3. `Seed.fs`: draw raters from customers other than the job's own; re-verify the determinism fingerprint — pending
4. `urgencyColor` on JobDetail's countdown — pending, plan task 16

**Priority:** Medium — deferred deliberately, tracked so it cannot be lost.

**Archived 2026-07-23** — all four were fixed with the screens they lived on during Phase 4 (plan tasks 13–17, all complete). Verified in the running apps: Catalog carries copy beyond bare trade names, unrated providers read "New" via `Format.rating` rather than `★0.0 (0)`, reviewer diversity was fixed by the seed's lap offset, and JobDetail's countdown is urgency-coloured.

---

### 2026-07-22 — The two-app walkthrough is only half run

**Current State:** Sign-in, job isolation with two live clients, booking through the real flow, the booking appearing live on the provider, accept, and the full running-late propose/answer loop are all verified on two simulators with screenshots.

**Limitation:** Depart, chat across two devices (typing indicator and seen receipts crossing), arrive, start work, complete, **payment**, and **rating** have never been exercised on two real apps. Payment is the significant one — the customer's total beside the provider's payout is the marketplace story and the screen an investor studies.

**Updated 2026-07-23 — the walkthrough was run end to end; one item remains.** A real booking (customer → Mike's Plumbing) appeared live on the provider, was accepted, ran the full running-late propose/accept/retarget loop across both devices, then depart → arrive → work → complete, landing the provider on **Payout $235.88** and the customer on **Paid $313.58** (subtotal $277.50, +13% HST vs −15% platform fee — the two figures differing is the marketplace proof), both rated, job `Closed`, and the provider's public average moved 3.7 (3) → 4.0 (4) without the provider→customer rating polluting it. The run found three defects, all since fixed: [no drive on depart](#2026-07-23--departing-flipped-the-status-but-never-drove-the-provider), [the map never tracking](#2026-07-23--the-map-never-tracked-live-signalr-credentials-and-a-casing-mismatch), and [notices covering titles](#2026-07-23--notices-expired-on-the-demo-clock-so-at-1x-they-never-cleared) — which is the fourth consecutive time the walkthrough earned its cost.

**Closing this gap requires:**
1. Boot both simulators, install current builds — ✅ shipped
2. Drive depart → arrive → work → complete, watching the map and both countdowns — ✅ shipped
3. Chat across devices, checking typing/seen cross the wire — **pending** (both Chat screens were opened and render correctly, but a message, typing indicator and seen receipt have still never been watched crossing between two live devices)
4. Payment on both phones side by side — ✅ shipped
5. Rating both ways, job closes — ✅ shipped

**Priority:** Medium — down from High. The money and reschedule beats are verified; only the chat-crossing signal remains unproven, and it is the one beat whose failure would be least visible on stage.

**Related:** [Lesson: rendering defects are structurally invisible to a green test suite](#2026-07-22--rendering-defects-are-structurally-invisible-to-a-green-test-suite)

**Archived 2026-07-23** — the walkthrough ran end to end (see the 2026-07-23 Updated block above: booking → accept → running-late loop → depart → arrive → work → complete → both payment screens → both ratings → `Closed`). The title's claim is no longer true. **One item did not retire and is carried forward:** chat *crossing* two devices — typing indicator and seen receipts — has still never been watched live, and is now tracked on its own in [Typing/Seen hub relay has no automated test](#2026-07-18--typingseen-hub-relay-has-no-automated-test-verified-manually-only).

---

### 2026-07-23 — Effects fire at Cmd construction, and the blast radius is unaudited

**Current State:** `apiCmd`/`delayCmd` wrap a hot `task { }`, so the effect starts when the `Cmd` is built inside `update`, not when Fabulous dispatches it. Discovered while mutation-testing an unrelated fix.

**Updated 2026-07-23 — the audit ran, and the gap is narrower than first written.** Item 1 below is closed: **no `Cmd` is constructed and then dropped anywhere in either app.** `grep -rn "let [A-Za-z_]* *= *\(apiCmd\|delayCmd\)"` over both apps returns nothing; the only `let`-bound command is `backOnline` in `Provider.Mobile/Update.fs` (bound to a `match` whose arms call `apiCmd`) and it is always returned in the batch, as are the `let cmd = match …` bindings in both `Navigate` arms. So there is **no invisible live API call and no product defect** — what remains is entirely about how much some tests prove.

**Limitation:** The affected tests can now be named exactly, which the first write-up could not do. Only tests asserting on a **recording stub reached through `apiCmd`** are weakened, because the stub is called during `Update.update` whether or not the Cmd is drained — five of them: `finishing a job puts an off-shift provider back online`, `a restored session hydrates the shift flag`, and `a data reset drops the stale world and refetches` (both apps).

The `runWith` typing/seen tests are **not** affected, and that distinction matters before anyone edits them: they go through `Cmd.ofSub (fun _ -> deps.SendTyping …)`, which is already cold, so their drain is load-bearing and their guards are genuinely covered.

**Ideal Solution:** Make `apiCmd` cold — `Cmd.ofSub (fun dispatch -> (task { … }) |> ignore)` — so nothing runs until dispatch. The five tests above should stay green *for the right reason* rather than turn red (the stubs return `Task.FromResult`, so the deps call still happens synchronously inside the drained sub); any test that does go red was asserting on something else and needs reading.

**Closing this gap requires:**
1. Audit both apps for `Cmd`s constructed and conditionally dropped — ~15 min — ✅ **done 2026-07-23: none exist**
2. Switch `apiCmd` **and `delayCmd`** to a cold wrapper in both apps — ~20 min — pending. `delayCmd` has the same shape, so its timer currently starts at construction; indistinguishable today, same latent trap.
3. Re-run `Customer.Mobile.Tests` + `Provider.Mobile.Tests`; triage any newly-red test as "was asserting on construction" — ~30 min — pending
4. **Add a test that proves `apiCmd` is cold** — construct against a recording stub, assert nothing recorded, drain, assert recorded — ~10 min — pending. This is the missing active mitigation: without it, item 2 silently reverts the first time the helper is rewritten and nothing anywhere fails.

**Active mitigation:** None automatic yet — item 4 is it, and it is not written. The manual rule until then: **mutate the Cmd's construction, not its presence in the returned batch** — removing it from `Cmd.batch` proves nothing, which is the exact false negative that surfaced this.

**Priority:** Medium — confirmed no live defect, but it silently weakens the one testing technique this project relies on for guard logic.

**Related:** [Lesson: a task-based Cmd can fire before it is dispatched](#2026-07-23--a-task-based-cmd-can-fire-before-it-is-dispatched); [MVU test helpers that discard the returned `Cmd<Msg>`…](#2026-07-18--mvu-test-helpers-that-discard-the-returned-cmdmsg-silently-hide-untested-guard-logic)

**Archived 2026-07-23** — closed by `bb4a5d8`. All four items shipped: the audit found no Cmd constructed and dropped (so there was never a live defect), `apiCmd` and `delayCmd` are cold in both apps, both mobile suites were re-run with nothing newly red, and the coldness tests that keep it that way are in place.

---

### 2026-07-19 — The in-app WebView map redesign has never been looked at

**Current State:** [MapHtml.fs](src/ClientShared/MapHtml.fs) was restyled in `9c33d32` — amber provider marker with a breathing halo, dark destination pin, reduced-motion path.

**Limitation:** It renders only inside a native Mac Catalyst WebView, which cannot be driven by the available browser tooling. Verification so far is: both apps compile (which type-checks the `sprintf` format string), the rendered HTML contains zero unresolved `%%`, keyframes emit as `0%` / `100%`, and the placeholders bind. Nobody has seen it draw.

**Ideal Solution:** Open the rendered HTML in a normal browser at the same viewport, or drive the running app and screenshot the tracking screen.

**Closing this gap requires:**
1. Serve the `MapHtml.render` output as a static route under `/dev` so it can be opened in a browser — under an hour, and useful permanently as a preview harness — pending
2. Or perform the two-app manual walkthrough and look at the tracking screen — no code, just the walkthrough that is already outstanding — pending

**Priority:** Medium — the risk is cosmetic, but it is the surface the demo audience actually watches.

**Related:** [Gap: the two-app manual acceptance walk](#2026-07-18--task-12s-two-app-manual-acceptance-walk-and-final-per-task-review-were-not-completed-before-disk-exhaustion-interrupted-the-session)

---

**Archived 2026-07-22** — closed. The iOS 26.5 simulator runtime was installed, the app was built, launched and screenshotted, and the map has now been looked at repeatedly. Doing so immediately found two defects it had been hiding: it flashed several times a second, and it had never received a live position because the backend had no CORS policy (the WebView's document origin is `null`).

---

### 2026-07-18 — Provider.Mobile's in-app "Start Demo" button hardcodes customer id 1

**Tradeoff:** The Provider.Mobile `DevSettings` screen's "▶ Start Demo (as this provider)" button calls `deps.StartDemo 1 s.UserId` — customer id `1` is hardcoded rather than derived from any session or selection. (Customer.Mobile's equivalent button correctly uses its own logged-in session's id, and separately picks an online provider or falls back to the first one.)

**Why:** The seed data always creates "John" as the first customer (id 1, verified against Plan 1's seeder), and the provider side of the demo button has no natural "which customer" context to select from — there's no customer-selection UI in Provider.Mobile, and adding one purely to make a demo-convenience button fully general would be scope the prototype doesn't need.

**Impact:** The button only works correctly against the seed data's default customer. If the seed data is ever regenerated with a different customer ordering, or a second customer is added and expected to be the demo's target, this button silently demos against the wrong (or a nonexistent) customer id rather than failing loudly.

**Prevention going forward:** Accepted recurrence — this is a demo-convenience shortcut, not a business rule, and is already called out in the Plan 3 self-review notes ("StartDemo hardcodes customer id 1 (John is seeded first...) for the Provider-side button"). No mechanism enforces it; if the seed order ever changes, this is the first place to check when the Provider-side Start Demo button stops working as expected.

**Revisit When:** If the seed data's customer ordering changes, or if Provider.Mobile ever needs a real customer-selection UI for any other reason (at which point the demo button should reuse that selection instead of the hardcoded id).

**Related:** [Compromise: rating auto-closes a Completed job single-sidedly](#2026-07-18--rating-a-completed-job-auto-closes-it-single-sidedly), [Gap: Typing/Seen hub relay has no automated test](#2026-07-18--typingseen-hub-relay-has-no-automated-test-verified-manually-only)

---

**Archived 2026-07-22** — resolved by deletion, commit `1016af1`. Task 0a had already removed every UI entry point, so the code was unreachable; repairing the hardcoded id would have kept a demo-scaffolding dependency in both shipping apps. The `Msg` case, the `DemoStarted` case, the `ApiDeps` field and the Api implementation are gone from both apps.

---


### 2026-07-18 — MAUI workload not installed; Customer.Mobile (Plan 2) cannot build yet

Archived 2026-07-18 — resolved. `dotnet workload install maui` was run at some point between this gap being logged and Plan 3 (Provider.Mobile) execution; `dotnet workload list` now reports `maui 10.0.20/10.0.100` installed, and both Customer.Mobile and Provider.Mobile build and run cleanly for `net10.0-maccatalyst` (0 warnings/0 errors each, verified directly). The original gap's fallback plan (retarget to net9/net8 if net10 manifests were unavailable) was never needed.

**Resolution:** No specific commit closes this — it was closed by an environment change (workload installation) outside the git history, observed as already-in-place at the start of Plan 3. The reasoning in [Compromise: targeted net10.0 instead of net8.0](#2026-07-18--targeted-net100-instead-of-the-designed-net80) stays active (net10-vs-net8 is still a real, accepted mismatch against the design docs) — only the *uncertainty about whether net10's MAUI workload would even be available* is retired.

**Original entry:** `dotnet workload list` reports zero installed workloads in this environment. Xcode is present (`/Applications/Xcode.app`), so iOS/Mac Catalyst codesigning tooling exists, but the MAUI SDK workload itself (`maui`, `maui-android`, `maui-ios`, `maui-maccatalyst`) has not been installed. This blocked all of Plan 2 (Customer.Mobile) from starting until closed.
