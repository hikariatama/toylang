public sealed class TextCursor
{
    private readonly string _src;
    public int Start { get; private set; }
    public int StartColumn { get; private set; }
    public int Index { get; private set; }
    public int Line { get; private set; } = 1;
    public int Column { get; private set; } = 1;
    public TextCursor(string src) => _src = src ?? throw new ArgumentNullException(nameof(src));
    public bool IsAtEnd => Index >= _src.Length;
    public void BeginToken() { Start = Index; StartColumn = Column; }
    public char Peek() => IsAtEnd ? '\0' : _src[Index];
    public char PeekNext() => Index + 1 >= _src.Length ? '\0' : _src[Index + 1];
    public bool Match(char c) { if (IsAtEnd || _src[Index] != c) return false; Advance(); return true; }
    public bool Match(string s)
    {
        if (s is null) throw new ArgumentNullException(nameof(s));
        if (Index + s.Length > _src.Length) return false;
        for (int i = 0; i < s.Length; i++) if (_src[Index + i] != s[i]) return false;
        for (int i = 0; i < s.Length; i++) Advance();
        return true;
    }
    public void Advance()
    {
        if (IsAtEnd) return;
        if (_src[Index] == '\n') { Line++; Column = 1; Index++; return; }
        Index++; Column++;
    }
    public void AdvanceWhile(Func<char, bool> p) { while (!IsAtEnd && p(_src[Index])) Advance(); }
    public string Capture() => _src.Substring(Start, Index - Start);
}
