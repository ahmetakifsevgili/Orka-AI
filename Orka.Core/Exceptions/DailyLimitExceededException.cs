using System;

namespace Orka.Core.Exceptions;

public class DailyLimitExceededException : Exception
{
    public DailyLimitExceededException(string message = "Günlük mesaj limitine ulaşıldı.") : base(message)
    {
    }
}
