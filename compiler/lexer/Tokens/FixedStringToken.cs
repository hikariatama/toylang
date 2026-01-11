public sealed class FixedStringToken : ITokenDefinition
{
    private readonly string _lexeme;
    private readonly TokenType _type;
    public int Priority { get; }
    public FixedStringToken(string lexeme, TokenType type)
    {
        _lexeme = lexeme ?? throw new ArgumentNullException(nameof(lexeme));
        _type = type;
        Priority = _lexeme.Length;
    }
    public bool TryMatch(TextCursor input, out Token token, out bool skip)
    {
        skip = false;
        token = default!;
        if (!input.Match(_lexeme)) return false;
        var text = _lexeme;
        token = new Token(_type, text, input.Start, input.Index, input.Line, input.StartColumn);
        return true;
    }
}
