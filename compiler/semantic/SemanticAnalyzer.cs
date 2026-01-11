using ToyLang.Syntax;

namespace ToyLang.Semantic;

public sealed class SemanticAnalyzer
{
    private readonly List<Diagnostic> _errors = new();
    private readonly List<Diagnostic> _warnings = new();
    private string _source = string.Empty;
    private int[] _lineStarts = Array.Empty<int>();
    private IReadOnlyDictionary<int, List<Token>> _tokensByLine = new Dictionary<int, List<Token>>();

    private sealed class MethodSig
    {
        public string Name { get; }
        public IReadOnlyList<SimpleType> Params { get; }
        public SimpleType? Return { get; }
        public int Line { get; }
        public bool IsCtor { get; }
        public MethodSig(string name, IReadOnlyList<SimpleType> ps, SimpleType? ret, int line, bool isCtor)
        {
            Name = name; Params = ps; Return = ret; Line = line; IsCtor = isCtor;
        }
    }

    private sealed class ClassInfo
    {
        public string Name { get; }
        public string? Base { get; }
        public IReadOnlyList<string> TypeParameters { get; }
        public Dictionary<string, SimpleType> Fields { get; } = new(StringComparer.Ordinal);
        public List<MethodSig> Methods { get; } = new();
        public ClassInfo(string name, string? b, IReadOnlyList<string> typeParameters)
        {
            Name = name;
            Base = b;
            TypeParameters = typeParameters;
        }
    }

    private readonly Dictionary<string, ClassInfo> _classes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<MethodSig>> _globals = new(StringComparer.Ordinal);

    private static bool Same(SimpleType a, SimpleType b) => TypeUtils.Same(a, b);

    private Diagnostic CreateDiagnostic(Stage stage, int line, string message, Severity severity, string? hint = null)
    {
        var (columnStart, columnEnd) = SourceMapping.ResolveColumns(line, hint, _source, _lineStarts, _tokensByLine);
        return new Diagnostic(stage, line, message, severity, columnStart, columnEnd);
    }

    private void AddError(Stage stage, int line, string message, string? hint = null)
        => _errors.Add(CreateDiagnostic(stage, line, message, Severity.Error, hint));

    private void AddWarning(Stage stage, int line, string message, string? hint = null)
        => _warnings.Add(CreateDiagnostic(stage, line, message, Severity.Warning, hint));

    public SemanticReport Analyze(ProgramAst ast, string source, IReadOnlyList<Token>? tokens = null)
    {
        _source = source ?? string.Empty;
        _lineStarts = SourceMapping.ComputeLineStarts(_source);
        _tokensByLine = SourceMapping.BuildTokenLineMap(tokens);
        SeedLibrary();
        foreach (var item in ast.Items)
        {
            if (item is ClassDecl c)
            {
                if (_classes.ContainsKey(c.Name)) AddError(Stage.Semantic, c.Line, $"Duplicate class '{c.Name}'", c.Name);
                else _classes[c.Name] = new ClassInfo(c.Name, c.BaseType?.Name, c.TypeParameters);
            }
            else if (item is MethodDecl m && !m.IsConstructor)
            {
                if (!_globals.TryGetValue(m.Name, out var list)) { list = new List<MethodSig>(); _globals[m.Name] = list; }
                list.Add(new MethodSig(m.Name, m.Parameters.Select(p => TypeUtils.FromTypeRef(p.Type)).ToList(), m.ReturnType != null ? TypeUtils.FromTypeRef(m.ReturnType) : null, m.Line, false));
            }
        }
        foreach (var cdecl in ast.Items.OfType<ClassDecl>())
        {
            ValidateClass(cdecl);
        }
        foreach (var item in ast.Items)
        {
            switch (item)
            {
                case MethodDecl m:
                    ValidateMethodTopLevel(m);
                    break;
                case Statement s:
                    CheckNoReturnAtTopLevel(s);
                    CheckBreakNotInLoop(s, inLoop: false);
                    break;
            }
        }
        return new SemanticReport(_errors, _warnings);
    }

    public SemanticReport Analyze(ProgramAst ast)
        => Analyze(ast, string.Empty, null);

