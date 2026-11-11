namespace Crave.Analytics.Infrastructure.Http;

public class AppException : Exception
{
    public int StatusCode { get; }
    public bool IsOperational { get; }

    public AppException(string message, int statusCode = 500, bool isOperational = true)
        : base(message)
    {
        StatusCode = statusCode;
        IsOperational = isOperational;
    }
}
