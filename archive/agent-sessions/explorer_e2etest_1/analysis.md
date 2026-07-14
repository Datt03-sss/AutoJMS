# E2E Test Infrastructure Design and Proposal

This analysis report describes the design of a new automated test suite `tests/AutoJMS.Tests` targeting `.NET 8.0-windows` with xUnit. It addresses isolating the J&T Express JMS API calls and the Windows physical printer spooler using mock interfaces, enabling the testing of 7 key scenarios without hitting real APIs or printers.

---

## 1. Codebase Investigation Summary

### 1.1 Print Service (`src/AutoJMS/Printing/PrintService.cs`)
- **Grid Dependency**: `PrintService` holds a reference to `DataGridView` (`_grid`) and a `DataTable` (`_displayTable`). It wires up events (e.g., `CellClick`) and performs UI operations such as `_grid.ClearSelection()`, `_grid.CurrentCell = null`, and `_grid.Invoke`/`BeginInvoke` to execute grid rendering updates safely across threads.
- **Direct API Calling**: Currently, `PrintService` directly executes HTTP calls through the static class `JmsApiClient.PostJsonAsync(...)` in methods like `FetchPrintApprovalInfoAsync` and `FetchTrackingRowsDirectAsync`.
- **Preflight & Safety**: It uses `IPrintSafetyGuard` (`PrintSafetyGuard`) to validate if a waybill matches the current bưu cục/middleCode of the active login session.

### 1.2 Main Form (`src/AutoJMS/Forms/Main.cs`)
- **Coordination Logic**: The `Main` form coordinates printing in `ExecutePrintAsync`. It:
  - Fetches the PDF URL from the JMS API via `GetPdfUrlViaCSharpAsync`.
  - Manages the local PDF download via `DownloadPdfWithRetryAsync`.
  - Integrates caching: checks the local file cache and tracks print history.
  - Submits printing to the physical spooler via `SubmitPrintImmediatelyAsync`, which uses PDFium to load the file, creates a `PrintDocument`, prints it, and polls Win32 print jobs via `WaitForPrinterJobCompletedAsync`.
  - Clears UI selections on both the Print grid (`_printService.SelectAll(false)`) and the Dashboard grid (`_fullStackForm?.ClearDashGridSelection()`).

### 1.3 Target Refactoring (`PrintJobCoordinator`)
To decouple UI from business logic, the printing orchestration from `Main.cs` will be extracted into a new class `PrintJobCoordinator` in `src/AutoJMS/Printing/PrintJobCoordinator.cs`. This class will implement:
```csharp
public interface IPrintJobCoordinator
{
    Task<PrintSubmitResult> PrintAsync(PrintJobRequest request);
}
```

---

## 2. Decoupling and Mocking Design

To run tests in isolation, we must intercept the J&T Express API calls, the physical printing spooler, the PDF downloads, and WinForms UI operations.

### 2.1 JMS API Client Mocking (`IJmsApiClient`)
Instead of referencing `JmsApiClient` directly, we introduce a mockable interface:
```csharp
namespace AutoJMS
{
    public interface IJmsApiClient
    {
        Task<HttpResponseMessage> PostJsonAsync(
            string url,
            string jsonBody,
            string routeName = "trackingExpress",
            string routerNameList = null,
            string origin = "https://jms.jtexpress.vn",
            CancellationToken ct = default);
    }
}
```
For production usage, we create a simple delegation wrapper:
```csharp
namespace AutoJMS
{
    public sealed class JmsApiClientWrapper : IJmsApiClient
    {
        public Task<HttpResponseMessage> PostJsonAsync(
            string url,
            string jsonBody,
            string routeName = "trackingExpress",
            string routerNameList = null,
            string origin = "https://jms.jtexpress.vn",
            CancellationToken ct = default)
        {
            return JmsApiClient.PostJsonAsync(url, jsonBody, routeName, routerNameList, origin, ct);
        }
    }
}
```
`PrintService` and the new `PrintJobCoordinator` will accept `IJmsApiClient` via constructor injection, defaulting to `new JmsApiClientWrapper()`.

### 2.2 Printer Spooler Mocking (`IPrinterSpoolerSubmitter`)
To isolate physical printing and Win32 spooler checking, we abstract spooler submission:
```csharp
namespace AutoJMS
{
    public interface IPrinterSpoolerSubmitter
    {
        Task<PrintSubmitResult> SubmitPrintImmediatelyAsync(PrintJobCacheEntry job, string firstWaybill);
    }
}
```
In tests, this will return mock spooler counts and execution outcomes without loading PDFium or writing to physical printer drivers.

