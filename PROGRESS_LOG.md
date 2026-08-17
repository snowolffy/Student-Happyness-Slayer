# PROGRESS_LOG — overnight autonomous run (4 parts)

Worktree: `.claude/worktrees/agent-a4b8e339d2fe9f48d`
Branch: `worktree-agent-a4b8e339d2fe9f48d`
Started: 2026-08-17 18:41 +0700

This file is the crash-recovery trail. Each checkpoint = one finished sub-item.

---

## Checkpoint 0 — 2026-08-17 18:41 — Baseline investigation

**Did:** Read every relevant file before touching anything. Ran a baseline
`dotnet build OnionProcOparetor.sln`.

**Baseline build:** SUCCEEDED (0 errors, 23 pre-existing warnings — NU1510 prune
warnings, NU1903 SQLitePCLRaw vulnerability, CA1416 platform warnings in
Agent/Worker.cs + ClientIdentity.cs, SYSLIB0057 in PublisherChecker.cs). These
warnings pre-date this run; I did not touch them.

### Ground-truth corrections found (task brief was partially stale)

The task brief said the pipe protocol already knows `StartVncSession`,
`StopVncSession`, `BlockTargetInput`, `UnblockTargetInput`. **It does not.**
On this branch (`main` @ e72ecab) the pipe protocol knows exactly three types:

- `Broadcast` (uses `Message` + `Title`)
- `ShowLockScreen`
- `HideLockScreen`

Verified in both `src/OnionProcOparetor.Agent/Services/AgentTrayNotifier.cs`
(three public Send* methods only) and
`src/OnionProcOparetor.AgentTray/Services/BroadcastPipeListener.cs`
(three cases in `HandleMessageLine`). No VNC / input-blocking code exists
anywhere in the solution. Everything else in the brief checked out.

Also: there is **no `OnionProcOparetor.UninstallHelper` project** in
`OnionProcOparetor.sln` on this branch — the solution has exactly 4 projects
(Agent, AgentTray, Console, Server). The uninstall password gate lives in the
Inno Setup script (`installer/OnionProcOparetor.iss`) instead.

### Part 1 pre-checks (requested by the task)

1. **Shutdown/Restart payload today:** `null`. `ActionPanelWindow.SendPowerActionAsync`
   calls `SendToTargetsAsync(commandType, null, commandType)` and
   `ApiClient.SendCommandAsync` serializes `null` → sends `payload = null`.
   So there is no existing payload shape to preserve — adding `DelaySeconds` is
   purely additive.
2. **Const delay:** confirmed — `CommandProcessor.PowerActionWarningDelaySeconds`
   = `10`, `private const int`, at
   `src/OnionProcOparetor.Agent/Services/CommandProcessor.cs:16`. The Console
   also hardcodes the number `10` in its confirm-dialog text
   (`ActionPanelWindow.xaml.cs:130`) — a second copy that must stay in sync.
3. **Pipe protocol types today:** see correction above (3 types).
4. **Topmost borderless reference:** `LockScreenWindow.xaml` — `WindowStyle="None"`,
   `WindowState="Maximized"`, `Topmost="True"`, `ResizeMode="NoResize"`,
   `ShowInTaskbar="False"`. Confirmed as the pattern to mirror (but the new
   window must NOT be maximized and must NOT install the keyboard hook).

### Part 1 item 4 pre-check — is there a shared class library?

**No.** The solution has 4 projects and **zero** shared/common class libraries.
Each project keeps its own hand-duplicated copy of the DTOs it needs — e.g.
`AgentTrayPipeMessage.cs` and `LogEntryDto.cs` each exist **twice**, once under
`src/OnionProcOparetor.Agent/Models/` and once under
`src/OnionProcOparetor.AgentTray/Models/`, with no project reference between
them. Also no `ProjectReference` element exists in any `.csproj`.

**Decision (unprompted):** follow the existing convention rather than fight it —
put `CountdownProgressBar` in AgentTray only (as the task allows), and hand-mirror
the two new envelope fields into both copies of `AgentTrayPipeMessage.cs`.
Introducing a shared library now would touch all 4 `.csproj` files and the
installer publish layout, which is far more invasive than the task warrants.

