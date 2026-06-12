using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;
using DevExpress.ExpressApp.ReportsV2;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.EF;
using DevExpress.XtraReports.UI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using XafReportParametersObjects.Module.BusinessObjects;
using XafReportParametersObjects.Module.Services;

namespace XafReportParametersObjects.Module.Controllers;

public class GenerateParameterObjectController : ViewController<DetailView>
{
    private readonly SimpleAction _generateAction;

    public GenerateParameterObjectController()
    {
        TargetObjectType = typeof(ReportParameterDefinition);

        _generateAction = new SimpleAction(this, "GenerateParameterObject", PredefinedCategory.Edit)
        {
            Caption = "Generate Parameter Object",
            ToolTip = "Inspect the linked report and generate a ReportParametersObjectBase source file",
            ImageName = "Action_Reload"
        };
        _generateAction.Execute += GenerateAction_Execute;
    }

    private void GenerateAction_Execute(object sender, SimpleActionExecuteEventArgs e)
    {
        var definition = (ReportParameterDefinition)View.CurrentObject;

        if (definition.Report is null)
        {
            Application.ShowViewStrategy.ShowMessage("Please link a Report before generating.");
            return;
        }

        var reportStorage = Application.ServiceProvider.GetRequiredService<IReportStorage>();
        XtraReport report;
        try
        {
            report = reportStorage.LoadReport(definition.Report);
        }
        catch (Exception ex)
        {
            Application.ShowViewStrategy.ShowMessage($"Failed to load report: {ex.Message}");
            return;
        }

        try
        {
            var result = ReportParameterInspector.Inspect(report);

            if (result.Parameters.Count == 0)
            {
                Application.ShowViewStrategy.ShowMessage("The report has no parameters.");
                return;
            }

            // Update metadata
            definition.IsStale = false;
            definition.Status = ReportParameterStatus.Generated;
            definition.ParameterSignatureHash = result.SignatureHash;

            // Preserve user customizations across regeneration (matched by parameter name)
            var oldSettings = definition.Fields.ToDictionary(
                f => f.ParameterName,
                f => (f.IncludeInCriteria, f.CriteriaPropertyPath));

            // Clear existing field metadata
            while (definition.Fields.Count > 0)
            {
                var field = definition.Fields[0];
                definition.Fields.Remove(field);
                View.ObjectSpace.Delete(field);
            }

            // Determine the report's data source business object type
            var boType = typeof(object);
            if (report.DataSource is DevExpress.Persistent.Base.ReportsV2.CollectionDataSource cds
                && !string.IsNullOrEmpty(cds.ObjectTypeName))
            {
                var resolved = Type.GetType(cds.ObjectTypeName);
                if (resolved is not null)
                    boType = resolved;
            }

            foreach (var param in result.Parameters)
            {
                var field = View.ObjectSpace.CreateObject<ReportParameterFieldDefinition>();
                field.ParameterName = param.Name;
                field.PropertyName = param.PropertyName;
                field.ClrTypeName = param.ClrType.FullName ?? "System.String";
                field.ReferencedTypeName = param.ReferencedTypeName;
                field.DefaultValue = param.DefaultValue?.ToString();
                field.IncludeInCriteria = true;
                field.CriteriaPropertyPath =
                    ReportParameterSourceGenerator.ResolveCriteriaPath(param.PropertyName, boType);

                if (oldSettings.TryGetValue(param.Name, out var old))
                {
                    field.IncludeInCriteria = old.IncludeInCriteria;
                    if (!string.IsNullOrWhiteSpace(old.CriteriaPropertyPath))
                        field.CriteriaPropertyPath = old.CriteriaPropertyPath;
                }

                definition.Fields.Add(field);
            }

            // Hide the report's own parameters so the viewer doesn't show
            // its built-in parameter panel (XAF's detail view handles the UI)
            HideReportParameters(reportStorage, definition.Report, report);

            // Generate C# source
            var source = ReportParameterSourceGenerator.Generate(
                definition.GeneratedClassName,
                definition.Fields,
                boType);

            // Write source file: check appsettings.json override first, then auto-detect
            var outputDir = ResolveOutputDirectory();
            if (outputDir is null)
            {
                Application.ShowViewStrategy.ShowMessage(
                    "Could not determine output directory for generated source.\n" +
                    "Set 'ReportParameters:OutputDirectory' in appsettings.json, " +
                    "or ensure the app runs from a build output under the solution tree.");
                return;
            }
            Directory.CreateDirectory(outputDir);
            var outputPath = Path.Combine(outputDir, $"{definition.GeneratedClassName}.cs");
            File.WriteAllText(outputPath, source, Encoding.UTF8);
            var fullPath = Path.GetFullPath(outputPath);

            View.ObjectSpace.CommitChanges();

            Application.ShowViewStrategy.ShowMessage(
                $"Generated '{definition.GeneratedClassName}' with {result.Parameters.Count} parameters.\n" +
                $"Source file: {fullPath}\n" +
                "Rebuild the application to activate the parameter object.");
        }
        finally
        {
            report.Dispose();
        }
    }

    /// <summary>
    /// Resolves the output directory for generated source files.
    /// Priority: appsettings.json "ReportParameters:OutputDirectory" > auto-detect Module project.
    /// </summary>
    private string? ResolveOutputDirectory()
    {
        // 1. Check appsettings.json override
        var config = Application.ServiceProvider.GetService<IConfiguration>();
        var configuredPath = config?["ReportParameters:OutputDirectory"];
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var resolved = Path.GetFullPath(configuredPath);
            return resolved;
        }

        // 2. Auto-detect: walk up from build output to find the Module project
        var moduleDir = FindModuleProjectDirectory();
        if (moduleDir is not null)
            return Path.Combine(moduleDir, "GeneratedParameters");

        return null;
    }

    /// <summary>
    /// Hides all XtraReport parameters so the report viewer doesn't show its own
    /// parameter panel. The XAF ReportParametersObjectBase detail view handles the UI.
    /// </summary>
    private static void HideReportParameters(IReportStorage reportStorage, ReportDataV2 reportData, XtraReport report)
    {
        var modified = false;

        foreach (var param in report.Parameters.Cast<DevExpress.XtraReports.Parameters.Parameter>())
        {
            if (param.Visible)
            {
                param.Visible = false;
                modified = true;
            }
        }

        if (modified)
        {
            reportStorage.SaveReport(
                (DevExpress.ExpressApp.ReportsV2.IReportDataV2Writable)reportData, report);
        }
    }

    /// <summary>
    /// Walks up from the build output directory to find the Module project folder
    /// by looking for its .csproj file.
    /// </summary>
    private static string? FindModuleProjectDirectory()
    {
        var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);

        for (var i = 0; i < 10 && dir is not null; i++, dir = dir.Parent)
        {
            var candidates = new[]
            {
                Path.Combine(dir.FullName, "XafReportParametersObjects.Module"),
                dir.GetDirectories("*.Module", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault()?.FullName
            };

            foreach (var candidate in candidates)
            {
                if (candidate is null) continue;
                if (Directory.Exists(candidate) &&
                    Directory.GetFiles(candidate, "*.csproj").Length > 0)
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
