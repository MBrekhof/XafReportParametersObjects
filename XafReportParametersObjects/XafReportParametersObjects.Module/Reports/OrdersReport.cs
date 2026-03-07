using DevExpress.Persistent.Base.ReportsV2;
using DevExpress.XtraReports.Parameters;
using DevExpress.XtraReports.UI;
using XafReportParametersObjects.Module.BusinessObjects;

namespace XafReportParametersObjects.Module.Reports;

/// <summary>
/// Sample predefined report with parameters for testing dynamic parameter object generation.
/// Parameters: CustomerName (string), StartDate (DateTime), SelectedCustomer (Customer lookup).
/// </summary>
public class OrdersReport : XtraReport
{
    public OrdersReport()
    {
        // Data source
        var dataSource = new CollectionDataSource { ObjectTypeName = typeof(Order).FullName };
        DataSource = dataSource;

        // Parameters
        var customerNameParam = new Parameter
        {
            Name = "CustomerName",
            Description = "Customer Name",
            Type = typeof(string),
            Value = "",
            Visible = true
        };

        var startDateParam = new Parameter
        {
            Name = "StartDate",
            Description = "Start Date",
            Type = typeof(DateTime),
            Value = DateTime.Today.AddMonths(-3),
            Visible = true
        };

        var minAmountParam = new Parameter
        {
            Name = "MinAmount",
            Description = "Minimum Amount",
            Type = typeof(decimal),
            Value = 0m,
            Visible = true
        };

        Parameters.AddRange(new[] { customerNameParam, startDateParam, minAmountParam });

        // Report layout
        Bands.Add(new TopMarginBand { HeightF = 20 });
        Bands.Add(new BottomMarginBand { HeightF = 20 });

        // Report header
        var reportHeader = new ReportHeaderBand { HeightF = 40 };
        reportHeader.Controls.Add(new XRLabel
        {
            Text = "Orders Report",
            LocationFloat = new DevExpress.Utils.PointFloat(0, 0),
            SizeF = new System.Drawing.SizeF(650, 30),
            Font = new System.Drawing.Font("Arial", 16, System.Drawing.FontStyle.Bold)
        });
        Bands.Add(reportHeader);

        // Column headers
        var pageHeader = new PageHeaderBand { HeightF = 25 };
        pageHeader.Controls.Add(CreateLabel("Order #", 0, 100, true));
        pageHeader.Controls.Add(CreateLabel("Date", 100, 100, true));
        pageHeader.Controls.Add(CreateLabel("Customer", 200, 150, true));
        pageHeader.Controls.Add(CreateLabel("Amount", 350, 100, true));
        pageHeader.Controls.Add(CreateLabel("Description", 450, 200, true));
        Bands.Add(pageHeader);

        // Detail band
        var detail = new DetailBand { HeightF = 25 };
        detail.Controls.Add(CreateBoundLabel("OrderNumber", 0, 100));
        detail.Controls.Add(CreateBoundLabel("OrderDate", 100, 100));
        detail.Controls.Add(CreateBoundLabel("Customer.Name", 200, 150));
        detail.Controls.Add(CreateBoundLabel("Amount", 350, 100));
        detail.Controls.Add(CreateBoundLabel("Description", 450, 200));
        Bands.Add(detail);

        DisplayName = "Orders Report";
    }

    private static XRLabel CreateLabel(string text, float x, float width, bool bold = false)
    {
        return new XRLabel
        {
            Text = text,
            LocationFloat = new DevExpress.Utils.PointFloat(x, 0),
            SizeF = new System.Drawing.SizeF(width, 25),
            Font = new System.Drawing.Font("Arial", 9, bold ? System.Drawing.FontStyle.Bold : System.Drawing.FontStyle.Regular)
        };
    }

    private static XRLabel CreateBoundLabel(string dataMember, float x, float width)
    {
        return new XRLabel
        {
            ExpressionBindings = { new ExpressionBinding("BeforePrint", "Text", $"[{dataMember}]") },
            LocationFloat = new DevExpress.Utils.PointFloat(x, 0),
            SizeF = new System.Drawing.SizeF(width, 25),
            Font = new System.Drawing.Font("Arial", 9)
        };
    }
}
