using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;
using DevExpress.ExpressApp.ReportsV2;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.EF;
using DevExpress.XtraReports.UI;
using Microsoft.Extensions.DependencyInjection;
using XafReportParametersObjects.Module.BusinessObjects;
using XafReportParametersObjects.Module.Services;

namespace XafReportParametersObjects.Module.Controllers;

public class GenerateParameterObjectController : ViewController<DetailView>
{
    private readonly SimpleAction _generateAction;
    private readonly SimpleAction _graduateAction;

    public GenerateParameterObjectController()
    {
        TargetObjectType = typeof(ReportParameterDefinition);

        _generateAction = new SimpleAction(this, "GenerateParameterObject", PredefinedCategory.Edit)
        {
            Caption = "Generate Parameter Object",
            ToolTip = "Inspect the linked report and generate a ReportParametersObjectBase subclass",
            ImageName = "Action_Reload"
        };
        _generateAction.Execute += GenerateAction_Execute;

        _graduateAction = new SimpleAction(this, "GraduateParameterObject", PredefinedCategory.Edit)
        {
            Caption = "Graduate",
            ToolTip = "Generate compilable C# source for this parameter object",
            ImageName = "Action_Export"
        };
        _graduateAction.Execute += GraduateAction_Execute;
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
                Application.ShowViewStrategy.ShowMessage("The report has no visible parameters.");
                return;
            }

            // Clear stale flag since we're regenerating
            definition.IsStale = false;
            definition.ParameterSignatureHash = result.SignatureHash;

            // Update field metadata - can't use Clear() with EF Core change tracking
            while (definition.Fields.Count > 0)
            {
                var field = definition.Fields[0];
                definition.Fields.Remove(field);
                View.ObjectSpace.Delete(field);
            }

            foreach (var param in result.Parameters)
            {
                var field = View.ObjectSpace.CreateObject<ReportParameterFieldDefinition>();
                field.ParameterName = param.Name;
                field.PropertyName = param.PropertyName;
                field.ClrTypeName = param.ClrType.FullName ?? "System.String";
                field.ReferencedTypeName = param.ReferencedTypeName;
                field.IsRequired = false;
                field.DefaultValue = param.DefaultValue?.ToString();
                field.IncludeInCriteria = true;
                field.CriteriaPropertyPath = param.IsLookup ? $"{param.PropertyName}.ID" : param.PropertyName;
                definition.Fields.Add(field);
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

            // Generate C# source
            var source = ReportParameterSourceGenerator.Generate(
                definition.GeneratedClassName,
                result.Parameters,
                boType);

            // Compile via Roslyn
            var compiler = new ReportParameterCompiler();
            var compilationResult = compiler.Compile(source, $"ReportParams_{definition.GeneratedClassName}");

            if (!compilationResult.Success)
            {
                var errors = string.Join("\n", compilationResult.Errors);
                Application.ShowViewStrategy.ShowMessage($"Compilation failed:\n{errors}");
                return;
            }

            // Get the generated type
            var fullTypeName = ReportParameterSourceGenerator.GetFullTypeName(definition.GeneratedClassName);
            var generatedType = compiler.GetGeneratedType(fullTypeName);

            if (generatedType is null)
            {
                Application.ShowViewStrategy.ShowMessage($"Type '{fullTypeName}' not found in compiled assembly.");
                return;
            }

            // Register with XAF TypesInfo
            XafTypesInfo.Instance.RegisterEntity(generatedType);

            // Associate with report
            definition.Report.ParametersObjectType = generatedType;

            definition.Status = ReportParameterStatus.Runtime;

            View.ObjectSpace.CommitChanges();

            Application.ShowViewStrategy.ShowMessage(
                $"Generated '{definition.GeneratedClassName}' with {result.Parameters.Count} parameters. " +
                "Restart the application to fully activate the parameter object.");
        }
        finally
        {
            report.Dispose();
        }
    }

    private void GraduateAction_Execute(object sender, SimpleActionExecuteEventArgs e)
    {
        var definition = (ReportParameterDefinition)View.CurrentObject;

        if (definition.Fields.Count == 0)
        {
            Application.ShowViewStrategy.ShowMessage("No fields defined. Generate the parameter object first.");
            return;
        }

        definition.GeneratedSource = ReportParameterGraduationService.GenerateGraduationSource(definition);
        definition.Status = ReportParameterStatus.Graduating;
        View.ObjectSpace.CommitChanges();

        Application.ShowViewStrategy.ShowMessage(
            "Graduation source generated. Copy the source from 'GeneratedSource' field " +
            "into your Module project and set Status to 'Compiled' after building.");
    }
}
