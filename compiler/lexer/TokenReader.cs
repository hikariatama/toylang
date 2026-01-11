public sealed class TokenReader
{
    private readonly IReadOnlyList<Token> _tokens;
    private int _pos;
    public TokenReader(IReadOnlyList<Token> tokens) => _tokens = tokens;
    public Token Current => Peek();
    public Token Peek(int offset = 0)
    {
        var index = _pos + offset;
        if (index >= _tokens.Count) return _tokens[^1];
        return _tokens[index];
    }
    public bool IsAtEnd => Current.Type == TokenType.Eof;
    public Token Consume() => _tokens[_pos++];
    public bool Match(TokenType type)
    {
        if (Current.Type != type) return false;
        _pos++;
        return true;
    }
    public Token Expect(TokenType type)
    {
        if (Current.Type == type) return Consume();
        var t = Current;
        return new Token(type, "", t.Start, t.Start, t.Line, t.Column);
    }
}