    private void SeedLibrary()
    {
        var intC = new ClassInfo("Integer", "AnyValue", Array.Empty<string>());
        intC.Methods.Add(new MethodSig("UnaryMinus", Array.Empty<SimpleType>(), SimpleType.Integer, 0, false));
        intC.Methods.Add(new MethodSig("Plus", [SimpleType.Integer], SimpleType.Integer, 0, false));
        intC.Methods.Add(new MethodSig("Plus", [SimpleType.Real], SimpleType.Real, 0, false));
        intC.Methods.Add(new MethodSig("Minus", [SimpleType.Integer], SimpleType.Integer, 0, false));
        intC.Methods.Add(new MethodSig("Minus", [SimpleType.Real], SimpleType.Real, 0, false));
        intC.Methods.Add(new MethodSig("Mult", [SimpleType.Integer], SimpleType.Integer, 0, false));
        intC.Methods.Add(new MethodSig("Mult", [SimpleType.Real], SimpleType.Real, 0, false));
        intC.Methods.Add(new MethodSig("Div", [SimpleType.Integer], SimpleType.Integer, 0, false));
        intC.Methods.Add(new MethodSig("Div", [SimpleType.Real], SimpleType.Real, 0, false));
        intC.Methods.Add(new MethodSig("Rem", [SimpleType.Integer], SimpleType.Integer, 0, false));
        intC.Methods.Add(new MethodSig("LessThan", [SimpleType.Integer], SimpleType.Boolean, 0, false));
        intC.Methods.Add(new MethodSig("LessThan", [SimpleType.Real], SimpleType.Boolean, 0, false));
        intC.Methods.Add(new MethodSig("LessEqual", [SimpleType.Integer], SimpleType.Boolean, 0, false));
        intC.Methods.Add(new MethodSig("LessEqual", [SimpleType.Real], SimpleType.Boolean, 0, false));
        intC.Methods.Add(new MethodSig("GreaterThan", [SimpleType.Integer], SimpleType.Boolean, 0, false));
        intC.Methods.Add(new MethodSig("GreaterThan", [SimpleType.Real], SimpleType.Boolean, 0, false));
        intC.Methods.Add(new MethodSig("GreaterEqual", [SimpleType.Integer], SimpleType.Boolean, 0, false));
        intC.Methods.Add(new MethodSig("GreaterEqual", [SimpleType.Real], SimpleType.Boolean, 0, false));
        intC.Methods.Add(new MethodSig("Equal", [SimpleType.Integer], SimpleType.Boolean, 0, false));
        intC.Methods.Add(new MethodSig("Equal", [SimpleType.Real], SimpleType.Boolean, 0, false));
        _classes[intC.Name] = intC;

        var realC = new ClassInfo("Real", "AnyValue", Array.Empty<string>());
        realC.Methods.Add(new MethodSig("UnaryMinus", Array.Empty<SimpleType>(), SimpleType.Real, 0, false));
        realC.Methods.Add(new MethodSig("Plus", [SimpleType.Real], SimpleType.Real, 0, false));
        realC.Methods.Add(new MethodSig("Plus", [SimpleType.Integer], SimpleType.Real, 0, false));
        realC.Methods.Add(new MethodSig("Minus", [SimpleType.Real], SimpleType.Real, 0, false));
        realC.Methods.Add(new MethodSig("Minus", [SimpleType.Integer], SimpleType.Real, 0, false));
        realC.Methods.Add(new MethodSig("Mult", [SimpleType.Real], SimpleType.Real, 0, false));
        realC.Methods.Add(new MethodSig("Mult", [SimpleType.Integer], SimpleType.Real, 0, false));
        realC.Methods.Add(new MethodSig("Div", [SimpleType.Integer], SimpleType.Real, 0, false));
        realC.Methods.Add(new MethodSig("Div", [SimpleType.Real], SimpleType.Real, 0, false));
        realC.Methods.Add(new MethodSig("Rem", [SimpleType.Integer], SimpleType.Real, 0, false));
        realC.Methods.Add(new MethodSig("LessThan", [SimpleType.Real], SimpleType.Boolean, 0, false));
        realC.Methods.Add(new MethodSig("LessThan", [SimpleType.Integer], SimpleType.Boolean, 0, false));
        realC.Methods.Add(new MethodSig("LessEqual", [SimpleType.Real], SimpleType.Boolean, 0, false));
        realC.Methods.Add(new MethodSig("LessEqual", [SimpleType.Integer], SimpleType.Boolean, 0, false));
        realC.Methods.Add(new MethodSig("GreaterThan", [SimpleType.Real], SimpleType.Boolean, 0, false));
        realC.Methods.Add(new MethodSig("GreaterThan", [SimpleType.Integer], SimpleType.Boolean, 0, false));
        realC.Methods.Add(new MethodSig("GreaterEqual", [SimpleType.Real], SimpleType.Boolean, 0, false));
        realC.Methods.Add(new MethodSig("GreaterEqual", [SimpleType.Integer], SimpleType.Boolean, 0, false));
        realC.Methods.Add(new MethodSig("Equal", [SimpleType.Real], SimpleType.Boolean, 0, false));
        realC.Methods.Add(new MethodSig("Equal", [SimpleType.Integer], SimpleType.Boolean, 0, false));
        _classes[realC.Name] = realC;

        var boolC = new ClassInfo("Boolean", "AnyValue", Array.Empty<string>());
        boolC.Methods.Add(new MethodSig("Or", [SimpleType.Boolean], SimpleType.Boolean, 0, false));
        boolC.Methods.Add(new MethodSig("And", [SimpleType.Boolean], SimpleType.Boolean, 0, false));
        boolC.Methods.Add(new MethodSig("Xor", [SimpleType.Boolean], SimpleType.Boolean, 0, false));
        boolC.Methods.Add(new MethodSig("Not", Array.Empty<SimpleType>(), SimpleType.Boolean, 0, false));
        boolC.Methods.Add(new MethodSig("Equal", [SimpleType.Boolean], SimpleType.Boolean, 0, false));
        _classes[boolC.Name] = boolC;

        var arrC = new ClassInfo("Array", "AnyRef", ["T"]);
        var arrElem = new SimpleType("T");
        arrC.Methods.Add(new MethodSig("Length", Array.Empty<SimpleType>(), SimpleType.Integer, 0, false));
        arrC.Methods.Add(new MethodSig("get", [SimpleType.Integer], arrElem, 0, false));
        arrC.Methods.Add(new MethodSig("set", [SimpleType.Integer, arrElem], null, 0, false));
        arrC.Methods.Add(new MethodSig("Slice", [SimpleType.Integer, SimpleType.Integer], new SimpleType("Array", [arrElem]), 0, false));
        arrC.Methods.Add(new MethodSig("Slice", [SimpleType.Integer], new SimpleType("Array", [arrElem]), 0, false));
        arrC.Methods.Add(new MethodSig("toList", Array.Empty<SimpleType>(), new SimpleType("List", [arrElem]), 0, false));
        _classes[arrC.Name] = arrC;

        var listC = new ClassInfo("List", "AnyRef", ["T"]);
        var listElem = new SimpleType("T");
        listC.Methods.Add(new MethodSig("Length", Array.Empty<SimpleType>(), SimpleType.Integer, 0, false));
        listC.Methods.Add(new MethodSig("get", [SimpleType.Integer], listElem, 0, false));
        listC.Methods.Add(new MethodSig("set", [SimpleType.Integer, listElem], null, 0, false));
        listC.Methods.Add(new MethodSig("append", [listElem], null, 0, false));
        listC.Methods.Add(new MethodSig("Slice", [SimpleType.Integer, SimpleType.Integer], new SimpleType("List", [listElem]), 0, false));
        listC.Methods.Add(new MethodSig("Slice", [SimpleType.Integer], new SimpleType("List", [listElem]), 0, false));
        listC.Methods.Add(new MethodSig("toArray", Array.Empty<SimpleType>(), new SimpleType("Array", [listElem]), 0, false));
        listC.Methods.Add(new MethodSig("head", Array.Empty<SimpleType>(), listElem, 0, false));
        listC.Methods.Add(new MethodSig("tail", Array.Empty<SimpleType>(), new SimpleType("List", [listElem]), 0, false));
        listC.Methods.Add(new MethodSig("NotEqual", [new SimpleType("List", [listElem])], SimpleType.Boolean, 0, false));
        listC.Methods.Add(new MethodSig("Equal", [new SimpleType("List", [listElem])], SimpleType.Boolean, 0, false));
        listC.Methods.Add(new MethodSig("pop", Array.Empty<SimpleType>(), listElem, 0, false));
        listC.Methods.Add(new MethodSig("pop", [SimpleType.Integer], listElem, 0, false));
        _classes[listC.Name] = listC;

        var strC = new ClassInfo("String", "AnyRef", Array.Empty<string>());
        strC.Methods.Add(new MethodSig("Concat", [SimpleType.String], SimpleType.String, 0, false));
        strC.Methods.Add(new MethodSig("Length", Array.Empty<SimpleType>(), SimpleType.Integer, 0, false));
        strC.Methods.Add(new MethodSig("ToInteger", Array.Empty<SimpleType>(), SimpleType.Integer, 0, false));
        strC.Methods.Add(new MethodSig("ToReal", Array.Empty<SimpleType>(), SimpleType.Real, 0, false));
        strC.Methods.Add(new MethodSig("ToBoolean", Array.Empty<SimpleType>(), SimpleType.Boolean, 0, false));
        strC.Methods.Add(new MethodSig("Equal", [SimpleType.String], SimpleType.Boolean, 0, false));
        strC.Methods.Add(new MethodSig("NotEqual", [SimpleType.String], SimpleType.Boolean, 0, false));
        strC.Methods.Add(new MethodSig("Split", Array.Empty<SimpleType>(), new SimpleType("List", [SimpleType.String]), 0, false));
        strC.Methods.Add(new MethodSig("Split", [SimpleType.String], new SimpleType("List", [SimpleType.String]), 0, false));
        strC.Methods.Add(new MethodSig("Split", [SimpleType.String, SimpleType.Integer], new SimpleType("List", [SimpleType.String]), 0, false));
        strC.Methods.Add(new MethodSig("Slice", [SimpleType.Integer], SimpleType.String, 0, false));
        strC.Methods.Add(new MethodSig("Slice", [SimpleType.Integer, SimpleType.Integer], SimpleType.String, 0, false));
        strC.Methods.Add(new MethodSig("StartsWith", [SimpleType.String], SimpleType.Boolean, 0, false));
        strC.Methods.Add(new MethodSig("EndsWith", [SimpleType.String], SimpleType.Boolean, 0, false));
        strC.Methods.Add(new MethodSig("Join", [new SimpleType("List", [SimpleType.String])], SimpleType.String, 0, false));
        // strC.Methods.Add(new MethodSig("IndexOf", [SimpleType.String], SimpleType.Integer, 0, false));
        strC.Methods.Add(new MethodSig("LastIndexOf", [SimpleType.String], SimpleType.Integer, 0, false));
        _classes[strC.Name] = strC;

        _classes["AnyValue"] = new ClassInfo("AnyValue", "Class", Array.Empty<string>());
        _classes["AnyRef"] = new ClassInfo("AnyRef", "Class", Array.Empty<string>());
        _classes["Class"] = new ClassInfo("Class", null, Array.Empty<string>());

        var mapC = new ClassInfo("Map", "AnyRef", ["TKey", "TValue"]);
        var mapKey = new SimpleType("TKey");
        var mapValue = new SimpleType("TValue");
        mapC.Methods.Add(new MethodSig("set", [mapKey, mapValue], null, 0, false));
        mapC.Methods.Add(new MethodSig("get", [mapKey], mapValue, 0, false));
        mapC.Methods.Add(new MethodSig("Contains", [mapKey], SimpleType.Boolean, 0, false));
        mapC.Methods.Add(new MethodSig("Remove", [mapKey], SimpleType.Boolean, 0, false));
        mapC.Methods.Add(new MethodSig("Keys", Array.Empty<SimpleType>(), new SimpleType("List", [mapKey]), 0, false));
        mapC.Methods.Add(new MethodSig("Values", Array.Empty<SimpleType>(), new SimpleType("List", [mapValue]), 0, false));
        _classes[mapC.Name] = mapC;

        var pairC = new ClassInfo("Pair", "AnyRef", ["TFirst", "TSecond"]);
        pairC.Methods.Add(new MethodSig("First", Array.Empty<SimpleType>(), new SimpleType("TFirst"), 0, false));
        pairC.Methods.Add(new MethodSig("Second", Array.Empty<SimpleType>(), new SimpleType("TSecond"), 0, false));
        _classes[pairC.Name] = pairC;

        var IOC = new ClassInfo("IO", "Class", Array.Empty<string>());
        IOC.Methods.Add(new MethodSig("Print", [new SimpleType("AnyValue")], null, 0, false));
        IOC.Methods.Add(new MethodSig("PrintLine", [new SimpleType("AnyValue")], null, 0, false));
        IOC.Methods.Add(new MethodSig("PrintInteger", [SimpleType.Integer], null, 0, false));
        IOC.Methods.Add(new MethodSig("PrintReal", [SimpleType.Real], null, 0, false));
        IOC.Methods.Add(new MethodSig("PrintBool", [SimpleType.Boolean], null, 0, false));
        IOC.Methods.Add(new MethodSig("PrintLine", Array.Empty<SimpleType>(), null, 0, false));
        IOC.Methods.Add(new MethodSig("PrintArray", [new SimpleType("AnyValue")], null, 0, false));
        IOC.Methods.Add(new MethodSig("PrintList", [new SimpleType("AnyValue")], null, 0, false));
        IOC.Methods.Add(new MethodSig("PrintMap", [new SimpleType("AnyValue")], null, 0, false));
        IOC.Methods.Add(new MethodSig("ReadLine", Array.Empty<SimpleType>(), SimpleType.String, 0, false));
        IOC.Methods.Add(new MethodSig("ReadInteger", Array.Empty<SimpleType>(), SimpleType.Integer, 0, false));
        IOC.Methods.Add(new MethodSig("ReadReal", Array.Empty<SimpleType>(), SimpleType.Real, 0, false));
        IOC.Methods.Add(new MethodSig("ReadBool", Array.Empty<SimpleType>(), SimpleType.Boolean, 0, false));
        IOC.Methods.Add(new MethodSig("FormatInteger", [SimpleType.Integer], SimpleType.String, 0, false));
        IOC.Methods.Add(new MethodSig("FormatReal", [SimpleType.Real], SimpleType.String, 0, false));
        IOC.Methods.Add(new MethodSig("FormatBool", [SimpleType.Boolean], SimpleType.String, 0, false));
        _classes[IOC.Name] = IOC;

        var mathC = new ClassInfo("Math", "Class", Array.Empty<string>());
        foreach (var fn in new[] { "Cos", "Sin", "Tan", "Acos", "Asin", "Atan", "Exp", "Log", "Sqrt" })
        {
            mathC.Methods.Add(new MethodSig(fn, [SimpleType.Real], SimpleType.Real, 0, false));
            mathC.Methods.Add(new MethodSig(fn, [SimpleType.Integer], SimpleType.Real, 0, false));
        }
        mathC.Methods.Add(new MethodSig("Atan2", [SimpleType.Real, SimpleType.Real], SimpleType.Real, 0, false));
        mathC.Methods.Add(new MethodSig("Atan2", [SimpleType.Integer, SimpleType.Integer], SimpleType.Real, 0, false));
        mathC.Methods.Add(new MethodSig("Atan2", [SimpleType.Real, SimpleType.Integer], SimpleType.Real, 0, false));
        mathC.Methods.Add(new MethodSig("Atan2", [SimpleType.Integer, SimpleType.Real], SimpleType.Real, 0, false));
        mathC.Methods.Add(new MethodSig("Pow", [SimpleType.Real, SimpleType.Real], SimpleType.Real, 0, false));
        mathC.Methods.Add(new MethodSig("Random", Array.Empty<SimpleType>(), SimpleType.Real, 0, false));
        _classes[mathC.Name] = mathC;

        var timeC = new ClassInfo("Time", "Class", Array.Empty<string>());
        timeC.Methods.Add(new MethodSig("Sleep", [SimpleType.Real], null, 0, false));
        timeC.Methods.Add(new MethodSig("PerfCounter", Array.Empty<SimpleType>(), SimpleType.Real, 0, false));
        timeC.Methods.Add(new MethodSig("Unix", Array.Empty<SimpleType>(), SimpleType.Real, 0, false));
        _classes[timeC.Name] = timeC;

        var screenC = new ClassInfo("Screen", "Class", Array.Empty<string>());
        screenC.Fields["Width"] = SimpleType.Real;
        screenC.Fields["Height"] = SimpleType.Real;
        _classes[screenC.Name] = screenC;
    }

