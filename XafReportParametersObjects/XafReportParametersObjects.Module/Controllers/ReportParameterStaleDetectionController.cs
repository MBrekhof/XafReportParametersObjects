using DevExpress.ExpressApp;
using DevExpress.ExpressApp.ReportsV2;
using Microsoft.Extensions.DependencyInjection;
using XafReportParametersObjects.Module.BusinessObjects;
using XafReportParametersObjects.Module.Services;

namespace XafReportParametersObjects.Module.Controllers;

public class ReportParameterStaleDetectionController : ViewController<DetailView>
{
    public ReportParameterStaleDetectionController()
    {
        TargetObjectType = typeof(ReportParameterDefinition);
    }

    protected override void OnActivated()
    {
        base.OnActivated();
        CheckStaleOnLoad();
    }

    private void CheckStaleOnLoad()
    {
        var definition = View.CurrentObject as ReportParameterDefinition;
        if (definition?.Report is null || string.IsNullOrEmpty(definition.ParameterSignatureHash))
            return;

        try
        {
            var reportStorage = Application.ServiceProvider.GetService<IReportStorage>();
            if (reportStorage is null) return;

            using var report = reportStorage.LoadReport(definition.Report);
            if (report is null) return;

            var result = ReportParameterInspector.Inspect(report);
            var wasStale = definition.IsStale;
            definition.IsStale = result.SignatureHash != definition.ParameterSignatureHash;

            if (definition.IsStale && !wasStale)
            {
                View.ObjectSpace.CommitChanges();
                Application.ShowViewStrategy.ShowMessage(
                    "Warning: The report's parameters have changed since the parameter object was generated. " +
                    "Click 'Generate Parameter Object' to regenerate.");
            }
        }
        catch
        {
            // Non-critical: stale detection is advisory
        }
    }
}
