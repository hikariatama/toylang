using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using ToyLang.Syntax;

namespace ToyLang.Wasm;

internal static class GenericMonomorphizer
{
    public static ProgramAst Monomorphize(ProgramAst program)
    {
        if (program is null)
            throw new ArgumentNullException(nameof(program));

        var rewriter = new Rewriter(program);
        return rewriter.Run();
    }

    private sealed class Rewriter
    {
        private readonly ProgramAst _program;
        private readonly Dictionary<string, ClassDecl> _genericClasses;
        private readonly Dictionary<string, ClassDecl> _allClasses;
        private readonly Dictionary<ClassKey, Specialization> _specializations = new();
        private readonly Queue<ClassKey> _pending = new();
        private readonly HashSet<string> _builtinGenericTypes = new(StringComparer.Ordinal)
        {
            "Array",
            "List",
            "Pair",
        };
        private readonly HashSet<string> _usedNames;

        public Rewriter(ProgramAst program)
        {
            _program = program;
            _allClasses = program.Items
                .OfType<ClassDecl>()
                .ToDictionary(c => c.Name, StringComparer.Ordinal);
            _genericClasses = _allClasses
                .Where(pair => pair.Value.TypeParameters.Count > 0)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            _usedNames = new HashSet<string>(_allClasses.Keys, StringComparer.Ordinal);
        }

        public ProgramAst Run()
        {
            var newItems = new List<TopLevelNode>();

            foreach (var item in _program.Items)
            {
                switch (item)
                {
                    case ClassDecl cls when cls.TypeParameters.Count > 0:
                        newItems.Add(cls);
                        break;
                    case ClassDecl cls:
                        newItems.Add(RewriteClass(cls, substitution: null));
                        break;
                    case MethodDecl method:
                        newItems.Add(RewriteMethod(method, substitution: null));
                        break;
                    case Statement stmt:
                        newItems.Add(RewriteStatement(stmt, substitution: null));
                        break;
                    default:
                        newItems.Add(item);
                        break;
                }
            }

            MaterializePendingSpecializations(newItems);

            return new ProgramAst(newItems);
        }

        private void MaterializePendingSpecializations(List<TopLevelNode> items)
        {
            while (_pending.Count > 0)
            {
                var key = _pending.Dequeue();
                if (!_specializations.TryGetValue(key, out var spec))
                    continue;

                if (spec.Declaration != null)
                {
                    items.Add(spec.Declaration);
                    continue;
                }

                if (!_genericClasses.TryGetValue(key.ClassName, out var genericDefinition))
                    continue;

                var substitution = BuildSubstitution(genericDefinition, spec.TypeArguments);
                var baseType = genericDefinition.BaseType != null
                    ? RewriteTypeRef(genericDefinition.BaseType, substitution)
                    : null;

                var members = new List<ClassMember>(genericDefinition.Members.Count);
                foreach (var member in genericDefinition.Members)
                {
                    switch (member)
                    {
                        case FieldDecl field:
                            members.Add(new FieldDecl(field.Name, RewriteExpression(field.Init, substitution), field.Line, field.Column));
                            break;
                        case MethodDecl method:
                            members.Add(RewriteMethod(method, substitution));
                            break;
                        default:
                            members.Add(member);
                            break;
                    }
                }

                var specialized = new ClassDecl(spec.Name, Array.Empty<string>(), baseType, members, genericDefinition.Line, genericDefinition.Column);
                _specializations[key] = spec with { Declaration = specialized };
                items.Add(specialized);
            }
        }

        private static IReadOnlyDictionary<string, TypeRef> BuildSubstitution(ClassDecl definition, ImmutableArray<TypeRef> arguments)
        {
            var map = new Dictionary<string, TypeRef>(StringComparer.Ordinal);
            for (var i = 0; i < definition.TypeParameters.Count; i++)
            {
                map[definition.TypeParameters[i]] = CloneTypeRef(arguments[i]);
            }

            return map;
        }

        private ClassDecl RewriteClass(ClassDecl cls, IReadOnlyDictionary<string, TypeRef>? substitution)
        {
            var baseType = cls.BaseType != null ? RewriteTypeRef(cls.BaseType, substitution) : null;
            var members = new List<ClassMember>(cls.Members.Count);
            foreach (var member in cls.Members)
            {
                switch (member)
                {
                    case FieldDecl field:
                        members.Add(new FieldDecl(field.Name, RewriteExpression(field.Init, substitution), field.Line, field.Column));
                        break;
                    case MethodDecl method:
                        members.Add(RewriteMethod(method, substitution));
                        break;
                    default:
                        members.Add(member);
                        break;
                }
            }

            return new ClassDecl(cls.Name, cls.TypeParameters, baseType, members, cls.Line, cls.Column);
        }

