using ToyLang.Syntax;

public sealed class Analyzer
{
    public ProgramAst analyze(IReadOnlyList<Token> tokens)
    {
        var filtered = tokens.Where(t => t.Type != TokenType.LineComment).ToList();
        if (filtered.Count == 0 || filtered[^1].Type != TokenType.Eof)
        {
            var eofLine = filtered.Count > 0 ? filtered[^1].Line : 1;
            filtered.Add(new Token(TokenType.Eof, string.Empty, 0, 0, eofLine, 1));
        }
        var reader = new TokenReader(filtered);
        var parser = new Parser(reader);
        return parser.ParseProgram();
    }

    private sealed class Parser
    {
        private readonly TokenReader _r;

        public Parser(TokenReader r) => _r = r;

        public ProgramAst ParseProgram()
        {
            var items = new List<TopLevelNode>();
            while (!IsAtEnd)
            {
                if (Current.Type == TokenType.Class) { items.Add(ParseClass()); continue; }
                if (Current.Type == TokenType.Method) { items.Add(ParseMethod(false)); continue; }
                if (Current.Type == TokenType.Var) { items.Add(ParseVarDecl()); continue; }
                if (Current.Type == TokenType.Eof) break;
                items.Add(ParseStatement());
            }
            return new ProgramAst(items);
        }

        private ClassDecl ParseClass()
        {
            var start = Consume(TokenType.Class, "class");
            var nameTok = Consume(TokenType.Identifier, "class name");
            var typeParams = ParseTypeParametersOpt();
            TypeRef? baseType = null;
            if (Match(TokenType.Extends)) baseType = ParseTypeRef();
            Consume(TokenType.Is, "'is'");
            var members = new List<ClassMember>();
            while (!IsAtEnd && Current.Type != TokenType.End)
            {
                if (Current.Type == TokenType.Var) { members.Add(ParseField()); continue; }
                if (Current.Type == TokenType.Method) { members.Add(ParseMethod(true)); continue; }
                if (Current.Type == TokenType.This) { members.Add(ParseCtor()); continue; }
                throw Error("member expected");
            }
            Consume(TokenType.End, "'end'");
            return new ClassDecl(nameTok.Lexeme, typeParams, baseType, members, start.Line, start.Column);
        }

        private FieldDecl ParseField()
        {
            var start = Consume(TokenType.Var, "'var'");
            var name = Consume(TokenType.Identifier, "field name");
            Consume(TokenType.Colon, "':'");
            var init = ParseExpression();
            return new FieldDecl(name.Lexeme, init, start.Line, start.Column);
        }

        private MethodDecl ParseCtor()
        {
            var start = Consume(TokenType.This, "'this'");
            var parameters = ParseParameters();
            var body = ParseMethodBodyOpt();
            return new MethodDecl("this", parameters, null, body, true, start.Line, start.Column);
        }

        private MethodDecl ParseMethod(bool inClass)
        {
            var start = Consume(TokenType.Method, "'method'");
            string name;
            if (Current.Type == TokenType.This)
            {
                _r.Consume();
                name = "this";
            }
            else
            {
                name = Consume(TokenType.Identifier, "method name").Lexeme;
            }
            var parameters = Current.Type == TokenType.ParensLeft ? ParseParameters() : Array.Empty<Parameter>();
            TypeRef? ret = null;
            if (Match(TokenType.Colon)) ret = ParseTypeRef();
            var body = ParseMethodBodyOpt();
            return new MethodDecl(name, parameters, ret, body, name == "this", start.Line, start.Column);
        }

        private MethodBody? ParseMethodBodyOpt()
        {
            if (Match(TokenType.EqualGreater))
            {
                var expr = ParseExpression();
                return new ExprBody(expr, expr.Line, expr.Column);
            }
            if (Match(TokenType.Is))
            {
                var stmts = new List<Statement>();
                while (!IsAtEnd && Current.Type != TokenType.End) stmts.Add(ParseStatement());
                var endToken = Consume(TokenType.End, "'end'");
                var line = stmts.Count > 0 ? stmts[0].Line : endToken.Line;
                var column = stmts.Count > 0 ? stmts[0].Column : endToken.Column;
                return new BlockBody(stmts, line, column);
            }
            return null;
        }