    private static bool TryGetTypedField(FieldDecl f, HashSet<string> typeParams, out SimpleType? type)
    {
        type = null;
        if (f.Init is IdentifierExpr id && typeParams.Contains(id.Name))
        {
            type = new SimpleType(id.Name);
            return true;
        }
        return false;
    }

    private void ValidateClass(ClassDecl c)
    {
        if (c.BaseType != null && !_classes.ContainsKey(c.BaseType.Name))
            AddWarning(Stage.Semantic, c.BaseType.Line, $"Base type '{c.BaseType.Name}' is not declared", c.BaseType.Name);

        var info = _classes[c.Name];
        var typeParams = new HashSet<string>(c.TypeParameters, StringComparer.Ordinal);

        var methodNames = new Dictionary<string, List<MethodSig>>(StringComparer.Ordinal);

        foreach (var m in c.Members)
        {
            switch (m)
            {
                case FieldDecl f:
                    SimpleType? fType;
                    if (TryGetTypedField(f, typeParams, out var declType))
                        fType = declType;
                    else
                        fType = TryInferExprType(f.Init, new Dictionary<string, SimpleType>(StringComparer.Ordinal), info.Fields, typeParams, inMethod: false);

                    if (info.Fields.ContainsKey(f.Name))
                        AddError(Stage.Semantic, f.Line, $"Duplicate field '{f.Name}'", f.Name);
                    else
                        info.Fields[f.Name] = fType ?? new SimpleType("AnyValue");
                    break;
                case MethodDecl md:
                    if (!methodNames.TryGetValue(md.Name, out var list)) { list = new List<MethodSig>(); methodNames[md.Name] = list; }
                    var sig = new MethodSig(md.Name, md.Parameters.Select(p => TypeUtils.FromTypeRef(p.Type)).ToList(), md.ReturnType != null ? TypeUtils.FromTypeRef(md.ReturnType) : null, md.Line, md.IsConstructor);
                    list.Add(sig);
                    info.Methods.Add(sig);
                    break;
            }
        }

        foreach (var m in c.Members)
        {
            switch (m)
            {
                case FieldDecl f:
                    if (!TryGetTypedField(f, typeParams, out _))
                        ValidateExprSemantics(f.Init, info, new Dictionary<string, SimpleType>(StringComparer.Ordinal), typeParams, inMethod: false);
                    break;
                case MethodDecl md:
                    var local = new Dictionary<string, SimpleType>(StringComparer.Ordinal);
                    foreach (var p in md.Parameters) local[p.Name] = TypeUtils.FromTypeRef(p.Type);
                    ValidateMethodBody(md, info, local, typeParams);
                    break;
            }
        }
    }

