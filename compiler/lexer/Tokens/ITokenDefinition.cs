public interface ITokenDefinition
{
    int Priority { get; }
    bool TryMatch(TextCursor input, out Token token, out bool skip);
}
