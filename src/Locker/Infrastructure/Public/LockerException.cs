namespace Locker
{
    using System.Diagnostics;

    public class LockerError : Exception
    {
        private string _errorCode;
        private string _headers;
        private string _httpBody;
        private int _httpStatus;
        private string _jsonBody;

        public LockerError() : base()
        {
        }

        public LockerError(string message) : base(message)
        {
        }

        public LockerError(string message, Exception err) : base(message, err)
        {
        }

        public LockerError(string? message = null, string httpBody = null, int httpStatus = default,
            string jsonBody = null,
            string headers = null, string errorCode = null) : base(message)
        {
            this._httpBody = httpBody;
            this._httpStatus = httpStatus;
            this._jsonBody = httpBody;
            this._headers = headers;
            this._errorCode = errorCode;
        }
    }

    public class APIError : LockerError
    {
        public APIError() : base()
        {
        }

        public APIError(string message) : base(message)
        {
        }

        public APIError(string? message = null, string httpBody = null, int httpStatus = default,
            string jsonBody = null,
            string headers = null, string errorCode = null) : base(message, httpBody, httpStatus, jsonBody, headers,
            errorCode)
        {
        }
    }

    public class CliRunError : LockerError
    {
        private Process _process;

        public CliRunError() : base()
        {
        }


        public CliRunError(string message, Process process = null) : base(message)
        {
            this._process = process;
        }
    }

    public class AuthenticationError : LockerError
    {
        public AuthenticationError() : base()
        {
        }

        public AuthenticationError(string message) : base(message)
        {
        }
    }

    public class PermissionDeniedError : LockerError
    {
        public PermissionDeniedError() : base()
        {
        }

        public PermissionDeniedError(string message) : base(message)
        {
        }

        public PermissionDeniedError(string? message = null, string httpBody = null, int httpStatus = default,
            string jsonBody = null,
            string headers = null, string errorCode = null) : base(message, httpBody, httpStatus, jsonBody, headers,
            errorCode)
        {
        }
    }

    public class RateLimitError : LockerError
    {
        public RateLimitError() : base()
        {
        }

        public RateLimitError(string message) : base(message)
        {
        }

        public RateLimitError(string? message = null, string httpBody = null, int httpStatus = default,
            string jsonBody = null,
            string headers = null, string errorCode = null) : base(message, httpBody, httpStatus, jsonBody, headers,
            errorCode)
        {
        }
    }
}