using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;
using System.Runtime.Loader;

namespace XafReportParametersObjects.Module.Services;

public sealed class ReportParameterCompiler
{
    private AssemblyLoadContext? _loadContext;
    private Assembly? _currentAssembly;

    public Assembly? CurrentAssembly => _currentAssembly;

    public CompilationResult Compile(string source, string assemblyName = "DynamicReportParameters")
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToList();

        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release));

        using var ms = new MemoryStream();
        var result = compilation.Emit(ms);

        if (!result.Success)
        {
            var errors = result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString())
                .ToList();

            return CompilationResult.Failed(errors);
        }

        ms.Seek(0, SeekOrigin.Begin);

        _loadContext = new AssemblyLoadContext(assemblyName, isCollectible: false);
        _currentAssembly = _loadContext.LoadFromStream(ms);

        return CompilationResult.Succeeded(_currentAssembly);
    }

    public Type? GetGeneratedType(string fullTypeName)
    {
        return _currentAssembly?.GetType(fullTypeName);
    }
}

public record CompilationResult
{
    public bool Success { get; init; }
    public Assembly? Assembly { get; init; }
    public List<string> Errors { get; init; } = new();

    public static CompilationResult Succeeded(Assembly assembly) =>
        new() { Success = true, Assembly = assembly };

    public static CompilationResult Failed(List<string> errors) =>
        new() { Success = false, Errors = errors };
}
