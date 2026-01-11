using System.Text.Json.Serialization;

namespace ToyLang.Syntax
{
    public abstract record AstNode(int Line, int Column);

    public sealed record ProgramAst(IReadOnlyList<TopLevelNode> Items) : AstNode(0, 0);

    [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
    [JsonDerivedType(typeof(ClassDecl), "class")]
    [JsonDerivedType(typeof(MethodDecl), "method")]
    [JsonDerivedType(typeof(VarDeclStmt), "var")]
    [JsonDerivedType(typeof(AssignStmt), "assign")]
    [JsonDerivedType(typeof(ExprStmt), "expr")]
    [JsonDerivedType(typeof(WhileStmt), "while")]
    [JsonDerivedType(typeof(IfStmt), "if")]
    [JsonDerivedType(typeof(ReturnStmt), "return")]
    [JsonDerivedType(typeof(BreakStmt), "break")]
    [JsonDerivedType(typeof(BlockStmt), "blockstmt")]
    public abstract record TopLevelNode(int Line, int Column) : AstNode(Line, Column);

    [JsonPolymorphic(TypeDiscriminatorPropertyName = "stmt")]
    [JsonDerivedType(typeof(VarDeclStmt), "var")]
    [JsonDerivedType(typeof(AssignStmt), "assign")]
    [JsonDerivedType(typeof(ExprStmt), "expr")]
    [JsonDerivedType(typeof(WhileStmt), "while")]
    [JsonDerivedType(typeof(IfStmt), "if")]
    [JsonDerivedType(typeof(ReturnStmt), "return")]
    [JsonDerivedType(typeof(BreakStmt), "break")]
    [JsonDerivedType(typeof(BlockStmt), "blockstmt")]
    public abstract record Statement(int Line, int Column) : TopLevelNode(Line, Column);

    [JsonPolymorphic(TypeDiscriminatorPropertyName = "expr")]
    [JsonDerivedType(typeof(IdentifierExpr), "id")]
    [JsonDerivedType(typeof(ThisExpr), "this")]
    [JsonDerivedType(typeof(LiteralExpr), "lit")]
    [JsonDerivedType(typeof(MemberAccessExpr), "member")]
    [JsonDerivedType(typeof(CallExpr), "call")]
    [JsonDerivedType(typeof(GenericRefExpr), "genref")]
    [JsonDerivedType(typeof(IndexExpr), "index")]
    [JsonDerivedType(typeof(ParenExpr), "paren")]
    public abstract record Expression(int Line, int Column) : AstNode(Line, Column);

    public sealed record IdentifierExpr(string Name, int Line, int Column) : Expression(Line, Column);
    public sealed record ThisExpr(int Line, int Column) : Expression(Line, Column);
    public sealed record LiteralExpr(string Lexeme, TokenType Kind, int Line, int Column) : Expression(Line, Column);
    public sealed record MemberAccessExpr(Expression Target, string Member, int Line, int Column) : Expression(Line, Column);
    public sealed record CallExpr(Expression Target, IReadOnlyList<Expression> Arguments, int Line, int Column) : Expression(Line, Column);
    public sealed record GenericRefExpr(Expression Target, IReadOnlyList<TypeRef> TypeArguments, int Line, int Column) : Expression(Line, Column);
    public sealed record IndexExpr(Expression Target, Expression Index, int Line, int Column) : Expression(Line, Column);
    public sealed record ParenExpr(Expression Inner, int Line, int Column) : Expression(Line, Column);

    public sealed record TypeRef(string Name, IReadOnlyList<TypeRef> TypeArguments, int Line, int Column);

    public sealed record VarDeclStmt(string Name, Expression Init, int Line, int Column) : Statement(Line, Column);
    public sealed record AssignStmt(Expression Target, Expression Value, int Line, int Column) : Statement(Line, Column);
    public sealed record ExprStmt(Expression Expr, int Line, int Column) : Statement(Line, Column);
    public sealed record IfStmt(Expression Condition, Statement Then, Statement? Else, int Line, int Column) : Statement(Line, Column);
    public sealed record WhileStmt(Expression Condition, IReadOnlyList<Statement> Body, int Line, int Column) : Statement(Line, Column);
    public sealed record ReturnStmt(Expression? Expr, int Line, int Column) : Statement(Line, Column);
    public sealed record BreakStmt(int Line, int Column) : Statement(Line, Column);
    public sealed record BlockStmt(IReadOnlyList<Statement> Statements, int Line, int Column) : Statement(Line, Column);

    [JsonPolymorphic(TypeDiscriminatorPropertyName = "member")]
    [JsonDerivedType(typeof(FieldDecl), "field")]
    [JsonDerivedType(typeof(MethodDecl), "method")]
    public abstract record ClassMember(int Line, int Column) : TopLevelNode(Line, Column);

    public sealed record ClassDecl(string Name, IReadOnlyList<string> TypeParameters, TypeRef? BaseType, IReadOnlyList<ClassMember> Members, int Line, int Column)
        : TopLevelNode(Line, Column);

    public sealed record FieldDecl(string Name, Expression Init, int Line, int Column) : ClassMember(Line, Column);

    public sealed record Parameter(string Name, TypeRef Type, int Line, int Column);

    [JsonPolymorphic(TypeDiscriminatorPropertyName = "body")]
    [JsonDerivedType(typeof(BlockBody), "block")]
    [JsonDerivedType(typeof(ExprBody), "expr")]
    public abstract record MethodBody(int Line, int Column) : AstNode(Line, Column);

    public sealed record BlockBody(IReadOnlyList<Statement> Statements, int Line, int Column) : MethodBody(Line, Column);
    public sealed record ExprBody(Expression Expr, int Line, int Column) : MethodBody(Line, Column);

    public sealed record MethodDecl(string Name, IReadOnlyList<Parameter> Parameters, TypeRef? ReturnType, MethodBody? Body, bool IsConstructor, int Line, int Column)
        : ClassMember(Line, Column);
}
