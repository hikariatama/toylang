using ToyLang.Syntax;

namespace ToyLang.Semantic;

public static class AstUtils
{
    public static T DeepClone<T>(T node) where T : AstNode => (T)CloneAst(node);

    public static TypeRef CloneTypeRef(TypeRef t)
        => new TypeRef(t.Name, t.TypeArguments.Select(CloneTypeRef).ToList(), t.Line, t.Column);

    private static AstNode CloneAst(AstNode node)
    {
        switch (node)
        {
            case ProgramAst p:
                return new ProgramAst(p.Items.Select(i => (TopLevelNode)CloneAst(i)).ToList());
            case ClassDecl c:
                return new ClassDecl(c.Name, c.TypeParameters.ToList(), c.BaseType != null ? CloneTypeRef(c.BaseType) : null,
                    c.Members.Select(m => (ClassMember)CloneAst(m)).ToList(), c.Line, c.Column);
            case FieldDecl f:
                return new FieldDecl(f.Name, (Expression)CloneAst(f.Init), f.Line, f.Column);
            case MethodDecl m:
                return new MethodDecl(m.Name, m.Parameters.Select(p => new Parameter(p.Name, CloneTypeRef(p.Type), p.Line, p.Column)).ToList(),
                    m.ReturnType != null ? CloneTypeRef(m.ReturnType) : null,
                    m.Body != null ? (MethodBody)CloneAst(m.Body) : null,
                    m.IsConstructor, m.Line, m.Column);
            case BlockBody b:
                return new BlockBody(b.Statements.Select(s => (Statement)CloneAst(s)).ToList(), b.Line, b.Column);
            case ExprBody eb:
                return new ExprBody((Expression)CloneAst(eb.Expr), eb.Line, eb.Column);
            case VarDeclStmt v:
                return new VarDeclStmt(v.Name, (Expression)CloneAst(v.Init), v.Line, v.Column);
            case AssignStmt a:
                return new AssignStmt((Expression)CloneAst(a.Target), (Expression)CloneAst(a.Value), a.Line, a.Column);
            case ExprStmt es:
                return new ExprStmt((Expression)CloneAst(es.Expr), es.Line, es.Column);
            case IfStmt i:
                return new IfStmt((Expression)CloneAst(i.Condition), (Statement)CloneAst(i.Then), i.Else != null ? (Statement)CloneAst(i.Else) : null, i.Line, i.Column);
            case WhileStmt w:
                return new WhileStmt((Expression)CloneAst(w.Condition), w.Body.Select(s => (Statement)CloneAst(s)).ToList(), w.Line, w.Column);
            case ReturnStmt r:
                return new ReturnStmt(r.Expr != null ? (Expression)CloneAst(r.Expr) : null, r.Line, r.Column);
            case BreakStmt br:
                return new BreakStmt(br.Line, br.Column);
            case BlockStmt bs:
                return new BlockStmt(bs.Statements.Select(s => (Statement)CloneAst(s)).ToList(), bs.Line, bs.Column);
            case IdentifierExpr ie:
                return new IdentifierExpr(ie.Name, ie.Line, ie.Column);
            case ThisExpr th:
                return new ThisExpr(th.Line, th.Column);
            case LiteralExpr le:
                return new LiteralExpr(le.Lexeme, le.Kind, le.Line, le.Column);
            case MemberAccessExpr ma:
                return new MemberAccessExpr((Expression)CloneAst(ma.Target), ma.Member, ma.Line, ma.Column);
            case CallExpr call:
                return new CallExpr((Expression)CloneAst(call.Target), call.Arguments.Select(a => (Expression)CloneAst(a)).ToList(), call.Line, call.Column);
            case GenericRefExpr gr:
                return new GenericRefExpr((Expression)CloneAst(gr.Target), gr.TypeArguments.Select(CloneTypeRef).ToList(), gr.Line, gr.Column);
            case IndexExpr ix:
                return new IndexExpr((Expression)CloneAst(ix.Target), (Expression)CloneAst(ix.Index), ix.Line, ix.Column);
            case ParenExpr pe:
                return new ParenExpr((Expression)CloneAst(pe.Inner), pe.Line, pe.Column);
            default:
                throw new NotSupportedException($"Unknown AST node: {node.GetType().Name}");
        }
    }
}