### 2.3 PDF Download Mocking (`IPdfDownloadService`)
To prevent actual network downloads of PDFs, we abstract PDF retrieval:
```csharp
namespace AutoJMS
{
    public interface IPdfDownloadService
    {
        Task<byte[]> DownloadPdfAsync(string pdfUrl, int keepPdfs, string waybillTag, CancellationToken ct);
        string GetCachedPdfPath(string pdfUrl);
    }
}
```
In tests, a fake implementation will bypass the real `HttpClient` and return a pre-configured byte array (e.g., mock PDF header `%PDF-1.4`).

### 2.4 UI Grid Clearing Abstraction (`IPrintUiController`)
To enable the clearing of dashboard/grid selections in tests without bringing in real Form classes:
```csharp
namespace AutoJMS
{
    public interface IPrintUiController
    {
        void ClearDashSelection();
        void ShowPrintMessage(string message, bool isError, int durationMs = 0);
    }
}
```

---

## 3. Test Project Setup (`tests/AutoJMS.Tests`)

### 3.1 Project File (`tests/AutoJMS.Tests/AutoJMS.Tests.csproj`)
This project targets `net8.0-windows` and enables `UseWindowsForms` so that `DataGridView` controls can be instantiated inside the test runner:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>disable</Nullable>
    <UseWindowsForms>true</UseWindowsForms>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.9.0" />
    <PackageReference Include="xunit" Version="2.6.6" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.7" />
    <PackageReference Include="coverlet.collector" Version="6.0.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\AutoJMS\AutoJMS.csproj" />
  </ItemGroup>

</Project>
```

### 3.2 Solution Integration (`AutoJMS.slnx`)
Add the test project to the Visual Studio Solution model file:
```xml
<Solution>
  <Project Path="src/AutoJMS/AutoJMS.csproj" DefaultStartup="true" />
  <Project Path="src/AutoJMS.Abstractions/AutoJMS.Abstractions.csproj" />
  <Project Path="tests/AutoJMS.Tests/AutoJMS.Tests.csproj" />
</Solution>
```

### 3.3 STA Thread Helper (`tests/AutoJMS.Tests/Helpers/StaThreadHelper.cs`)
To avoid cross-thread exceptions when manipulating WinForms controls in xUnit, we introduce a thread helper:
```csharp
using System;
using System.Threading;

