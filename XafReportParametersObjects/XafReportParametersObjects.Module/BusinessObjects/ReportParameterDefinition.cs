using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.EF;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace XafReportParametersObjects.Module.BusinessObjects;

[DefaultClassOptions]
[NavigationItem("ReportParameters")]
public class ReportParameterDefinition : BaseObject
{
    public virtual ReportDataV2 Report { get; set; }

    [Required]
    [StringLength(200)]
    public virtual string GeneratedClassName { get; set; } = string.Empty;

    [Browsable(false)]
    public virtual string GeneratedSource { get; set; }

    public virtual ReportParameterStatus Status { get; set; } = ReportParameterStatus.Runtime;

    [StringLength(100)]
    [Browsable(false)]
    public virtual string ParameterSignatureHash { get; set; }

    [DefaultValue(false)]
    public virtual bool IsStale { get; set; }

    [DevExpress.ExpressApp.DC.Aggregated]
    public virtual ObservableCollection<ReportParameterFieldDefinition> Fields { get; set; } = new();
}
