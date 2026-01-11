public sealed class LineCommentToken : ITokenDefinition
{
    public int Priority => 2;
    public bool TryMatch(TextCursor input, out Token token, out bool skip)
    {
        skip = false;
        token = default!;
        if (!(input.Peek() == '/' && input.PeekNext() == '/'))
        {
            return false;
        }
        var start = input.Index;
        var column = input.StartColumn;
        input.Advance(); input.Advance();
        input.AdvanceWhile(c => c != '\n' && c != '\r');
        var text = input.Capture();
        token = new Token(TokenType.LineComment, text, start, input.Index, input.Line, column);
        return true;
    }
}
