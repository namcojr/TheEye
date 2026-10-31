namespace TheEye.Core.Api;

public sealed class IvaoApiOptions
{
    public const string DefaultBaseAddress = "https://api.ivao.aero";
    public const string DefaultOrigin = "https://webeye.ivao.aero";

    public string BaseAddress { get; set; } = DefaultBaseAddress;

    public string Origin { get; set; } = DefaultOrigin;

    /// <summary>
    /// IVAO consumer key. The public summary endpoints work without it; full
    /// pilot/ATC detail and filed flight-plan endpoints require it.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// The IVAO sign-in of the member. When it yields a token the client sends
    /// it as <c>Authorization: Bearer</c> (the credential WebEye uses for the
    /// recorded tracks) and leaves the consumer key alone.
    /// </summary>
    public IIvaoAccessTokenSource? AccessTokenSource { get; set; }

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    public IvaoApiOptions Clone() => new()
    {
        BaseAddress = BaseAddress,
        Origin = Origin,
        ApiKey = ApiKey,
        AccessTokenSource = AccessTokenSource,
        Timeout = Timeout
    };
}
