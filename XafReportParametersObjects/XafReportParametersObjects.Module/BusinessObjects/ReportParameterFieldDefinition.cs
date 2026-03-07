using DevExpress.Persistent.BaseImpl.EF;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace XafReportParametersObjects.Module.BusinessObjects;

public class ReportParameterFieldDefinition : BaseObject
{
    [Required]
    [StringLength(100)]
    public virtual string ParameterName { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public virtual string PropertyName { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    public virtual string ClrTypeName { get; set; } = "System.String";

    [StringLength(200)]
    public virtual string ReferencedTypeName { get; set; }

    [DefaultValue(false)]
    public virtual bool IsRequired { get; set; }

    [StringLength(500)]
    public virtual string DefaultValue { get; set; }

    [DefaultValue(true)]
    public virtual bool IncludeInCriteria { get; set; } = true;

    [StringLength(200)]
    public virtual string CriteriaPropertyPath { get; set; }

    public virtual ReportParameterDefinition ReportParameterDefinition { get; set; }
}