        private MethodDecl RewriteMethod(MethodDecl method, IReadOnlyDictionary<string, TypeRef>? substitution)
        {
            var parameters = method.Parameters
                .Select(p => new Parameter(p.Name, RewriteTypeRef(p.Type, substitution), p.Line, p.Column))
                .ToList();

            var returnType = method.ReturnType != null
                ? RewriteTypeRef(method.ReturnType, substitution)
                : null;

            MethodBody? body = method.Body switch
            {
                BlockBody block => new BlockBody(RewriteStatements(block.Statements, substitution), block.Line, block.Column),
                ExprBody exprBody => new ExprBody(RewriteExpression(exprBody.Expr, substitution), exprBody.Line, exprBody.Column),
                _ => method.Body,
            };

            return new MethodDecl(method.Name, parameters, returnType, body, method.IsConstructor, method.Line, method.Column);
        }

        private IReadOnlyList<Statement> RewriteStatements(IReadOnlyList<Statement> statements, IReadOnlyDictionary<string, TypeRef>? substitution)
        {
            var rewritten = new List<Statement>(statements.Count);
            foreach (var statement in statements)
            {
                rewritten.Add(RewriteStatement(statement, substitution));
            }

            return rewritten;
        }

        private Statement RewriteStatement(Statement statement, IReadOnlyDictionary<string, TypeRef>? substitution)
        {
            return statement switch
            {
                VarDeclStmt varDecl => new VarDeclStmt(varDecl.Name, RewriteExpression(varDecl.Init, substitution), varDecl.Line, varDecl.Column),
                AssignStmt assign => new AssignStmt(RewriteExpression(assign.Target, substitution), RewriteExpression(assign.Value, substitution), assign.Line, assign.Column),
                ExprStmt exprStmt => new ExprStmt(RewriteExpression(exprStmt.Expr, substitution), exprStmt.Line, exprStmt.Column),
                BlockStmt block => new BlockStmt(RewriteStatements(block.Statements, substitution), block.Line, block.Column),
                IfStmt ifStmt => new IfStmt(
                    RewriteExpression(ifStmt.Condition, substitution),
                    RewriteStatement(ifStmt.Then, substitution),
                    ifStmt.Else != null ? RewriteStatement(ifStmt.Else, substitution) : null,
                    ifStmt.Line,
                    ifStmt.Column),
                WhileStmt whileStmt => new WhileStmt(
                    RewriteExpression(whileStmt.Condition, substitution),
                    RewriteStatements(whileStmt.Body, substitution),
                    whileStmt.Line,
                    whileStmt.Column),
                ReturnStmt returnStmt => new ReturnStmt(
                    returnStmt.Expr != null ? RewriteExpression(returnStmt.Expr, substitution) : null,
                    returnStmt.Line,
                    returnStmt.Column),
                BreakStmt => statement,
                _ => statement,
            };
        }

        private Expression RewriteExpression(Expression expression, IReadOnlyDictionary<string, TypeRef>? substitution)
        {
            switch (expression)
            {
                case IdentifierExpr or LiteralExpr or ThisExpr:
                    return expression;
                case ParenExpr paren:
                    return new ParenExpr(RewriteExpression(paren.Inner, substitution), paren.Line, paren.Column);
                case MemberAccessExpr memberAccess:
                    return new MemberAccessExpr(RewriteExpression(memberAccess.Target, substitution), memberAccess.Member, memberAccess.Line, memberAccess.Column);
                case IndexExpr index:
                    return new IndexExpr(RewriteExpression(index.Target, substitution), RewriteExpression(index.Index, substitution), index.Line, index.Column);
                case GenericRefExpr genericRef:
                    {
                        var target = RewriteExpression(genericRef.Target, substitution);
                        var typeArgs = genericRef.TypeArguments.Select(arg => RewriteTypeRef(arg, substitution)).ToList();

                        if (target is IdentifierExpr identifier && ShouldMonomorphize(identifier.Name))
                        {
                            var specializedName = EnsureClassInstance(identifier.Name, typeArgs);
                            return new IdentifierExpr(specializedName, genericRef.Line, genericRef.Column);
                        }

                        return new GenericRefExpr(target, typeArgs, genericRef.Line, genericRef.Column);
                    }
                case CallExpr call:
                    {
                        if (call.Target is IdentifierExpr id && substitution != null && substitution.TryGetValue(id.Name, out var mapped))
                        {
                            var rewrittenArgs = call.Arguments.Select(arg => RewriteExpression(arg, substitution)).ToList();
                            return BuildConstructorCall(CloneTypeRef(mapped), rewrittenArgs, call.Line, call.Column);
                        }

                        var newTarget = RewriteExpression(call.Target, substitution);
                        var newArgs = call.Arguments.Select(arg => RewriteExpression(arg, substitution)).ToList();
                        return new CallExpr(newTarget, newArgs, call.Line, call.Column);
                    }
                default:
                    return expression;
            }
        }

