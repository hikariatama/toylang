public sealed class SyntaxError : Exception
{
    public int Line { get; }
    public int Column { get; }

    public SyntaxError(int line, int column, string message)
        : base($"[Line {line}, Column {column}] Error: {message}")
    {
        Line = line;
        Column = column;
    }

    public SyntaxError(int line, string message)
        : this(line, 1, message)
    {
    }
}

public sealed class Lexer
{
    private readonly TextCursor _input;
    private readonly IReadOnlyList<ITokenDefinition> _rules;
    private readonly List<Token> _tokens = new();
    public Lexer(string source, IEnumerable<ITokenDefinition>? rules = null)
    {
        _input = new TextCursor(source);
        _rules = (rules ?? TokenSet.Default()).OrderByDescending(r => r.Priority).ToList();
    }
    public IReadOnlyList<Token> ScanTokens()
    {
        while (!_input.IsAtEnd)
        {
            _input.BeginToken();
            if (!TryScanOne()) throw new SyntaxError(_input.Line, _input.Column, $"Unexpected character '{_input.Peek()}'");
        }
        _tokens.Add(new Token(TokenType.Eof, string.Empty, _input.Index, _input.Index, _input.Line, _input.Column));
        return _tokens;
    }
    private bool TryScanOne()
    {
        foreach (var rule in _rules)
        {
            var matched = rule.TryMatch(_input, out var token, out var skip);
            if (!matched) continue;
            if (!skip) _tokens.Add(token);
            return true;
        }
        return false;
    }
}

