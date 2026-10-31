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

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    public IvaoApiOptions Clone() => new()
    {
        BaseAddress = BaseAddress,
        Origin = Origin,
        ApiKey = ApiKey,
        Timeout = Timeout
    };
}
