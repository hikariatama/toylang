using ToyLang.Syntax;

namespace ToyLang.Semantic;

public static class Optimizer
{
    public enum OptimizationKind
    {
        InlineFunction,
        ConstantFold,
        IfSimplify,
        WhileEliminate,
        RemoveUnusedVar,
        UnreachableElimination,
        Other
    }

    public sealed record OptimizationStep(
        OptimizationKind Kind,
        string Message,
        int Line,
        string? Hint,
        string? Before,
        string? After,
        int? Start = null,
        int? End = null
    );

    private sealed record InlineDef(IReadOnlyList<string> Params, Expression Body);

    public static (ProgramAst Program, List<OptimizationStep> Steps) OptimizeWithReport(ProgramAst ast)
    {
        var steps = new List<OptimizationStep>();
        var sanitized = RemoveInvalidFlow(ast, steps);
        var clone = AstUtils.DeepClone(sanitized);
        var inlineable = CollectInlineable(clone);
        var userGlobals = CollectUserGlobalFunctions(clone);
        var items = new List<TopLevelNode>();
        foreach (var item in clone.Items) items.Add((TopLevelNode)RewriteNode(item, inlineable, steps));
        items = items.Select(i => RemoveUnusedVars(i, steps, userGlobals)).ToList();
        items = items.Select(i => SimplifyToReturnLiteral(i, steps)).ToList();
        return (new ProgramAst(items), steps);
    }

    private static TopLevelNode SimplifyToReturnLiteral(TopLevelNode node, List<OptimizationStep> steps)
    {
        return node switch
        {
            MethodDecl m => SimplifyMethodToReturnLiteral(m, steps),
            ClassDecl c => SimplifyClassMembersToReturnLiteral(c, steps),
            _ => node,
        };
    }

    private static MethodDecl SimplifyMethodToReturnLiteral(MethodDecl method, List<OptimizationStep> steps)
    {
        if (method.Body is BlockBody body && body.Statements.Count > 0)
        {
            var priorStatements = body.Statements.Take(body.Statements.Count - 1);
            var last = body.Statements[body.Statements.Count - 1];
            if (last is ReturnStmt ret && ret.Expr is LiteralExpr lit && priorStatements.All(IsEffectivelyNoOpStatement))
            {
                var before = CodePrinter.Print(method);
                var simplified = new MethodDecl(method.Name, method.Parameters, method.ReturnType, new ExprBody(lit, lit.Line, lit.Column), method.IsConstructor, method.Line, method.Column);
                steps.Add(new OptimizationStep(OptimizationKind.ConstantFold, $"Collapsed method '{method.Name}' to return literal", ret.Line, "return", before, CodePrinter.Print(simplified)));
                return simplified;
            }
        }

        return method;
    }

    private static ClassDecl SimplifyClassMembersToReturnLiteral(ClassDecl cls, List<OptimizationStep> steps)
    {
        var updatedMembers = new List<ClassMember>(cls.Members.Count);
        foreach (var member in cls.Members)
        {
            if (member is MethodDecl method)
            {
                updatedMembers.Add(SimplifyMethodToReturnLiteral(method, steps));
            }
            else
            {
                updatedMembers.Add(member);
            }
        }

        return new ClassDecl(cls.Name, cls.TypeParameters, cls.BaseType, updatedMembers, cls.Line, cls.Column);
    }

    private static Dictionary<(string Name, int Arity), InlineDef> CollectInlineable(ProgramAst ast)
    {
        var lastByKey = new Dictionary<(string, int), InlineDef>();
        var counts = new Dictionary<(string, int), int>();
        foreach (var item in ast.Items)
        {
            if (item is MethodDecl m && !m.IsConstructor && m.Body is ExprBody eb)
            {
                var key = (m.Name, m.Parameters.Count);
                counts[key] = counts.TryGetValue(key, out var c) ? c + 1 : 1;
                lastByKey[key] = new InlineDef(m.Parameters.Select(p => p.Name).ToList(), eb.Expr);
            }
        }
        var unique = new Dictionary<(string, int), InlineDef>();
        foreach (var kv in lastByKey)
            if (counts.TryGetValue(kv.Key, out var c) && c == 1) unique[kv.Key] = kv.Value;
        return unique;
    }

