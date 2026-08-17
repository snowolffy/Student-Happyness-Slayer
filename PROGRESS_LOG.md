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

---

## Checkpoint 4 — 2026-08-17 21:15 — PART 4 COMPLETE (taskbar hiding + auto-recover)

**Built:** YES — 0 errors, warning count unchanged from baseline.

### Files created

- `src/OnionProcOparetor.Agent/Services/UserSessionLauncher.cs` — the Session 0 →
  user session process spawn (WTS + `CreateProcessAsUser` P/Invoke).
- `src/OnionProcOparetor.Agent/Services/AgentTrayRecoveryService.cs` — locates the
  tray exe, applies the cooldown, waits out the startup grace period.
- `src/OnionProcOparetor.AgentTray/Services/SingleInstanceGuard.cs`
- `src/OnionProcOparetor.AgentTray/Services/HideOnCloseWindowBehavior.cs`

### Files modified

- `src/OnionProcOparetor.Agent/Services/AgentTrayNotifier.cs` — recover-then-retry-once.
- `src/OnionProcOparetor.Agent/Program.cs` — registers the two new services.
- `src/OnionProcOparetor.Agent/appsettings.json` — optional `AgentTray:ExecutablePath`.
- `src/OnionProcOparetor.AgentTray/App.xaml.cs` — single-instance check, Exit
  confirmation, restore-hidden-window handling.
- `src/OnionProcOparetor.AgentTray/LoginWindow.xaml.cs`,
  `StatusWindow.xaml.cs`, `LogTerminalWindow.xaml.cs` — hide-on-close applied.

### Item 1 — hiding from the taskbar

As established in Checkpoint 0, the tray app itself was **already** invisible to
the taskbar (no `StartupUri`, no MainWindow). The real taskbar surfaces were the
three on-demand windows, all of which defaulted to `ShowInTaskbar = true`:
`LoginWindow`, `StatusWindow`, `LogTerminalWindow`. All three now call
`this.EnableHideOnClose()`, which sets `ShowInTaskbar = false` *and* converts the
close button to `e.Cancel = true; Hide();`.

Three consequences had to be handled, or the change would have introduced bugs:

1. **`LoginWindow` closes itself after a successful login** to hand over to
   `StatusWindow`. Under hide-on-close that `Close()` would be cancelled, leaving
   a hidden login window alive forever — and `OpenLoginWindow` would then find it
   before the StatusWindow. Added `ForceClose()` (mirrors the existing
   `LockScreenWindow.ForceClose()` pattern) for closes the *code* intends, and
   `OpenLoginWindow` now prefers `StatusWindow` explicitly.
2. **`LogTerminalWindow` stopped its 3 s refresh timer on `Closed`**, which now
   never fires. It would have polled the local API forever behind a hidden
   window. Rebound to `IsVisibleChanged`.
3. **`Activate()` does nothing on a hidden window**, so "Open Status..." would
   appear to do nothing after the user had closed the window once. Added
   `RestoreAndActivate` (Show → un-minimize → Activate). `StatusWindow`'s
   "OPEN LOG TERMINAL" button likewise now reuses the existing hidden window
   instead of stacking up new ones.

### Item 2 — "Exit Tray" is no longer a one-click mistake

Chose the **confirmation dialog** option (the task offered dialog *or* modifier
key). Reasoning: a hidden-behind-Shift menu item is undiscoverable for the
legitimate case — an admin who genuinely needs to close it — whereas a dialog
stops the accident *and* explains the consequence. The dialog spells out exactly
what stops working (lock/unlock, shutdown warnings, teacher messages), and
**defaults to "No"**, so Enter or Esc is the safe answer. The capability is not
removed, per the task's explicit instruction.

`ConfirmThenExit` also force-closes the guarded windows before `Shutdown()`.
`LockScreenWindow` and `ShutdownNotifyWindow` have their *own* `ForceClose()`
instance methods with a separate `_allowClose` flag — those are called explicitly
rather than letting them fall through to the extension method, which would not
set that flag and would leave them un-closable.

### Item 3 — auto-recover (the hardest piece; it works, and here is what it does)

