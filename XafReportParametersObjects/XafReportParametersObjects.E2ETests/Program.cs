using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.Playwright;

// End-to-end test for the generate -> rebuild -> auto-link -> filter workflow.
// Run with:  dotnet run --project XafReportParametersObjects.E2ETests
//
// Phase 1 (browser): copy the predefined report, create a ReportParameterDefinition,
//   Generate, fix the StartDate criteria path via the grid, regenerate, assert the
//   emitted .cs file. Then the app is stopped and the solution rebuilt so the
//   generated class compiles in.
// Phase 2 (browser): restart, assert the Updater linked the generated type to the
//   report, run the report with criteria and assert the preview shows exactly the
//   matching seed row (ORD-001) and not the excluded ones (ORD-002, ORD-003).

const string BaseUrl = "http://localhost:5100";
const string ClassName = "E2ETestParameters";
const string ReportName = "Orders Report (E2E)";
const string ConnectionString =
    @"Data Source=(localdb)\mssqllocaldb;Integrated Security=SSPI;Initial Catalog=XafReportParametersObjects;Encrypt=False";

var repoRoot = FindRepoRoot();
var blazorProj = Path.Combine(repoRoot, "XafReportParametersObjects", "XafReportParametersObjects.Blazor.Server");
var generatedFile = Path.Combine(repoRoot, "XafReportParametersObjects", "XafReportParametersObjects.Module",
    "GeneratedParameters", $"{ClassName}.cs");

Process? app = null;
var failed = false;

