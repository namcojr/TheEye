namespace TheEye.Core.Api;

public sealed class IvaoApiOptions
{
    public const string DefaultBaseAddress = "https://api.ivao.aero";
    public const string DefaultOrigin = "https://webeye.ivao.aero";

    public string BaseAddress { get; set; } = DefaultBaseAddress;

    public string Origin { get; set; } = DefaultOrigin;

    /// <summary>
    /// The IVAO sign-in of the member. When it yields a token the client sends
    /// it as <c>Authorization: Bearer</c>, the credential WebEye uses for
    /// authenticated feeds.
    /// </summary>
    public IIvaoAccessTokenSource? AccessTokenSource { get; set; }

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    public IvaoApiOptions Clone() => new()
    {
        BaseAddress = BaseAddress,
        Origin = Origin,
        AccessTokenSource = AccessTokenSource,
        Timeout = Timeout
    };
}
