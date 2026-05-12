namespace Orka.Infrastructure.Services;

public sealed class ContentSafetyOptions
{
    public UploadContentSafetyOptions Uploads { get; set; } = new();
}

public sealed class UploadContentSafetyOptions
{
    public long MaxFileBytes { get; set; } = 10 * 1024 * 1024;
    public long MaxMultipartBodyBytes { get; set; } = 0;
    public int MaxPdfPages { get; set; } = 100;
    public int MaxExtractedChars { get; set; } = 200_000;
    public int MaxChunksPerSource { get; set; } = 100;
    public int MaxEmbeddingChunksPerUpload { get; set; } = 100;
    public int MaxEmbeddingChunksPerUserPerDay { get; set; } = 500;
    public int MaxUploadsPerUserPerHour { get; set; } = 10;
    public int MaxKorteksFileResearchPerUserPerHour { get; set; } = 10;
    public int MaxSourcesPerTopic { get; set; } = 50;

    public long EffectiveMaxMultipartBodyBytes()
    {
        if (MaxMultipartBodyBytes > 0)
            return MaxMultipartBodyBytes;

        const long defaultMultipartOverheadBytes = 1024 * 1024;
        return checked(MaxFileBytes + defaultMultipartOverheadBytes);
    }
}
