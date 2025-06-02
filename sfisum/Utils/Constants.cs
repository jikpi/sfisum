namespace sfisum.Utils;

internal static class Constants
{
    public const int FileBufferSize = 32 * 1024; //32KB
    public const char CommentChar = ';';

    public const float
        PredictedStructureCapacityPercentage = 0.1f; //What % of the max size is allocated for set structures.
    
    public const long RefreshMatchMinFileSize = 4 * 1024; // 4KB
}