    private void ValidateMethodTopLevel(MethodDecl m)
    {
        var local = new Dictionary<string, SimpleType>(StringComparer.Ordinal);
        foreach (var p in m.Parameters) local[p.Name] = TypeUtils.FromTypeRef(p.Type);
        ValidateMethodBody(m, null, local, null);
    }

    private void ValidateMethodBody(MethodDecl m, ClassInfo? cls, Dictionary<string, SimpleType> locals, HashSet<string>? typeParams)
    {
        if (m.ReturnType == null && m.Body is ExprBody) AddWarning(Stage.Semantic, m.Line, $"Method '{m.Name}' has expression body but no return type", m.Name);
        if (m.Body is ExprBody eb)
        {
            var t = TryInferExprType(eb.Expr, locals, cls?.Fields, typeParams, inMethod: true, currentClass: cls);
            ValidateExprSemantics(eb.Expr, cls, locals, typeParams, inMethod: true);
            if (m.ReturnType != null && t != null && !Same(t, TypeUtils.FromTypeRef(m.ReturnType))) AddError(Stage.Semantic, m.Line, $"Return type mismatch in '{m.Name}'", m.Name);
        }
        else if (m.Body is BlockBody b)
        {
            var afterReturn = false;
            foreach (var st in b.Statements)
            {
                if (afterReturn)
                    AddWarning(Stage.Semantic, st.Line, "Unreachable code", hint: null);
                ValidateStmt(st, cls, locals, typeParams, m, inLoop: false);
                if (st is ReturnStmt)
                    afterReturn = true;
            }
            if (m.ReturnType != null && !afterReturn)
                AddWarning(Stage.Semantic, m.Line, $"Method '{m.Name}' may not return a value", m.Name);
        }
        if (cls == null && m.IsConstructor) AddError(Stage.Semantic, m.Line, "Constructor declared outside of class", m.Name);
    }

