using Cortex.Core.Objects;

namespace Cortex.Core.Providers;

public sealed class ProviderFactory : IProviderFactory
{
    private readonly OpenRouterProvider _openRouter;
    private readonly OllamaProvider _ollama;

    public ProviderFactory(OpenRouterProvider openRouter, OllamaProvider ollama)
    {
        _openRouter = openRouter;
        _ollama = ollama;
    }

    public IProvider Get(ChatProviderKind kind) => kind switch
    {
        ChatProviderKind.OpenRouter => _openRouter,
        ChatProviderKind.Ollama => _ollama,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public IProvider GetOpenRouter() => _openRouter;
    public IProvider GetOllama() => _ollama;
}