    private static AstNode RewriteNode(AstNode node, IDictionary<(string Name, int Arity), InlineDef> inlineable, List<OptimizationStep> steps)
    {
        switch (node)
        {
            case ClassDecl c:
                {
                    var newMembers = new List<ClassMember>();
                    foreach (var mem in c.Members)
                    {
                        var rewritten = (ClassMember)RewriteNode(mem, inlineable, steps);
                        newMembers.Add(rewritten);
                    }
                    return new ClassDecl(c.Name, c.TypeParameters, c.BaseType, newMembers, c.Line, c.Column);
                }
            case FieldDecl f:
                return f with { Init = (Expression)RewriteNode(f.Init, inlineable, steps) };
            case MethodDecl m:
                {
                    MethodBody? body = null;
                    if (m.Body is ExprBody eb)
                    {
                        body = new ExprBody((Expression)RewriteNode(eb.Expr, inlineable, steps), eb.Line, eb.Column);
                    }
                    else if (m.Body is BlockBody b)
                    {
                        var newSts = new List<Statement>();
                        var afterReturn = false;
                        foreach (var st in b.Statements)
                        {
                            if (afterReturn) break;
                            var rw = (Statement)RewriteNode(st, inlineable, steps);
                            newSts.Add(rw);
                            if (rw is ReturnStmt) afterReturn = true;
                        }
                        if (afterReturn && newSts.Count < b.Statements.Count)
                        {
                            var line = newSts.First(s => s is ReturnStmt).Line;
                            steps.Add(new OptimizationStep(
                                OptimizationKind.UnreachableElimination,
                                "Removed unreachable code after return",
                                line,
                                "return",
                                CodePrinter.Print(b),
                                CodePrinter.Print(new BlockBody(newSts, b.Line, b.Column))
                            ));
                        }
                        body = new BlockBody(newSts, b.Line, b.Column);
                    }
                    return new MethodDecl(m.Name, m.Parameters, m.ReturnType, body, m.IsConstructor, m.Line, m.Column);
                }
            case VarDeclStmt v:
                return v with { Init = (Expression)RewriteNode(v.Init, inlineable, steps) };
            case AssignStmt a:
                return a with { Target = (Expression)RewriteNode(a.Target, inlineable, steps), Value = (Expression)RewriteNode(a.Value, inlineable, steps) };
            case ExprStmt es:
                return es with { Expr = (Expression)RewriteNode(es.Expr, inlineable, steps) };
            case BlockStmt bs:
                return new BlockStmt(bs.Statements.Select(s => (Statement)RewriteNode(s, inlineable, steps)).ToList(), bs.Line, bs.Column);
            case IfStmt i:
                {
                    var cond = (Expression)RewriteNode(i.Condition, inlineable, steps);
                    var thenS = (Statement)RewriteNode(i.Then, inlineable, steps);
                    var elseS = i.Else != null ? (Statement)RewriteNode(i.Else, inlineable, steps) : null;
                    var boolVal = TryEvalBoolean(cond);
                    if (boolVal.HasValue)
                    {
                        var val = boolVal.Value;
                        var before = CodePrinter.Print(i);
                        AstNode afterNode = val ? thenS : elseS ?? new ExprStmt(new LiteralExpr("", TokenType.Literal, i.Line, i.Column), i.Line, i.Column);
                        steps.Add(new OptimizationStep(
                            OptimizationKind.IfSimplify,
                            $"Replaced if with {(val ? "then" : (elseS != null ? "else" : "unit"))} branch",
                            i.Line,
                            "if",
                            before,
                            CodePrinter.Print(afterNode)
                        ));
                        return afterNode;
                    }
                    return new IfStmt(cond, thenS, elseS, i.Line, i.Column);
                }
            case WhileStmt w:
                {
                    var nc = (Expression)RewriteNode(w.Condition, inlineable, steps);
                    var wStmts = w.Body.Select(s => (Statement)RewriteNode(s, inlineable, steps)).ToList();
                    var trimmed = new List<Statement>();
                    bool terminated = false;
                    int termLine = w.Line;
                    string? termKind = null;
                    foreach (var s in wStmts)
                    {
                        if (terminated) break;
                        trimmed.Add(s);
                        if (s is BreakStmt) { terminated = true; termLine = s.Line; termKind = "break"; }
                        else if (s is ReturnStmt) { terminated = true; termLine = s.Line; termKind = "return"; }
                    }
                    if (terminated && trimmed.Count < wStmts.Count)
                    {
                        var beforeLoop = CodePrinter.Print(w);
                        var afterLoop = new WhileStmt(nc, trimmed, w.Line, w.Column);
                        steps.Add(new OptimizationStep(
                            OptimizationKind.UnreachableElimination,
                            $"Removed unreachable code after {termKind}",
                            termLine,
                            termKind,
                            beforeLoop,
                            CodePrinter.Print(afterLoop)
                        ));
                        return afterLoop;
                    }
                    var whileBool = TryEvalBoolean(nc);
                    if (whileBool.HasValue && whileBool.Value == false)
                    {
                        var before = CodePrinter.Print(w);
                        var afterNode = new ExprStmt(new LiteralExpr("", TokenType.Literal, w.Line, w.Column), w.Line, w.Column);
                        steps.Add(new OptimizationStep(
                            OptimizationKind.WhileEliminate,
                            "Removed while loop with constant false condition",
                            w.Line,
                            "while",
                            before,
                            CodePrinter.Print(afterNode)
                        ));
                        return afterNode;
                    }
                    return new WhileStmt(nc, wStmts, w.Line, w.Column);
                }
            case ReturnStmt r:
                return r.Expr != null ? new ReturnStmt((Expression)RewriteNode(r.Expr, inlineable, steps), r.Line, r.Column) : r;
            case BreakStmt b:
                return b;
            case MemberAccessExpr ma:
                return ma with { Target = (Expression)RewriteNode(ma.Target, inlineable, steps) };
            case CallExpr c:
                {
                    var target = (Expression)RewriteNode(c.Target, inlineable, steps);
                    var args = c.Arguments.Select(a => (Expression)RewriteNode(a, inlineable, steps)).ToList();

                    if (target is IdentifierExpr id)
                    {
                        var key = (id.Name, args.Count);
                        if (inlineable.TryGetValue(key, out var def))
                        {
                            var map = new Dictionary<string, Expression>(StringComparer.Ordinal);
                            for (int i = 0; i < def.Params.Count; i++) map[def.Params[i]] = args[i];
                            var before = CodePrinter.Print(c);
                            var afterNode = AstUtils.DeepClone(Substitute(def.Body, map)) with { Line = c.Line, Column = c.Column };
                            steps.Add(new OptimizationStep(
                                OptimizationKind.InlineFunction,
                                $"Inlined call to '{id.Name}'",
                                c.Line,
                                id.Name,
                                before,
                                CodePrinter.Print(afterNode)
                            ));
                            return (Expression)RewriteNode(afterNode, inlineable, steps);
                        }
                    }

                    if (target is MemberAccessExpr mem && (mem.Member is "Plus" or "Minus" or "Mult"))
                    {
                        var left = (Expression)RewriteNode(mem.Target, inlineable, steps);
                        var right = args.Count == 1 ? args[0] : null;

                        if (left is LiteralExpr l1 && right is LiteralExpr l2)
                        {
                            if (l1.Kind == TokenType.Integer && l2.Kind == TokenType.Integer &&
                                int.TryParse(l1.Lexeme, out var a1) && int.TryParse(l2.Lexeme, out var a2))
                            {
                                var resI = mem.Member switch { "Plus" => a1 + a2, "Minus" => a1 - a2, _ => a1 * a2 };
                                var before = CodePrinter.Print(new CallExpr(new MemberAccessExpr(left, mem.Member, mem.Line, mem.Column), new List<Expression> { right }, c.Line, c.Column));
                                var afterNode = new LiteralExpr(resI.ToString(), TokenType.Integer, c.Line, c.Column);
                                steps.Add(new OptimizationStep(
                                    OptimizationKind.ConstantFold,
                                    $"Folded integer {mem.Member.ToLower()} to {resI}",
                                    c.Line,
                                    mem.Member,
                                    before,
                                    CodePrinter.Print(afterNode)
                                ));
                                return afterNode;
                            }

                            if ((l1.Kind == TokenType.Integer || l1.Kind == TokenType.Real) &&
                                (l2.Kind == TokenType.Integer || l2.Kind == TokenType.Real) &&
                                double.TryParse(l1.Lexeme, out var d1) && double.TryParse(l2.Lexeme, out var d2))
                            {
                                var res = mem.Member switch { "Plus" => d1 + d2, "Minus" => d1 - d2, _ => d1 * d2 };
                                var before = CodePrinter.Print(new CallExpr(new MemberAccessExpr(left, mem.Member, mem.Line, mem.Column), new List<Expression> { right }, c.Line, c.Column));
                                var afterNode = new LiteralExpr(res.ToString(), TokenType.Real, c.Line, c.Column);
                                steps.Add(new OptimizationStep(
                                    OptimizationKind.ConstantFold,
                                    $"Folded real {mem.Member.ToLower()} to {res}",
                                    c.Line,
                                    mem.Member,
                                    before,
                                    CodePrinter.Print(afterNode)
                                ));
                                return afterNode;
                            }
                        }

                        return new CallExpr(new MemberAccessExpr(left, mem.Member, mem.Line, mem.Column), args, c.Line, c.Column);
                    }

                    return new CallExpr(target, args, c.Line, c.Column);
                }
            case GenericRefExpr gr:
                return gr with { Target = (Expression)RewriteNode(gr.Target, inlineable, steps) };
            case IndexExpr ix:
                return ix with { Target = (Expression)RewriteNode(ix.Target, inlineable, steps), Index = (Expression)RewriteNode(ix.Index, inlineable, steps) };
            case ParenExpr pe:
                return pe with { Inner = (Expression)RewriteNode(pe.Inner, inlineable, steps) };
            default:
                return node;
        }
    }