    private void ValidateStmt(Statement s, ClassInfo? cls, Dictionary<string, SimpleType> locals, HashSet<string>? typeParams, MethodDecl? method, bool inLoop)
    {
        switch (s)
        {
            case VarDeclStmt v:
                if (locals.ContainsKey(v.Name))
                    AddError(Stage.Semantic, v.Line, $"Variable '{v.Name}' already declared in this scope", v.Name);
                ValidateExprSemantics(v.Init, cls, locals, typeParams, inMethod: true);
                var vt = TryInferExprType(v.Init, locals, cls?.Fields, typeParams, inMethod: true, currentClass: cls);
                locals[v.Name] = vt ?? new SimpleType("AnyValue");
                break;
            case AssignStmt a:
                if (a.Target is IdentifierExpr id)
                {
                    var t = default(SimpleType?);
                    if (locals.TryGetValue(id.Name, out var tl)) t = tl;
                    else if (cls != null && TryResolveFieldInHierarchy(cls, id.Name, out var tf)) t = tf;
                    if (t == null)
                        AddError(Stage.Semantic, a.Line, $"Assignment to undeclared variable '{id.Name}'", id.Name);
                    var vt2 = TryInferExprType(a.Value, locals, cls?.Fields, typeParams, inMethod: true, currentClass: cls);
                    ValidateExprSemantics(a.Value, cls, locals, typeParams, inMethod: true);
                    if (vt2 != null && t != null && !Same(vt2, t)) AddError(Stage.Semantic, a.Line, $"Type mismatch in assignment to '{id.Name}'", id.Name);
                }
                else if (a.Target is IndexExpr indexTarget)
                {
                    ValidateExprSemantics(indexTarget, cls, locals, typeParams, inMethod: true);
                    ValidateExprSemantics(a.Value, cls, locals, typeParams, inMethod: true);

                    var recvType = TryInferExprType(indexTarget.Target, locals, cls?.Fields, typeParams, inMethod: true, currentClass: cls);
                    var valueType = TryInferExprType(a.Value, locals, cls?.Fields, typeParams, inMethod: true, currentClass: cls);
                    var indexType = TryInferExprType(indexTarget.Index, locals, cls?.Fields, typeParams, inMethod: true, currentClass: cls);

                    if (recvType != null)
                    {
                        if (TypeUtils.IsArray(recvType))
                        {
                            if (indexType != null && !TypeUtils.IsInteger(indexType))
                                AddError(Stage.Semantic, a.Line, "Array index must be Integer", null);

                            var elementType = TypeUtils.ArrayElementType(recvType);
                            if (elementType != null && valueType != null && !Same(elementType, valueType))
                                AddError(Stage.Semantic, a.Line, $"Value assigned to Array element must be of type '{elementType}'", null);
                        }
                        else if (TypeUtils.IsList(recvType))
                        {
                            if (indexType != null && !TypeUtils.IsInteger(indexType))
                                AddError(Stage.Semantic, a.Line, "List index must be Integer", null);

                            var elementType = TypeUtils.ListElementType(recvType);
                            if (elementType != null && valueType != null && !Same(elementType, valueType))
                                AddError(Stage.Semantic, a.Line, $"Value assigned to List element must be of type '{elementType}'", null);
                        }
                        else if (TypeUtils.IsMap(recvType))
                        {
                            var mapTypes = TypeUtils.MapElementTypes(recvType);
                            if (mapTypes.HasValue)
                            {
                                if (indexType != null && !Same(mapTypes.Value.Key, indexType))
                                    AddError(Stage.Semantic, a.Line, $"Map key must be of type '{mapTypes.Value.Key}'", null);

                                if (valueType != null && !Same(mapTypes.Value.Value, valueType))
                                    AddError(Stage.Semantic, a.Line, $"Map value must be of type '{mapTypes.Value.Value}'", null);
                            }
                        }
                        else
                        {
                            AddError(Stage.Semantic, a.Line, $"Type '{recvType}' does not support indexed assignment", null);
                        }
                    }
                }
                else
                {
                    ValidateExprSemantics(a.Target, cls, locals, typeParams, inMethod: true);
                    ValidateExprSemantics(a.Value, cls, locals, typeParams, inMethod: true);
                }
                break;
            case ExprStmt es:
                ValidateExprSemantics(es.Expr, cls, locals, typeParams, inMethod: true);
                break;
            case BlockStmt block:
                foreach (var st in block.Statements)
                    ValidateStmt(st, cls, locals, typeParams, method, inLoop);
                break;
            case IfStmt i:
                ValidateExprSemantics(i.Condition, cls, locals, typeParams, inMethod: true);
                var thenLocals = new Dictionary<string, SimpleType>(locals, StringComparer.Ordinal);
                ValidateStmt(i.Then, cls, thenLocals, typeParams, method, inLoop);
                if (i.Else != null)
                {
                    var elseLocals = new Dictionary<string, SimpleType>(locals, StringComparer.Ordinal);
                    ValidateStmt(i.Else, cls, elseLocals, typeParams, method, inLoop);
                }
                break;
            case WhileStmt w:
                ValidateExprSemantics(w.Condition, cls, locals, typeParams, inMethod: true);
                var wt = TryInferExprType(w.Condition, locals, cls?.Fields, typeParams, inMethod: true, currentClass: cls);
                if (wt != null && !TypeUtils.IsBoolean(wt))
                    AddError(Stage.Semantic, w.Line, "While condition must be Boolean", "while");
                var loopLocals = new Dictionary<string, SimpleType>(locals, StringComparer.Ordinal);
                foreach (var st in w.Body)
                    ValidateStmt(st, cls, loopLocals, typeParams, method, inLoop: true);
                break;
            case ReturnStmt r:
                if (method == null) AddError(Stage.Semantic, r.Line, "Return used outside of method", "return");
                var mret = method?.ReturnType != null ? TypeUtils.FromTypeRef(method.ReturnType) : null;
                if (r.Expr != null)
                {
                    var rt = TryInferExprType(r.Expr, locals, cls?.Fields, typeParams, inMethod: true, currentClass: cls);
                    ValidateExprSemantics(r.Expr, cls, locals, typeParams, inMethod: true);
                    if (mret == null) AddError(Stage.Semantic, r.Line, "Return with a value in a void method", "return");
                    else if (rt != null && !Same(rt, mret)) AddError(Stage.Semantic, r.Line, "Return type does not match method signature", "return");
                }
                else
                {
                    if (mret != null) AddError(Stage.Semantic, r.Line, "Return without a value in a non-void method", "return");
                }
                break;
            case BreakStmt b:
                if (!inLoop)
                    AddError(Stage.Semantic, b.Line, "Break used outside of a loop", "break");
                break;
        }
    }

    private void CheckNoReturnAtTopLevel(Statement s)
    {
        switch (s)
        {
            case ReturnStmt r:
                AddError(Stage.Semantic, r.Line, "Return used at top-level", "return");
                break;
            case BlockStmt block:
                foreach (var st in block.Statements)
                    CheckNoReturnAtTopLevel(st);
                break;
        }
    }

    private void CheckBreakNotInLoop(Statement s, bool inLoop)
    {
        switch (s)
        {
            case BreakStmt b:
                if (!inLoop)
                    AddError(Stage.Semantic, b.Line, "Break used outside of a loop", "break");
                break;
            case IfStmt i:
                CheckBreakNotInLoop(i.Then, inLoop);
                if (i.Else != null) CheckBreakNotInLoop(i.Else, inLoop);
                break;
            case WhileStmt w:
                foreach (var st in w.Body)
                    CheckBreakNotInLoop(st, true);
                break;
            case BlockStmt block:
                foreach (var st in block.Statements)
                    CheckBreakNotInLoop(st, inLoop);
                break;
        }
    }

