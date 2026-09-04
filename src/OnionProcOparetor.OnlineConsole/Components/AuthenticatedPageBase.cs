using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using OnionProcOparetor.OnlineConsole.Services;

namespace OnionProcOparetor.OnlineConsole.Components;

/// <summary>
/// Base for any page that calls ApiClient directly from its own OnInitializedAsync - i.e. loads data on
/// first render, before the user has clicked anything.
///
/// Restoring the login token onto the circuit's ApiClient from the auth cookie's claims (see
/// AuthSessionClaims) has to happen deterministically before that first API call. Putting the restore in
/// a shared ancestor layout was tried first and looked correct, but turned out to be a race rather than
/// a guarantee - discovered live, the confusing way (worked during the initial prerender, then 401'd and
/// crashed the circuit on the very next render): Blazor renders a component's children as soon as its
/// OnInitializedAsync hits its *first* await, not after the whole method completes. So a child page's own
/// OnInitializedAsync (and the HTTP call inside it) could start, and even finish, before the parent
/// layout's async token-restore was done - a race that happened to resolve one way during prerendering
/// and the other way once the real interactive circuit took over.
///
/// Doing the restore as the first statement of the PAGE's own OnInitializedAsync removes the race
/// entirely: everything after it runs strictly later in the same method, on the same component, no
/// parent/child ordering ambiguity involved.
///
/// Not needed by modals/dialogs - those are only ever opened by a click, long after the circuit (and its
/// one shared, scoped ApiClient - see Program.cs) is already fully attached.
/// </summary>
public abstract class AuthenticatedPageBase : ComponentBase
{
    [Inject] protected ApiClient Api { get; set; } = null!;
    [CascadingParameter] private Task<AuthenticationState>? AuthStateTask { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (AuthStateTask is not null)
        {
            var state = await AuthStateTask;
            AuthSessionClaims.TryAttach(Api, state.User);
        }
    }
}