Nothing existed to build on — confirmed in Checkpoint 0 that no session-spawn
code was anywhere in the Agent. Implemented the standard chain in
`UserSessionLauncher`:

`WTSGetActiveConsoleSessionId` → `WTSQueryUserToken` → `DuplicateTokenEx`
(to a **primary** token — the token from WTS is an impersonation token and cannot
create a process) → `CreateEnvironmentBlock` → `CreateProcessAsUser` with
`lpDesktop = "winsta0\default"` and `CREATE_UNICODE_ENVIRONMENT`.

`Process.Start()` would not work here at all: a service's child process lands in
Session 0, which has no desktop, so the tray would run invisibly with no icon and
no useful pipe. That is why the whole P/Invoke file exists.

Details worth knowing:

- **Session resolution has a fallback.** Console session first (the normal lab
  case); if it returns `0xFFFFFFFF` or has no logged-on user, it enumerates
  sessions via `WTSEnumerateSessions` for the first `WTSActive` one with a real
  user token. Covers RDP and fast-user-switching.
- **All handles are released** — user token, duplicated token, environment block,
  and the returned process/thread handles — in a `finally`. Closing the process
  handle does not kill the spawned process; not closing it leaks on every
  recovery.
- **Tray exe lookup order:** `AgentTray:ExecutablePath` from config (added to
  `appsettings.json`, blank by default) → `..\ClientTray\OnionProcOparetor.AgentTray.exe`
  (verified against `installer/OnionProcOparetor.iss`, which installs the Agent to
  `{app}\Client\` and the tray to `{app}\ClientTray\`) → same directory (dev layout).
- **Requires LocalSystem.** `WTSQueryUserToken` needs `SE_TCB_NAME`. The installer
  creates the service with `sc.exe` and no account, i.e. LocalSystem, so this
  holds in production. Running the Agent as a normal user during development will
  fail at that call — handled, logged with the specific reason, returns false.

### Checkpoint requirement: does killing AgentTray.exe recover on the next command?

Yes, by design, and this is the exact path: killing the tray from Task Manager
leaves no pipe server. The next command that needs the tray (lock, unlock,
broadcast, shutdown warning) fails its pipe connect, which triggers
`TryRecoverThenRetryAsync` → spawn into the active session → wait 2.5 s → retry
the send once. The same code path also covers "the tray was never running at
all", since both cases present identically as a failed connect. Every attempt is
logged, success and failure alike (item 4).

**This is reasoned from the implementation, not observed** — see the manual
verification list in the final summary. It cannot be exercised here: there is no
installed service, no second logged-on session, and the sandbox cannot
meaningfully test `WTSQueryUserToken`/`CreateProcessAsUser`.

### Checkpoint requirement: total retry timeout added per command

Worst case, when the tray is dead **and** the cooldown is not active:

| Step | Cost |
|---|---|
| First pipe connect timeout | 3.0 s |
| Locate exe + `CreateProcessAsUser` | ~0.2 s |
| Startup grace (tray opening its pipe) | 2.5 s |
| Retry pipe connect timeout | 3.0 s |
| **Total** | **≈ 8.7 s** |

When the 30 s cooldown *is* active, recovery is skipped entirely and the cost
stays at the original 3.0 s. Only one retry is attempted, deliberately: this runs
inside `CommandProcessor` with other commands queued behind it, and if a freshly
started tray still cannot be reached, the problem is not "the tray was closed"
and retrying again will not fix it. Power actions are unaffected in user-visible
terms — the countdown deadline is set *after* the pipe work completes, so the
student still gets the full warning period (Checkpoint 1).

### Checkpoint requirement: tray icon still works after the taskbar change

The `TaskbarIcon` and its context menu were not touched — the taskbar changes are
confined to the three on-demand `Window` objects, and the tray icon is a
`Hardcodet.Wpf.TaskbarNotification.TaskbarIcon`, not a window. Right-click menu
and double-click both still route to `OpenLoginWindow`, which now also restores a
hidden window. Needs a real click-through to confirm visually (see final summary).

### Unprompted decisions made in Part 4

- **Added a single-instance mutex to AgentTray.** Not requested, but auto-recover
  is unsafe without it: a failed pipe connect does **not** prove the tray is dead.
  `BroadcastPipeListener` serves one connection at a time and recreates the server
  after each message, so a connect landing in that window fails against a
  perfectly healthy tray — and we would then spawn a duplicate, giving two tray
  icons and two listeners competing for messages. `Local\`-scoped, not `Global\`,
  so fast-user-switching sessions each keep their own tray (they have separate
  desktops and genuinely each need one). The duplicate exits silently — it was
  started by the Agent, not the user, so a dialog would only confuse whoever is
  at the machine. If the mutex cannot be acquired for an unexpected reason it
  proceeds anyway: a duplicate tray is a far better failure than no tray.
- **30 s recovery cooldown.** Without it, a burst of commands arriving while the
  tray is down would each try to spawn a process.
- **2.5 s startup grace** before the retry — a cold WPF app needs roughly 1-2 s to
  reach `OnStartup` and open the pipe. Balances "long enough to work" against
  "does not stall command processing".
- **Recovery never throws.** Wrapped at both the launcher and notifier levels; the
  worst case is a logged error and the original fail-secure skip, preserving
  existing behaviour exactly.
- **`ForceClose` as an extension method** rather than a shared base class, since
  introducing one would mean editing the root element of every `.xaml` plus every
  code-behind, versus one line per constructor.
- **`AgentTray:ExecutablePath` config escape hatch** so a non-standard install
  layout can be fixed without a code change.

---

# FINAL SUMMARY

Run finished 2026-08-17 21:30 +0700. Branch `worktree-agent-a4b8e339d2fe9f48d`.

## Part outcomes

| Part | Outcome | Notes |
|---|---|---|
| 1 — Countdown + notify window | **Succeeded** | All 5 sub-items done. Drift eliminated on both clocks |
| 2 — Ack/tracking backend | **Succeeded** | Additive schema change; delivery semantics untouched |
| 3 — Report UI | **Succeeded** | Gate satisfied (Part 2 built + real acks in place) |
| 4 — Taskbar + auto-recover | **Succeeded** | Includes the session-0 spawn; see verification caveats below |

Nothing was skipped and nothing failed. The only mid-run setback was an empty EF
migration from `--no-build` (Checkpoint 2), corrected immediately.

## Final solution build

`dotnet build OnionProcOparetor.sln --no-incremental` from the repo root:
**Build succeeded — 0 errors, 23 warnings.** The warning count is identical to the
pre-change baseline and the warnings are the same pre-existing ones (NU1510
pruning, NU1903 SQLitePCLRaw advisory, CA1416 platform checks in
`Worker.cs`/`ClientIdentity.cs`, SYSLIB0057 in `PublisherChecker.cs`). **No new
warnings were introduced.**

## All files created (18)

**Agent**

- `src/OnionProcOparetor.Agent/Models/PowerActionPayload.cs`
- `src/OnionProcOparetor.Agent/Models/CommandExecutionResult.cs`
- `src/OnionProcOparetor.Agent/Services/CommandResultReporter.cs`
- `src/OnionProcOparetor.Agent/Services/UserSessionLauncher.cs`
- `src/OnionProcOparetor.Agent/Services/AgentTrayRecoveryService.cs`

**AgentTray**

- `src/OnionProcOparetor.AgentTray/Models/ShutdownNotifyDto.cs`
- `src/OnionProcOparetor.AgentTray/Controls/CountdownProgressBar.xaml` + `.xaml.cs`
- `src/OnionProcOparetor.AgentTray/ShutdownNotifyWindow.xaml` + `.xaml.cs`
- `src/OnionProcOparetor.AgentTray/Services/SingleInstanceGuard.cs`
- `src/OnionProcOparetor.AgentTray/Services/HideOnCloseWindowBehavior.cs`

**Console**

- `src/OnionProcOparetor.Console/Models/CommandResultDto.cs`
- `src/OnionProcOparetor.Console/Models/ActionReportModels.cs`
- `src/OnionProcOparetor.Console/ActionReportWindow.xaml` + `.xaml.cs`

**Server**

- `src/OnionProcOparetor.Server/Services/CommandResultTimeoutService.cs`
- `src/OnionProcOparetor.Server/Migrations/20260817115444_AddCommandExecutionTracking.cs` + `.Designer.cs`

**Repo root**

- `PROGRESS_LOG.md` (this file)

## All files modified (18)

- `src/OnionProcOparetor.Agent/Program.cs`
- `src/OnionProcOparetor.Agent/Models/AgentTrayPipeMessage.cs`
- `src/OnionProcOparetor.Agent/Services/AgentTrayNotifier.cs`
- `src/OnionProcOparetor.Agent/Services/CommandProcessor.cs`
- `src/OnionProcOparetor.Agent/Services/LabHubConnection.cs`
- `src/OnionProcOparetor.Agent/Services/ServerClient.cs`
- `src/OnionProcOparetor.Agent/appsettings.json`
- `src/OnionProcOparetor.AgentTray/App.xaml.cs`
- `src/OnionProcOparetor.AgentTray/LoginWindow.xaml.cs`
- `src/OnionProcOparetor.AgentTray/StatusWindow.xaml.cs`
- `src/OnionProcOparetor.AgentTray/LogTerminalWindow.xaml.cs`
- `src/OnionProcOparetor.AgentTray/Models/AgentTrayPipeMessage.cs`
- `src/OnionProcOparetor.AgentTray/Services/BroadcastPipeListener.cs`
- `src/OnionProcOparetor.Console/ActionPanelWindow.xaml` + `.xaml.cs`
- `src/OnionProcOparetor.Console/DashboardWindow.xaml.cs`
- `src/OnionProcOparetor.Console/Services/ApiClient.cs`
- `src/OnionProcOparetor.Console/Services/LabHubClient.cs`
- `src/OnionProcOparetor.Server/Program.cs`
- `src/OnionProcOparetor.Server/Data/AppDbContext.cs`
- `src/OnionProcOparetor.Server/Hubs/LabHub.cs`
- `src/OnionProcOparetor.Server/Models/ClientCommand.cs`
- `src/OnionProcOparetor.Server/Migrations/AppDbContextModelSnapshot.cs` (EF-generated)
- `src/OnionProcOparetor.Server/appsettings.json`

## Consolidated list of unprompted decisions

**Structural**

1. No shared class library created; `CountdownProgressBar` lives in AgentTray and
   `AgentTrayPipeMessage` stays hand-mirrored, matching the existing convention.
2. Execution status added as **new columns**, not new `ClientCommandStatus`
   members, so the poll/redelivery filter keeps its exact meaning.
3. Kept the existing `int` command id; no GUID introduced.
4. Circular DI (`CommandProcessor` ↔ `LabHubConnection`) broken with a settable
   delegate on `CommandResultReporter`.
5. `AgentTrayNotifier.Send*` changed from `Task` to `Task<bool>` — required for
   any real result to be knowable.
6. The timeout sweeper lives on the Server, not the Console.
7. `ActionPanelWindow` closes first and `DashboardWindow` opens the report, to
   satisfy "replaces, does not stack" without making the panel modeless.
8. The report window performs the sends itself, so it can open instantly.
9. It reuses `DashboardWindow`'s existing hub connection rather than opening a
   second one.
10. Status enums are transported as **strings**, not ints, so a future enum edit
    on one side cannot silently corrupt the other.
11. `ForceClose` implemented as an extension method rather than a base class.
12. **Added a single-instance mutex to AgentTray** (not requested) — auto-recover
    is genuinely unsafe without it.

**Behavioural**

13. Out-of-range countdown values clamp *and abort the click*; the teacher must
    confirm the corrected number rather than shutting down a room on a value they
    never typed. Non-numeric input aborts outright.
14. The shutdown notify window cannot be dismissed by the user, but is otherwise
    fully non-blocking (no focus steal, no input hook).
15. A repeat `ShowShutdownNotify` replaces the existing window; `ShowLockScreen`
    stays idempotent as before.
16. "Success" for Shutdown/Restart means **scheduled**, not powered off — inherent
    to the action. A power action whose tray warning failed still reports Success,
    with the failure noted in the message.
17. Unknown command types now report `Failed` instead of only logging.
18. Handler exceptions become `Failed` + message rather than escaping into a
    less-informative `TimedOut`.
19. Duplicate commands re-ack but do not re-report.
20. Report status never regresses; `SendFailed` is terminal.
21. Batch polling stops once all rows are terminal; the push subscription stays.
22. No separate simplified single-target view — one grid, one code path.
23. "Exit Tray" guarded by a **confirmation dialog defaulting to No** (chose this
    over the modifier-key option: discoverable for the legitimate case, still
    accident-proof). Capability retained, per instruction.
24. 30 s recovery cooldown; 2.5 s startup grace; exactly one retry.
25. `AgentTray:ExecutablePath` config escape hatch for non-standard layouts.

**Knock-on fixes required by the above** (would otherwise have been new bugs)

26. `LoginWindow` uses `ForceClose()` after login; `OpenLoginWindow` prefers
    `StatusWindow`.
27. `LogTerminalWindow`'s refresh timer rebound from `Closed` to
    `IsVisibleChanged`, or it would poll forever behind a hidden window.
28. `RestoreAndActivate` added, since `Activate()` is a no-op on a hidden window.
29. `StatusWindow` reuses its existing log-terminal window instead of stacking new
    hidden ones.
30. `ConfirmThenExit` force-closes `LockScreenWindow`/`ShutdownNotifyWindow` via
    their own `ForceClose()` methods, not the extension, so `_allowClose` is set.

## What a human MUST verify manually before real use

Everything below was reasoned from the code and compiles, but **none of it was
executed** — this environment has no installed service, no second logged-on
session, no lab machine, and no running Server/Agent pair.

**Highest risk — verify first**

1. **Session-0 → user-session spawn on a real Windows box.**
   `WTSQueryUserToken` / `DuplicateTokenEx` / `CreateProcessAsUser` behave
   differently under a real LocalSystem service than in any dev environment, and
   this code has never been run. Install the Agent as a service, log in as a
   student user, kill `OnionProcOparetor.AgentTray.exe` via Task Manager, then
   send a Lock command from the Console. Expect: the tray reappears and the lock
   screen shows, with `AgentTray auto-recover` lines in
   `%ProgramData%\OnionProcOparetor\Agent\logs\agent.log`. **Check the tray icon
   appears in the student's session, not nowhere** — a process spawned into
   Session 0 by mistake still "starts successfully" but is invisible and useless.
2. **The single-instance mutex actually prevents duplicate trays.** Trigger
   recovery while the tray *is* healthy and confirm no second icon appears.
3. **Shutdown countdown timing against the real power-off**, ideally at both 1 s
   and 120 s. Confirm the bar hits 0 essentially as the machine goes down, and
   that the window is placed correctly (it assumes the primary monitor's work
   area — **multi-monitor placement is explicitly not handled**, consistent with
   the existing single-monitor assumption, so check this if any lab machine has
   two screens).

**Functional**

4. Run the Server once and confirm the migration applies cleanly to an existing
   populated database. Expect a one-off burst of `TimedOut` warnings for
   pre-existing command rows (explained in Checkpoint 2) — not a bug.
5. The report window against a real batch: some machines online, some off, at
   least one with the tray killed, so all four terminal states appear.
6. **Close the report window while acks are still outstanding**, then let the
   remaining acks arrive. This is the crash path the unsubscribe logic guards;
   it must not throw.
7. Bulk responsiveness with a realistic 40-machine selection.
8. Tray icon right-click/double-click still opens Status after the taskbar
   change, and the window genuinely no longer appears on the taskbar.
9. "Exit Tray" → confirm the dialog appears, No is the default, and Yes actually
   exits (including while a lock screen or countdown window is open).
10. Countdown validation end to end: non-numeric, 0, 121, and a valid value.

**Worth a look**

11. Whether the 30 s default result timeout suits the real network — a slow lab
    switch may need more; it is config-only (`CommandTracking:ResultTimeoutSeconds`).
12. The installer publishes the new files automatically (no `.csproj` changes were
    made, so this is expected to be fine) — confirm `ClientTray` still lands next
    to `Client` so the auto-recover path resolution holds.
