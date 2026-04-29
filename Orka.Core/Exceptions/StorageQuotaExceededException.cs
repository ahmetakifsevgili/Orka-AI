namespace Orka.Core.Exceptions;

public sealed class StorageQuotaExceededException : InvalidOperationException
{
    public StorageQuotaExceededException()
        : base("storage quota exceeded")
    {
    }
}
