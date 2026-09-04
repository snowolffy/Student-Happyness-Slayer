namespace OnionProcOparetor.OnlineConsole.Services;

public enum ToastKind { Success, Error, Info }

public record Toast(Guid Id, ToastKind Kind, string Title, string? Detail);

/// <summary>
/// One place for "tell the user what just happened".
///
/// Replaces the previous per-page pattern of assigning to a private `_status` string that got rendered
/// into a &lt;div class="status-text"&gt; near the top of the page. That pattern has a specific failure on
/// the device this app is mostly used from: by the time a teacher taps a button in a 40-row list, the
/// top of the page is far off-screen, so the confirmation ("ทำรายการแล้ว") and - much worse - the
/// failure message ("ทำรายการไม่สำเร็จ") were rendered somewhere nobody would ever look. A toast
/// appears next to the thumb, is announced by screen readers via the aria-live host, and expires by
/// itself so there's no stale "succeeded" text lingering from three actions ago.
///
/// Scoped (per Blazor circuit), like every other UI-state service here: a toast belongs to one browser
/// tab's session, and a singleton would broadcast one admin's messages into every other admin's screen.
/// </summary>
public class ToastService
{
    private readonly List<Toast> _toasts = new();

    public IReadOnlyList<Toast> Toasts => _toasts;

    public event Action? Changed;

    public void Success(string title, string? detail = null) => Show(ToastKind.Success, title, detail);
    public void Error(string title, string? detail = null) => Show(ToastKind.Error, title, detail);
    public void Info(string title, string? detail = null) => Show(ToastKind.Info, title, detail);

    /// <summary>Convenience for the very common "call the API, then report ok/failed" shape.</summary>
    public void Result(bool ok, string okTitle, string failTitle, string? failDetail = null)
    {
        if (ok) Success(okTitle); else Error(failTitle, failDetail);
    }

    public void Show(ToastKind kind, string title, string? detail = null)
    {
        var toast = new Toast(Guid.NewGuid(), kind, title, detail);
        _toasts.Add(toast);
        Changed?.Invoke();

        // Errors stay up longer than confirmations, and both stay long enough to actually be read:
        // a success is glanceable ("done"), an error usually carries a server message worth reading.
        var lifetime = kind == ToastKind.Error ? TimeSpan.FromSeconds(8) : TimeSpan.FromSeconds(3.5);

        // Fire-and-forget rather than a System.Threading.Timer: no IDisposable to own, and if the
        // circuit is gone by the time this resolves, Dismiss just mutates a list nobody is rendering.
        _ = Task.Delay(lifetime).ContinueWith(_ => Dismiss(toast.Id));
    }

    public void Dismiss(Guid id)
    {
        if (_toasts.RemoveAll(t => t.Id == id) > 0)
        {
            Changed?.Invoke();
        }
    }
}
