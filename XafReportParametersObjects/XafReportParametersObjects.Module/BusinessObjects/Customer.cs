using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.EF;
using System.ComponentModel;

namespace XafReportParametersObjects.Module.BusinessObjects;

[DefaultClassOptions]
[NavigationItem("SampleData")]
public class Customer : BaseObject
{
    public virtual string Name { get; set; } = string.Empty;
    public virtual string Email { get; set; } = string.Empty;
    public virtual string City { get; set; } = string.Empty;

    [DefaultValue(true)]
    public virtual bool IsActive { get; set; } = true;
}
