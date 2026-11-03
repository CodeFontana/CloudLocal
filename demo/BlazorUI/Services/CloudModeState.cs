using BlazorUI.Configuration;
using Microsoft.AspNetCore.Components;

namespace BlazorUI.Services;

public sealed class CloudModeState
{
    public CloudModeState(CloudProviderMode initial)
    {
        Current = initial;
    }

    public CloudProviderMode Current { get; private set; }

    public event Action? Changed;

    public void Set(CloudProviderMode mode)
    {
        if (Current == mode)
        {
            return;
        }

        Current = mode;
        Changed?.Invoke();
    }
}