### Part 2 pre-checks (requested by the task)

1. **Does a command have a unique identifier?** Yes — `ClientCommand.Id`, an
   `int` DB identity. Server returns the whole `ClientCommand` (id included) from
   `POST /api/commands/{clientGuid}` via `Results.Created(...)`. **Console
   currently discards it** — `ApiClient.SendCommandAsync` returns only
   `response.IsSuccessStatusCode` as a `bool`. So the id already exists and just
   needs to be captured; no new GUID is warranted.
2. **What does the Agent send back after processing?** Only
   `ServerClient.AckCommandAsync(commandId)` → `POST /api/commands/{id}/ack` →
   sets `Status = Delivered` + `DeliveredAt`. This is a *delivery* ack fired
   unconditionally after `ProcessCommandAsync` returns — it is sent even when the
   command was a duplicate that was skipped entirely, and even if the handler
   swallowed an exception. It proves nothing about execution.
3. **Does the HTTP-poll fallback persist state?** Yes — same `ClientCommands`
   table. `GET /api/poll/{clientGuid}` returns rows where
   `Status == Pending`, so flipping `Status` off `Pending` is what stops
   redelivery. **This is load-bearing and must not change semantics.**
4. **Schema today:** `ClientCommands(Id, ClientGuid, CommandType, PayloadJson,
   CreatedAt, DeliveredAt, Status)`, index on `(ClientGuid, Status)`. EF Core +
   SQLite with 3 existing migrations, latest `20260722110840_AddClientCommand`.
   `db.Database.Migrate()` runs on Server startup (`Program.cs:81`).

### Part 4 pre-checks (requested by the task)

1. **Does AgentTray have a MainWindow at startup?** **No.** `App.xaml` has no
   `StartupUri` and sets `ShutdownMode="OnExplicitShutdown"`; `OnStartup` builds
   only a `TaskbarIcon`. So the tray app itself is already invisible to the
   taskbar — the brief's suspicion was right.
   **The actual taskbar-visible surfaces are the on-demand windows:**
   `LoginWindow`, `StatusWindow`, and `LogTerminalWindow` (opened from
   StatusWindow). None of them sets `ShowInTaskbar`, so all three default to
   `True`. `LockScreenWindow` already correctly sets `ShowInTaskbar="False"`.
   Those three are what Part 4 item 1 must fix.
2. **Context menu:** "Open Status..." → `OpenLoginWindow()`; separator;
   "Exit Tray" → `Shutdown()` directly with **no confirmation whatsoever**
   (`App.xaml.cs:48-50`). Confirmed.
3. **Launch at login:** `RegistryStartup.EnsureAutoStartEnabled()` writes HKCU
   `...\CurrentVersion\Run\OnionProcOparetorAgentTray` = `Environment.ProcessPath`,
   called by AgentTray on its own startup, idempotent. Confirmed.
4. **How does AgentTrayNotifier check for a listener?** It does not check — it
   just opens a `NamedPipeClientStream` with a 3 s connect timeout and catches
   everything, logging a warning. No retry, no spawn. Confirmed.
