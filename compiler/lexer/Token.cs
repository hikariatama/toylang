public sealed record Token(TokenType Type, string Lexeme, int Start, int End, int Line, int Column);
