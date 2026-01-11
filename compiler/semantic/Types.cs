using ToyLang.Syntax;

namespace ToyLang.Semantic;

public enum Severity
{
    Info,
    Warning,
    Error
}

public enum Stage
{
    Lex,
    Parse,
    Semantic,
    Optimize
}

public sealed record Diagnostic(
    Stage Stage,
    int Line,
    string Message,
    Severity Severity,
    int? Start = null,
    int? End = null
);

public sealed record SemanticReport(
    IReadOnlyList<Diagnostic> Errors,
    IReadOnlyList<Diagnostic> Warnings
);

public sealed record PipelineOutput(
    IReadOnlyList<Token> Tokens,
    ProgramAst? Ast,
    SemanticReport Semantic,
    ProgramAst? OptimizedAst,
    Diagnostic? StageError,
    IReadOnlyList<Optimizer.OptimizationStep>? Optimizations,
    string? OptimizedSource,
    string? WasmModuleBase64
);
