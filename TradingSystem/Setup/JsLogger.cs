using Microsoft.JSInterop;

namespace TradingSystem.Setup;

public class JsLogger
{
    private readonly IJSRuntime JsRuntime;
    public JsLogger(IJSRuntime jSRuntime)
    {
        this.JsRuntime = jSRuntime;
    }

    public async Task LogAsync(object message)
    {
        await this.JsRuntime.InvokeVoidAsync("console.log", message);
    }
    
}