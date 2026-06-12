using DevExpress.Data.Filtering;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.EF;
using DevExpress.ExpressApp.Updating;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.EF;
using Microsoft.Extensions.DependencyInjection;
using XafReportParametersObjects.Module.BusinessObjects;
using XafReportParametersObjects.Module.Reports;

namespace XafReportParametersObjects.Module.DatabaseUpdate
{
    // For more typical usage scenarios, be sure to check out https://docs.devexpress.com/eXpressAppFramework/DevExpress.ExpressApp.Updating.ModuleUpdater
    public class Updater : ModuleUpdater
    {
        public Updater(IObjectSpace objectSpace, Version currentDBVersion) :
            base(objectSpace, currentDBVersion)
        {
        }
        public override void UpdateDatabaseAfterUpdateSchema()
        {
            base.UpdateDatabaseAfterUpdateSchema();

            if (ObjectSpace.FirstOrDefault<Customer>(c => c.Name == "Acme Corp") is null)
            {
                var acme = ObjectSpace.CreateObject<Customer>();
                acme.Name = "Acme Corp";
                acme.Email = "info@acme.com";
                acme.City = "Amsterdam";

                var globex = ObjectSpace.CreateObject<Customer>();
                globex.Name = "Globex";
                globex.Email = "info@globex.com";
                globex.City = "Rotterdam";

                var order1 = ObjectSpace.CreateObject<Order>();
                order1.OrderNumber = "ORD-001";
                order1.OrderDate = new DateTime(2026, 1, 15);
                order1.Amount = 1500m;
                order1.Customer = acme;
                order1.Description = "Widget batch";

                var order2 = ObjectSpace.CreateObject<Order>();
                order2.OrderNumber = "ORD-002";
                order2.OrderDate = new DateTime(2026, 2, 20);
                order2.Amount = 3200m;
                order2.Customer = globex;
                order2.Description = "Gadget shipment";

                var order3 = ObjectSpace.CreateObject<Order>();
                order3.OrderNumber = "ORD-003";
                order3.OrderDate = new DateTime(2026, 3, 1);
                order3.Amount = 750m;
                order3.Customer = acme;
                order3.Description = "Spare parts";

                ObjectSpace.CommitChanges();
            }

            // Ensure the predefined report is associated with the hardcoded parameters object
            // even if the report row already existed before this spike.
            var ordersReport = ObjectSpace.FirstOrDefault<ReportDataV2>(r => r.DisplayName == "Orders Report");
            if (ordersReport is not null && ordersReport.ParametersObjectType != typeof(OrdersReportParameters))
            {
                ordersReport.ParametersObjectType = typeof(OrdersReportParameters);
                ObjectSpace.CommitChanges();
            }

            LinkGeneratedParameterObjects();
        }

        // The Generate action writes a .cs file but can't link the type to the report —
        // the type only exists after the developer rebuilds. This closes the loop on
        // every startup: resolve each generated class by name and attach it.
        private void LinkGeneratedParameterObjects()
        {
            var linked = false;
            foreach (var definition in ObjectSpace.GetObjects<ReportParameterDefinition>())
            {
                if (definition.Report is null || definition.Status != ReportParameterStatus.Generated)
                    continue;

                // Predefined reports declare their parameters object in code
                // (PredefinedReportsUpdater owns those rows and recreates them when
                // their metadata is changed). Only link user-created reports.
                if (!string.IsNullOrEmpty(definition.Report.PredefinedReportTypeName))
                    continue;

                var parametersType = Type.GetType(
                    Services.ReportParameterSourceGenerator.GetFullTypeName(definition.GeneratedClassName));
                if (parametersType is not null && definition.Report.ParametersObjectType != parametersType)
                {
                    definition.Report.ParametersObjectType = parametersType;
                    linked = true;
                }
            }
            if (linked)
                ObjectSpace.CommitChanges();
        }
        public override void UpdateDatabaseBeforeUpdateSchema()
        {
            base.UpdateDatabaseBeforeUpdateSchema();
        }
    }
}