    private void ValidateExprSemantics(Expression e, ClassInfo? cls, Dictionary<string, SimpleType> locals, HashSet<string>? typeParams, bool inMethod)
    {
        switch (e)
        {
            case IdentifierExpr id:
                if (!locals.ContainsKey(id.Name) &&
                    !(inMethod && cls != null && TryResolveFieldInHierarchy(cls, id.Name, out _)) &&
                    !_globals.ContainsKey(id.Name) &&
                    !_classes.ContainsKey(id.Name))
                    AddError(Stage.Semantic, id.Line, $"Identifier '{id.Name}' is not declared", id.Name);
                break;
            case ThisExpr th:
                if (!inMethod) AddError(Stage.Semantic, th.Line, "'this' used outside of a method", "this");
                break;
            case LiteralExpr:
                break;
            case MemberAccessExpr ma:
                ValidateExprSemantics(ma.Target, cls, locals, typeParams, inMethod);
                break;
            case CallExpr call:
                ValidateExprSemantics(call.Target, cls, locals, typeParams, inMethod);
                foreach (var a in call.Arguments) ValidateExprSemantics(a, cls, locals, typeParams, inMethod);
                var tt = TryInferExprType(call.Target, locals, cls?.Fields, typeParams, inMethod, cls);
                if (call.Target is IdentifierExpr fn && _globals.TryGetValue(fn.Name, out var sigs))
                {
                    var argTypes = call.Arguments.Select(a => TryInferExprType(a, locals, cls?.Fields, typeParams, inMethod, cls)).ToList();
                    if (!sigs.Any(s => s.Params.Count == argTypes.Count && CheckArgs(s.Params, argTypes, substitution: null)))
                        _errors.Add(new Diagnostic(Stage.Semantic, call.Line, $"No matching function '{fn.Name}' for given arguments", Severity.Error));
                }
                else if (call.Target is MemberAccessExpr mem)
                {
                    var recvT = TryInferExprType(mem.Target, locals, cls?.Fields, typeParams, inMethod, cls);
                    var argTypes = call.Arguments.Select(a => TryInferExprType(a, locals, cls?.Fields, typeParams, inMethod, cls)).ToList();
                    if (recvT != null)
                    {
                        if (!TryResolveMethod(recvT, mem.Member, argTypes))
                            _warnings.Add(new Diagnostic(Stage.Semantic, call.Line, $"Method '{mem.Member}' not verified on receiver '{recvT}' for arguments signature '({string.Join(", ", argTypes.Select(t => t?.ToString() ?? "Unknown"))})'", Severity.Warning));
                    }
                }
                else if (call.Target is GenericRefExpr gr && gr.Target is IdentifierExpr gid && string.Equals(gid.Name, "Array", StringComparison.Ordinal))
                {
                    if (call.Arguments.Count != 1)
                    {
                        _errors.Add(new Diagnostic(Stage.Semantic, call.Line, "Array constructor expects exactly one length argument", Severity.Error));
                    }
                    else
                    {
                        var argType = TryInferExprType(call.Arguments[0], locals, cls?.Fields, typeParams, inMethod, cls);
                        if (argType != null && !TypeUtils.IsInteger(argType))
                            _errors.Add(new Diagnostic(Stage.Semantic, call.Line, "Array length must be Integer", Severity.Error));
                    }
                }
                break;
            case GenericRefExpr gr:
                ValidateExprSemantics(gr.Target, cls, locals, typeParams, inMethod);
                break;
            case IndexExpr ix:
                ValidateExprSemantics(ix.Target, cls, locals, typeParams, inMethod);
                ValidateExprSemantics(ix.Index, cls, locals, typeParams, inMethod);
                var recvType = TryInferExprType(ix.Target, locals, cls?.Fields, typeParams, inMethod, cls);
                var indexType = TryInferExprType(ix.Index, locals, cls?.Fields, typeParams, inMethod, cls);

                if (recvType != null)
                {
                    if (TypeUtils.IsArray(recvType))
                    {
                        if (indexType != null && !TypeUtils.IsInteger(indexType))
                            _errors.Add(new Diagnostic(Stage.Semantic, ix.Line, "Array index must be Integer", Severity.Error));

                        if (ix.Index is LiteralExpr lit && lit.Kind == TokenType.Integer)
                        {
                            if (int.TryParse(lit.Lexeme, out var idx) && idx < 0)
                                _errors.Add(new Diagnostic(Stage.Semantic, ix.Line, "Array index must be non-negative", Severity.Error));

                            var len = TryConstArrayLength(ix.Target, locals, cls?.Fields);
                            if (len.HasValue && idx >= len.Value)
                                _warnings.Add(new Diagnostic(Stage.Semantic, ix.Line, "Array index may be out of bounds", Severity.Warning));
                        }
                    }
                    else if (TypeUtils.IsList(recvType))
                    {
                        if (indexType != null && !TypeUtils.IsInteger(indexType))
                            _errors.Add(new Diagnostic(Stage.Semantic, ix.Line, "List index must be Integer", Severity.Error));
                    }
                    else if (TypeUtils.IsMap(recvType))
                    {
                        if (indexType != null)
                        {
                            var mapTypes = TypeUtils.MapElementTypes(recvType);
                            if (mapTypes.HasValue)
                            {
                                var keyType = mapTypes.Value.Key;
                                if (!TypeUtils.Same(keyType, indexType))
                                    _errors.Add(new Diagnostic(Stage.Semantic, ix.Line, $"Map key must be of type '{keyType}'", Severity.Error));
                            }
                        }
                    }
                    else
                    {
                        _errors.Add(new Diagnostic(Stage.Semantic, ix.Line, $"Type '{recvType}' does not support indexing", Severity.Error));
                    }
                }
                break;
            case ParenExpr pe:
                ValidateExprSemantics(pe.Inner, cls, locals, typeParams, inMethod);
                break;
        }
    }

    private bool CheckArgs(IReadOnlyList<SimpleType> ps, IReadOnlyList<SimpleType?> args, IReadOnlyDictionary<string, SimpleType>? substitution)
    {
        if (ps.Count != args.Count) return false;
        for (int i = 0; i < ps.Count; i++)
        {
            if (args[i] == null)
                return false;

            var expected = substitution != null ? SubstituteType(ps[i], substitution!) : ps[i];

            if (!Same(expected, args[i]!) && expected.Name != "AnyValue")
                return false;
        }
        return true;
    }

