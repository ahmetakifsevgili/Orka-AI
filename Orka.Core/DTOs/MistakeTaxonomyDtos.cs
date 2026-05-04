using Orka.Core.Enums;

namespace Orka.Core.DTOs;

public sealed record MistakeTaxonomyResult(
    MistakeCategory Category,
    string CategoryLabel,
    string Reason,
    int TriggerCount,
    Guid? RemediationPlanId,
    bool RemediationTriggered,
    bool SuggestFlashcard);
