using System.Text.Json;
using System.Text.Json.Serialization;

namespace TheEye.Core.Api;

/// <summary>
/// Deserialization settings shared by the live client and the tests. IVAO
/// occasionally sends numbers as strings and adds new fields, so unknown
/// members are ignored and trailing commas are tolerated.
/// </summary>
public static class IvaoJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        return options;
    }
}

/// <summary>
/// Thrown when the IVAO API responds with a non-success status code. The body
/// is kept (truncated) because IVAO includes useful validation messages there.
/// </summary>
public sealed class IvaoApiException : Exception
{
    public IvaoApiException(int statusCode, string? responseBody, string message)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public int StatusCode { get; }

    public string? ResponseBody { get; }

    public bool IsUnauthorized => StatusCode is 401 or 403;
}
