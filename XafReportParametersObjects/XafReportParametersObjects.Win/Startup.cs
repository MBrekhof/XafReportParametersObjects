using DevExpress.ExpressApp;
using DevExpress.ExpressApp.ApplicationBuilder;
using DevExpress.ExpressApp.Design;
using DevExpress.ExpressApp.EFCore;
using DevExpress.ExpressApp.Security;
using DevExpress.ExpressApp.Win;
using DevExpress.ExpressApp.Win.ApplicationBuilder;
using DevExpress.Persistent.Base;
using DevExpress.XtraEditors;
using Microsoft.EntityFrameworkCore;
using System.Configuration;

namespace XafReportParametersObjects.Win
{
    public class ApplicationBuilder : IDesignTimeApplicationFactory
    {
        public static WinApplication BuildApplication(string connectionString)
        {
            var builder = WinApplication.CreateBuilder();
            // Register custom services for Dependency Injection. For more information, refer to the following topic: https://docs.devexpress.com/eXpressAppFramework/404430/
            // builder.Services.AddScoped<CustomService>();
            // Register 3rd-party IoC containers (like Autofac, Dryloc, etc.)
            // builder.UseServiceProviderFactory(new DryIocServiceProviderFactory());
            // builder.UseServiceProviderFactory(new AutofacServiceProviderFactory());

            builder.UseApplication<XafReportParametersObjectsWindowsFormsApplication>();
            builder.Modules
                .AddConditionalAppearance()
                .AddFileAttachments()
                .AddNotifications()
                .AddOffice()
                .AddReports(options =>
                {
                    options.EnableInplaceReports = true;
                    options.ReportDataType = typeof(DevExpress.Persistent.BaseImpl.EF.ReportDataV2);
                    options.ReportStoreMode = DevExpress.ExpressApp.ReportsV2.ReportStoreModes.XML;
                })
                .AddValidation(options =>
                {
                    options.AllowValidationDetailsAccess = false;
                })
                .AddViewVariants()
                .Add<XafReportParametersObjects.Module.XafReportParametersObjectsModule>()
                .Add<XafReportParametersObjectsWinModule>();
            builder.ObjectSpaceProviders
                .AddEFCore(options =>
                {
                    options.PreFetchReferenceProperties();
                })
                    .WithDbContext<XafReportParametersObjects.Module.BusinessObjects.XafReportParametersObjectsEFCoreDbContext>((application, options) =>
                    {
                        // Uncomment this code to use an in-memory database. This database is recreated each time the server starts. With the in-memory database, you don't need to make a migration when the data model is changed.
                        // Do not use this code in production environment to avoid data loss.
                        // We recommend that you refer to the following help topic before you use an in-memory database: https://docs.microsoft.com/en-us/ef/core/testing/in-memory
                        //options.UseInMemoryDatabase();
                        options.UseConnectionString(connectionString);
                    })
                .AddNonPersistent();
            builder.AddBuildStep(application =>
            {
                application.ConnectionString = connectionString;
#if DEBUG
                // Always update in DEBUG (not only under a debugger): the module updater
                // links generated parameter objects to reports on startup.
                if(application.CheckCompatibilityType == CheckCompatibilityType.DatabaseSchema) {
                    application.DatabaseUpdateMode = DatabaseUpdateMode.UpdateDatabaseAlways;
                }
#endif
            });
            var winApplication = builder.Build();
            return winApplication;
        }

        XafApplication IDesignTimeApplicationFactory.Create()
            => BuildApplication(XafApplication.DesignTimeConnectionString);
    }
}
