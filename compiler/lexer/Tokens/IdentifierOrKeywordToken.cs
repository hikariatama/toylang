public sealed class IdentifierOrKeywordToken : ITokenDefinition
{
    private readonly IReadOnlyDictionary<string, TokenType> _keywords;
    public int Priority => 3;
    public IdentifierOrKeywordToken(IReadOnlyDictionary<string, TokenType> keywords) => _keywords = keywords ?? throw new ArgumentNullException(nameof(keywords));
    public bool TryMatch(TextCursor input, out Token token, out bool skip)
    {
        token = default!;
        skip = false;
        var c = input.Peek();
        if (!IsAlpha(c)) return false;
        input.Advance();
        while (IsAlphaNumeric(input.Peek())) input.Advance();
        var text = input.Capture();
        var type = _keywords.TryGetValue(text, out var kw) ? kw : TokenType.Identifier;
        token = new Token(type, text, input.Start, input.Index, input.Line, input.StartColumn);
        return true;
    }
    private static bool IsAlpha(char c) => char.IsLetter(c) || c == '_';
    private static bool IsAlphaNumeric(char c) => IsAlpha(c) || char.IsDigit(c);
}