    private static IReadOnlyDictionary<string, SimpleType>? BuildClassSubstitution(ClassInfo info, SimpleType receiver)
    {
        if (info.TypeParameters.Count == 0)
            return null;

        if (!string.Equals(info.Name, receiver.Name, StringComparison.Ordinal))
            return null;

        if (info.TypeParameters.Count != receiver.TypeArgs.Count)
            return null;

        var map = new Dictionary<string, SimpleType>(StringComparer.Ordinal);
        for (var i = 0; i < info.TypeParameters.Count; i++)
        {
            map[info.TypeParameters[i]] = receiver.TypeArgs[i];
        }

        return map;
    }

    private static SimpleType SubstituteType(SimpleType type, IReadOnlyDictionary<string, SimpleType> substitution)
    {
        if (type.TypeArgs.Count == 0 && substitution.TryGetValue(type.Name, out var mapped))
            return mapped;

        if (type.TypeArgs.Count == 0)
            return type;

        var newArgs = new SimpleType[type.TypeArgs.Count];
        var changed = false;
        for (var i = 0; i < type.TypeArgs.Count; i++)
        {
            var replaced = SubstituteType(type.TypeArgs[i], substitution);
            newArgs[i] = replaced;
            if (!ReferenceEquals(replaced, type.TypeArgs[i]))
                changed = true;
        }

        return changed ? new SimpleType(type.Name, newArgs) : type;
    }

    private static SimpleType? SubstituteTypeIfNeeded(SimpleType? type, IReadOnlyDictionary<string, SimpleType>? substitution)
        => type == null || substitution == null ? type : SubstituteType(type!, substitution!);

    private bool TryResolveMethod(SimpleType recv, string name, IReadOnlyList<SimpleType?> args)
    {
        if (TypeUtils.IsArray(recv))
        {
            if (string.Equals(name, "Length", StringComparison.Ordinal))
                return args.Count == 0;

            if (string.Equals(name, "get", StringComparison.Ordinal))
            {
                if (args.Count != 1) return false;
                var indexType = args[0];
                if (indexType == null) return true;
                return TypeUtils.IsInteger(indexType);
            }

            if (string.Equals(name, "set", StringComparison.Ordinal))
            {
                if (args.Count != 2) return false;
                var indexType = args[0];
                if (indexType != null && !TypeUtils.IsInteger(indexType)) return false;
                var element = TypeUtils.ArrayElementType(recv);
                var valueType = args[1];
                if (element == null || valueType == null)
                    return true;
                return TypeUtils.Same(element, valueType);
            }
        }

        if (TypeUtils.IsList(recv))
        {
            if (string.Equals(name, "head", StringComparison.Ordinal))
                return args.Count == 0;

            if (string.Equals(name, "tail", StringComparison.Ordinal))
                return args.Count == 0;

            if (string.Equals(name, "append", StringComparison.Ordinal))
            {
                if (args.Count != 1) return false;
                var element = TypeUtils.ListElementType(recv);
                var valueType = args[0];
                if (element == null || valueType == null)
                    return true;
                return TypeUtils.Same(element, valueType);
            }

            if (string.Equals(name, "get", StringComparison.Ordinal))
            {
                if (args.Count != 1) return false;
                var indexType = args[0];
                if (indexType == null)
                    return true;
                return TypeUtils.IsInteger(indexType);
            }

            if (string.Equals(name, "set", StringComparison.Ordinal))
            {
                if (args.Count != 2) return false;
                var indexType = args[0];
                if (indexType != null && !TypeUtils.IsInteger(indexType))
                    return false;

                var element = TypeUtils.ListElementType(recv);
                var valueType = args[1];
                if (element == null || valueType == null)
                    return true;
                return TypeUtils.Same(element, valueType);
            }

            if (string.Equals(name, "NotEqual", StringComparison.Ordinal))
            {
                if (args.Count != 1) return false;
                var otherType = args[0];
                if (otherType == null)
                    return true;
                return TypeUtils.Same(recv, otherType);
            }
        }

        if (TypeUtils.IsMap(recv))
        {
            var pair = TypeUtils.MapElementTypes(recv);
            if (!pair.HasValue)
                return false;

            var (keyType, valueType) = pair.Value;

            switch (name)
            {
                case "set":
                    if (args.Count != 2) return false;
                    var setKey = args[0];
                    var setValue = args[1];
                    if (setKey != null && !TypeUtils.Same(keyType, setKey))
                        return false;
                    if (setValue != null && !TypeUtils.Same(valueType, setValue))
                        return false;
                    return true;
                case "get":
                    if (args.Count != 1) return false;
                    var getKey = args[0];
                    if (getKey != null && !TypeUtils.Same(keyType, getKey))
                        return false;
                    return true;
                case "Contains":
                case "Remove":
                    if (args.Count != 1) return false;
                    var cmpKey = args[0];
                    if (cmpKey != null && !TypeUtils.Same(keyType, cmpKey))
                        return false;
                    return true;
                case "Keys":
                case "Values":
                    return args.Count == 0;
            }
        }

        if (_classes.TryGetValue(recv.Name, out var ci))
        {
            var cur = ci;
            var substitution = BuildClassSubstitution(cur, recv);
            while (cur != null)
            {
                var methods = cur.Methods.Where(m => string.Equals(m.Name, name, StringComparison.Ordinal)).ToList();
                if (methods.Any(m => CheckArgs(m.Params, args, substitution)))
                    return true;

                cur = cur.Base != null && _classes.TryGetValue(cur.Base, out var b) ? b : null;
                substitution = null;
            }
        }

        return false;
    }

    private int? TryConstArrayLength(Expression target, Dictionary<string, SimpleType> locals, Dictionary<string, SimpleType>? fields)
    {
        if (target is CallExpr c && c.Target is IdentifierExpr id && id.Name == "Array" && c.Arguments.Count == 1 && c.Arguments[0] is LiteralExpr lit && lit.Kind == TokenType.Integer && int.TryParse(lit.Lexeme, out var n)) return n;
        if (target is IdentifierExpr v && locals.ContainsKey(v.Name)) return null;
        if (target is IdentifierExpr f && fields != null && fields.ContainsKey(f.Name)) return null;
        return null;
    }

