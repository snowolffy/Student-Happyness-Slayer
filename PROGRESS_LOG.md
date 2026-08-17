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
