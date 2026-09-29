namespace ProdvisorAi.Demo.Services;

/// <summary>
/// A failure that is shown to the user. <see cref="Kind"/> is a stable code the UI translates
/// (unauthorized, quota, rate_limit, network, timeout, refusal, incomplete, ...).
/// </summary>
public class AiServiceException(string kind, string message, int? httpStatus = null) : Exception(message)
{
    public string Kind { get; } = kind;
    public int? HttpStatus { get; } = httpStatus;
}
