using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ToyLang.Semantic;
using ToyLang.Syntax;
using ToyLang.Wasm;

public class Program
{
    public static void Main(string[] args)
    {
        string? inputFile = null;
        string? outputFile = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-i":
                    if (i + 1 < args.Length)
                    {
                        inputFile = args[++i];
                    }
                    else
                    {
                        Console.WriteLine("Error: Missing file name for -i option.");
                        return;
                    }
                    break;
                case "-o":
                    if (i + 1 < args.Length)
                    {
                        outputFile = args[++i];
                    }
                    else
                    {
                        Console.WriteLine("Error: Missing file name for -o option.");
                        return;
                    }
                    break;
                default:
                    if (inputFile == null && !args[i].StartsWith("-"))
                    {
                        inputFile = args[i];
                    }
                    else
                    {
                        Console.WriteLine($"Error: Unknown option or multiple input files specified: {args[i]}");
                        return;
                    }
                    break;
            }
        }

        string sourceCode = "";

        if (inputFile != null)
        {
            try
            {
                sourceCode = File.ReadAllText(inputFile);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error reading file '{inputFile}': {e.Message}");
                return;
            }
        }
        else
        {
            Console.WriteLine("Enter source code (press Ctrl+D or Ctrl+Z then Enter to finish):");
            sourceCode = Console.In.ReadToEnd();
        }

        if (string.IsNullOrEmpty(sourceCode))
        {
            Console.WriteLine("No source code provided.");
            return;
        }

        var lineStarts = SourceMapping.ComputeLineStarts(sourceCode);

        TextWriter writer = outputFile != null
            ? new StreamWriter(outputFile, false, new UTF8Encoding(false))
            : Console.Out;

        try
        {
            var options = new JsonSerializerOptions
            {
                Converters = { new JsonStringEnumConverter() },
                MaxDepth = 2048,
                WriteIndented = false
            };

            IReadOnlyList<Token> tokens;
            IReadOnlyDictionary<int, List<Token>> tokensByLine;
            try
            {
                tokens = new Lexer(sourceCode).ScanTokens();
                tokensByLine = SourceMapping.BuildTokenLineMap(tokens);
            }
            catch (SyntaxError le)
            {
                var startOffset = SourceMapping.LineColumnToOffset(le.Line, le.Column, lineStarts, sourceCode);
                var endOffset = startOffset.HasValue ? Math.Min(startOffset.Value + 1, sourceCode.Length) : (int?)null;
                var lexErr = new Diagnostic(Stage.Lex, le.Line, le.Message, Severity.Error, startOffset, endOffset);
                var output = new PipelineOutput(Array.Empty<Token>(), null, new SemanticReport(Array.Empty<Diagnostic>(), Array.Empty<Diagnostic>()), null, lexErr, null, null, null);
                writer.WriteLine(JsonSerializer.Serialize(output, options));
                return;
            }



            ProgramAst? ast = null;
            try
            {
                var analyzer = new Analyzer();
                ast = analyzer.analyze(tokens);
            }
            catch (SyntaxError pe)
            {
                var startOffset = SourceMapping.LineColumnToOffset(pe.Line, pe.Column, lineStarts, sourceCode);
                var endOffset = startOffset.HasValue ? Math.Min(startOffset.Value + 1, sourceCode.Length) : (int?)null;
                var parseErr = new Diagnostic(Stage.Parse, pe.Line, pe.Message, Severity.Error, startOffset, endOffset);
                var output = new PipelineOutput(tokens, null, new SemanticReport(Array.Empty<Diagnostic>(), Array.Empty<Diagnostic>()), null, parseErr, null, null, null);
                writer.WriteLine(JsonSerializer.Serialize(output, options));
                return;
            }

            var sema = new SemanticAnalyzer();
            var report = sema.Analyze(ast, sourceCode, tokens);

            ProgramAst? optimized = null;
            List<Optimizer.OptimizationStep>? steps = null;
            string? optimizedSource = null;
            string? wasmModuleBase64 = null;
            Diagnostic? stageError = null;
            var res = Optimizer.OptimizeWithReport(ast);
            optimized = res.Program;
            steps = res.Steps;
            optimizedSource = optimized != null ? CodePrinter.PrintProgram(optimized) : null;
            if (steps != null)
            {
                steps = steps.Select(step =>
                {
                    var hint = step.Hint ?? ExtractHint(step.Before);
                    var resolvedLine = SourceMapping.ResolveLine(step.Line, hint, tokensByLine) ?? step.Line;
                    var (startOffset, endOffset) = SourceMapping.ResolveSpan(resolvedLine, hint, sourceCode, lineStarts, tokensByLine);
                    return step with { Line = resolvedLine, Start = startOffset, End = endOffset };
                }).ToList();
            }
            if (optimized != null)
            {
                try
                {
                    var wasmBytes = WasmCompiler.Compile(optimized);
                    wasmModuleBase64 = Convert.ToBase64String(wasmBytes);
                }
                catch (Exception ex)
                {
                    wasmModuleBase64 = null;
                    stageError = new Diagnostic(Stage.Optimize, 0, ex.Message, Severity.Error);
                }
            }

            var result = new PipelineOutput(tokens, ast, report, optimized, stageError, steps, optimizedSource, wasmModuleBase64);
            writer.WriteLine(JsonSerializer.Serialize(result, options));
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e.Message);
            Environment.ExitCode = 1;
        }
        finally
        {
            if (!ReferenceEquals(writer, Console.Out))
                writer.Dispose();
        }
    }

    private static string? ExtractHint(string? before)
    {
        if (string.IsNullOrWhiteSpace(before))
            return null;

        var trimmed = before.TrimStart();
        var newlineIndex = trimmed.IndexOfAny(new[] { '\r', '\n' });
        if (newlineIndex >= 0)
            trimmed = trimmed[..newlineIndex];

        if (trimmed.Length == 0)
            return null;

        var start = 0;
        while (start < trimmed.Length && !char.IsLetter(trimmed[start]) && trimmed[start] != '_')
            start++;

        if (start >= trimmed.Length)
            return null;

        var end = start;
        while (end < trimmed.Length && (char.IsLetterOrDigit(trimmed[end]) || trimmed[end] == '_'))
            end++;

        if (end == start)
            return null;

        return trimmed[start..end];
    }
}
