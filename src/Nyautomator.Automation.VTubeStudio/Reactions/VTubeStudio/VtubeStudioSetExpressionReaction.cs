using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace NyautomatorUI.Server.Automation;

public sealed class VtubeStudioSetExpressionReaction : VtubeStudioReactionBase
{
    public override string Id => "Action.VTubeStudio.SetExpression";
    public override string DisplayName => "VTube Studio: Set Expression";
    public override string Description => "Activates or deactivates a VTube Studio expression.";

    [Description("Expression file to set, such as exp_smile.exp3.json.")]
    [OptionsProvider(typeof(VtubeStudioExpressionOptionsProvider))]
    public string ExpressionFile { get; set; } = string.Empty;

    [Description("True to enable the expression, false to disable it.")]
    public bool Active { get; set; } = true;

    [Description("Fade duration in seconds.")]
    public double FadeTimeSeconds { get; set; } = 0.25d;

    protected override async Task ExecuteCoreAsync(CancellationToken cancellationToken)
    {
        var setExpression = VtubeStudioAutomationHost.SetExpression;
        if (setExpression is null)
        {
            Log("VTube Studio set-expression delegate is not available.");
            return;
        }

        var expressionFile = ParseOptionValue(ExpressionFile);
        if (string.IsNullOrWhiteSpace(expressionFile))
            return;

        var fadeTime = double.IsFinite(FadeTimeSeconds) ? FadeTimeSeconds : 0.25d;
        if (fadeTime < 0d)
            fadeTime = 0d;

        var success = await setExpression(expressionFile, Active, fadeTime, cancellationToken).ConfigureAwait(false);
        IsSuccessful(success, "Set expression");
    }
}
