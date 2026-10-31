namespace TheEye.Core.Api;

/// <summary>
/// Hands the API client a bearer token for the signed-in member. The recorded
/// tracks feed only answers an authenticated request, so the client asks for a
/// token before every call and gives the source one chance to renew it when
/// IVAO rejects the token.
/// </summary>
public interface IIvaoAccessTokenSource
{
    /// <summary>
    /// The current access token, or null when nobody is signed in.
    /// <paramref name="forceRefresh"/> is set when IVAO has just refused the
    /// previous token and a renewed one may still work.
    /// </summary>
    Task<string?> GetAccessTokenAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default);
}
