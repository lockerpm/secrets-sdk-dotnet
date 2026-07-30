namespace Locker;

/// <summary>Base class for safe Locker SDK failures.</summary>
public class LockerError : Exception
{
    public LockerError(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }

    internal LockerError(
        string message,
        int? code,
        string? requestId,
        string? kind,
        bool? retryable,
        int? cliExitCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        RequestId = requestId;
        Kind = kind;
        Retryable = retryable;
        CliExitCode = cliExitCode;
    }

    public int? Code { get; }

    public string? RequestId { get; }

    public string? ServerRequestId { get; internal set; }

    public string? Kind { get; }

    public bool? Retryable { get; }

    public int? CliExitCode { get; }
}

public class APIError : LockerError
{
    internal APIError(string message, int code, string requestId, string kind, bool retryable)
        : base(message, code, requestId, kind, retryable)
    {
    }

    public APIError(string message)
        : base(message)
    {
    }
}

public class ProtocolError : LockerError
{
    internal ProtocolError(
        string message,
        int? code = null,
        string? requestId = null,
        string? kind = null,
        Exception? innerException = null)
        : base(message, code, requestId, kind, false, innerException: innerException)
    {
    }
}

public class CliRunError : LockerError
{
    internal CliRunError(string message, int? exitCode = null, Exception? innerException = null)
        : base(message, null, null, null, null, exitCode, innerException)
    {
    }

    public CliRunError(string message)
        : base(message)
    {
    }
}

public sealed class LockerTimeoutError : CliRunError
{
    internal LockerTimeoutError()
        : base("Locker CLI protocol exchange timed out.")
    {
    }
}

public sealed class LockerResponseTooLargeError : CliRunError
{
    internal LockerResponseTooLargeError(string streamName)
        : base($"Locker CLI {streamName} exceeded the configured byte limit.")
    {
    }
}

public class AuthenticationError : LockerError
{
    internal AuthenticationError(string message, int code, string? requestId, string kind, bool retryable)
        : base(message, code, requestId, kind, retryable)
    {
    }

    public AuthenticationError(string message)
        : base(message)
    {
    }
}

public class PermissionDeniedError : LockerError
{
    internal PermissionDeniedError(string message, int code, string requestId, string kind, bool retryable)
        : base(message, code, requestId, kind, retryable)
    {
    }

    public PermissionDeniedError(string message)
        : base(message)
    {
    }
}

public class RateLimitError : LockerError
{
    internal RateLimitError(
        string message,
        int code,
        string requestId,
        string kind,
        bool retryable,
        int? retryAfterSeconds = null)
        : base(message, code, requestId, kind, retryable)
    {
        RetryAfterSeconds = retryAfterSeconds;
    }

    public RateLimitError(string message)
        : base(message)
    {
    }

    public int? RetryAfterSeconds { get; }
}

public class ResourceNotFoundError : LockerError
{
    internal ResourceNotFoundError(string message, int code, string requestId, string kind, bool retryable)
        : base(message, code, requestId, kind, retryable)
    {
    }

    public ResourceNotFoundError(string message)
        : base(message)
    {
    }
}

/// <summary>The operation conflicts with the current Locker resource state.</summary>
public class ConflictError : APIError
{
    internal ConflictError(string message, int code, string requestId, string kind, bool retryable)
        : base(message, code, requestId, kind, retryable)
    {
    }

    public ConflictError(string message)
        : base(message)
    {
    }
}

/// <summary>A create operation targeted a Locker resource that already exists.</summary>
public sealed class AlreadyExistsError : ConflictError
{
    internal AlreadyExistsError(
        string message,
        int code,
        string requestId,
        string kind,
        bool retryable)
        : base(message, code, requestId, kind, retryable)
    {
    }

    public AlreadyExistsError(string message)
        : base(message)
    {
    }
}

/// <summary>Locker rejected semantically invalid operation input.</summary>
public sealed class ValidationError : APIError
{
    internal ValidationError(string message, int code, string requestId, string kind, bool retryable)
        : base(message, code, requestId, kind, retryable)
    {
    }

    public ValidationError(string message)
        : base(message)
    {
    }
}

/// <summary>An older Locker CLI rejected a request without a more precise class.</summary>
public sealed class RequestRejectedError : APIError
{
    internal RequestRejectedError(
        string message,
        int code,
        string requestId,
        string kind,
        bool retryable)
        : base(message, code, requestId, kind, retryable)
    {
    }

    public RequestRejectedError(string message)
        : base(message)
    {
    }

}

/// <summary>The operation result exceeded the negotiated protocol response limit.</summary>
public sealed class ResponseTooLargeError : APIError
{
    internal ResponseTooLargeError(
        string message,
        int code,
        string requestId,
        string kind,
        bool retryable)
        : base(message, code, requestId, kind, retryable)
    {
    }

    public ResponseTooLargeError(string message)
        : base(message)
    {
    }
}

/// <summary>The Locker operation was cancelled before a result was available.</summary>
public sealed class OperationCancelledError : APIError
{
    internal OperationCancelledError(
        string message,
        int code,
        string requestId,
        string kind,
        bool retryable)
        : base(message, code, requestId, kind, retryable)
    {
    }

    public OperationCancelledError(string message)
        : base(message)
    {
    }
}

/// <summary>Locker rejected a response or artifact that failed integrity validation.</summary>
public sealed class IntegrityError : APIError
{
    internal IntegrityError(string message, int code, string requestId, string kind, bool retryable)
        : base(message, code, requestId, kind, retryable)
    {
    }

    public IntegrityError(string message)
        : base(message)
    {
    }
}

public class APIServerError : LockerError
{
    internal APIServerError(string message, int code, string requestId, string kind, bool retryable)
        : base(message, code, requestId, kind, retryable)
    {
    }

    public APIServerError(string message)
        : base(message)
    {
    }
}

public class APIConnectionError : LockerError
{
    internal APIConnectionError(string message, int code, string requestId, string kind, bool retryable)
        : base(message, code, requestId, kind, retryable)
    {
    }

    public APIConnectionError(string message)
        : base(message)
    {
    }
}

public sealed class LocalStorageError : LockerError
{
    internal LocalStorageError(string message, int code, string requestId, string kind, bool retryable)
        : base(message, code, requestId, kind, retryable)
    {
    }
}

public sealed class LockerCliDistributionUnavailableError : LockerError
{
    internal LockerCliDistributionUnavailableError(string message)
        : base(message)
    {
    }
}
