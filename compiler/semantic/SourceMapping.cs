namespace ToyLang.Semantic;

internal static class SourceMapping
{
    public static int[] ComputeLineStarts(string source)
    {
        if (source is null)
            return Array.Empty<int>();

        var lineStarts = new List<int> { 0 };
        for (var i = 0; i < source.Length; i++)
        {
            if (source[i] == '\n')
                lineStarts.Add(i + 1);
        }

        return lineStarts.ToArray();
    }

    public static IReadOnlyDictionary<int, List<Token>> BuildTokenLineMap(IReadOnlyList<Token>? tokens)
    {
        var map = new Dictionary<int, List<Token>>();
        if (tokens is null)
            return map;

        foreach (var token in tokens)
        {
            if (!map.TryGetValue(token.Line, out var list))
            {
                list = new List<Token>();
                map[token.Line] = list;
            }
            list.Add(token);
        }

        foreach (var list in map.Values)
        {
            list.Sort((a, b) => a.Start.CompareTo(b.Start));
        }

        return map;
    }

    public static (int? ColumnStart, int? ColumnEnd) ResolveColumns(
        int line,
        string? hint,
        string source,
        int[] lineStarts,
        IReadOnlyDictionary<int, List<Token>> tokensByLine)
    {
        if (line <= 0)
            return (null, null);

        if (tokensByLine.TryGetValue(line, out var tokens) && tokens.Count > 0)
        {
            if (!string.IsNullOrEmpty(hint))
            {
                var token = tokens.FirstOrDefault(t => string.Equals(t.Lexeme, hint, StringComparison.Ordinal));
                if (token == null)
                {
                    token = tokens.FirstOrDefault(t => t.Lexeme.Contains(hint!, StringComparison.Ordinal));
                }

                if (token != null)
                {
                    var start = token.Column;
                    var tokenLength = token.Lexeme.Length;

                    if (!string.Equals(token.Lexeme, hint, StringComparison.Ordinal))
                    {
                        var idx = token.Lexeme.IndexOf(hint!, StringComparison.Ordinal);
                        if (idx >= 0)
                        {
                            start += idx;
                            tokenLength = hint!.Length;
                        }
                    }

                    if (tokenLength <= 0)
                        tokenLength = 1;

                    return (start, start + tokenLength - 1);
                }
            }

            var reference = tokens[0];
            var lexemeLength = reference.Lexeme.Length > 0 ? reference.Lexeme.Length : 1;
            return (reference.Column, reference.Column + lexemeLength - 1);
        }

        if (lineStarts.Length == 0)
            return (null, null);

        var clampedIndex = Math.Clamp(line - 1, 0, lineStarts.Length - 1);
        var lineStart = lineStarts[clampedIndex];
        var endExclusive = clampedIndex + 1 < lineStarts.Length ? lineStarts[clampedIndex + 1] : source.Length;
        if (endExclusive < lineStart)
            endExclusive = lineStart;

        var length = endExclusive - lineStart;
        if (length <= 0)
            return (null, null);

        var span = source.AsSpan(lineStart, length);
        var newlineIndex = span.IndexOf('\n');
        if (newlineIndex >= 0)
            span = span[..newlineIndex];

        var pos = 0;
        while (pos < span.Length && char.IsWhiteSpace(span[pos]))
            pos++;

        if (pos >= span.Length)
            return (null, null);

        var column = pos + 1;
        return (column, column);
    }

    public static (int? Start, int? End) ResolveSpan(
        int line,
        string? hint,
        string source,
        int[] lineStarts,
        IReadOnlyDictionary<int, List<Token>> tokensByLine)
    {
        if (line <= 0)
            return (null, null);

        if (tokensByLine.TryGetValue(line, out var tokens) && tokens.Count > 0)
        {
            if (!string.IsNullOrEmpty(hint))
            {
                var token = tokens.FirstOrDefault(t => string.Equals(t.Lexeme, hint, StringComparison.Ordinal));
                if (token == null)
                    token = tokens.FirstOrDefault(t => t.Lexeme.Contains(hint!, StringComparison.Ordinal));

                if (token != null)
                {
                    var start = token.Start;
                    var end = token.End;

                    if (!string.Equals(token.Lexeme, hint, StringComparison.Ordinal))
                    {
                        var idx = token.Lexeme.IndexOf(hint!, StringComparison.Ordinal);
                        if (idx >= 0)
                        {
                            start += idx;
                            end = start + hint!.Length;
                        }
                    }

                    if (end <= start)
                        end = start + Math.Max(1, hint!.Length);

                    return (start, Math.Min(end, source.Length));
                }
            }

            var reference = tokens[0];
            var length = reference.End - reference.Start;
            if (length <= 0)
                length = Math.Max(1, reference.Lexeme.Length);
            var refEnd = reference.Start + length;
            return (reference.Start, Math.Min(refEnd, source.Length));
        }

        if (lineStarts.Length == 0)
            return (null, null);

        var lineIndex = Math.Clamp(line - 1, 0, lineStarts.Length - 1);
        var startOffset = lineStarts[lineIndex];
        var endOffset = lineIndex + 1 < lineStarts.Length ? lineStarts[lineIndex + 1] : source.Length;

        if (endOffset < startOffset)
            endOffset = startOffset;

        if (endOffset > startOffset)
        {
            var span = source.AsSpan(startOffset, endOffset - startOffset);
            var newlineIndex = span.IndexOf('\n');
            if (newlineIndex >= 0)
                endOffset = startOffset + newlineIndex;
        }

        if (endOffset <= startOffset)
            endOffset = Math.Min(source.Length, startOffset + 1);

        return (startOffset, Math.Min(endOffset, source.Length));
    }

    public static int? LineColumnToOffset(int line, int? column, int[] lineStarts, string source)
    {
        if (line <= 0 || column == null || column <= 0 || lineStarts.Length == 0)
            return null;

        var idx = Math.Clamp(line - 1, 0, lineStarts.Length - 1);
        var start = lineStarts[idx];
        var offset = start + (column.Value - 1);
        if (offset < 0)
            offset = 0;
        if (offset > source.Length)
            offset = source.Length;
        return offset;
    }

    public static int? ResolveLine(int line, string? hint, IReadOnlyDictionary<int, List<Token>> tokensByLine)
    {
        bool MatchesHint(Token token)
        {
            if (string.IsNullOrEmpty(hint)) return true;
            if (string.Equals(token.Lexeme, hint, StringComparison.Ordinal)) return true;
            return token.Lexeme.Contains(hint, StringComparison.Ordinal);
        }

        if (line > 0 && tokensByLine.TryGetValue(line, out var tokens) && tokens.Any(MatchesHint))
        {
            return line;
        }

        if (!string.IsNullOrEmpty(hint))
        {
            foreach (var kv in tokensByLine.OrderBy(kv => kv.Key))
            {
                if (kv.Value.Any(token => string.Equals(token.Lexeme, hint, StringComparison.Ordinal)))
                    return kv.Key;
            }

            foreach (var kv in tokensByLine.OrderBy(kv => kv.Key))
            {
                if (kv.Value.Any(token => token.Lexeme.Contains(hint, StringComparison.Ordinal)))
                    return kv.Key;
            }
        }

        return line > 0 ? line : null;
    }
}
