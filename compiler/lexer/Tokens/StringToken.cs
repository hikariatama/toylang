using System.Text;

public sealed class StringToken : ITokenDefinition
{
    public int Priority => 1;

    public bool TryMatch(TextCursor input, out Token token, out bool skip)
    {
        skip = false;
        token = default!;
        if (input.Peek() != '"') return false;
        var startColumn = input.StartColumn;
        input.Advance();
        var builder = new StringBuilder();
        while (!input.IsAtEnd)
        {
            var ch = input.Peek();
            if (ch == '"')
            {
                input.Advance();
                token = new Token(TokenType.String, builder.ToString(), input.Start, input.Index, input.Line, startColumn);
                return true;
            }
            if (ch == '\\')
            {
                input.Advance();
                if (input.IsAtEnd)
                    throw new SyntaxError(input.Line, startColumn, "Unterminated string literal");
                var escaped = input.Peek();
                if (escaped == 'x')
                {
                    input.Advance();
                    var hexDigits = new StringBuilder();
                    for (int i = 0; i < 2; i++)
                    {
                        if (input.IsAtEnd)
                            throw new SyntaxError(input.Line, startColumn, "Unterminated string literal");
                        var hexCh = input.Peek();
                        if (!IsHexDigit(hexCh))
                            throw new SyntaxError(input.Line, input.Column, $"Invalid hexadecimal digit '{hexCh}' in escape sequence");
                        hexDigits.Append(hexCh);
                        input.Advance();
                    }
                    var byteValue = Convert.ToByte(hexDigits.ToString(), 16);
                    builder.Append((char)byteValue);
                    continue;
                }
                builder.Append(escaped switch
                {
                    '"' => '"',
                    '\\' => '\\',
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    '0' => '\0',
                    _ => throw new SyntaxError(input.Line, input.Column, $"Unknown escape sequence \\{escaped}")
                });
                input.Advance();
                continue;
            }
            if (ch == '\n')
                throw new SyntaxError(input.Line, startColumn, "Unterminated string literal");
            builder.Append(ch);
            input.Advance();
        }
        throw new SyntaxError(input.Line, startColumn, "Unterminated string literal");
    }

    private bool IsHexDigit(char hexCh)
    {
        return (hexCh >= '0' && hexCh <= '9') ||
               (hexCh >= 'a' && hexCh <= 'f') ||
               (hexCh >= 'A' && hexCh <= 'F');
    }
}