        private IReadOnlyList<Parameter> ParseParameters()
        {
            Consume(TokenType.ParensLeft, "'('");
            var ps = new List<Parameter>();
            if (Current.Type != TokenType.ParensRight)
            {
                while (true)
                {
                    var id = Consume(TokenType.Identifier, "parameter name");
                    Consume(TokenType.Colon, "':'");
                    var type = ParseTypeRef();
                    ps.Add(new Parameter(id.Lexeme, type, id.Line, id.Column));
                    if (!Match(TokenType.Comma)) break;
                }
            }
            Consume(TokenType.ParensRight, "')'");
            return ps;
        }

        private IReadOnlyList<string> ParseTypeParametersOpt()
        {
            var names = new List<string>();
            if (!Match(TokenType.BracketLeft)) return names;
            if (Current.Type != TokenType.BracketRight)
            {
                while (true)
                {
                    var id = Consume(TokenType.Identifier, "type parameter");
                    names.Add(id.Lexeme);
                    if (!Match(TokenType.Comma)) break;
                }
            }
            Consume(TokenType.BracketRight, "']'");
            return names;
        }

        private TypeRef ParseTypeRef()
        {
            var id = Consume(TokenType.Identifier, "type name");
            var args = new List<TypeRef>();
            if (Match(TokenType.BracketLeft))
            {
                if (Current.Type != TokenType.BracketRight)
                {
                    while (true)
                    {
                        args.Add(ParseTypeRef());
                        if (!Match(TokenType.Comma)) break;
                    }
                }
                Consume(TokenType.BracketRight, "']'");
            }
            return new TypeRef(id.Lexeme, args, id.Line, id.Column);
        }

        private VarDeclStmt ParseVarDecl()
        {
            var start = Consume(TokenType.Var, "'var'");
            var name = Consume(TokenType.Identifier, "variable name");
            if (Match(TokenType.Is))
            {
                var initIs = ParseExpression();
                return new VarDeclStmt(name.Lexeme, initIs, start.Line, start.Column);
            }
            if (Match(TokenType.ColonEqual))
            {
                var initCe = ParseExpression();
                return new VarDeclStmt(name.Lexeme, initCe, start.Line, start.Column);
            }
            if (Match(TokenType.Colon))
            {
                Expression init = Current.Type == TokenType.Identifier ? ParseTypeLikeExpression() : ParseExpression();
                return new VarDeclStmt(name.Lexeme, init, start.Line, start.Column);
            }
            throw new SyntaxError(Current.Line, Current.Column, "Expected ':', 'is', or ':='");
        }

        private Expression ParseTypeLikeExpression()
        {
            var id = Consume(TokenType.Identifier, "type-like name");
            Expression expr = new IdentifierExpr(id.Lexeme, id.Line, id.Column);
            while (true)
            {
                if (Current.Type == TokenType.BracketLeft)
                {
                    var targs = ParseGenericArgs();
                    expr = new GenericRefExpr(expr, targs, expr.Line, expr.Column);
                    continue;
                }
                if (Match(TokenType.Dot))
                {
                    var m = Consume(TokenType.Identifier, "member name");
                    expr = new MemberAccessExpr(expr, m.Lexeme, m.Line, m.Column);
                    continue;
                }
                if (Current.Type == TokenType.ParensLeft)
                {
                    var args = ParseCallArgs();
                    expr = new CallExpr(expr, args, expr.Line, expr.Column);
                    continue;
                }
                break;
            }
            return expr;
        }