    private SimpleType? TryInferExprType(Expression e, Dictionary<string, SimpleType> locals, Dictionary<string, SimpleType>? fields, HashSet<string>? typeParams, bool inMethod, ClassInfo? currentClass = null)
    {
        switch (e)
        {
            case LiteralExpr lit:
                if (lit.Kind == TokenType.Integer) return SimpleType.Integer;
                if (lit.Kind == TokenType.Real) return SimpleType.Real;
                if (lit.Kind == TokenType.Boolean) return SimpleType.Boolean;
                if (lit.Kind == TokenType.String) return SimpleType.String;
                return null;
            case IdentifierExpr id:
                if (locals.TryGetValue(id.Name, out var t)) return t;
                if (inMethod && fields != null)
                {
                    if (fields.TryGetValue(id.Name, out var ft)) return ft;
                    if (currentClass != null && TryResolveFieldInHierarchy(currentClass, id.Name, out var inh)) return inh;
                }
                if (_classes.ContainsKey(id.Name)) return new SimpleType(id.Name);
                return null;
            case ThisExpr:
                return currentClass != null ? new SimpleType(currentClass.Name) : null;
            case ParenExpr pe:
                return TryInferExprType(pe.Inner, locals, fields, typeParams, inMethod, currentClass);
            case GenericRefExpr gr:
                {
                    var args = gr.TypeArguments.Select(TypeUtils.FromTypeRef).ToList();
                    if (gr.Target is IdentifierExpr gid)
                        return new SimpleType(gid.Name, args);

                    var targetType = TryInferExprType(gr.Target, locals, fields, typeParams, inMethod, currentClass);
                    return targetType != null ? new SimpleType(targetType.Name, args) : null;
                }
            case MemberAccessExpr ma:
                {
                    var recv = TryInferExprType(ma.Target, locals, fields, typeParams, inMethod, currentClass);
                    if (recv == null) return null;
                    if (TypeUtils.IsArray(recv) && string.Equals(ma.Member, "Length", StringComparison.Ordinal)) return SimpleType.Integer;
                    if (_classes.TryGetValue(recv.Name, out var ci))
                    {
                        var substitution = BuildClassSubstitution(ci, recv);
                        if (TryResolveFieldInHierarchy(ci, ma.Member, out var ftt, substitution)) return ftt;
                    }
                    return null;
                }
            case IndexExpr ix:
                {
                    var recv = TryInferExprType(ix.Target, locals, fields, typeParams, inMethod, currentClass);
                    if (recv == null) return null;
                    if (TypeUtils.IsArray(recv))
                        return TypeUtils.ArrayElementType(recv);
                    if (TypeUtils.IsList(recv))
                        return TypeUtils.ListElementType(recv);
                    if (TypeUtils.IsMap(recv))
                    {
                        var mapTypes = TypeUtils.MapElementTypes(recv);
                        return mapTypes.HasValue ? mapTypes.Value.Value : null;
                    }
                    return null;
                }
            case CallExpr c:
                {
                    if (c.Target is GenericRefExpr gr)
                    {
                        var typeArgs = gr.TypeArguments.Select(TypeUtils.FromTypeRef).ToList();
                        if (gr.Target is IdentifierExpr gid)
                        {
                            if (string.Equals(gid.Name, "Array", StringComparison.Ordinal))
                            {
                                var element = typeArgs.Count == 1 ? typeArgs[0] : new SimpleType("AnyValue");
                                return SimpleType.ArrayOf(element);
                            }

                            if (_classes.ContainsKey(gid.Name))
                                return new SimpleType(gid.Name, typeArgs);
                        }

                        var targetType = TryInferExprType(gr.Target, locals, fields, typeParams, inMethod, currentClass);
                        if (targetType != null)
                            return new SimpleType(targetType.Name, typeArgs);
                    }

                    if (c.Target is IdentifierExpr id)
                    {
                        if (_classes.ContainsKey(id.Name))
                        {
                            if (string.Equals(id.Name, "Array", StringComparison.Ordinal))
                            {
                                if (c.Target is IdentifierExpr) return new SimpleType("Array", [new SimpleType("AnyValue")]);
                                return new SimpleType("Array", [new SimpleType("AnyValue")]);
                            }
                            return new SimpleType(id.Name);
                        }
                        if (_globals.TryGetValue(id.Name, out var list))
                        {
                            var args = c.Arguments.Select(a => TryInferExprType(a, locals, fields, typeParams, inMethod, currentClass)).ToList();
                            foreach (var s in list)
                            {
                                if (CheckArgs(s.Params, args, substitution: null)) return s.Return;
                            }
                            return null;
                        }
                    }
                    if (c.Target is MemberAccessExpr mem)
                    {
                        var recv = TryInferExprType(mem.Target, locals, fields, typeParams, inMethod, currentClass);
                        var args = c.Arguments.Select(a => TryInferExprType(a, locals, fields, typeParams, inMethod, currentClass)).ToList();
                        if (recv == null) return null;
                        if (TypeUtils.IsArray(recv))
                        {
                            if (string.Equals(mem.Member, "get", StringComparison.Ordinal) && args.Count == 1 && args[0] != null && TypeUtils.IsInteger(args[0]!)) return TypeUtils.ArrayElementType(recv);
                            if (string.Equals(mem.Member, "set", StringComparison.Ordinal) && args.Count == 2 && args[0] != null && TypeUtils.IsInteger(args[0]!)) return null;
                            if (string.Equals(mem.Member, "Length", StringComparison.Ordinal) && args.Count == 0) return SimpleType.Integer;
                        }
                        if (TypeUtils.IsMap(recv))
                        {
                            var mapTypes = TypeUtils.MapElementTypes(recv);
                            if (mapTypes.HasValue)
                            {
                                switch (mem.Member)
                                {
                                    case "set":
                                        return null;
                                    case "get":
                                        return mapTypes.Value.Value;
                                    case "Contains":
                                    case "Remove":
                                        return SimpleType.Boolean;
                                    case "Keys":
                                        return new SimpleType("List", [mapTypes.Value.Key]);
                                    case "Values":
                                        return new SimpleType("List", [mapTypes.Value.Value]);
                                }
                            }
                        }
                        if (_classes.TryGetValue(recv.Name, out var ci))
                        {
                            var cur = ci;
                            var substitution = BuildClassSubstitution(cur, recv);
                            while (cur != null)
                            {
                                var ms = cur.Methods.Where(m => string.Equals(m.Name, mem.Member, StringComparison.Ordinal)).ToList();
                                foreach (var s in ms)
                                {
                                    if (CheckArgs(s.Params, args, substitution))
                                        return SubstituteTypeIfNeeded(s.Return, substitution);
                                }
                                cur = cur.Base != null && _classes.TryGetValue(cur.Base, out var b) ? b : null;
                                substitution = null;
                            }
                        }
                    }
                    return null;
                }
            default:
                return null;
        }
    }

    private bool TryResolveFieldInHierarchy(ClassInfo ci, string name, out SimpleType? type, IReadOnlyDictionary<string, SimpleType>? substitution = null)
    {
        var cur = ci;
        while (cur != null)
        {
            if (cur.Fields.TryGetValue(name, out var t))
            {
                type = SubstituteTypeIfNeeded(t, substitution);
                return true;
            }
            cur = cur.Base != null && _classes.TryGetValue(cur.Base, out var b) ? b : null;
            substitution = null;
        }
        type = null;
        return false;
    }
}
