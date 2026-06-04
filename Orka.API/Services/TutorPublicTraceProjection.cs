using System.Text.Json;
using Orka.Core.Entities;

namespace Orka.API.Services;

public static class TutorPublicTraceProjection
{
    public static JsonDocument? TryParseTurnState(TutorTurnState? turnState)
    {
        if (turnState == null || string.IsNullOrWhiteSpace(turnState.StateJson))
            return null;

        try
        {
            return JsonDocument.Parse(turnState.StateJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