    private static HashSet<string> CollectUserGlobalFunctions(ProgramAst ast)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in ast.Items)
        {
            if (item is MethodDecl m && !m.IsConstructor)
                set.Add(m.Name);
        }
        return set;
    }

    private static List<CallExpr> ExtractSideEffectingCalls(Expression e, HashSet<string> userGlobals)
    {
        var list = new List<CallExpr>();
        void Walk(Expression ex)
        {
            switch (ex)
            {
                case CallExpr c:
                    if (IsPotentiallySideEffecting(c, userGlobals))
                    {
                        list.Add(c);
                        return;
                    }
                    Walk(c.Target);
                    foreach (var a in c.Arguments) Walk(a);
                    break;
                case MemberAccessExpr ma:
                    Walk(ma.Target);
                    break;
                case GenericRefExpr gr:
                    Walk(gr.Target);
                    break;
                case IndexExpr ix:
                    Walk(ix.Target); Walk(ix.Index);
                    break;
                case ParenExpr pe:
                    Walk(pe.Inner);
                    break;
                default:
                    break;
            }
        }
        Walk(e);
        return list;
    }

    private static bool IsPotentiallySideEffecting(CallExpr c, HashSet<string> userGlobals)
    {
        if (c.Target is IdentifierExpr id)
        {
            if (userGlobals.Contains(id.Name)) return true;
            if (id.Name is "Integer" or "Real" or "Boolean" or "String" or "Array" or "List" or "Pair") return false;
            return false;
        }
        if (c.Target is MemberAccessExpr mem)
        {
            if (mem.Member is "Plus" or "Minus" or "Mult" or "Div" or "Rem" or "Less" or "LessEqual" or "Greater" or "GreaterEqual" or "Equal" or "UnaryMinus" or "Not" or "And" or "Or" or "Xor" or "get" or "Length" or "Concat" or "ToInteger" or "ToReal" or "ToBoolean")
                return false;
            return true;
        }
        return false;
    }

    private static bool? TryEvalBoolean(Expression e)
    {
        switch (e)
        {
            case LiteralExpr bl when bl.Kind == TokenType.Boolean:
                return bl.Lexeme.Equals("true", StringComparison.OrdinalIgnoreCase);
            case ParenExpr pe:
                return TryEvalBoolean(pe.Inner);
            case CallExpr c when c.Target is IdentifierExpr id && id.Name == "Boolean" && c.Arguments.Count == 1:
                return TryEvalBoolean(c.Arguments[0]);
            case CallExpr c2 when c2.Target is MemberAccessExpr mem && mem.Member == "Not" && c2.Arguments.Count == 0:
                {
                    var inner = TryEvalBoolean(mem.Target);
                    return inner.HasValue ? !inner.Value : null;
                }
            default:
                return null;
        }
    }

    private static Expression Substitute(Expression e, IReadOnlyDictionary<string, Expression> map)
    {
        switch (e)
        {
            case IdentifierExpr id:
                if (map.TryGetValue(id.Name, out var rep)) return AstUtils.DeepClone(rep) with { Line = id.Line, Column = id.Column };
                return id;
            case MemberAccessExpr ma:
                return ma with { Target = Substitute(ma.Target, map) };
            case CallExpr c:
                return new CallExpr(Substitute(c.Target, map), c.Arguments.Select(a => Substitute(a, map)).ToList(), c.Line, c.Column);
            case GenericRefExpr gr:
                return gr with { Target = Substitute(gr.Target, map) };
            case IndexExpr ix:
                return ix with { Target = Substitute(ix.Target, map), Index = Substitute(ix.Index, map) };
            case ParenExpr pe:
                return pe with { Inner = Substitute(pe.Inner, map) };
            default:
                return e;
        }
    }

    private static TopLevelNode RemoveUnusedVars(TopLevelNode node, List<OptimizationStep> steps, HashSet<string> userGlobals)
    {
        if (node is MethodDecl m && m.Body is BlockBody b)
        {
            return RemoveUnusedVarsInMethod(m, steps, userGlobals);
        }
        if (node is ClassDecl c)
        {
            var newMembers = new List<ClassMember>();
            foreach (var mem in c.Members)
            {
                if (mem is MethodDecl md && md.Body is BlockBody)
                {
                    var updated = RemoveUnusedVarsInMethod(md, steps, userGlobals);
                    newMembers.Add(updated);
                }
                else
                {
                    newMembers.Add(mem);
                }
            }
            return new ClassDecl(c.Name, c.TypeParameters, c.BaseType, newMembers, c.Line, c.Column);
        }
        return node;
    }

    private static MethodDecl RemoveUnusedVarsInMethod(MethodDecl m, List<OptimizationStep> steps, HashSet<string> userGlobals)
    {
        var b = (BlockBody)m.Body!;
        var constEnv = new Dictionary<string, (string lexeme, TokenType kind)>(StringComparer.Ordinal);
        var folded = new List<Statement>();
        foreach (var st in b.Statements)
        {
            if (st is VarDeclStmt vd)
            {
                folded.Add(vd);
                if (vd.Init is LiteralExpr lit)
                    constEnv[vd.Name] = (lit.Lexeme, lit.Kind);
                else constEnv.Remove(vd.Name);
                continue;
            }
            if (st is AssignStmt asg)
            {
                var rewrittenRhs = SubstituteIdWithConst(asg.Value, constEnv);
                rewrittenRhs = FoldArithmetic(rewrittenRhs, steps, asg.Line, asg.Column);
                if (asg.Target is IdentifierExpr idt && rewrittenRhs is CallExpr call && call.Target is MemberAccessExpr mem)
                {
                    var memTarget = SubstituteIdWithConst(mem.Target, constEnv);
                    if (memTarget is IdentifierExpr idLeft && string.Equals(idLeft.Name, idt.Name, StringComparison.Ordinal) && call.Arguments.Count == 1 && call.Arguments[0] is LiteralExpr argLit)
                    {
                        if (constEnv.TryGetValue(idt.Name, out var cur) && (cur.kind == TokenType.Integer || cur.kind == TokenType.Real) && (argLit.Kind == TokenType.Integer || argLit.Kind == TokenType.Real) && (mem.Member is "Plus" or "Minus" or "Mult"))
                        {
                            if (double.TryParse(cur.lexeme, out var l) && double.TryParse(argLit.Lexeme, out var r))
                            {
                                var res = mem.Member switch { "Plus" => l + r, "Minus" => l - r, _ => l * r };
                                var kind = (cur.kind == TokenType.Real || argLit.Kind == TokenType.Real) ? TokenType.Real : TokenType.Integer;
                                var resText = kind == TokenType.Integer ? ((int)res).ToString() : res.ToString();
                                var before = CodePrinter.Print(asg);
                                var afterNode = new AssignStmt(asg.Target, new LiteralExpr(resText, kind, asg.Line, asg.Column), asg.Line, asg.Column);
                                steps.Add(new OptimizationStep(OptimizationKind.ConstantFold, $"Folded reassignment of '{idt.Name}' via {mem.Member}", asg.Line, idt.Name, before, CodePrinter.Print(afterNode)));
                                folded.Add(afterNode);
                                constEnv[idt.Name] = (resText, kind);
                                continue;
                            }
                        }
                    }
                }
                if (rewrittenRhs is LiteralExpr litR && asg.Target is IdentifierExpr idSet)
                    constEnv[idSet.Name] = (litR.Lexeme, litR.Kind);
                else if (asg.Target is IdentifierExpr idClear)
                    constEnv.Remove(idClear.Name);
                folded.Add(asg with { Value = rewrittenRhs });
                continue;
            }
            if (st is ReturnStmt ret)
            {
                if (ret.Expr != null)
                {
                    var newExpr = SubstituteIdWithConst(ret.Expr, constEnv);
                    newExpr = FoldArithmetic(newExpr, steps, ret.Line, ret.Column);
                    folded.Add(new ReturnStmt(newExpr, ret.Line, ret.Column));
                    continue;
                }
            }
            if (st is ExprStmt es)
            {
                if (es.Expr is LiteralExpr le && string.IsNullOrEmpty(le.Lexeme))
                {
                    continue;
                }
                folded.Add(es);
                continue;
            }
            folded.Add(st);
        }

        var usage = new HashSet<string>(StringComparer.Ordinal);
        foreach (var st in folded) CollectReads(st, usage);
        var newSts = new List<Statement>();
        foreach (var st in folded)
        {
            if (st is VarDeclStmt v)
            {
                if (!usage.Contains(v.Name))
                {
                    var calls = ExtractSideEffectingCalls(v.Init, userGlobals);
                    if (calls.Count > 0)
                    {
                        foreach (var c in calls) newSts.Add(new ExprStmt(c, c.Line, c.Column));
                        steps.Add(new OptimizationStep(
                            OptimizationKind.RemoveUnusedVar,
                            $"Partially removed unused local '{v.Name}'",
                            v.Line,
                            v.Name,
                            CodePrinter.Print(v),
                            string.Join("\n", calls.Select(cc => CodePrinter.Print(new ExprStmt(cc, cc.Line, cc.Column))))
                        ));
                    }
                    else
                    {
                        steps.Add(new OptimizationStep(
                            OptimizationKind.RemoveUnusedVar,
                            $"Removed unused local '{v.Name}'",
                            v.Line,
                            v.Name,
                            CodePrinter.Print(v),
                            null
                        ));
                    }
                    continue;
                }
            }
            newSts.Add(st);
        }
        var beforeBody = b;
        var afterBody = new BlockBody(newSts, b.Line, b.Column);
        if (newSts.Count != b.Statements.Count)
        {
            steps.Add(new OptimizationStep(
                OptimizationKind.RemoveUnusedVar,
                "Pruned unused variable declarations",
                b.Line,
                null,
                CodePrinter.Print(beforeBody),
                CodePrinter.Print(afterBody)
            ));
        }

        if (newSts.Count > 0 && newSts[newSts.Count - 1] is ReturnStmt retStmt && retStmt.Expr is LiteralExpr litRet && newSts.Take(newSts.Count - 1).All(IsEffectivelyNoOpStatement))
        {
            var before = CodePrinter.Print(m);
            var newM = new MethodDecl(m.Name, m.Parameters, m.ReturnType, new ExprBody(litRet, litRet.Line, litRet.Column), m.IsConstructor, m.Line, m.Column);
            steps.Add(new OptimizationStep(OptimizationKind.ConstantFold, $"Collapsed method '{m.Name}' to return literal", retStmt.Line, "return", before, CodePrinter.Print(newM)));
            return newM;
        }

        return new MethodDecl(m.Name, m.Parameters, m.ReturnType, afterBody, m.IsConstructor, m.Line, m.Column);
    }

    private static Expression FoldArithmetic(Expression e, List<OptimizationStep> steps, int line, int column)
    {
        Expression Recurse(Expression x)
        {
            return x switch
            {
                MemberAccessExpr ma => ma with { Target = Recurse(ma.Target) },
                CallExpr c =>
                    FoldCall(new CallExpr(Recurse(c.Target), c.Arguments.Select(Recurse).ToList(), c.Line, c.Column)),
                GenericRefExpr gr => gr with { Target = Recurse(gr.Target) },
                IndexExpr ix => ix with { Target = Recurse(ix.Target), Index = Recurse(ix.Index) },
                ParenExpr pe => pe with { Inner = Recurse(pe.Inner) },
                _ => x
            };
        }

        Expression FoldCall(CallExpr c)
        {
            if (c.Target is IdentifierExpr idCons && c.Arguments.Count == 1 && c.Arguments[0] is LiteralExpr lone)
            {
                if ((idCons.Name == "Integer" && lone.Kind == TokenType.Integer) ||
                    (idCons.Name == "Real" && lone.Kind == TokenType.Real) ||
                    (idCons.Name == "Boolean" && lone.Kind == TokenType.Boolean))
                {
                    return new LiteralExpr(lone.Lexeme, lone.Kind, c.Line, c.Column);
                }
            }
            if (c.Target is MemberAccessExpr mem && c.Arguments.Count == 1)
            {
                if (mem.Target is LiteralExpr l && c.Arguments[0] is LiteralExpr r && (mem.Member is "Plus" or "Minus" or "Mult"))
                {
                    bool isNum(TokenType k) => k == TokenType.Integer || k == TokenType.Real;
                    if (isNum(l.Kind) && isNum(r.Kind))
                    {
                        if (double.TryParse(l.Lexeme, out var dl) && double.TryParse(r.Lexeme, out var dr))
                        {
                            var res = mem.Member switch { "Plus" => dl + dr, "Minus" => dl - dr, _ => dl * dr };
                            var kind = (l.Kind == TokenType.Real || r.Kind == TokenType.Real) ? TokenType.Real : TokenType.Integer;
                            var resText = kind == TokenType.Integer ? ((int)res).ToString() : res.ToString();
                            steps.Add(new OptimizationStep(OptimizationKind.ConstantFold, $"Folded literal {mem.Member.ToLower()} to {resText}", line, mem.Member, CodePrinter.Print(c), resText));
                            return new LiteralExpr(resText, kind, line, column);
                        }
                    }
                }
            }
            return c;
        }

        var rec = Recurse(e);
        return TryFoldSimpleBinary(rec, steps, line, column);
    }

    private static Expression SubstituteIdWithConst(Expression e, IReadOnlyDictionary<string, (string lexeme, TokenType kind)> env)
    {
        switch (e)
        {
            case IdentifierExpr id when env.TryGetValue(id.Name, out var val):
                return new LiteralExpr(val.lexeme, val.kind, id.Line, id.Column);
            case MemberAccessExpr ma:
                return ma with { Target = SubstituteIdWithConst(ma.Target, env) };
            case CallExpr c:
                return new CallExpr(SubstituteIdWithConst(c.Target, env), c.Arguments.Select(a => SubstituteIdWithConst(a, env)).ToList(), c.Line, c.Column);
            case GenericRefExpr gr:
                return gr with { Target = SubstituteIdWithConst(gr.Target, env) };
            case IndexExpr ix:
                return ix with { Target = SubstituteIdWithConst(ix.Target, env), Index = SubstituteIdWithConst(ix.Index, env) };
            case ParenExpr pe:
                return pe with { Inner = SubstituteIdWithConst(pe.Inner, env) };
            default:
                return e;
        }
    }

    private static Expression TryFoldSimpleBinary(Expression e, List<OptimizationStep> steps, int line, int column)
    {
        if (e is CallExpr call && call.Target is MemberAccessExpr mem && call.Arguments.Count == 1)
        {
            var left = mem.Target as LiteralExpr;
            var right = call.Arguments[0] as LiteralExpr;
            if (left != null && right != null)
            {
                bool isNum(TokenType k) => k == TokenType.Integer || k == TokenType.Real;
                if (isNum(left.Kind) && isNum(right.Kind) && (mem.Member is "Plus" or "Minus" or "Mult"))
                {
                    if (double.TryParse(left.Lexeme, out var l) && double.TryParse(right.Lexeme, out var r))
                    {
                        var res = mem.Member switch { "Plus" => l + r, "Minus" => l - r, _ => l * r };
                        var kind = (left.Kind == TokenType.Real || right.Kind == TokenType.Real) ? TokenType.Real : TokenType.Integer;
                        var resText = kind == TokenType.Integer ? ((int)res).ToString() : res.ToString();
                        steps.Add(new OptimizationStep(OptimizationKind.ConstantFold, $"Folded literal {mem.Member.ToLower()} to {resText}", line, mem.Member, CodePrinter.Print(e), resText));
                        return new LiteralExpr(resText, kind, line, column);
                    }
                }
            }
        }
        return e;
    }

    private static void CollectReads(AstNode n, HashSet<string> usage)
    {
        switch (n)
        {
            case IdentifierExpr id:
                usage.Add(id.Name);
                break;
            case FieldDecl f:
                CollectReads(f.Init, usage);
                break;
            case VarDeclStmt v:
                CollectReads(v.Init, usage);
                break;
            case AssignStmt a:
                CollectReads(a.Value, usage);
                if (a.Target is IdentifierExpr idWrite)
                {
                    usage.Add(idWrite.Name);
                }
                else
                {
                    CollectReads(a.Target, usage);
                }
                break;
            case ExprStmt es:
                CollectReads(es.Expr, usage);
                break;
            case IfStmt i:
                CollectReads(i.Condition, usage);
                CollectReads(i.Then, usage);
                if (i.Else != null) CollectReads(i.Else, usage);
                break;
            case WhileStmt w:
                CollectReads(w.Condition, usage);
                foreach (var s in w.Body) CollectReads(s, usage);
                break;
            case ReturnStmt r:
                if (r.Expr != null) CollectReads(r.Expr, usage);
                break;
            case BlockStmt b:
                foreach (var s in b.Statements) CollectReads(s, usage);
                break;
            case MemberAccessExpr ma:
                CollectReads(ma.Target, usage);
                break;
            case CallExpr c:
                CollectReads(c.Target, usage);
                foreach (var a in c.Arguments) CollectReads(a, usage);
                break;
            case GenericRefExpr gr:
                CollectReads(gr.Target, usage);
                break;
            case IndexExpr ix:
                CollectReads(ix.Target, usage);
                CollectReads(ix.Index, usage);
                break;
            case ParenExpr pe:
                CollectReads(pe.Inner, usage);
                break;
            case BlockBody b:
                foreach (var s in b.Statements) CollectReads(s, usage);
                break;
        }
    }

    private static bool IsEffectivelyNoOpStatement(Statement statement)
    {
        return statement switch
        {
            ExprStmt { Expr: LiteralExpr { Lexeme: "" } } => true,
            BlockStmt block => block.Statements.All(IsEffectivelyNoOpStatement),
            _ => false,
        };
    }

    private static ProgramAst RemoveInvalidFlow(ProgramAst ast, List<OptimizationStep> steps)
    {
        var newItems = new List<TopLevelNode>();
        foreach (var item in ast.Items)
        {
            switch (item)
            {
                case MethodDecl m when m.Body is BlockBody b:
                    {
                        var cleaned = RemoveInvalidFlowInStatements(b.Statements, steps, inLoop: false);
                        newItems.Add(new MethodDecl(m.Name, m.Parameters, m.ReturnType, new BlockBody(cleaned, b.Line, b.Column), m.IsConstructor, m.Line, m.Column));
                        break;
                    }
                case ClassDecl c:
                    {
                        var newMembers = new List<ClassMember>();
                        foreach (var mem in c.Members)
                        {
                            if (mem is MethodDecl mm && mm.Body is BlockBody bb)
                            {
                                var cleaned = RemoveInvalidFlowInStatements(bb.Statements, steps, inLoop: false);
                                newMembers.Add(new MethodDecl(mm.Name, mm.Parameters, mm.ReturnType, new BlockBody(cleaned, bb.Line, bb.Column), mm.IsConstructor, mm.Line, mm.Column));
                            }
                            else
                            {
                                newMembers.Add(mem);
                            }
                        }
                        newItems.Add(new ClassDecl(c.Name, c.TypeParameters, c.BaseType, newMembers, c.Line, c.Column));
                        break;
                    }
                case ReturnStmt r:
                    {
                        steps.Add(new OptimizationStep(
                            OptimizationKind.Other,
                            "Removed top-level 'return' statement",
                            r.Line,
                            "return",
                            CodePrinter.Print(r),
                            null
                        ));
                        break;
                    }
                case BreakStmt b:
                    {
                        steps.Add(new OptimizationStep(
                            OptimizationKind.Other,
                            "Removed top-level 'break' statement",
                            b.Line,
                            "break",
                            CodePrinter.Print(b),
                            null
                        ));
                        break;
                    }
                default:
                    newItems.Add(item);
                    break;
            }
        }
        return new ProgramAst(newItems);
    }

    private static List<Statement> RemoveInvalidFlowInStatements(IReadOnlyList<Statement> statements, List<OptimizationStep> steps, bool inLoop)
    {
        var result = new List<Statement>();
        foreach (var s in statements)
        {
            switch (s)
            {
                case BreakStmt br when !inLoop:
                    steps.Add(new OptimizationStep(
                        OptimizationKind.Other,
                        "Removed 'break' outside of loop",
                        br.Line,
                        "break",
                        CodePrinter.Print(br),
                        null
                    ));
                    continue;

                case IfStmt i:
                    {
                        var thenS = RemoveInvalidFlowInStatement(i.Then, steps, inLoop) ?? new ExprStmt(new LiteralExpr("", TokenType.Literal, i.Line, i.Column), i.Line, i.Column);
                        var elseS = i.Else != null ? RemoveInvalidFlowInStatement(i.Else, steps, inLoop) : null;
                        result.Add(new IfStmt(i.Condition, thenS, elseS, i.Line, i.Column));
                        break;
                    }

                case WhileStmt w:
                    {
                        var cleanedBody = RemoveInvalidFlowInStatements(w.Body, steps, inLoop: true);
                        result.Add(new WhileStmt(w.Condition, cleanedBody, w.Line, w.Column));
                        break;
                    }

                case BlockStmt bs:
                    {
                        var cleanedBlock = RemoveInvalidFlowInStatements(bs.Statements, steps, inLoop);
                        result.Add(new BlockStmt(cleanedBlock, bs.Line, bs.Column));
                        break;
                    }

                default:
                    result.Add(s);
                    break;
            }
        }
        return result;
    }

    private static Statement? RemoveInvalidFlowInStatement(Statement s, List<OptimizationStep> steps, bool inLoop)
    {
        switch (s)
        {
            case BreakStmt br when !inLoop:
                steps.Add(new OptimizationStep(
                    OptimizationKind.Other,
                    "Removed 'break' outside of loop",
                    br.Line,
                    "break",
                    CodePrinter.Print(br),
                    null
                ));
                return null;

            case IfStmt i:
                {
                    var thenS = RemoveInvalidFlowInStatement(i.Then, steps, inLoop) ?? new ExprStmt(new LiteralExpr("", TokenType.Literal, i.Line, i.Column), i.Line, i.Column);
                    var elseS = i.Else != null ? RemoveInvalidFlowInStatement(i.Else, steps, inLoop) : null;
                    return new IfStmt(i.Condition, thenS, elseS, i.Line, i.Column);
                }

            case WhileStmt w:
                {
                    var cleanedBody = RemoveInvalidFlowInStatements(w.Body, steps, inLoop: true);
                    return new WhileStmt(w.Condition, cleanedBody, w.Line, w.Column);
                }

            case BlockStmt bs:
                {
                    var cleaned = RemoveInvalidFlowInStatements(bs.Statements, steps, inLoop);
                    return new BlockStmt(cleaned, bs.Line, bs.Column);
                }

            default:
                return s;
        }
    }
}
