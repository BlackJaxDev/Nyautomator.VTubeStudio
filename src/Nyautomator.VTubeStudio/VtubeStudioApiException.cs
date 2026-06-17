namespace Nyautomator.VTubeStudio;

public sealed class VtubeStudioApiException : Exception
{
    public int ErrorId { get; }

    public VtubeStudioApiException(int errorId, string message)
        : base(message)
    {
        ErrorId = errorId;
    }
}
