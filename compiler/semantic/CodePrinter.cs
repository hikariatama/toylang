using ToyLang.Syntax;

namespace ToyLang.Semantic;

public static class CodePrinter
{
    public static string Print(AstNode node)
    {
        return node switch
        {
            ProgramAst p => PrintProgram(p),
            Statement s => PrintStmt(s),
            TopLevelNode t => PrintTopLevel(t),
            Expression e => PrintExpr(e),
            MethodBody b => PrintBody(b),
            _ => node.ToString() ?? string.Empty
        };
    }

    public static string PrintProgram(ProgramAst p)
    {
        return string.Join("\n\n", p.Items.Select(PrintTopLevel));
    }

    private static string PrintTopLevel(TopLevelNode n)
    {
        return n switch
        {
            ClassDecl c => PrintClass(c),
            FieldDecl f => PrintStmt(new VarDeclStmt(f.Name, f.Init, f.Line, f.Column)),
            MethodDecl m => PrintMethod(m),
            // TODO: Remove it from ast idk
            BreakStmt b => PrintStmt(b),
            ReturnStmt r => PrintStmt(r),
            _ => n.GetType().Name
        };
    }

    private static string PrintClass(ClassDecl c)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("class ").Append(c.Name);
        if (c.TypeParameters.Count > 0)
        {
            sb.Append('[').Append(string.Join(", ", c.TypeParameters)).Append(']');
        }
        if (c.BaseType != null)
        {
            sb.Append(" extends ").Append(PrintType(c.BaseType));
        }
        sb.Append(" is\n");
        foreach (var m in c.Members)
        {
            var printed = PrintTopLevel(m).Split('\n');
            foreach (var line in printed)
            {
                sb.Append("    ").Append(line).Append("\n");
            }
        }
        sb.Append("end");
        return sb.ToString();
    }

    private static string PrintMethod(MethodDecl m)
    {
        var sb = new System.Text.StringBuilder();
        static string IndentAll(string s, int spaces = 4)
        {
            var pad = new string(' ', spaces);
            var lines = s.Split('\n');
            return string.Join("\n", lines.Select(l => pad + l));
        }
        if (m.IsConstructor)
        {
            sb.Append("this");
            if (m.Parameters.Count > 0)
            {
                sb.Append("(").Append(string.Join(", ", m.Parameters.Select(p => $"{p.Name}: {PrintType(p.Type)}"))).Append(") ");
            }
            else
            {
                sb.Append(' ');
            }
        }
        else
        {
            sb.Append("method ").Append(m.Name);
            if (m.Parameters.Count > 0)
            {
                sb.Append("(")
                  .Append(string.Join(", ", m.Parameters.Select(p => $"{p.Name}: {PrintType(p.Type)}")))
                  .Append(")");
            }
            if (m.ReturnType != null) sb.Append(" : ").Append(PrintType(m.ReturnType));
            sb.Append(' ');
        }
        if (m.Body is ExprBody eb)
        {
            sb.Append("=> ").Append(PrintExpr(eb.Expr));
        }
        else if (m.Body is BlockBody bb)
        {
            sb.Append("is\n");
            foreach (var st in bb.Statements)
            {
                var printed = PrintStmt(st);
                sb.Append(IndentAll(printed)).Append("\n");
            }
            sb.Append("end");
        }
        else
        {
            sb.Append("is end");
        }
        return sb.ToString();
    }

    private static string PrintBody(MethodBody b) => b switch
    {
        BlockBody bb => string.Join("\n", bb.Statements.Select(PrintStmt)),
        ExprBody eb => PrintExpr(eb.Expr),
        _ => string.Empty
    };

    private static string PrintStmt(Statement s)
    {
        return s switch
        {
            VarDeclStmt v => $"var {v.Name} : {PrintExpr(v.Init)}",
            AssignStmt a => $"{PrintExpr(a.Target)} := {PrintExpr(a.Value)}",
            ExprStmt es => PrintExpr(es.Expr),
            ReturnStmt r => r.Expr != null ? $"return {PrintExpr(r.Expr)}" : "return",
            BreakStmt => "break",
            BlockStmt b => string.Join("\n", b.Statements.Select(PrintStmt)),
            IfStmt i => $"if {PrintExpr(i.Condition)} then\n{Indent(PrintStmt(i.Then))}\n" + (i.Else != null ? $"else\n{Indent(PrintStmt(i.Else))}\n" : "") + "end",
            WhileStmt w => $"while {PrintExpr(w.Condition)} loop\n{Indent(string.Join("\n", w.Body.Select(PrintStmt)))}\nend",
            _ => s.GetType().Name
        };
    }

    private static string Indent(string s, int spaces = 4)
    {
        var pad = new string(' ', spaces);
        var lines = s.Split('\n');
        return string.Join("\n", lines.Select(l => pad + l));
    }

    private static string PrintExpr(Expression e)
    {
        return e switch
        {
            IdentifierExpr id => id.Name,
            ThisExpr => "this",
            LiteralExpr lit => PrintLiteral(lit),
            MemberAccessExpr ma => $"{PrintExpr(ma.Target)}.{ma.Member}",
            CallExpr c => PrintCall(c),
            GenericRefExpr gr => $"{PrintExpr(gr.Target)}[{string.Join(", ", gr.TypeArguments.Select(PrintType))}]",
            IndexExpr ix => $"{PrintExpr(ix.Target)}[{PrintExpr(ix.Index)}]",
            ParenExpr pe => $"({PrintExpr(pe.Inner)})",
            _ => e.GetType().Name
        };
    }

    private static string PrintCall(CallExpr c)
    {
        if (c.Target is IdentifierExpr id && c.Arguments.Count == 1 && c.Arguments[0] is LiteralExpr lit)
        {
            if (id.Name is "Integer" or "Real" or "Boolean" or "String")
            {
                return $"{id.Name}({PrintLiteralRaw(lit)})";
            }
        }
        return $"{PrintExpr(c.Target)}({string.Join(", ", c.Arguments.Select(PrintExpr))})";
    }

    private static string PrintType(TypeRef t)
    {
        return t.TypeArguments.Count == 0 ? t.Name : $"{t.Name}[{string.Join(", ", t.TypeArguments.Select(PrintType))}]";
    }

    private static string PrintLiteral(LiteralExpr lit)
    {
        return lit.Kind switch
        {
            TokenType.Integer => $"Integer({lit.Lexeme})",
            TokenType.Real => $"Real({lit.Lexeme})",
            TokenType.Boolean => $"Boolean({lit.Lexeme.ToLowerInvariant()})",
            TokenType.String => $"String({PrintLiteralRaw(lit)})",
            _ => lit.Lexeme
        };
    }

    private static string PrintLiteralRaw(LiteralExpr lit)
    {
        return lit.Kind switch
        {
            TokenType.Integer => lit.Lexeme,
            TokenType.Real => lit.Lexeme,
            TokenType.Boolean => lit.Lexeme.ToLowerInvariant(),
            TokenType.String => $"\"{EscapeString(lit.Lexeme)}\"",
            _ => lit.Lexeme
        };
    }

    private static string EscapeString(string value)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var ch in value)
        {
            sb.Append(ch switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                '\0' => "\\0",
                _ => ch.ToString()
            });
        }
        return sb.ToString();
    }
}
