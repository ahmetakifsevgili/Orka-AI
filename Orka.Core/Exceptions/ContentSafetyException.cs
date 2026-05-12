namespace Orka.Core.Exceptions;

public sealed class ContentSafetyException : Exception
{
    public ContentSafetyException(int statusCode, string publicMessage)
        : base(publicMessage)
    {
        StatusCode = statusCode;
        PublicMessage = publicMessage;
    }

    public int StatusCode { get; }
    public string PublicMessage { get; }

    public static ContentSafetyException BadRequest(string message) => new(400, message);
    public static ContentSafetyException PayloadTooLarge(string message) => new(413, message);
    public static ContentSafetyException TooManyRequests(string message) => new(429, message);
}
