namespace CommunitySos.Application.Common.Exceptions;

// Use-case level failures. The API maps each type to an HTTP status (see GlobalExceptionHandler);
// Code is a stable machine-readable identifier the mobile app can localise.

/// <summary>Base type so the API can map all application failures uniformly.</summary>
public abstract class AppException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;

    /// <summary>Extra machine-readable fields copied into the problem response (e.g. the id of a conflicting record).</summary>
    public IReadOnlyDictionary<string, object?>? Details { get; init; }
}

/// <summary>404 - the resource does not exist or the caller may not know it exists.</summary>
public sealed class NotFoundException(string code, string message) : AppException(code, message)
{
    public static NotFoundException For(string entity) =>
        new($"{entity.ToLowerInvariant()}.not_found", $"{entity} was not found.");
}

/// <summary>403 - authenticated, but not allowed to perform this action on this resource.</summary>
public sealed class ForbiddenException(string code, string message) : AppException(code, message);

/// <summary>401 - credentials (Firebase ID token, refresh token) are missing, invalid or expired.</summary>
public sealed class AuthenticationFailedException(string code, string message) : AppException(code, message);

/// <summary>422 - the request is well-formed but a business precondition is not met (e.g. invitation required).</summary>
public sealed class UnprocessableException(string code, string message) : AppException(code, message);

/// <summary>409 - the request conflicts with current state (duplicate, stale version, already processed).</summary>
public sealed class ConflictException(string code, string message) : AppException(code, message);
