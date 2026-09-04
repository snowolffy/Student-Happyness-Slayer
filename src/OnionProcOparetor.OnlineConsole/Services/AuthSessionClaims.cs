using System.Security.Claims;

namespace OnionProcOparetor.OnlineConsole.Services;

/// <summary>
/// The Server's AuthToken (from POST /api/auth/login) has to survive from one HTTP request (the login
/// form post) to every later request/circuit for this browser, without re-prompting for a password. The
/// auth cookie is where that lives - these claims are what gets packed into it, and this class is the
/// one place that packs/unpacks them, used from three spots that would otherwise have to agree on claim
/// names by convention: the /login endpoint (packs), the Blazor circuit bootstrap in Routes.razor's code
/// (unpacks into the circuit's own scoped ApiClient), and the VNC WebSocket bridge (unpacks into a
/// throwaway ApiClient for that one request, since a raw WebSocket request isn't part of any Blazor
/// circuit and so never goes through the circuit bootstrap).
/// </summary>
public static class AuthSessionClaims
{
    private const string TokenClaim = "oc:token";
    private const string AdminIdClaim = "oc:adminId";
    private const string HasDefaultPasswordClaim = "oc:hasDefaultPassword";

    public static ClaimsPrincipal BuildPrincipal(string username, string token, int adminId, bool hasDefaultPassword)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Name, username),
            new Claim(TokenClaim, token),
            new Claim(AdminIdClaim, adminId.ToString()),
            new Claim(HasDefaultPasswordClaim, hasDefaultPassword ? "1" : "0")
        ], authenticationType: "OnlineConsoleCookie");

        return new ClaimsPrincipal(identity);
    }

    /// <summary>Restore a freshly-constructed (scoped) ApiClient's session state from the cookie's claims, without hitting /api/auth/login again.</summary>
    public static bool TryAttach(ApiClient apiClient, ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated is not true)
        {
            return false;
        }

        var token = user.FindFirst(TokenClaim)?.Value;
        var username = user.Identity.Name;
        var adminIdText = user.FindFirst(AdminIdClaim)?.Value;
        var hasDefaultPasswordText = user.FindFirst(HasDefaultPasswordClaim)?.Value;

        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(username) || !int.TryParse(adminIdText, out var adminId))
        {
            return false;
        }

        apiClient.AttachSession(token, username, adminId, hasDefaultPasswordText == "1");
        return true;
    }
}
