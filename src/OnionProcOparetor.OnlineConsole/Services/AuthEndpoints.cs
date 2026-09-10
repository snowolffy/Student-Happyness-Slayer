using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace OnionProcOparetor.OnlineConsole.Services;

/// <summary>
/// Plain (non-Blazor) minimal-API endpoints for sign-in/out/change-password. These have to be regular
/// request/response endpoints rather than interactive Blazor component code: HttpContext.SignInAsync
/// writes the Set-Cookie response header, which is impossible once a Blazor Server circuit's response
/// has already started streaming over its SignalR connection ("headers are read-only" at that point).
/// This is the same split the official ASP.NET Core Identity + Blazor template uses for its Account
/// pages - GET renders a static (non-interactive) Razor component with a plain HTML &lt;form&gt;, POST is
/// handled here.
///
/// These live at /auth/... rather than reusing the page's own path (e.g. POST /login instead of
/// POST /auth/login) - discovered the hard way (AmbiguousMatchException at runtime): a Razor component
/// mapped via MapRazorComponents has no HTTP-method restriction on its route by default, so it matches
/// POST too, and a MapPost registered at that same literal path then collides with it. Distinct paths
/// sidestep that entirely instead of fighting the component's routing metadata.
///
/// Each handler validates the antiforgery token itself (rather than relying on ambient middleware
/// behavior that differs subtly across templates/versions) - these are authenticated, state-changing
/// POSTs reachable by a plain cookie, exactly the shape CSRF protection exists for.
/// </summary>
public static class AuthEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/auth/login", async (HttpContext context, ApiClient apiClient, IAntiforgery antiforgery) =>
        {
            if (!await TryValidateAntiforgeryAsync(context, antiforgery))
            {
                return Results.Redirect("/login?error=" + Uri.EscapeDataString("แบบฟอร์มหมดอายุ กรุณาลองใหม่"));
            }

            var form = await context.Request.ReadFormAsync();
            var username = form["username"].ToString();
            var password = form["password"].ToString();

            var (success, message) = await apiClient.LoginAsync(username, password);
            if (!success)
            {
                return Results.Redirect($"/login?error={Uri.EscapeDataString(message)}");
            }

            var principal = AuthSessionClaims.BuildPrincipal(username, apiClient.Token!, apiClient.AdminId, apiClient.HasDefaultPassword);
            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(12) // matches Server's AuthToken lifetime (Program.cs: AddHours(12))
            });

            return Results.Redirect(apiClient.HasDefaultPassword ? "/change-password" : "/");
        });

        app.MapPost("/auth/logout", async (HttpContext context, IAntiforgery antiforgery) =>
        {
            if (!await TryValidateAntiforgeryAsync(context, antiforgery))
            {
                return Results.Redirect("/");
            }

            // Only ends the OnlineConsole web session (this cookie). Deliberately does nothing about the
            // viewer's OS-level Tailscale connection - there's nothing reachable from a browser tab that
            // could disconnect it, and that's exactly the behavior the user asked for: logging out here
            // must not touch Tailscale, which stays connected until the person disconnects it themselves.
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/login");
        }).RequireAuthorization();

        app.MapPost("/auth/change-password", async (HttpContext context, ApiClient apiClient, IAntiforgery antiforgery) =>
        {
            if (!await TryValidateAntiforgeryAsync(context, antiforgery))
            {
                return Results.Redirect("/change-password?error=" + Uri.EscapeDataString("แบบฟอร์มหมดอายุ กรุณาลองใหม่"));
            }

            if (!AuthSessionClaims.TryAttach(apiClient, context.User))
            {
                return Results.Redirect("/login");
            }

            var form = await context.Request.ReadFormAsync();
            var newPassword = form["newPassword"].ToString();
            var confirmPassword = form["confirmPassword"].ToString();

            if (newPassword.Length < 8)
            {
                return Results.Redirect("/change-password?error=" + Uri.EscapeDataString("Password ต้องยาวอย่างน้อย 8 ตัวอักษร"));
            }
            if (newPassword != confirmPassword)
            {
                return Results.Redirect("/change-password?error=" + Uri.EscapeDataString("Password ที่กรอกไม่ตรงกัน"));
            }

            var (success, message) = await apiClient.ChangePasswordAsync(newPassword);
            if (!success)
            {
                return Results.Redirect("/change-password?error=" + Uri.EscapeDataString(message));
            }

            // Re-issue the cookie so its hasDefaultPassword claim flips to false immediately - otherwise
            // the route guard in MainLayout would keep redirecting back to /change-password forever using
            // the stale claim from the old cookie.
            var principal = AuthSessionClaims.BuildPrincipal(apiClient.Username!, apiClient.Token!, apiClient.AdminId, hasDefaultPassword: false);
            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(12)
            });

            return Results.Redirect("/");
        }).RequireAuthorization();
    }

    private static async Task<bool> TryValidateAntiforgeryAsync(HttpContext context, IAntiforgery antiforgery)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
    }
}
