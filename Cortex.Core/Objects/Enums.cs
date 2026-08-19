namespace Cortex.Core.Objects;

public enum AuthProvider
{
    Google,
    GitHub
}

public enum MessageRole
{
    System,
    User,
    Assistant
}

public enum ChatProviderKind
{
    OpenRouter,
    Ollama,
    LmStudio
}
