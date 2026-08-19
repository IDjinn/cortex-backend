using Cortex.Core.Objects;

namespace Cortex.Core.Providers;

public sealed class ProviderFactory : IProviderFactory
{
    private readonly OpenRouterProvider _openRouter;
    private readonly OllamaProvider _ollama;
    private readonly LmStudioProvider _lmStudio;

    public ProviderFactory(OpenRouterProvider openRouter, OllamaProvider ollama, LmStudioProvider lmStudio)
    {
        _openRouter = openRouter;
        _ollama = ollama;
        _lmStudio = lmStudio;
    }

    public IProvider Get(ChatProviderKind kind) => kind switch
    {
        ChatProviderKind.OpenRouter => _openRouter,
        ChatProviderKind.Ollama => _ollama,
        ChatProviderKind.LmStudio => _lmStudio,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