        private Statement ParseStatement()
        {
            if (Current.Type == TokenType.Var) return ParseVarDecl();
            if (Current.Type == TokenType.While) return ParseWhile();
            if (Current.Type == TokenType.If) return ParseIf();
            if (Current.Type == TokenType.Return) return ParseReturn();
            if (Current.Type == TokenType.Break) return ParseBreak();
            var lhs = ParseExpression();
            if (Match(TokenType.ColonEqual))
            {
                var rhs = ParseExpression();
                return new AssignStmt(lhs, rhs, lhs.Line, lhs.Column);
            }
            return new ExprStmt(lhs, lhs.Line, lhs.Column);
        }

        private WhileStmt ParseWhile()
        {
            var w = Consume(TokenType.While, "'while'");
            var cond = ParseExpression();
            Consume(TokenType.Loop, "'loop'");
            var body = new List<Statement>();
            while (!IsAtEnd && Current.Type != TokenType.End) body.Add(ParseStatement());
            Consume(TokenType.End, "'end'");
            return new WhileStmt(cond, body, w.Line, w.Column);
        }

        private IfStmt ParseIf()
        {
            var i = Consume(TokenType.If, "'if'");
            var cond = ParseExpression();
            Consume(TokenType.Then, "'then'");
            var thenStmt = ParseBranchBody(cond.Line, cond.Column, TokenType.Elif, TokenType.Else, TokenType.End);
            var elseStmt = ParseIfElseTail();
            Consume(TokenType.End, "'end'");
            return new IfStmt(cond, thenStmt, elseStmt, i.Line, i.Column);
        }

        private Statement? ParseIfElseTail()
        {
            if (Current.Type == TokenType.Elif)
            {
                var elifToken = Consume(TokenType.Elif, "'elif'");
                var cond = ParseExpression();
                Consume(TokenType.Then, "'then'");
                var thenBranch = ParseBranchBody(cond.Line, cond.Column, TokenType.Elif, TokenType.Else, TokenType.End);
                var elseBranch = ParseIfElseTail();
                return new IfStmt(cond, thenBranch, elseBranch, elifToken.Line, elifToken.Column);
            }

            if (Current.Type == TokenType.Else)
            {
                var elseToken = Consume(TokenType.Else, "'else'");
                return ParseBranchBody(elseToken.Line, elseToken.Column, TokenType.End);
            }

            return null;
        }

        private Statement ParseBranchBody(int fallbackLine, int fallbackColumn, params TokenType[] terminators)
        {
            var statements = new List<Statement>();

            while (!IsAtEnd)
            {
                if (Current.Type == TokenType.End)
                    break;

                var shouldStop = false;
                foreach (var t in terminators)
                {
                    if (Current.Type == t)
                    {
                        shouldStop = true;
                        break;
                    }
                }

                if (shouldStop)
                    break;

                statements.Add(ParseStatement());
            }

            if (statements.Count == 0)
                return new BlockStmt(System.Array.Empty<Statement>(), fallbackLine, fallbackColumn);

            if (statements.Count == 1)
                return statements[0];

            var first = statements[0];
            return new BlockStmt(statements, first.Line, first.Column);
        }

        private ReturnStmt ParseReturn()
        {
            var r = Consume(TokenType.Return, "'return'");
            Expression? expr = null;
            if (Current.Type != TokenType.End &&
                Current.Type != TokenType.Else &&
                Current.Type != TokenType.Eof)
            {
                expr = ParseExpression();
            }
            return new ReturnStmt(expr, r.Line, r.Column);
        }

        private BreakStmt ParseBreak()
        {
            var b = Consume(TokenType.Break, "'break'");
            return new BreakStmt(b.Line, b.Column);
        }

        private Expression ParseExpression() => ParsePostfix();

        private Expression ParsePostfix()
        {
            var expr = ParsePrimary();
            while (true)
            {
                if (Match(TokenType.Dot))
                {
                    var id = Consume(TokenType.Identifier, "member name");
                    expr = new MemberAccessExpr(expr, id.Lexeme, id.Line, id.Column);
                    continue;
                }
                if (Current.Type == TokenType.BracketLeft)
                {
                    if (LooksLikeGenericRef())
                    {
                        var targs = ParseGenericArgs();
                        expr = new GenericRefExpr(expr, targs, expr.Line, expr.Column);
                        continue;
                    }
                    var index = ParseIndexArg();
                    expr = new IndexExpr(expr, index, expr.Line, expr.Column);
                    continue;
                }
                if (Current.Type == TokenType.ParensLeft)
                {
                    var call = ParseCallArgs();
                    expr = new CallExpr(expr, call, expr.Line, expr.Column);
                    continue;
                }
                break;
            }
            return expr;
        }

