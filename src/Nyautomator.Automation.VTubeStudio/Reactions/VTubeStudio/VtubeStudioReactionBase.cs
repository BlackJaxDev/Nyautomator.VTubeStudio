using System;
using System.Threading;
using System.Threading.Tasks;

namespace NyautomatorUI.Server.Automation;

public abstract class VtubeStudioReactionBase : ReactionType, IAsyncReaction
{
    public sealed override void Execute()
        => ExecuteAsync().GetAwaiter().GetResult();

    public async Task ExecuteAsync()
    {
        try
        {
            await ExecuteCoreAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log($"Unexpected error: {ex.Message}");
        }
    }

    protected abstract Task ExecuteCoreAsync(CancellationToken cancellationToken);

    protected static string? ParseOptionValue(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return null;

        var separator = trimmed.IndexOf('|');
        if (separator <= 0)
            return trimmed;

        return trimmed[..separator].Trim();
    }

    protected static bool IsSuccessful(bool result, string operation)
    {
        if (!result)
            Log($"{operation} failed or was rejected.");

        return result;
    }

    protected static void Log(string message)
        => Console.WriteLine($"[Automation][VTubeStudio] {message}");
}
