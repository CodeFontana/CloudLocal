using BlazorUI.Configuration;
using BlazorUI.Services;
using CodeFoxtrot.CloudLocal.Blobs;
using CodeFoxtrot.CloudLocal.Messaging;
using CodeFoxtrot.CloudLocal.Queues;
using CodeFoxtrot.CloudLocal.Tables;
using CodeFoxtrot.CloudLocal.Vault;
using Microsoft.AspNetCore.Components;

namespace BlazorUI.Features.Components;

public readonly record struct ActionFeedbackResult(string Status, string? Detail = null);

public abstract class DemoPage : ComponentBase
{
    [Inject]
    protected CloudModeState ModeState { get; set; } = default!;

    protected string? StatusMessage { get; private set; }

    protected string? Detail { get; private set; }

    protected bool IsError { get; private set; }

    protected bool IsBusy { get; private set; }

    protected Task RunAsync(Func<Task<string>> action)
    {
        return RunAsync(async () => new ActionFeedbackResult(await action()));
    }

    protected async Task RunAsync(Func<Task<ActionFeedbackResult>> action)
    {
        IsBusy = true;
        try
        {
            ActionFeedbackResult result = await action();
            StatusMessage = $"{result.Status} Using {ProviderLabel(ModeState.Current)}.";
            Detail = result.Detail;
            IsError = false;
        }
        catch (Exception exception)
        {
            StatusMessage = $"{ProviderLabel(ModeState.Current)}: {exception.Message}";
            Detail = null;
            IsError = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string ProviderLabel(CloudProviderMode mode)
    {
        return mode switch
        {
            CloudProviderMode.Local => "local files",
            CloudProviderMode.Azure => "Azure",
            CloudProviderMode.Aws => "AWS",
            _ => mode.ToString()
        };
    }
}
