public sealed class WhitespaceToken : ITokenDefinition
{
    public int Priority => 1;

    public bool TryMatch(TextCursor input, out Token token, out bool skip)
    {
        token = default!;
        if (!char.IsWhiteSpace(input.Peek())) { skip = false; return false; }
        skip = true;
        input.AdvanceWhile(char.IsWhiteSpace);
        return true;
    }
}