namespace AutoJMS.Tests.Helpers
{
    public static class StaThreadHelper
    {
        public static void Run(Action action)
        {
            Exception exception = null;
            var thread = new Thread(() =>
            {
                try { action(); }
                catch (Exception ex) { exception = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (exception != null) throw exception;
        }

        public static T Run<T>(Func<T> func)
        {
            T result = default;
            Exception exception = null;
            var thread = new Thread(() =>
            {
                try { result = func(); }
                catch (Exception ex) { exception = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (exception != null) throw exception;
            return result;
        }
    }
}
```

---

## 4. Draft Mock Implementations

These lightweight mocks will reside in `tests/AutoJMS.Tests/Mocks/`:

### 4.1 `FakeJmsApiClient.cs`
```csharp
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AutoJMS;

namespace AutoJMS.Tests.Mocks
{
    public class FakeJmsApiClient : IJmsApiClient
    {
        public int ApiCallCount { get; private set; }
        public List<(string Url, string Body)> CalledEndpoints { get; } = new();
        
        public string PrintWaybillResponseJson { get; set; } = @"{""code"":""200"",""msg"":""Success"",""data"":""http://mock-jms-pdf-url/waybill.pdf""}";
        public string PrintListPageResponseJson { get; set; } = @"{""code"":""200"",""data"":{""records"":[]}}";
        public string TrackingResponseJson { get; set; } = @"{""code"":""200"",""data"":[]}";

        public Task<HttpResponseMessage> PostJsonAsync(
            string url,
            string jsonBody,
            string routeName = "trackingExpress",
            string routerNameList = null,
            string origin = "https://jms.jtexpress.vn",
            CancellationToken ct = default)
        {
            ApiCallCount++;
            CalledEndpoints.Add((url, jsonBody));

            string responseContent = "{}";
            if (url.Contains("printWaybill"))
            {
                responseContent = PrintWaybillResponseJson;
            }
            else if (url.Contains("pringListPage") || url.Contains("listPage"))
            {
                responseContent = PrintListPageResponseJson;
            }
            else if (url.Contains("keywordList"))
            {
                responseContent = TrackingResponseJson;
            }

            var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseContent, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(httpResponse);
        }
    }
}
```

### 4.2 `FakePdfDownloadService.cs`
```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using AutoJMS;

namespace AutoJMS.Tests.Mocks
{
    public class FakePdfDownloadService : IPdfDownloadService
    {
        public int DownloadCallCount { get; private set; }
        public byte[] MockPdfBytes { get; set; } = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2d, 0x31, 0x2e, 0x34 }; // %PDF-1.4 header
        public string MockPdfPath { get; set; } = "C:\\Temp\\mock_waybill.pdf";

        public Task<byte[]> DownloadPdfAsync(string pdfUrl, int keepPdfs, string waybillTag, CancellationToken ct)
        {
            DownloadCallCount++;
            return Task.FromResult(MockPdfBytes);
        }

        public string GetCachedPdfPath(string pdfUrl) => string.Empty;
    }
}
```

### 4.3 `FakePrinterSpoolerSubmitter.cs`
```csharp
using System.Threading.Tasks;
using AutoJMS;

namespace AutoJMS.Tests.Mocks
{
    public class FakePrinterSpoolerSubmitter : IPrinterSpoolerSubmitter
    {
        public int SubmitCallCount { get; private set; }
        public bool SubmitShouldSucceed { get; set; } = true;
        public string FailureReason { get; set; } = "";

        public Task<PrintSubmitResult> SubmitPrintImmediatelyAsync(PrintJobCacheEntry job, string firstWaybill)
        {
            SubmitCallCount++;
            return Task.FromResult(new PrintSubmitResult
            {
                CompletedBySpooler = SubmitShouldSucceed,
                ElapsedMs = 50,
                PrinterName = "FakePDFPrinter",
                DocumentName = $"JMS_Print_{firstWaybill}",
                SpoolerJobsBefore = 0,
                SpoolerJobsAfter = SubmitShouldSucceed ? 0 : 1,
                Reason = SubmitShouldSucceed ? "spooler-job-completed" : FailureReason
            });
        }
    }
}
```

### 4.4 `FakePrinterPreflightService.cs`
```csharp
using System.Threading;
using System.Threading.Tasks;
using AutoJMS;

namespace AutoJMS.Tests.Mocks
{
    public class FakePrinterPreflightService : IPrinterPreflightService
    {
        public bool CanPrint { get; set; } = true;
        public string ReasonCode { get; set; } = "PRINTER_OK";

        public Task<PrinterPreflightResult> CheckAsync(string printerName, CancellationToken cancellationToken)
        {
            return Task.FromResult(new PrinterPreflightResult
            {
                CanPrint = CanPrint,
                PrinterName = printerName,
                ReasonCode = ReasonCode,
                StatusText = CanPrint ? "Printer is ready" : "Printer has an error",
                QueueJobCount = 0,
                ErrorJobCount = 0
            });
        }
    }
}
```

### 4.5 `FakePrintUiController.cs`
```csharp
using AutoJMS;

namespace AutoJMS.Tests.Mocks
{
    public class FakePrintUiController : IPrintUiController
    {
        public bool ClearDashSelectionCalled { get; private set; }
        public string LastMessageShown { get; private set; }
        public bool LastMessageIsError { get; private set; }

        public void ClearDashSelection()
        {
            ClearDashSelectionCalled = true;
        }

        public void ShowPrintMessage(string message, bool isError, int durationMs = 0)
        {
            LastMessageShown = message;
            LastMessageIsError = isError;
        }
    }
}
```

### 4.6 `FakeSiteContextProvider.cs`
```csharp
using System.Threading;
using System.Threading.Tasks;
using AutoJMS;

namespace AutoJMS.Tests.Mocks
{
    public class FakeSiteContextProvider : ISiteContextProvider
    {
        public SiteContext Current { get; set; } = new();

        public Task RefreshAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
```

### 4.7 `FakeTrackingService.cs`
```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using AutoJMS;

namespace AutoJMS.Tests.Mocks
{
    public class FakeTrackingService : ITrackingService
    {
        public List<TrackingRow> Rows { get; set; } = new();

        public Task SearchTrackingAsync(string waybillsText, bool updateMainGrid = true) => Task.CompletedTask;
        public void ClearData() => Rows.Clear();
        public void ExportToExcel() { }
        public void ExportSpecial() { }
        public List<TrackingRow> GetAllRows() => Rows;
        public Task<string> GetDKCHHistoryAsync(string waybill) => Task.FromResult(string.Empty);
    }
}
```

---

## 5. Draft Test Cases Implementation Design

The following draft implementations cover Tiers 1-4 as requested. These should be placed in `tests/AutoJMS.Tests/PrintJobCoordinatorTests.cs`.

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;
using Xunit;
using AutoJMS;
using AutoJMS.Tests.Helpers;
using AutoJMS.Tests.Mocks;

namespace AutoJMS.Tests
{
    public class PrintJobCoordinatorTests
    {
        private (PrintJobCoordinator Coordinator, 
                 FakeJmsApiClient ApiClient, 
                 FakePdfDownloadService Downloader, 
                 FakePrinterSpoolerSubmitter Spooler, 
                 FakePrinterPreflightService Preflight, 
                 FakePrintUiController UiController, 
                 PrintService PrintService, 
                 FakeTrackingService TrackingService) SetupEnvironment(string middleCode = "5165A")
        {
            var grid = new DataGridView();
            grid.Columns.Add("Select", "Select");
            grid.Columns.Add("Mã vận đơn", "Mã vận đơn");
            
            var trackingService = new FakeTrackingService();
            var preflight = new FakePrinterPreflightService();
            var apiClient = new FakeJmsApiClient();
            var downloader = new FakePdfDownloadService();
            var spooler = new FakePrinterSpoolerSubmitter();
            var uiController = new FakePrintUiController();
            
            var safetyGuard = new PrintSafetyGuard();
            var siteContextProvider = new FakeSiteContextProvider
            {
                Current = new SiteContext
                {
                    MiddleCode = middleCode,
                    MiddleCodeAliases = new[] { middleCode },
                    AllowSegment2Match = true,
                    Source = "test"
                }
            };
            
            var printService = new PrintService(
                grid,
                trackingService,
                safetyGuard,
                () => "MOCK_JMS_TOKEN",
                siteContextProvider,
                apiClient);

            var coordinator = new PrintJobCoordinator(
                printService,
                apiClient,
                downloader,
                spooler,
                preflight,
                uiController);
                
            return (coordinator, apiClient, downloader, spooler, preflight, uiController, printService, trackingService);
        }

        // ==========================================
        // TIER 1: Feature Coverage
        // ==========================================

        [Fact]
        public void Test_Case1_FirstPrint_Success()
        {
            StaThreadHelper.Run(async () =>
            {
                // Arrange
                var env = SetupEnvironment();
                string waybill = "840012345678";
                
                var trackingRow = new TrackingRow
                {
                    WaybillNo = waybill,
                    MaDoan2 = "5165A",
                    SafetyEvents = new List<TrackingSafetyEvent>
                    {
                        new TrackingSafetyEvent { ScanNetworkCode = "5165A", ScanTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") }
                    }
                };
                env.TrackingService.Rows.Add(trackingRow);

                env.ApiClient.PrintListPageResponseJson = @"{""code"":""200"",""data"":{""records"":[{""waybillNo"":""840012345678"",""statusName"":""Approved"",""printCount"":0}]}}";
                env.ApiClient.TrackingResponseJson = @"{""code"":""200"",""data"":[{""keyword"":""840012345678"",""details"":[{""scanNetworkCode"":""5165A"",""scanTime"":""2026-06-22 09:00:00""}]}]}";
                
                await env.PrintService.SearchAndLoadAsync(waybill, PrintMode.InHoan);
                env.PrintService.SelectAll(true);

                var request = new PrintJobRequest
                {
                    SelectedWaybills = new List<string> { waybill },
                    PrintType = 1,
                    ApplyTypeCode = 4,
                    KeepPdfs = 500,
                    Source = "Print"
                };
                
                // Act
                var result = await env.Coordinator.PrintAsync(request);

                // Assert
                Assert.NotNull(result);
                Assert.True(result.CompletedBySpooler);
                Assert.Equal(1, env.ApiClient.ApiCallCount); // printWaybill endpoint call
                Assert.Equal(1, env.Downloader.DownloadCallCount); // Download PDF
                Assert.Equal(1, env.Spooler.SubmitCallCount); // Spooler submit
                
                // Assert clearing
                Assert.Empty(env.PrintService.GetSelectedWaybills());
                Assert.True(env.UiController.ClearDashSelectionCalled);
            });
        }

        // ==========================================
        // TIER 2: Boundary & Corner Cases
        // ==========================================

        [Fact]
        public void Test_Case2_ReprintWithin60s_HitsCache()
        {
            StaThreadHelper.Run(async () =>
            {
                // Arrange
                var env = SetupEnvironment();
                string waybill = "840012345678";
                
                var trackingRow = new TrackingRow
                {
                    WaybillNo = waybill,
                    MaDoan2 = "5165A",
                    SafetyEvents = new List<TrackingSafetyEvent>
                    {
                        new TrackingSafetyEvent { ScanNetworkCode = "5165A", ScanTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") }
                    }
                };
                env.TrackingService.Rows.Add(trackingRow);

                env.ApiClient.PrintListPageResponseJson = @"{""code"":""200"",""data"":{""records"":[{""waybillNo"":""840012345678"",""statusName"":""Approved"",""printCount"":0}]}}";
                env.ApiClient.TrackingResponseJson = @"{""code"":""200"",""data"":[{""keyword"":""840012345678"",""details"":[{""scanNetworkCode"":""5165A""}]}]}";
                
                await env.PrintService.SearchAndLoadAsync(waybill, PrintMode.InHoan);
                
                var request = new PrintJobRequest
                {
                    SelectedWaybills = new List<string> { waybill },
                    PrintType = 1,
                    ApplyTypeCode = 4,
                    KeepPdfs = 500,
                    Source = "Print"
                };
                
                // First Print: cache miss
                env.PrintService.SelectAll(true);
                var result1 = await env.Coordinator.PrintAsync(request);
                Assert.True(result1.CompletedBySpooler);
                
                int apiCallsAfterFirst = env.ApiClient.ApiCallCount;
                int downloadsAfterFirst = env.Downloader.DownloadCallCount;

                // Second Print (within 60s): cache hit
                env.PrintService.SelectAll(true);
                var result2 = await env.Coordinator.PrintAsync(request);

                // Assert
                Assert.True(result2.CompletedBySpooler);
                Assert.Equal(apiCallsAfterFirst, env.ApiClient.ApiCallCount); // No new API calls
                Assert.Equal(downloadsAfterFirst, env.Downloader.DownloadCallCount); // No new downloads
                Assert.Equal(2, env.Spooler.SubmitCallCount); // Spooler submit increments
                Assert.Empty(env.PrintService.GetSelectedWaybills()); // Selection cleared
            });
        }

        [Fact]
        public void Test_Case4_ApiFail_SelectionRemains()
        {
            StaThreadHelper.Run(async () =>
            {
                // Arrange
                var env = SetupEnvironment();
                string waybill = "840012345678";
                
                var trackingRow = new TrackingRow
                {
                    WaybillNo = waybill,
                    MaDoan2 = "5165A",
                    SafetyEvents = new List<TrackingSafetyEvent>
                    {
                        new TrackingSafetyEvent { ScanNetworkCode = "5165A", ScanTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") }
                    }
                };
                env.TrackingService.Rows.Add(trackingRow);

                env.ApiClient.PrintListPageResponseJson = @"{""code"":""200"",""data"":{""records"":[{""waybillNo"":""840012345678"",""statusName"":""Approved"",""printCount"":0}]}}";
                env.ApiClient.TrackingResponseJson = @"{""code"":""200"",""data"":[{""keyword"":""840012345678"",""details"":[{""scanNetworkCode"":""5165A""}]}]}";
                
                await env.PrintService.SearchAndLoadAsync(waybill, PrintMode.InHoan);
                
                // Force JMS API failure
                env.ApiClient.PrintWaybillResponseJson = @"{""code"":""500"",""msg"":""Internal Server Error"",""data"":null}";

                env.PrintService.SelectAll(true);
                Assert.Single(env.PrintService.GetSelectedWaybills());

                var request = new PrintJobRequest
                {
                    SelectedWaybills = new List<string> { waybill },
                    PrintType = 1,
                    ApplyTypeCode = 4,
                    KeepPdfs = 500,
                    Source = "Print"
                };

                // Act
                var result = await env.Coordinator.PrintAsync(request);

                // Assert
                Assert.Null(result); // Return null on print pipeline failure
                Assert.Equal(0, env.Downloader.DownloadCallCount);
                Assert.Equal(0, env.Spooler.SubmitCallCount);
                
                // Grid selection remains
                Assert.Single(env.PrintService.GetSelectedWaybills());
                Assert.False(env.UiController.ClearDashSelectionCalled);
                Assert.Contains("Lỗi từ máy chủ JMS", env.UiController.LastMessageShown); // Warning to user
            });
        }

        [Fact]
        public void Test_Case5_PrinterSubmitFail_LogAndNoClear()
        {
            StaThreadHelper.Run(async () =>
            {
                // Arrange
                var env = SetupEnvironment();
                string waybill = "840012345678";
                
                var trackingRow = new TrackingRow
                {
                    WaybillNo = waybill,
                    MaDoan2 = "5165A",
                    SafetyEvents = new List<TrackingSafetyEvent>
                    {
                        new TrackingSafetyEvent { ScanNetworkCode = "5165A", ScanTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") }
                    }
                };
                env.TrackingService.Rows.Add(trackingRow);

                env.ApiClient.PrintListPageResponseJson = @"{""code"":""200"",""data"":{""records"":[{""waybillNo"":""840012345678"",""statusName"":""Approved"",""printCount"":0}]}}";
                env.ApiClient.TrackingResponseJson = @"{""code"":""200"",""data"":[{""keyword"":""840012345678"",""details"":[{""scanNetworkCode"":""5165A""}]}]}";
                
                await env.PrintService.SearchAndLoadAsync(waybill, PrintMode.InHoan);
                
                // Force printer submit failure
                env.Spooler.SubmitShouldSucceed = false;
                env.Spooler.FailureReason = "PRINT_SPOOLER_FAILED";

                env.PrintService.SelectAll(true);
                Assert.Single(env.PrintService.GetSelectedWaybills());

                var request = new PrintJobRequest
                {
                    SelectedWaybills = new List<string> { waybill },
                    PrintType = 1,
                    ApplyTypeCode = 4,
                    KeepPdfs = 500,
                    Source = "Print"
                };

                // Act
                var result = await env.Coordinator.PrintAsync(request);

                // Assert
                Assert.NotNull(result);
                Assert.False(result.CompletedBySpooler);
                Assert.Equal("PRINT_SPOOLER_FAILED", result.Reason);
                Assert.Equal(1, env.Spooler.SubmitCallCount);
                
                // Grid selection remains
                Assert.Single(env.PrintService.GetSelectedWaybills());
                Assert.False(env.UiController.ClearDashSelectionCalled);
            });
        }

        // ==========================================
        // TIER 3: Cross-Feature Combinations
        // ==========================================

        [Fact]
        public void Test_Case3_RapidSpam_RunsOnlyOnce()
        {
            StaThreadHelper.Run(async () =>
            {
                // Arrange
                var env = SetupEnvironment();
                string waybill = "840012345678";
                
                var trackingRow = new TrackingRow
                {
                    WaybillNo = waybill,
                    MaDoan2 = "5165A",
                    SafetyEvents = new List<TrackingSafetyEvent>
                    {
                        new TrackingSafetyEvent { ScanNetworkCode = "5165A", ScanTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") }
                    }
                };
                env.TrackingService.Rows.Add(trackingRow);

                env.ApiClient.PrintListPageResponseJson = @"{""code"":""200"",""data"":{""records"":[{""waybillNo"":""840012345678"",""statusName"":""Approved"",""printCount"":0}]}}";
                env.ApiClient.TrackingResponseJson = @"{""code"":""200"",""data"":[{""keyword"":""840012345678"",""details"":[{""scanNetworkCode"":""5165A""}]}]}";
                
                await env.PrintService.SearchAndLoadAsync(waybill, PrintMode.InHoan);
                env.PrintService.SelectAll(true);

                var request = new PrintJobRequest
                {
                    SelectedWaybills = new List<string> { waybill },
                    PrintType = 1,
                    ApplyTypeCode = 4,
                    KeepPdfs = 500,
                    Source = "Print"
                };

                // Act: Trigger two parallel requests to simulate rapid double-clicks
                var task1 = env.Coordinator.PrintAsync(request);
                var task2 = env.Coordinator.PrintAsync(request);

                await Task.WhenAll(task1, task2);

                var result1 = await task1;
                var result2 = await task2;

                // Assert
                bool oneSucceeded = (result1 != null && result1.CompletedBySpooler) ^ (result2 != null && result2.CompletedBySpooler);
                Assert.True(oneSucceeded, "Exactly one of the parallel print tasks must succeed");

                // One request was rejected/ignored
                var rejectedResult = (result1 == null || !result1.CompletedBySpooler) ? result1 : result2;
                if (rejectedResult != null)
                {
                    Assert.Equal("DUPLICATE_PRINT_REQUEST_IGNORED", rejectedResult.Reason);
                }

                // Verify resource consumption is exactly 1 (No duplicate API calls or spooling)
                Assert.Equal(1, env.ApiClient.ApiCallCount);
                Assert.Equal(1, env.Downloader.DownloadCallCount);
                Assert.Equal(1, env.Spooler.SubmitCallCount);
            });
        }

        // ==========================================
        // TIER 4: Real-World Application Scenarios
        // ==========================================

        [Fact]
        public void Test_Case6_SelectionClearing_TabPrint()
        {
            StaThreadHelper.Run(async () =>
            {
                // Arrange
                var env = SetupEnvironment();
                string waybill = "840012345678";
                
                var trackingRow = new TrackingRow
                {
                    WaybillNo = waybill,
                    MaDoan2 = "5165A",
                    SafetyEvents = new List<TrackingSafetyEvent>
                    {
                        new TrackingSafetyEvent { ScanNetworkCode = "5165A", ScanTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") }
                    }
                };
                env.TrackingService.Rows.Add(trackingRow);

                env.ApiClient.PrintListPageResponseJson = @"{""code"":""200"",""data"":{""records"":[{""waybillNo"":""840012345678"",""statusName"":""Approved"",""printCount"":0}]}}";
                env.ApiClient.TrackingResponseJson = @"{""code"":""200"",""data"":[{""keyword"":""840012345678"",""details"":[{""scanNetworkCode"":""5165A""}]}]}";
                
                await env.PrintService.SearchAndLoadAsync(waybill, PrintMode.InHoan);
                
                env.PrintService.SelectAll(true);
                Assert.Single(env.PrintService.GetSelectedWaybills());

                var request = new PrintJobRequest
                {
                    SelectedWaybills = new List<string> { waybill },
                    PrintType = 1,
                    ApplyTypeCode = 4,
                    KeepPdfs = 500,
                    Source = "Print"
                };

                // Act
                var result = await env.Coordinator.PrintAsync(request);

                // Assert
                Assert.True(result.CompletedBySpooler);
                
                // TabPrint selection cleared
                Assert.Empty(env.PrintService.GetSelectedWaybills());
            });
        }

        [Fact]
        public void Test_Case7_SelectionClearing_TabDash()
        {
            StaThreadHelper.Run(async () =>
            {
                // Arrange
                var env = SetupEnvironment();
                string waybill = "840012345678";
                
                var trackingRow = new TrackingRow
                {
                    WaybillNo = waybill,
                    MaDoan2 = "5165A",
                    SafetyEvents = new List<TrackingSafetyEvent>
                    {
                        new TrackingSafetyEvent { ScanNetworkCode = "5165A", ScanTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") }
                    }
                };
                env.TrackingService.Rows.Add(trackingRow);

                env.ApiClient.PrintListPageResponseJson = @"{""code"":""200"",""data"":{""records"":[{""waybillNo"":""840012345678"",""statusName"":""Approved"",""printCount"":0}]}}";
                env.ApiClient.TrackingResponseJson = @"{""code"":""200"",""data"":[{""keyword"":""840012345678"",""details"":[{""scanNetworkCode"":""5165A""}]}]}";
                
                await env.PrintService.SearchAndLoadAsync(waybill, PrintMode.InHoan);
                env.PrintService.SelectAll(true);

                var request = new PrintJobRequest
                {
                    SelectedWaybills = new List<string> { waybill },
                    PrintType = 1,
                    ApplyTypeCode = 4,
                    KeepPdfs = 500,
                    Source = "Print"
                };

                // Act
                var result = await env.Coordinator.PrintAsync(request);

                // Assert
                Assert.True(result.CompletedBySpooler);
                
                // TabDash selection cleared
                Assert.True(env.UiController.ClearDashSelectionCalled);
            });
        }
    }
}
```
