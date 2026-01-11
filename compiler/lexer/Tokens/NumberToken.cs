public sealed class NumberToken : ITokenDefinition
{
    public int Priority => 3;
    public bool TryMatch(TextCursor input, out Token token, out bool skip)
    {
        token = default!;
        skip = false;
        var first = input.Peek();
        var second = input.PeekNext();
        var startsWithDigits = char.IsDigit(first);
        var startsWithMinusDigits = first == '-' && char.IsDigit(second);
        var startsWithDotDigits = first == '.' && char.IsDigit(second);

        if (!(startsWithDigits || startsWithMinusDigits || startsWithDotDigits)) return false;

        if (startsWithMinusDigits) input.Advance();

        if (startsWithDotDigits)
        {
            input.Advance();
            input.AdvanceWhile(char.IsDigit);
            var textDot = input.Capture();
            token = new Token(TokenType.Real, textDot, input.Start, input.Index, input.Line, input.StartColumn);
            return true;
        }

        input.AdvanceWhile(char.IsDigit);
        var type = TokenType.Integer;

        if (input.Peek() == '.')
        {
            var after = input.PeekNext();
            if (char.IsDigit(after))
            {
                input.Advance();
                input.AdvanceWhile(char.IsDigit);
                type = TokenType.Real;
            }
            else if (!char.IsLetter(after) && after != '_')
            {
                input.Advance();
                type = TokenType.Real;
            }
        }

        var text = input.Capture();
        token = new Token(type, text, input.Start, input.Index, input.Line, input.StartColumn);
        return true;
    }
}