        private bool LooksLikeGenericRef()
        {
            if (Current.Type != TokenType.BracketLeft) return false;
            var t1 = _r.Peek(1);
            if (t1.Type != TokenType.Identifier) return false;
            int depth = 0;
            int k = 0;
            while (true)
            {
                var t = _r.Peek(k);
                if (t.Type == TokenType.Eof) return false;
                if (t.Type == TokenType.BracketLeft) depth++;
                else if (t.Type == TokenType.BracketRight)
                {
                    depth--;
                    if (depth == 0)
                    {
                        var after = _r.Peek(k + 1);
                        return after.Type == TokenType.ParensLeft;
                    }
                }
                k++;
            }
        }

        private Expression ParseIndexArg()
        {
            Consume(TokenType.BracketLeft, "'['");
            var e = ParseExpression();
            Consume(TokenType.BracketRight, "']'");
            return e;
        }

        private IReadOnlyList<TypeRef> ParseGenericArgs()
        {
            Consume(TokenType.BracketLeft, "'['");
            var list = new List<TypeRef>();
            if (Current.Type != TokenType.BracketRight)
            {
                while (true)
                {
                    list.Add(ParseTypeRef());
                    if (!Match(TokenType.Comma)) break;
                }
            }
            Consume(TokenType.BracketRight, "']'");
            return list;
        }

        private IReadOnlyList<Expression> ParseCallArgs()
        {
            Consume(TokenType.ParensLeft, "'('");
            var list = new List<Expression>();
            if (Current.Type != TokenType.ParensRight)
            {
                while (true)
                {
                    list.Add(ParseExpression());
                    if (!Match(TokenType.Comma)) break;
                }
            }
            Consume(TokenType.ParensRight, "')'");
            return list;
        }

        private Expression ParsePrimary()
        {
            var t = Current;
            if (t.Type == TokenType.Integer || t.Type == TokenType.Real)
            {
                _r.Consume();
                return new LiteralExpr(t.Lexeme, t.Type, t.Line, t.Column);
            }
            if (t.Type == TokenType.True || t.Type == TokenType.False)
            {
                _r.Consume();
                return new LiteralExpr(t.Lexeme, TokenType.Boolean, t.Line, t.Column);
            }
            if (t.Type == TokenType.String)
            {
                _r.Consume();
                return new LiteralExpr(t.Lexeme, TokenType.String, t.Line, t.Column);
            }
            if (t.Type == TokenType.This)
            {
                _r.Consume();
                return new ThisExpr(t.Line, t.Column);
            }
            if (t.Type == TokenType.Identifier)
            {
                _r.Consume();
                return new IdentifierExpr(t.Lexeme, t.Line, t.Column);
            }
            if (Current.Type == TokenType.ParensLeft)
            {
                var open = Consume(TokenType.ParensLeft, "'('");
                var inner = ParseExpression();
                Consume(TokenType.ParensRight, "')'");
                return new ParenExpr(inner, open.Line, open.Column);
            }
            throw Error("expression expected");
        }

        private Token Current => _r.Current;
        private bool IsAtEnd => _r.IsAtEnd;

        private bool Match(TokenType tt)
        {
            if (Current.Type != tt) return false;
            _r.Consume();
            return true;
        }

        private Token Consume(TokenType tt, string what)
        {
            var t = Current;
            if (!_r.Match(tt)) throw new SyntaxError(t.Line, t.Column, $"Expected {what}");
            return t;
        }

        private Exception Error(string message) => new SyntaxError(Current.Line, Current.Column, message);
    }
}