        private Expression BuildConstructorCall(TypeRef type, IReadOnlyList<Expression> arguments, int line, int column)
        {
            if (ShouldMonomorphize(type.Name))
            {
                var specializedName = EnsureClassInstance(type.Name, type.TypeArguments);
                return new CallExpr(new IdentifierExpr(specializedName, line, column), arguments, line, column);
            }

            if (type.TypeArguments.Count > 0)
            {
                var target = new GenericRefExpr(new IdentifierExpr(type.Name, line, column), type.TypeArguments.Select(CloneTypeRef).ToList(), line, column);
                return new CallExpr(target, arguments, line, column);
            }

            return new CallExpr(new IdentifierExpr(type.Name, line, column), arguments, line, column);
        }

        private TypeRef RewriteTypeRef(TypeRef type, IReadOnlyDictionary<string, TypeRef>? substitution)
        {
            if (substitution != null && substitution.TryGetValue(type.Name, out var mapped) && type.TypeArguments.Count == 0)
                return CloneTypeRef(mapped, type.Line, type.Column);

            var rewrittenArgs = type.TypeArguments.Select(arg => RewriteTypeRef(arg, substitution)).ToList();

            if (ShouldMonomorphize(type.Name))
            {
                var specializedName = EnsureClassInstance(type.Name, rewrittenArgs);
                return new TypeRef(specializedName, Array.Empty<TypeRef>(), type.Line, type.Column);
            }

            return new TypeRef(type.Name, rewrittenArgs, type.Line, type.Column);
        }

        private bool ShouldMonomorphize(string typeName)
            => _genericClasses.ContainsKey(typeName) && !_builtinGenericTypes.Contains(typeName);

        private string EnsureClassInstance(string className, IReadOnlyList<TypeRef> typeArguments)
        {
            if (!_genericClasses.TryGetValue(className, out var definition))
                return className;

            if (typeArguments.Count != definition.TypeParameters.Count)
                throw new InvalidOperationException($"Generic class '{className}' expects {definition.TypeParameters.Count} type argument(s) but received {typeArguments.Count}.");

            var normalizedArgs = typeArguments.Select(CloneTypeRef).Select(arg => RewriteTypeRef(arg, substitution: null)).ToList();
            var key = CreateKey(className, normalizedArgs);

            if (_specializations.TryGetValue(key, out var existing))
                return existing.Name;

            var name = CreateUniqueName(className, normalizedArgs);
            var specialization = new Specialization(name, normalizedArgs.ToImmutableArray(), null);
            _specializations[key] = specialization;
            _pending.Enqueue(key);
            return name;
        }

        private ClassKey CreateKey(string className, IReadOnlyList<TypeRef> typeArgs)
        {
            var keys = typeArgs.Select(GetTypeKey).ToImmutableArray();
            return new ClassKey(className, keys);
        }

        private string CreateUniqueName(string className, IReadOnlyList<TypeRef> typeArgs)
        {
            var builder = new StringBuilder();
            builder.Append("__Gen_");
            AppendSanitized(builder, className);
            foreach (var arg in typeArgs)
            {
                builder.Append("__");
                builder.Append(MangleType(arg));
            }

            var baseName = builder.ToString();
            var name = baseName;
            var suffix = 1;
            while (_usedNames.Contains(name))
            {
                suffix++;
                name = baseName + "_" + suffix.ToString(CultureInfo.InvariantCulture);
            }

            _usedNames.Add(name);
            return name;
        }

        private static string GetTypeKey(TypeRef type)
        {
            if (type.TypeArguments.Count == 0)
                return type.Name;

            return $"{type.Name}[{string.Join(",", type.TypeArguments.Select(GetTypeKey))}]";
        }

        private static string MangleType(TypeRef type)
        {
            var builder = new StringBuilder();
            AppendSanitized(builder, type.Name);
            if (type.TypeArguments.Count > 0)
            {
                builder.Append("_of_");
                for (var i = 0; i < type.TypeArguments.Count; i++)
                {
                    if (i > 0)
                        builder.Append("_and_");
                    builder.Append(MangleType(type.TypeArguments[i]));
                }
            }

            return builder.ToString();
        }

        private static void AppendSanitized(StringBuilder builder, string value)
        {
            foreach (var ch in value)
            {
                if (ch <= 0x7F && (char.IsLetterOrDigit(ch) || ch == '_'))
                {
                    builder.Append(ch);
                }
                else
                {
                    builder.Append("_u");
                    builder.Append(((int)ch).ToString("X4", CultureInfo.InvariantCulture));
                }
            }
        }
    }

    private static TypeRef CloneTypeRef(TypeRef type, int line, int column)
    {
        var clonedArgs = type.TypeArguments.Select(CloneTypeRef).ToList();
        return new TypeRef(type.Name, clonedArgs, line, column);
    }

    private static TypeRef CloneTypeRef(TypeRef type)
    {
        var clonedArgs = type.TypeArguments.Select(CloneTypeRef).ToList();
        return new TypeRef(type.Name, clonedArgs, type.Line, type.Column);
    }

    private readonly record struct ClassKey(string ClassName, ImmutableArray<string> ArgumentKeys);

    private sealed record Specialization(string Name, ImmutableArray<TypeRef> TypeArguments, ClassDecl? Declaration);
}
