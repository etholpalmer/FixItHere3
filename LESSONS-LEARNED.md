# Lessons Learned

> Engineering journal capturing what was learned, what went wrong, and what
> tradeoffs were made during development of this project.

## Environment

- **OS:** macOS (Darwin 25.5.0)
- **Tooling:** Claude Code CLI (agentic session), no traditional IDE
- **SDK:** .NET 10.0.302 (single SDK installed; no MAUI workload installed as of this writing)
- **Key Tools:** F# 9 (ships with .NET 10 SDK), EF Core 10.0.10, xUnit 2.9.3, FsCheck.Xunit 2.16.6 (pinned), SignalR, SQLite, MAUI workload 10.0.20/10.0.100 (installed mid-project; see Archive)
- **Last Updated:** 2026-07-18 (Plan 3 / Provider.Mobile)

---

## Lessons

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

**Active mitigation:** None automated yet — see [Gap: no fantomas/format-on-save hook wired up](#2026-07-18--no-fantomas-format-on-save-hook-configured). A pre-commit `dotnet build` catches it immediately, but doesn't prevent the first-draft error.

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

**Related:** [Mistake: content-root resolution regressed 9 passing tests](#2026-07-18--contentroot-fix-attempt-regressed-9-previously-passing-tests)

---

### 2026-07-18 — Diagnostic technique: force runtime state into an assertion-failure message

**Insight:** When debugging an ASP.NET Core integration test where `Console.WriteLine`/logging output isn't reliably surfaced by `dotnet test`, the fastest way to inspect actual runtime state (e.g., "what environment name did the factory actually boot with?") is to add a **throwaway test that asserts the expected value against the actual value**, and read the runtime value out of the assertion failure's "Expected/Actual" diff — not `failwithf` (which needs a generic return type workaround and produced its own unrelated compile error here) but a plain `Assert.Equal(expected, actual)`.

**Discovery:** Used twice in this session: once to confirm `WebApplicationFactory` boots `"Production"` not `"Development"`, and implicitly again validating the content-root fix. Both resolved a "why is this 404ing when the real app returns 200" mystery in one test run instead of several rounds of blind guessing.

**Impact:** This generalizes to any integration-test debugging session: add a `[<Fact>]` that intentionally asserts a *deliberately wrong* expected value against the thing you want to inspect, run once, read the diff, delete the test. Faster and more reliable than adding logging plumbing for a one-off question.

**Related:** [Lesson: WebApplicationFactory boots in Production](#2026-07-18--webapplicationfactoryt-boots-in-production-and-env-vars-must-be-set-before-the-sut-s-module-loads)

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

**Active mitigation:** None automated. A `grep -rn "|> fst" tests/**/UpdateTests.fs` skim before trusting any test suite's coverage claims for Cmd-driven behavior is the manual equivalent for now.

**Related:** [Mistake: auto-reply guard was never actually exercised by its own regression tests](#2026-07-18--auto-reply-guard-was-never-actually-exercised-by-its-own-regression-tests)

---

## Mistakes & Fixes

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

**Related:** [Lesson: F# 9's stricter indentation breaks the one-line idiom](#2026-07-18--f-9s-stricter-indentation-breaks-the-one-line-attr-type-x---idiom)

---

### 2026-07-18 — Dev endpoints and `/dev` console 404 under `WebApplicationFactory`

**Symptom:** `POST /dev/reset`, `POST /dev/demo/start`, and `GET /dev/index.html` all returned 404 under the xUnit `WebApplicationFactory`-based tests, despite the exact same build serving 200s under `dotnet run --project src/Backend.Api`.

**Attempted:** First fix attempt overrode `ConfigureWebHost` to call `builder.UseEnvironment("Development")` — did not work. Added a throwaway diagnostic test (see the diagnostic-technique lesson) to confirm the actual environment name at runtime, which revealed `"Production"`.

**Root Cause:** The F# `Program.fs` uses minimal-hosting module-level code (`let builder = WebApplication.CreateBuilder()` at module scope, no `[<EntryPoint>]`). The environment is captured once when `CreateBuilder()` runs, which happens the moment anything forces `Program` module's static initializer — earlier than `WebApplicationFactory.ConfigureWebHost` executes.

**Fix:** Set `ASPNETCORE_ENVIRONMENT` via `Environment.SetEnvironmentVariable` inside the `Factory` type's primary constructor (`type Factory() as this = ... do ...`), guaranteeing it runs before `CreateClient()`/`Program` module load. See [`tests/Backend.Api.Tests/AppFactory.fs`](tests/Backend.Api.Tests/AppFactory.fs).

**Prevention:** For F# minimal-hosting apps under `WebApplicationFactory`, always set environment-affecting variables in the factory constructor, never in `ConfigureWebHost`.

**Time Lost:** ~20 minutes across three attempts (ConfigureWebHost → module-level `do` → constructor).

**Severity:** High — blocked 3 of the plan's dev-endpoint tests and, transitively, verifying the entire `/dev` console tracer bullet.

**Related:** [Lesson: WebApplicationFactory boots in Production](#2026-07-18--webapplicationfactoryt-boots-in-production-and-env-vars-must-be-set-before-the-sut-s-module-loads)

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

### 2026-07-18 — Task 12's two-app manual acceptance walk and final per-task review were not completed before disk exhaustion interrupted the session

**Current State:** Commit `cec1e7c` ("docs: Provider.Mobile run instructions; prototype acceptance complete") landed with all 78 automated tests passing and both mobile apps building cleanly for `net10.0-maccatalyst`. However, per this project's standing execution/review split (Sonnet 5 implements, Opus 4.8 reviews each task's diff — see the plan's "Execution profile"/"Reviewer checklist" sections in [docs/superpowers/plans/2026-07-18-provider-mobile.md](docs/superpowers/plans/2026-07-18-provider-mobile.md)), the final task's Opus review had not yet been dispatched, and the plan's Task 12 Step 2 (a manual two-app-side-by-side click-through: online toggle, accept, live GPS tracking, chat typing/seen, auto-reply, fake payment, ratings, both apps' in-app Start Demo buttons, `/dev` Reset Demo) had not been independently re-driven interactively in this session — the disk-exhaustion incident interrupted verification before either step ran.

**Limitation:** There is also no tool in this environment analogous to Playwright for web apps that can drive a running native Mac Catalyst window — a MAUI/Fabulous UI can't currently be click-tested by the agent itself the way a browser-rendered page can via the Browser pane tools. Even absent the disk incident, Task 12 Step 2 would have needed either the user to drive it manually or a native UI-automation tool this project doesn't have wired up.

**Recommended Improvement:** Now that disk headroom is restored (~5.2GB free as of this writing), dispatch the deferred Opus review pass over the Task 12 diff, and explicitly ask the user to perform (or confirm they already performed) the two-app manual walkthrough from the plan's Step 2 — don't infer it happened just because the commit message says "acceptance complete."

**Closing this gap requires:**
1. Dispatch the Opus 4.8 review pass for the final task's diff per the standing execution-profile — pending
2. Get explicit user confirmation of the two-app manual acceptance walk (Step 2 of Task 12), or perform it together interactively — pending
3. Longer-term: evaluate whether any native macOS UI-automation tool (e.g., XCUITest driven headlessly, or `cliclick`/AppleScript against the built `.app`) could give the agent a Playwright-equivalent for MAUI/Mac Catalyst windows — not started, speculative

**Priority:** High — this is the last open item before the branch can be considered genuinely done, not just committed.

**Related:** [Gap: no disk-headroom check before/during long MAUI build-heavy agentic sessions](#2026-07-18--no-disk-headroom-check-beforeduring-long-maui-build-heavy-agentic-sessions)

---

### 2026-07-18 — Typing/Seen hub relay has no automated test; verified manually only

**Current State:** The SignalR `Typing`/`Seen` hub relay (provider ↔ customer typing indicator and read-receipt) added in Provider.Mobile Tasks 7/9 and mirrored into Customer.Mobile Task 11 has no automated backend hub test — `grep` across `tests/Backend.Api.Tests` for `Typing`/`Seen` returns nothing. The plan's own self-review notes call this out explicitly: "hub relay (Typing/Seen) is manually verified (no automated hub-method test) — accepted for the demo, noted in the spec's testing posture."

**Limitation:** A regression in the hub relay (e.g., a typo in the SignalR method/group name, or a broken de-dup/throttle on the client) would not be caught by `dotnet test` — only by a human manually watching both apps' chat screens simultaneously.

**Recommended Improvement:** Add a `Microsoft.AspNetCore.SignalR.Client.TestHost`-based (or in-memory hub context) test that connects two fake clients to the hub and asserts a `Typing`/`Seen` message sent by one is relayed to the other for the same job, mirroring the pattern already used for the REST endpoints in `WebApplicationFactory`.

**Closing this gap requires:**
1. Stand up an in-process SignalR test harness (two `HubConnection`s against the `WebApplicationFactory`'s `TestServer`) — roughly half a day given no existing precedent in this repo for hub-level testing — pending
2. Write the actual relay-assertion tests for `Typing` and `Seen` — an hour once the harness exists — pending

**Priority:** Low for the prototype's current scope (explicitly accepted in the plan's self-review), but the first thing to add if this project graduates past demo status.

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

**Related:** [Lesson: F# 9's stricter indentation breaks the one-line idiom](#2026-07-18--f-9s-stricter-indentation-breaks-the-one-line-attr-type-x---idiom)

---

## Compromises

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

### 2026-07-18 — Provider.Mobile's in-app "Start Demo" button hardcodes customer id 1

**Tradeoff:** The Provider.Mobile `DevSettings` screen's "▶ Start Demo (as this provider)" button calls `deps.StartDemo 1 s.UserId` — customer id `1` is hardcoded rather than derived from any session or selection. (Customer.Mobile's equivalent button correctly uses its own logged-in session's id, and separately picks an online provider or falls back to the first one.)

**Why:** The seed data always creates "John" as the first customer (id 1, verified against Plan 1's seeder), and the provider side of the demo button has no natural "which customer" context to select from — there's no customer-selection UI in Provider.Mobile, and adding one purely to make a demo-convenience button fully general would be scope the prototype doesn't need.

**Impact:** The button only works correctly against the seed data's default customer. If the seed data is ever regenerated with a different customer ordering, or a second customer is added and expected to be the demo's target, this button silently demos against the wrong (or a nonexistent) customer id rather than failing loudly.

**Prevention going forward:** Accepted recurrence — this is a demo-convenience shortcut, not a business rule, and is already called out in the Plan 3 self-review notes ("StartDemo hardcodes customer id 1 (John is seeded first...) for the Provider-side button"). No mechanism enforces it; if the seed order ever changes, this is the first place to check when the Provider-side Start Demo button stops working as expected.

**Revisit When:** If the seed data's customer ordering changes, or if Provider.Mobile ever needs a real customer-selection UI for any other reason (at which point the demo button should reuse that selection instead of the hardcoded id).

**Related:** [Compromise: rating auto-closes a Completed job single-sidedly](#2026-07-18--rating-a-completed-job-auto-closes-it-single-sidedly), [Gap: Typing/Seen hub relay has no automated test](#2026-07-18--typingseen-hub-relay-has-no-automated-test-verified-manually-only)

---

## Archive

> Entries moved here when the underlying condition no longer applies.
> Kept for historical context.

### 2026-07-18 — MAUI workload not installed; Customer.Mobile (Plan 2) cannot build yet

Archived 2026-07-18 — resolved. `dotnet workload install maui` was run at some point between this gap being logged and Plan 3 (Provider.Mobile) execution; `dotnet workload list` now reports `maui 10.0.20/10.0.100` installed, and both Customer.Mobile and Provider.Mobile build and run cleanly for `net10.0-maccatalyst` (0 warnings/0 errors each, verified directly). The original gap's fallback plan (retarget to net9/net8 if net10 manifests were unavailable) was never needed.

**Resolution:** No specific commit closes this — it was closed by an environment change (workload installation) outside the git history, observed as already-in-place at the start of Plan 3. The reasoning in [Compromise: targeted net10.0 instead of net8.0](#2026-07-18--targeted-net100-instead-of-the-designed-net80) stays active (net10-vs-net8 is still a real, accepted mismatch against the design docs) — only the *uncertainty about whether net10's MAUI workload would even be available* is retired.

**Original entry:** `dotnet workload list` reports zero installed workloads in this environment. Xcode is present (`/Applications/Xcode.app`), so iOS/Mac Catalyst codesigning tooling exists, but the MAUI SDK workload itself (`maui`, `maui-android`, `maui-ios`, `maui-maccatalyst`) has not been installed. This blocked all of Plan 2 (Customer.Mobile) from starting until closed.