try
{
    Step("Pre-clean: remove artifacts from previous runs");
    Sql($"""
        DELETE f FROM ReportParameterFieldDefinitions f
          JOIN ReportParameterDefinitions d ON f.ReportParameterDefinitionID = d.ID
          WHERE d.GeneratedClassName = '{ClassName}';
        DELETE FROM ReportParameterDefinitions WHERE GeneratedClassName = '{ClassName}';
        DELETE FROM ReportDataV2 WHERE DisplayName = '{ReportName}';
        """);
    if (File.Exists(generatedFile)) File.Delete(generatedFile);

    Step("Build app (pre-test, without the generated class)");
    RunOrThrow("dotnet", $"build \"{blazorProj}\" -v q --nologo");

    Step("Start Blazor app");
    app = StartApp(blazorProj);
    await WaitForHttpOk();

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });

    // ---------- Phase 1: copy report, generate, customize, regenerate ----------
    {
        var page = await NewPage(browser);

        Step("Copy the predefined report to get a user-owned report");
        await page.GotoAsync($"{BaseUrl}/ReportDataV2_ListView", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var predefinedRow = page.GetByRole(AriaRole.Row)
            .Filter(new() { Has = page.GetByRole(AriaRole.Gridcell, new() { Name = "Orders Report", Exact = true }) });
        await predefinedRow.GetByRole(AriaRole.Checkbox).First.ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Copy Predefined Report" }).ClickAsync();
        // The copy gets the same display name; rename it so it is unambiguous in the UI.
        await RetryAsync(() =>
        {
            var n = Sql($"UPDATE ReportDataV2 SET DisplayName = '{ReportName}' " +
                        "WHERE PredefinedReportTypeName IS NULL AND DisplayName = 'Orders Report'");
            return n == 1 ? Task.CompletedTask : throw new Exception("copy row not found yet");
        });

        Step("Create ReportParameterDefinition");
        await page.GotoAsync($"{BaseUrl}/ReportParameterDefinition_ListView", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.GetByRole(AriaRole.Button, new() { Name = "New", Exact = true }).ClickAsync();
        var classNameBox = page.GetByRole(AriaRole.Textbox, new() { Name = "Generated Class Name" });
        await classNameBox.ClickAsync();
        await classNameBox.PressSequentiallyAsync(ClassName);
        await page.GetByRole(AriaRole.Combobox, new() { Name = "Report" }).ClickAsync();
        await page.GetByText(ReportName, new() { Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();

        Step("Generate (first pass)");
        await page.GetByRole(AriaRole.Button, new() { Name = "Generate Parameter Object" }).ClickAsync();
        await page.GetByText("StartDate").First.WaitForAsync(); // fields grid populated
        Assert(File.Exists(generatedFile), $"generated file exists: {generatedFile}");
        var source = File.ReadAllText(generatedFile);
        Assert(source.Contains("Customer.Name = ?"), "criteria path Customer.Name inferred");
        Assert(!source.Contains("OrderDate"), "StartDate has no criteria yet (no Start/End convention)");

        Step("Set CriteriaPropertyPath = OrderDate on the StartDate field");
        await page.GetByText("StartDate").First.DblClickAsync();
        var pathBox = page.GetByRole(AriaRole.Textbox, new() { Name = "Criteria Property Path" });
        await pathBox.ClickAsync();
        await page.Keyboard.PressAsync("Control+a");
        await pathBox.PressSequentiallyAsync("OrderDate");
        await page.Keyboard.PressAsync("Tab");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();

        Step("Regenerate and assert the user edit is honored + preserved");
        await page.GotoAsync($"{BaseUrl}/ReportParameterDefinition_ListView", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.GetByText(ClassName).First.DblClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Generate Parameter Object" }).ClickAsync();
        await RetryAsync(() =>
        {
            var src = File.ReadAllText(generatedFile);
            return src.Contains("OrderDate >= ?") ? Task.CompletedTask : throw new Exception("OrderDate not in file yet");
        });
        source = File.ReadAllText(generatedFile);
        Assert(source.Contains("Customer.Name = ?"), "Customer.Name criteria survived regeneration");
        Assert(source.Contains("OrderDate >= ?"), "custom OrderDate path emitted");
        Assert(source.Contains("Amount >= ?"), "MinAmount range criteria emitted");

        await page.CloseAsync();
    }

    Step("Stop app, rebuild with the generated class, restart");
    KillApp(ref app);
    RunOrThrow("dotnet", $"build \"{blazorProj}\" -v q --nologo");
    app = StartApp(blazorProj);
    await WaitForHttpOk();

    // ---------- Phase 2: auto-link + filtered preview ----------
    {
        var page = await NewPage(browser);

        Step("Trigger startup logon (runs the module updaters)");
        await page.GotoAsync($"{BaseUrl}/ReportDataV2_ListView", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.GetByText(ReportName).First.WaitForAsync();

        Step("Assert the Updater linked the generated type to the report");
        await RetryAsync(() =>
        {
            var linked = SqlScalar($"SELECT ParametersObjectTypeName FROM ReportDataV2 WHERE DisplayName = '{ReportName}'");
            return linked == $"XafReportParametersObjects.Module.GeneratedParameters.{ClassName}"
                ? Task.CompletedTask
                : throw new Exception($"not linked yet (current: {linked ?? "<null>"})");
        });

        Step("Run the report: parameter dialog, criteria, preview");
        await page.GetByText(ReportName).First.DblClickAsync();
        var customerBox = page.GetByRole(AriaRole.Textbox, new() { Name = "Customer Name" });
        await customerBox.ClickAsync();
        await customerBox.PressSequentiallyAsync("Acme Corp");
        var minAmount = page.GetByRole(AriaRole.Spinbutton, new() { Name = "Min Amount" });
        await minAmount.ClickAsync();
        await page.Keyboard.PressAsync("Control+a");
        await minAmount.PressSequentiallyAsync("1000");
        await page.Keyboard.PressAsync("Tab");
        await page.GetByRole(AriaRole.Button, new() { Name = "Preview" }).ClickAsync();

        Step("Assert the preview is filtered by the generated GetCriteria()");
        // The Blazor report viewer renders pages as bitmap <img> elements — no DOM text.
        // Export to CSV and assert on the file content instead.
        await page.Locator("img[src^='data:image']").First.WaitForAsync(new() { Timeout = 60_000 });
        var download = await page.RunAndWaitForDownloadAsync(async () =>
        {
            await page.GetByRole(AriaRole.Button, new() { Name = "Export To" }).ClickAsync();
            await page.GetByRole(AriaRole.Menuitemcheckbox, new() { Name = "CSV" }).ClickAsync();
        });
        var csvPath = Path.Combine(Path.GetTempPath(), $"e2e-{Guid.NewGuid():N}.csv");
        await download.SaveAsAsync(csvPath);
        var csv = File.ReadAllText(csvPath);
        File.Delete(csvPath);
        Console.WriteLine($"    exported CSV:\n{csv.Trim().ReplaceLineEndings("\n    ")}");
        Assert(csv.Contains("ORD-001"), "ORD-001 (Acme Corp, 1500) present");
        Assert(!csv.Contains("ORD-002"), "ORD-002 (Globex) filtered out by Customer.Name");
        Assert(!csv.Contains("ORD-003"), "ORD-003 (750 < 1000) filtered out by Amount");

        await page.CloseAsync();
    }

    Console.WriteLine("\n=== E2E PASSED ===");
}
catch (Exception ex)
{
    failed = true;
    Console.WriteLine($"\n=== E2E FAILED ===\n{ex}");
}
finally
{
    KillApp(ref app);
}
return failed ? 1 : 0;

// ---------- helpers ----------

static void Step(string name) => Console.WriteLine($"\n--- {name}");

static void Assert(bool condition, string what)
{
    if (!condition) throw new Exception($"Assert failed: {what}");
    Console.WriteLine($"    [ok] {what}");
}

// XAF Blazor round-trips are async; poll a check for up to 15s.
static async Task RetryAsync(Func<Task> check, int seconds = 15)
{
    Exception? last = null;
    for (var i = 0; i < seconds * 2; i++)
    {
        try { await check(); return; }
        catch (Exception ex) { last = ex; await Task.Delay(500); }
    }
    throw last!;
}

static async Task<IPage> NewPage(IBrowser browser)
{
    var page = await browser.NewPageAsync();
    page.SetDefaultTimeout(20_000);
    page.SetDefaultNavigationTimeout(20_000);
    return page;
}

static Process StartApp(string blazorProj)
{
    var psi = new ProcessStartInfo("dotnet",
        $"run --no-build --project \"{blazorProj}\" --urls {BaseUrl}")
    {
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };
    var p = Process.Start(psi)!;
    p.OutputDataReceived += (_, _) => { };
    p.ErrorDataReceived += (_, _) => { };
    p.BeginOutputReadLine();
    p.BeginErrorReadLine();
    return p;
}

static void KillApp(ref Process? app)
{
    if (app is null) return;
    try { app.Kill(entireProcessTree: true); app.WaitForExit(10_000); } catch { /* already gone */ }
    app.Dispose();
    app = null;
}

static async Task WaitForHttpOk()
{
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
    for (var i = 0; i < 60; i++)
    {
        try
        {
            var resp = await http.GetAsync(BaseUrl);
            if (resp.IsSuccessStatusCode) { Console.WriteLine($"    app ready at {BaseUrl}"); return; }
        }
        catch { /* not up yet */ }
        await Task.Delay(2000);
    }
    throw new Exception($"app did not become ready at {BaseUrl}");
}

static void RunOrThrow(string file, string args)
{
    var p = Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = false })!;
    p.WaitForExit();
    if (p.ExitCode != 0) throw new Exception($"`{file} {args}` exited with {p.ExitCode}");
}

static int Sql(string sql)
{
    using var conn = new SqlConnection(ConnectionString);
    conn.Open();
    using var cmd = new SqlCommand(sql, conn);
    return cmd.ExecuteNonQuery();
}

static string? SqlScalar(string sql)
{
    using var conn = new SqlConnection(ConnectionString);
    conn.Open();
    using var cmd = new SqlCommand(sql, conn);
    return cmd.ExecuteScalar()?.ToString();
}

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    for (; dir is not null; dir = dir.Parent)
        if (dir.GetFiles("*.slnx").Length > 0) return dir.FullName;
    throw new Exception("repo root (.slnx) not found above " + AppContext.BaseDirectory);
}