5. **Install layout (needed for the Part 4 spawn):** from
   `installer/OnionProcOparetor.iss` — Agent is installed to `{app}\Client\` and
   AgentTray to `{app}\ClientTray\`. So from the Agent's `AppContext.BaseDirectory`
   the tray exe is at `..\ClientTray\OnionProcOparetor.AgentTray.exe`.

**Assumptions/decisions made so far:** the shared-library decision above. Nothing
else yet.

**Built:** yes (baseline, untouched).

---

## Checkpoint 1 — 2026-08-17 19:05 — PART 1 COMPLETE (countdown + notify window)

**Built:** YES — `dotnet build OnionProcOparetor.sln` → 0 errors, 23 warnings
(same 23 as baseline; introduced none).

### Files created

- `src/OnionProcOparetor.Agent/Models/PowerActionPayload.cs` — the new
  `{ delaySeconds }` payload + `Clamp()` + the three shared constants
  (`DefaultDelaySeconds=10`, `MinDelaySeconds=1`, `MaxDelaySeconds=120`).
- `src/OnionProcOparetor.AgentTray/Models/ShutdownNotifyDto.cs` — listener →
  App.xaml.cs hand-off DTO (mirrors the existing `BroadcastMessageDto` pattern).
- `src/OnionProcOparetor.AgentTray/Controls/CountdownProgressBar.xaml` + `.xaml.cs`
  — reusable countdown control (`Start(totalSeconds, labelFormat)`, `Stop()`,
  `Completed` event) exactly as the task specified.
- `src/OnionProcOparetor.AgentTray/ShutdownNotifyWindow.xaml` + `.xaml.cs` —
  the new corner window.

### Files modified

- `src/OnionProcOparetor.Agent/Models/AgentTrayPipeMessage.cs` — added `Action`,
  `DelaySeconds`.
- `src/OnionProcOparetor.AgentTray/Models/AgentTrayPipeMessage.cs` — same two
  fields, hand-mirrored (no shared library — see Checkpoint 0).
- `src/OnionProcOparetor.Agent/Services/AgentTrayNotifier.cs` — added
  `SendShutdownNotifyAsync(isRestart, delaySeconds, ct)`.
- `src/OnionProcOparetor.Agent/Services/CommandProcessor.cs` — `HandlePowerActionAsync`
  now takes `payloadJson`, resolves+clamps the delay, sends `ShowShutdownNotify`
  instead of a plain `Broadcast`, and waits on a deadline instead of one
  `Task.Delay`. Removed the now-dead `PowerActionWarningDelaySeconds` const
  (moved to `PowerActionPayload.DefaultDelaySeconds`).
- `src/OnionProcOparetor.AgentTray/Services/BroadcastPipeListener.cs` — new
  `ShutdownNotifyRequested` event + `"ShowShutdownNotify"` case.
- `src/OnionProcOparetor.AgentTray/App.xaml.cs` — subscribes the new event, owns
  `_shutdownNotifyWindow`, closes it in `OnExit`.
- `src/OnionProcOparetor.Console/ActionPanelWindow.xaml` + `.xaml.cs` — the
  1-120 s delay input, its validation, and the new payload.

### Checkpoint requirement: countdown vs. real shutdown time — no drift

Two independent drift sources were found and both are now eliminated. This was
the substantive part of Part 1, so recording the reasoning in full:

1. **Agent-side drift.** The original code was
   `await Task.Delay(TimeSpan.FromSeconds(10))` — a single sleep. `Task.Delay`
   guarantees only "will not wake *early*", never "wakes *on time*". On a loaded
   lab machine (thread-pool starvation while the enforcement scan runs) that can
   overshoot by seconds. Replaced with an absolute deadline
   (`deadlineUtc = UtcNow + delay`) and a loop that re-reads the wall clock every
   <=250 ms, so error cannot accumulate — worst case is one 250 ms step.
2. **Tray-side drift.** The naive countdown (`_remaining--` on each
   `DispatcherTimer` tick) accumulates error on *every* delayed tick, and
   `DispatcherTimer` is explicitly best-effort — it yields to input and render
   priority. Over 120 s this is visibly wrong. `CountdownProgressBar` therefore
   also stores an absolute deadline at `Start()` and recomputes remaining time
   from `DateTime.UtcNow` every tick; a late tick just skips numbers rather than
   dragging the whole countdown late. Tick interval is 100 ms so the bar animates
   smoothly, but the displayed integer is derived, not decremented.

**Ordering, so the two clocks agree:** `HandlePowerActionAsync` `await`s the pipe
send to completion *first*, and only then does `DelayThenExitWindowsAsync`
compute `deadlineUtc = UtcNow + delay`. The Tray starts its own clock when it
receives the message, which is strictly *before* that line runs. Net effect: the
progress bar reaches 0 a few ms *before* `ExitWindowsEx` fires. That direction is
deliberate — the failure mode a user would notice is "counter sits at 0 while
nothing happens", and this ordering makes that impossible. It also means the 3 s
pipe-connect timeout (when the Tray is dead) is *not* subtracted from the user's
warning time; the machine still waits the full N seconds.

### Checkpoint requirement: DelaySeconds validated on both Console and Agent

Three layers total:

1. **Console, keystroke level** — `DelaySecondsBox_PreviewTextInput` rejects
   non-digits; `MaxLength="3"`.
2. **Console, on click** — `TryResolveDelaySeconds()` runs before the confirm
   dialog. Non-numeric/empty → MessageBox + focus the box + **abort, no command
   sent**. Numeric but outside 1-120 → clamp, write the clamped value back into
   the box, MessageBox explaining it, and **still abort this click**.
3. **Agent** — `ResolvePowerActionDelaySeconds()` re-clamps with
   `PowerActionPayload.Clamp()` regardless of what arrived, and logs a warning
   when it had to clamp. Null/malformed/unparseable payload → falls back to 10 s.

### Unprompted decisions made in Part 1

- **Out-of-range values clamp but still abort the click** rather than clamping
  and proceeding. The task said "clamp/warn if out of range". Clamping *and*
  firing would mean one click shuts down a room of machines on a number the
  teacher never typed. Requiring a second click after showing the corrected value
  costs one click and removes that whole class of accident. Non-numeric input
  aborts outright.
- **Validation runs before the confirm dialog**, so the confirm text quotes the
  real number ("will warn users 45 seconds in advance"). The old hardcoded "10"
  in that string — a second copy of the constant — is now gone.
- **`ShowShutdownNotify` extends the existing flat envelope** with two optional
  fields rather than adding a nested payload-JSON field. The envelope was already
  flat (`Message`/`Title` are equally type-specific), so this matches the
  existing design and needs no second deserialization step.
- **The notify window cannot be closed by the user** (`OnClosing` → `e.Cancel`,
  same guard as `LockScreenWindow`), auto-closing only when the countdown hits 0.
  Rationale: a closable warning gets dismissed reflexively and then the machine
  dies "without warning" from the user's point of view. It is deliberately
  non-blocking in every other way — small, bottom-right, `ShowActivated="False"`
  so it does not steal focus from whatever the student is saving, and no keyboard
  hook. That is the whole point of a countdown.
- **A repeat `ShowShutdownNotify` replaces the existing window** instead of being
  ignored (unlike `ShowLockScreen`, which is idempotent). A second command
  carries a different deadline, so keeping the old window would display a time
  that is simply wrong.
- **Bottom-right of `SystemParameters.WorkArea`**, primary monitor only —
  consistent with the existing documented single-monitor assumption in
  `LockScreenWindow`. Using `WorkArea` (not screen bounds) keeps it clear of the
  taskbar wherever the user has docked it.
- **`CountdownProgressBar` draws its own bar** (two nested `Border`s) instead of
  using `ProgressBar`. Some OEM Windows themes override `ProgressBar`'s fill
  colour outright, which would silently break the red urgency cue on exactly the
  school machines this ships to.
- **Backward compatible on purpose:** a `Shutdown`/`Restart` command with a null
  payload (an older Console, or a row already sitting in `ClientCommands` from
  before this deploy) still behaves exactly as before at 10 s.

---

## Checkpoint 2 — 2026-08-17 19:45 — PART 2 COMPLETE (command ack/tracking backend)

**Built:** YES — 0 errors, 23 warnings (unchanged from baseline).
**Scope respected:** backend infrastructure only. No report UI in this part.

### Chosen approach — and why it is the least invasive one

**Kept the existing `int` command id.** No GUID introduced. `ClientCommand.Id`
already exists, is already returned by `POST /api/commands/{clientGuid}`, and is
already what the Agent dedups and acks with. The only gap was that
`ApiClient.SendCommandAsync` threw the response body away — so the change is
"stop discarding the id", not "invent an identifier".

**Added execution status as new columns rather than extending
`ClientCommandStatus`.** This was the key call. `GET /api/poll/{clientGuid}`
selects `Status == Pending` — that enum *is* the redelivery switch. Overloading
it with execution outcomes would silently entangle "did the action work" with
"should we send this again", and any future status added for reporting would risk
changing redelivery behaviour. So delivery and execution are now two independent
axes on the same row:

- `Status` / `DeliveredAt` — delivery. **Completely untouched semantics.**
- `ExecutionStatus` / `ExecutionMessage` / `ExecutedAt` — execution result. New.

Not one line of the existing poll or ack logic changed as a result.

### Schema changes (migration `20260817115444_AddCommandExecutionTracking`)

Additive only — 4 new nullable-or-defaulted columns on `ClientCommands`, 2 new
indexes, no column altered or dropped:

| Column | Type | Notes |
|---|---|---|
| `BatchId` | TEXT null | groups one Console click across N machines |
| `ExecutionStatus` | INTEGER not null, default 0 | `CommandExecutionStatus`; 0 = Pending |
| `ExecutionMessage` | TEXT null | error detail / note |
| `ExecutedAt` | TEXT null | when the real result arrived |

Indexes added: `IX_ClientCommands_BatchId` (the report window polls by batch) and
`IX_ClientCommands_ExecutionStatus_CreatedAt` (the timeout sweeper's query).
`db.Database.Migrate()` already runs at Server startup, so it self-applies.

*Note on generating it:* the first `dotnet ef migrations add --no-build` produced
an **empty** migration because it read a stale assembly. Caught it, deleted both
files (the model snapshot was still untouched — verified against git), and
regenerated with a real build. Worth knowing if this needs repeating.

### Full status enum list (checkpoint requirement)

**Delivery — `ClientCommandStatus`** (pre-existing, unchanged, values pinned
because SQLite stores them as ints):
`Pending = 0`, `Delivered = 1`, `Failed = 2`.

**Execution — `CommandExecutionStatus`** (new):
- `Pending = 0` — no result yet, still inside the timeout window.
- `Succeeded = 1` — the Agent confirmed the action worked.
- `Failed = 2` — the Agent confirmed it did *not* work; `ExecutionMessage` says why.
- `TimedOut = 3` — nothing came back in time. **Deliberately distinct from
  Failed**: Failed means the machine tried and could not; TimedOut means we have
  no idea what happened (powered off? network? Agent dead?). For a teacher
  standing in the room those call for completely different responses.

### How the result travels back (SignalR + HTTP fallback, matching the existing pattern)

- **SignalR:** new hub method `LabHub.ReportCommandResult(commandId, success, message)`.
- **HTTP fallback:** new `POST /api/commands/{commandId}/result`, unauthenticated
  to match the existing `/ack` and `/poll` endpoints.
- Both funnel into one `CommandResultRecorder.RecordAsync` — the same
  "one entry point for both transports" discipline `ProcessPendingCommandAsync`
  already uses Agent-side.
- `RecordAsync` is idempotent (a row that already has a real result is not
  overwritten), with one deliberate exception: a **late real result may overwrite
  `TimedOut`**, because genuine data from the machine beats our guess.

### How Console gets status in real time (checkpoint requirement)

Three layers, deliberately overlapping:

1. **Push** — Server broadcasts `CommandResultChanged` to the existing `"console"`
   SignalR group on every result *and* every timeout. `LabHubClient` exposes it
   as a new `CommandResultChanged` event. No new connection or group needed.
2. **Pull** — `GET /api/commands/batch/{batchId}` returns every command in a
   batch with current status. Used to seed the report window (results can land
   before the window subscribes) and as a poll fallback if the hub drops. This
   mirrors DashboardWindow, which already polls alongside SignalR.
3. **Server-side timeout sweeper** — `CommandResultTimeoutService`, a
   `BackgroundService` on a 5 s `PeriodicTimer`, flips still-`Pending` commands
   older than the configured timeout to `TimedOut` and pushes that too.

**Timeout is configurable:** `CommandTracking:ResultTimeoutSeconds` in
`appsettings.json`, default **30 s**, guarded against nonsense values (<= 0 falls
back to the default with a warning — otherwise every command would time out the
instant it was created).

### Files created (Part 2)

- `src/OnionProcOparetor.Server/Services/CommandResultTimeoutService.cs`
- `src/OnionProcOparetor.Server/Migrations/20260817115444_AddCommandExecutionTracking.cs` (+ `.Designer.cs`)
- `src/OnionProcOparetor.Agent/Models/CommandExecutionResult.cs`
- `src/OnionProcOparetor.Agent/Services/CommandResultReporter.cs`
- `src/OnionProcOparetor.Console/Models/CommandResultDto.cs`

### Files modified (Part 2)

- `src/OnionProcOparetor.Server/Models/ClientCommand.cs` — 4 new columns, new
  `CommandExecutionStatus` enum.
- `src/OnionProcOparetor.Server/Data/AppDbContext.cs` — 2 new indexes.
- `src/OnionProcOparetor.Server/Hubs/LabHub.cs` — `ReportCommandResult` hub
  method, `CommandResultRecorder`, `CommandResultDto`, `BroadcastCommandResult`.
- `src/OnionProcOparetor.Server/Program.cs` — accepts `BatchId` on command
  create; new `/result` and `/batch/{batchId}` endpoints; registers the timeout
  service; new `CommandResultRequest` record.
- `src/OnionProcOparetor.Server/Migrations/AppDbContextModelSnapshot.cs` (EF-generated)
- `src/OnionProcOparetor.Server/appsettings.json` — `CommandTracking` section.
- `src/OnionProcOparetor.Agent/Services/AgentTrayNotifier.cs` — all four `Send*`
  methods now return `Task<bool>` instead of `Task`.
- `src/OnionProcOparetor.Agent/Services/CommandProcessor.cs` — every handler now
  returns `CommandExecutionResult`; results reported after the ack.
- `src/OnionProcOparetor.Agent/Services/ServerClient.cs` — `ReportCommandResultAsync`.
- `src/OnionProcOparetor.Agent/Services/LabHubConnection.cs` — registers the
  SignalR sender into the reporter.
- `src/OnionProcOparetor.Agent/Program.cs` — registers `CommandResultReporter`.
- `src/OnionProcOparetor.Console/Services/ApiClient.cs` — `SendCommandTrackedAsync`,
  `GetBatchStatusAsync`, `CreatedCommandDto`.
- `src/OnionProcOparetor.Console/Services/LabHubClient.cs` — `CommandResultChanged`.

### Unprompted decisions made in Part 2

- **Execution result as new columns, not an extended `ClientCommandStatus`** —
  full reasoning above. This is the central decision of Part 2.
- **Circular DI broken with a settable delegate.** `LabHubConnection` already
  injects `CommandProcessor`; having `CommandProcessor` inject `LabHubConnection`
  back to send results would make the container throw at resolve time. Instead
  `CommandResultReporter` (depends only on `ServerClient`) exposes a
  `SignalRSender` property that `LabHubConnection` plugs itself into during
  `StartAsync`. One seam, at the one place the cycle would otherwise close.
- **`AgentTrayNotifier.Send*` changed from `Task` to `Task<bool>`.** Unavoidable:
  the methods swallowed all failures and returned void, so nothing downstream
  could ever know whether the action actually happened. Still fail-secure — it
  returns `false` rather than throwing, so no caller's control flow changes.
- **"Success" for Shutdown/Restart means "scheduled", not "powered off".**
  Reporting an actual power-off is impossible by construction — the process
  reporting it dies with the machine. The `ExecutionMessage` states the scheduled
  delay explicitly so the teacher knows what was promised, and the Dashboard's
  existing offline indicator is the real confirmation the machine went down.
  **This is the one action whose Success has a weaker meaning than the others,
  and it is inherent to the action, not a shortcut.**
- **A power action whose tray warning failed still reports Success, with a
  message saying the warning could not be shown.** The shutdown genuinely is
  scheduled and genuinely will happen (fail-secure — the teacher's command must
  not be cancelled just because nobody is logged in), so reporting Failed would
  be a lie. But "nobody saw a warning" matters a lot when someone *is* at the
  machine, so it is surfaced in the message rather than hidden.
- **Unknown command types now report Failed** instead of only logging a warning.
  A Console newer than the Agent should show up in the report, not vanish.
- **Duplicate commands re-ack but do not re-report.** The dedup path already
  proved the real result was reported on the first pass, and the recorder is
  idempotent anyway.
- **Every handler exception is converted to `Failed` + the exception message**
  (`ExecuteAndCaptureResultAsync`). Letting it escape would produce a `TimedOut`
  — the strictly less informative outcome — when we already know exactly what
  went wrong.
- **The timeout sweeper lives on the Server, not the Console.** The teacher may
  close the report window or the whole Console; those commands still need a
  terminal state, and two Consoles open at once must not disagree about what
  timed out.
- **Known one-time effect on first deploy:** existing `ClientCommands` rows get
  `ExecutionStatus = 0 (Pending)` from the migration default, so the sweeper
  marks them all `TimedOut` on first startup, with one warning log line each.
  This is semantically correct (their real outcome is genuinely unknown) but
  expect a short burst of warnings in `server.log` the first time. Not treated as
  a bug; noted so nobody chases it.

---

## Checkpoint 3 — 2026-08-17 20:20 — PART 3 COMPLETE (ActionReportWindow)

**Gate check:** Part 3 was permitted to proceed — Part 2 built successfully, and
the identifier (`CommandId`), the real execution ack, and the status tracking are
all in place and recorded in Checkpoint 2. Gate satisfied, so Part 3 was done.

**Built:** YES — 0 errors.

### Files created

- `src/OnionProcOparetor.Console/Models/ActionReportModels.cs` —
  `ActionReportStatus`, `ActionReportRow` (INotifyPropertyChanged),
  `ActionReportRequest`.
- `src/OnionProcOparetor.Console/ActionReportWindow.xaml` + `.xaml.cs`.

### Files modified

- `src/OnionProcOparetor.Console/ActionPanelWindow.xaml.cs` — added
  `PendingReport`, `OpenReportFor(...)`; Toggle/Lock/Unlock/Shutdown/Restart now
  route through the report instead of updating `StatusText` inline.
- `src/OnionProcOparetor.Console/DashboardWindow.xaml.cs` — new
  `RunActionPanelAsync` shared by both entry points; opens the report window.

### Checkpoint requirement: how ActionPanelWindow triggers ActionReportWindow

`ActionPanelWindow` is opened with **`ShowDialog()` (modal)** from two places in
`DashboardWindow`. A modal dialog opening another window before it closes gives
exactly the stacked-windows result the task forbids. So the panel never opens the
report itself. Instead:

1. Teacher clicks an action → the panel builds an `ActionReportRequest` (label,
   target rows, batch id, and a `SendAsync` delegate that knows how to dispatch
   to one machine), assigns it to the public `PendingReport` property, sets
   `DialogResult`, and calls `Close()`.
2. Control returns to `DashboardWindow.RunActionPanelAsync`, which — now that the
   panel is fully closed — checks `panel.PendingReport` and opens
   `ActionReportWindow` non-modally.

**The report window is what actually sends the commands, not the panel.** That is
deliberate: dispatching 40 HTTP POSTs before opening the window would freeze the
UI for seconds with no feedback. This way the window appears instantly with all
targets listed as "กำลังส่ง" and fills in as each send completes. Sends run
through a `SemaphoreSlim` capped at 8 concurrent.

### Checkpoint requirement: closing mid-flight must not crash on late acks

This is the real hazard here, because `LabHubClient` belongs to `DashboardWindow`
and therefore **outlives** the report window. Three guards, all needed:

1. `Closed` → unsubscribes `CommandResultChanged`. Without this the dead window's
   handler keeps firing for the rest of the session.
2. An `_isClosed` flag set first thing in the `Closed` handler. Unsubscribing
   alone is not sufficient — callbacks already queued via `Dispatcher.BeginInvoke`
   still run after unsubscribe, and would touch a closed window's Dispatcher.
   Both the SignalR callback and the queued lambda re-check the flag.
3. The `DispatcherTimer` poll is stopped, and the in-flight send loop checks
   `_isClosed` before touching any row.

`Dispatcher.BeginInvoke` is used rather than `Invoke` so the SignalR receive loop
is never blocked waiting on the UI thread — with 40 machines replying at once,
blocking per-message would stall the whole hub connection.

### Checkpoint requirement: which actions are handled differently, and why

| Action | Handling | Why |
|---|---|---|
| Shutdown, Restart, LockWorkstation, UnlockWorkstation | **Tracked** report (batch id, waits for real per-client acks) | genuine `ClientCommand`s with real execution results |
| Toggle Enforcement | **Immediate** report (batch id `null`, terminal on HTTP response) | `POST /api/clients/{id}/toggle` is a server-side DB write. There is no client-side command and no per-client ack — the Agent notices at its next poll. Waiting for an ack that structurally does not exist would leave every row Pending until it timed out |
| Settings, Delete | **No report window** — unchanged | Single-client-only, and both already have their own modal confirmation/dialog flow. Neither is a per-client fan-out, so a per-machine result table for one row would add a window without adding information. `Delete` also closes the panel with `DialogResult = true` as its own completion signal |

So the report is the central pattern for every fan-out action, with Toggle using
the immediate variant and the two single-client management actions staying as
they were.

### Checkpoint requirement: 40 machines without UI lag

`DataGrid` with `EnableRowVirtualization`, `EnableColumnVirtualization`,
`VirtualizingPanel.IsVirtualizing="True"`,
`VirtualizationMode="Recycling"`, and `ScrollViewer.CanContentScroll="True"` —
explicitly set rather than relying on defaults, so a later edit cannot silently
regress it. No plain `StackPanel`/`ItemsControl` anywhere in the list.

Critically, rows are an `ObservableCollection<ActionReportRow>` where
`ActionReportRow` implements `INotifyPropertyChanged`. Individual cells update in
place as results stream in. The alternative — reassigning `ItemsSource` on each
result, which is what `DashboardWindow` does for its grids — would rebuild the
whole visual tree per arriving ack and visibly stutter with 40 near-simultaneous
replies.

### Unprompted decisions made in Part 3

- **Panel closes first; Dashboard opens the report.** The only way to satisfy
  "replaces, does not stack" given the existing modal `ShowDialog()` call sites,
  without converting the panel to modeless (which would change how both existing
  entry points behave).
- **The report window owns the sending**, via the `SendAsync` delegate on the
  request. Keeps the window responsive from the first frame.
- **Reuses `DashboardWindow`'s existing `LabHubClient`** instead of opening a
  second hub connection — it has already joined the `"console"` group. The
  parameter is nullable, and with `null` the window still works correctly on
  polling alone.
- **Two extra UI-only statuses, `Sending` and `SendFailed`,** which have no
  backend equivalent because they describe failures that happen before a command
  row exists. `SendFailed` is terminal and is never overwritten by later results —
  the distinction between "could not send" and "sent but no reply" is exactly
  what the teacher needs to tell "my Console/network is broken" from "that
  machine is broken".
- **Status never regresses.** A `Pending` poll result cannot overwrite a terminal
  status already delivered by SignalR — the two channels race by design.
- **Polling stops once every row is terminal**, while the SignalR subscription
  stays live. Avoids hammering the Server if a teacher leaves the window open.
- **No single-target special-case window.** The task allows a simpler view for
  one machine; the same grid with one row already reads as simple, and the header
  switches to naming the machine directly. One code path, no divergence, and the
  ack status is shown identically as required.
- **Failure detail is surfaced three ways** — a `รายละเอียด` column, a row-level
  tooltip on hover, and a detail panel on selection — because error text from the
  Agent is often longer than a column can show.
