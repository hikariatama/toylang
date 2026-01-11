public static class TokenSet
{
    public static IReadOnlyList<ITokenDefinition> Default() =>
        new List<ITokenDefinition>
        {
            new WhitespaceToken(),
            new LineCommentToken(),
            new FixedStringToken(":=", TokenType.ColonEqual),
            new FixedStringToken("=>", TokenType.EqualGreater),
            new FixedStringToken("(", TokenType.ParensLeft),
            new FixedStringToken(")", TokenType.ParensRight),
            new FixedStringToken("[", TokenType.BracketLeft),
            new FixedStringToken("]", TokenType.BracketRight),
            new FixedStringToken(",", TokenType.Comma),
            new FixedStringToken(".", TokenType.Dot),
            new FixedStringToken(":", TokenType.Colon),
            new IdentifierOrKeywordToken(new Dictionary<string, TokenType>
            {
                {"class", TokenType.Class},
                {"extends", TokenType.Extends},
                {"is", TokenType.Is},
                {"end", TokenType.End},
                {"var", TokenType.Var},
                {"method", TokenType.Method},
                {"while", TokenType.While},
                {"loop", TokenType.Loop},
                {"if", TokenType.If},
                {"then", TokenType.Then},
                {"elif", TokenType.Elif},
                {"else", TokenType.Else},
                {"return", TokenType.Return},
                {"break", TokenType.Break},
                {"this", TokenType.This},
                {"true", TokenType.True},
                {"false", TokenType.False}
            }),
            new StringToken(),
            new NumberToken()
        }
        .OrderByDescending(r => r.Priority)
        .ToList();
}
