# AutoJMS Test Infrastructure & Design Analysis

This document outlines the design and architecture for the test infrastructure in `tests/AutoJMS.Tests` targeting `net8.0-windows` and utilizing xUnit. It details mockable abstractions for `JmsApiClient` and the Printer Spooler, and drafts the implementation for the 7 required test cases across Tiers 1-4.

---

## 1. Codebase Analysis & Current Print Flow

Currently, printing in AutoJMS is triggered from the UI (`Main.cs`) and coordinates several components:
1. **Selection & Validation**: `PrintService.ValidateSelectedBeforePrintAsync` checks if the selected waybills are valid for the active site context (retrieved from WMI/Settings).
2. **PDF URL Retrieval**: Calls the static class `JmsApiClient.PostJsonAsync` to contact the J&T Express JMS API.
3. **Caching**: Checked locally. If the PDF was downloaded recently, it uses the cached file instead of downloading it again.
4. **Printer Preflight & Spooler Monitoring**: Calls WMI (via `PrinterPreflightService.cs`) to check queue health, submits the document using `PrintDocument`, and monitors the spooler queue for success.
5. **Selection Clearing**: Clears selected rows on `tabPrint_grid` and `tabDash_grid` if spooling succeeds.

---

## 2. Decoupling & Mockable Interfaces

To run tests in isolation without actual network requests or a physical printer, we introduce three core abstractions:

### A. IJmsApiClient
To mock HTTP calls, we transition the static `JmsApiClient` calls to use an interface:
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
To preserve compatibility with existing code and comply with the **Minimal Edit Rule**, we keep `JmsApiClient` as a static class, but introduce a mockable instance wrapper:
```csharp
namespace AutoJMS
{
    public static class JmsApiClient
    {
        public static IJmsApiClient Instance { get; set; } = new DefaultJmsApiClient();

        public static Task<HttpResponseMessage> PostJsonAsync(
            string url,
            string jsonBody,
            string routeName = "trackingExpress",
            string routerNameList = null,
            string origin = "https://jms.jtexpress.vn",
            CancellationToken ct = default)
        {
            return Instance.PostJsonAsync(url, jsonBody, routeName, routerNameList, origin, ct);
        }
    }
}
```
`DefaultJmsApiClient` implements `IJmsApiClient` using the original `HttpClient` call logic.

### B. IPrinterSpoolerSubmitter
The actual Win32 printing spooler commands and queue monitoring logic currently located in `Main.cs` (e.g. `SubmitPrintImmediatelyAsync`) are wrapped in `IPrinterSpoolerSubmitter`:
```csharp
namespace AutoJMS
{
    public interface IPrinterSpoolerSubmitter
    {
        Task<PrintSubmitResult> SubmitPrintImmediatelyAsync(PrintJobCacheEntry job, string firstWaybill);
    }
}
```

### C. IPrintUiController
To mock grid and message updates on the UI thread without running the actual Windows Forms event loop, we define:
```csharp
namespace AutoJMS
{
    public interface IPrintUiController
    {
        void ClearPrintGridSelection();
        void ClearDashGridSelection();
        void ShowPrintMessage(string message, bool isError, int durationMs = 0);
    }
}
```

---

## 3. Test Project Setup (`tests/AutoJMS.Tests`)

### A. File Layout
The test project structure will be created under `tests/AutoJMS.Tests/`:
```
tests/AutoJMS.Tests/
├── AutoJMS.Tests.csproj
├── Mocks/
│   ├── MockJmsApiClient.cs
│   ├── MockPrinterSpoolerSubmitter.cs
│   ├── MockPrintUiController.cs
│   └── MockPrintService.cs
└── PrintJobCoordinatorTests.cs
```

### B. Project Configuration File (`tests/AutoJMS.Tests/AutoJMS.Tests.csproj`)
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>disable</Nullable>
    <UseWindowsForms>true</UseWindowsForms>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.5.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.3">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="coverlet.collector" Version="6.0.0">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\AutoJMS\AutoJMS.csproj" />
  </ItemGroup>
</Project>
```

### C. Solution Integration (`AutoJMS.slnx`)
The solution file will include the new test project:
```xml
<Solution>
  <Project Path="src/AutoJMS/AutoJMS.csproj" DefaultStartup="true" />
  <Project Path="src/AutoJMS.Abstractions/AutoJMS.Abstractions.csproj" />
  <Project Path="tests/AutoJMS.Tests/AutoJMS.Tests.csproj" />
</Solution>
```

---

## 4. Mock Implementations

### Mocks/MockJmsApiClient.cs
```csharp
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS.Tests.Mocks
{
    public class MockJmsApiClient : IJmsApiClient
    {
        public int CallCount { get; private set; }
        public string LastUrl { get; private set; }
        public string LastJsonBody { get; private set; }
        
        public HttpStatusCode ResponseStatusCode { get; set; } = HttpStatusCode.OK;
        public string ResponseBody { get; set; }

        public Task<HttpResponseMessage> PostJsonAsync(
            string url,
            string jsonBody,
            string routeName = "trackingExpress",
            string routerNameList = null,
            string origin = "https://jms.jtexpress.vn",
            CancellationToken ct = default)
        {
            CallCount++;
            LastUrl = url;
            LastJsonBody = jsonBody;

            var response = new HttpResponseMessage(ResponseStatusCode);
            if (ResponseBody != null)
            {
                response.Content = new StringContent(ResponseBody);
            }
            return Task.FromResult(response);
        }

        public void Reset()
        {
            CallCount = 0;
            LastUrl = null;
            LastJsonBody = null;
        }
    }
}
```

### Mocks/MockPrinterSpoolerSubmitter.cs
```csharp
using System.Threading.Tasks;

namespace AutoJMS.Tests.Mocks
{
    public class MockPrinterSpoolerSubmitter : IPrinterSpoolerSubmitter
    {
        public int SubmitCount { get; private set; }
        public PrintJobCacheEntry LastSubmittedJob { get; private set; }
        public string LastFirstWaybill { get; private set; }

        public bool SimulateSuccess { get; set; } = true;
        public string FailureReason { get; set; } = "UnknownError";

        public Task<PrintSubmitResult> SubmitPrintImmediatelyAsync(PrintJobCacheEntry job, string firstWaybill)
        {
            SubmitCount++;
            LastSubmittedJob = job;
            LastFirstWaybill = firstWaybill;

            if (SimulateSuccess)
            {
                return Task.FromResult(new PrintSubmitResult
                {
                    CompletedBySpooler = true,
                    PrinterName = "MockPrinter",
                    DocumentName = "MockDoc",
                    ElapsedMs = 10,
                    SpoolerJobsBefore = 0,
                    SpoolerJobsAfter = 1,
                    Reason = "Success"
                });
            }
            else
            {
                return Task.FromResult(new PrintSubmitResult
                {
                    CompletedBySpooler = false,
                    PrinterName = "MockPrinter",
                    DocumentName = "MockDoc",
                    ElapsedMs = 10,
                    SpoolerJobsBefore = 0,
                    SpoolerJobsAfter = 0,
                    Reason = FailureReason
                });
            }
        }

        public void Reset()
        {
            SubmitCount = 0;
            LastSubmittedJob = null;
            LastFirstWaybill = null;
        }
    }
}
```

### Mocks/MockPrintUiController.cs
```csharp
namespace AutoJMS.Tests.Mocks
{
    public class MockPrintUiController : IPrintUiController
    {
        public bool ClearPrintGridCalled { get; private set; }
        public bool ClearDashGridCalled { get; private set; }
        public string LastMessage { get; private set; }
        public bool LastMessageIsError { get; private set; }

        public void ClearPrintGridSelection() => ClearPrintGridCalled = true;
        public void ClearDashGridSelection() => ClearDashGridCalled = true;
        public void ShowPrintMessage(string message, bool isError, int durationMs = 0)
        {
            LastMessage = message;
            LastMessageIsError = isError;
        }

        public void Reset()
        {
            ClearPrintGridCalled = false;
            ClearDashGridCalled = false;
            LastMessage = null;
            LastMessageIsError = false;
        }
    }
}
```

### Mocks/MockPrintService.cs
```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS.Tests.Mocks
{
    public class MockPrintService : IPrintService
    {
        public PrintMode CurrentMode { get; set; } = PrintMode.InHoan;
        
        public event Action<int, int> OnPrintStatsChanged;
        public event Action<PrintSafetyResult> OnPrintSafetyBlocked;

        public List<string> SelectedWaybills { get; set; } = new();
        public bool ValidateResult { get; set; } = true;
        public bool ValidateCalled { get; private set; }
        public bool ClearSelectionCalled { get; private set; }

        public Task SearchAndLoadAsync(string waybillsText, PrintMode mode)
        {
            CurrentMode = mode;
            return Task.CompletedTask;
        }

        public Task<bool> ValidateSelectedBeforePrintAsync(IEnumerable<string> waybills, string currentInputText)
        {
            ValidateCalled = true;
            return Task.FromResult(ValidateResult);
        }

        public Task<IReadOnlyList<PrintApprovalInfo>> RefreshPrintApprovalInfoAsync(IEnumerable<string> waybills, int printType, string phase)
        {
            return Task.FromResult<IReadOnlyList<PrintApprovalInfo>>(new List<PrintApprovalInfo>());
        }

        public Task<IReadOnlyList<PrintStatusSnapshot>> RefreshPrintStatusAsync(IEnumerable<string> waybills, int printType, PrintStatusRefreshReason reason, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<PrintStatusSnapshot>>(new List<PrintStatusSnapshot>());
        }

        public void QueuePostPrintRefresh(IEnumerable<string> waybills, int printType) {}

        public IReadOnlyList<PrintStatusSnapshot> GetLastPrintStatusSnapshots() => new List<PrintStatusSnapshot>();

        public PrintSafetyResult GetLastAllowedPrintSafetyResult(string waybillNo) => new() { CanPrint = true };

        public void SelectAll(bool isChecked) {}

        public void ClearSelection()
        {
            ClearSelectionCalled = true;
            SelectedWaybills.Clear();
        }

        public List<string> GetSelectedWaybills() => SelectedWaybills;

        public void SetMode(PrintMode mode) => CurrentMode = mode;

        public void Reset()
        {
            ValidateCalled = false;
            ClearSelectionCalled = false;
            SelectedWaybills.Clear();
        }
    }
}
```

---

## 5. Draft Implementation of the 7 Test Cases

Below is the draft structure for `PrintJobCoordinatorTests.cs`. It tests all edge cases and satisfies the requirement checks in isolation.

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using AutoJMS.Printing;
using AutoJMS.Tests.Mocks;
using Xunit;

namespace AutoJMS.Tests
{
    public class PrintJobCoordinatorTests : IDisposable
    {
        private readonly MockPrintService _printService;
        private readonly MockJmsApiClient _jmsApiClient;
        private readonly MockPrinterSpoolerSubmitter _spoolerSubmitter;
        private readonly MockPrintUiController _uiController;
        private readonly PrintJobCoordinator _coordinator;
        private readonly string _tempPdfPath;

        public PrintJobCoordinatorTests()
        {
            _printService = new MockPrintService();
            _jmsApiClient = new MockJmsApiClient();
            _spoolerSubmitter = new MockPrinterSpoolerSubmitter();
            _uiController = new MockPrintUiController();
            
            _coordinator = new PrintJobCoordinator(
                _printService, 
                _jmsApiClient, 
                _spoolerSubmitter, 
                _uiController);

            // Create a dummy PDF bytes payload
            _tempPdfPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            File.WriteAllBytes(_tempPdfPath, new byte[] { 0x25, 0x50, 0x44, 0x46, 0x00 }); 
        }

        public void Dispose()
        {
            if (File.Exists(_tempPdfPath))
            {
                try { File.Delete(_tempPdfPath); } catch { }
            }
        }

        [Fact]
        public async Task Case1_FirstPrint_ShouldSucceedAndClearSelections()
        {
            // Arrange
            var waybills = new List<string> { "84000123456" };
            _printService.SelectedWaybills = waybills;
            _printService.ValidateResult = true;

            var mockResponseJson = "{\"code\":\"200\",\"msg\":\"success\",\"data\":\"http://test.com/sample.pdf\"}";
            _jmsApiClient.ResponseStatusCode = HttpStatusCode.OK;
            _jmsApiClient.ResponseBody = mockResponseJson;

            _coordinator.SetDownloaderFunc((url) => Task.FromResult(_tempPdfPath));

            var request = new PrintJobRequest
            {
                SelectedWaybills = waybills,
                PrintType = 1,
                ApplyTypeCode = 4,
                KeepPdfs = 10,
                Source = "Print"
            };

            // Act
            var result = await _coordinator.PrintAsync(request);

            // Assert
            Assert.True(result.CompletedBySpooler);
            Assert.Equal(1, _jmsApiClient.CallCount); // apiRequestCountForJob = 1
            Assert.True(_uiController.ClearPrintGridCalled); // Selection cleared
            Assert.True(_uiController.ClearDashGridCalled);
        }

        [Fact]
        public async Task Case2_ReprintWithin60s_ShouldHitCacheAndNotDownloadAgain()
        {
            // Arrange
            var waybills = new List<string> { "84000123456" };
            _printService.SelectedWaybills = waybills;
            _printService.ValidateResult = true;

            var mockResponseJson = "{\"code\":\"200\",\"msg\":\"success\",\"data\":\"http://test.com/sample.pdf\"}";
            _jmsApiClient.ResponseStatusCode = HttpStatusCode.OK;
            _jmsApiClient.ResponseBody = mockResponseJson;

            int downloadCount = 0;
            _coordinator.SetDownloaderFunc((url) => 
            {
                downloadCount++;
                return Task.FromResult(_tempPdfPath);
            });

            var request = new PrintJobRequest
            {
                SelectedWaybills = waybills,
                PrintType = 1,
                ApplyTypeCode = 4,
                KeepPdfs = 10,
                Source = "Print"
            };

            // Act - First Print
            var result1 = await _coordinator.PrintAsync(request);
            Assert.True(result1.CompletedBySpooler);
            Assert.Equal(1, _jmsApiClient.CallCount);
            Assert.Equal(1, downloadCount);

            // Act - Reprint within 60s
            _printService.SelectedWaybills = waybills; 
            _jmsApiClient.Reset(); 
            _uiController.Reset();

            var result2 = await _coordinator.PrintAsync(request);

            // Assert
            Assert.True(result2.CompletedBySpooler);
            Assert.Equal(1, _jmsApiClient.CallCount); // apiRequestCountForJob = 1
            Assert.Equal(1, downloadCount); // no second download (downloadCount remains 1)
            Assert.True(_uiController.ClearPrintGridCalled);
            Assert.True(_uiController.ClearDashGridCalled);
        }

        [Fact]
        public async Task Case3_RapidSpam_ShouldIgnoreSecondConcurrentRequest()
        {
            // Arrange
            var waybills = new List<string> { "84000123456" };
            _printService.SelectedWaybills = waybills;
            _printService.ValidateResult = true;

            _jmsApiClient.ResponseStatusCode = HttpStatusCode.OK;
            _jmsApiClient.ResponseBody = "{\"code\":\"200\",\"msg\":\"success\",\"data\":\"http://test.com/sample.pdf\"}";
            
            _coordinator.SetDownloaderFunc(async (url) => 
            {
                await Task.Delay(100); // Simulate network/download delay
                return _tempPdfPath;
            });

            var request = new PrintJobRequest
            {
                SelectedWaybills = waybills,
                PrintType = 1,
                ApplyTypeCode = 4,
                KeepPdfs = 10,
                Source = "Print"
            };

            // Act - Spam concurrent requests
            var printTask1 = _coordinator.PrintAsync(request);
            var printTask2 = _coordinator.PrintAsync(request); // Sent while printTask1 is active

            var result1 = await printTask1;
            var result2 = await printTask2;

            // Assert
            Assert.True(result1.CompletedBySpooler);
            Assert.False(result2.CompletedBySpooler);
            Assert.Equal("DUPLICATE_PRINT_REQUEST_IGNORED", result2.Reason);
            Assert.Equal(1, _jmsApiClient.CallCount); 
            Assert.Equal(1, _spoolerSubmitter.SubmitCount); 
        }

        [Fact]
        public async Task Case4_ApiFail_ShouldNotPrintFromCacheAndKeepSelection()
        {
            // Arrange
            var waybills = new List<string> { "84000123456" };
            _printService.SelectedWaybills = waybills;
            _printService.ValidateResult = true;

            // 1. Success print first to populate cache
            _jmsApiClient.ResponseStatusCode = HttpStatusCode.OK;
            _jmsApiClient.ResponseBody = "{\"code\":\"200\",\"msg\":\"success\",\"data\":\"http://test.com/sample.pdf\"}";
            _coordinator.SetDownloaderFunc((url) => Task.FromResult(_tempPdfPath));

            var request = new PrintJobRequest
            {
                SelectedWaybills = waybills,
                PrintType = 1,
                ApplyTypeCode = 4,
                KeepPdfs = 10,
                Source = "Print"
            };

            var successResult = await _coordinator.PrintAsync(request);
            Assert.True(successResult.CompletedBySpooler);

            // 2. Now simulate API failure on subsequent request
            _printService.SelectedWaybills = waybills; 
            _jmsApiClient.Reset();
            _jmsApiClient.ResponseStatusCode = HttpStatusCode.InternalServerError;
            _jmsApiClient.ResponseBody = "Internal Server Error";
            _spoolerSubmitter.Reset();
            _uiController.Reset();

            // Act
            var failResult = await _coordinator.PrintAsync(request);

            // Assert
            Assert.False(failResult.CompletedBySpooler);
            Assert.Equal(0, _spoolerSubmitter.SubmitCount); // Did NOT print
            Assert.False(_uiController.ClearPrintGridCalled); // Selections remain
            Assert.False(_uiController.ClearDashGridCalled);
            Assert.True(_uiController.LastMessageIsError); 
        }

        [Fact]
        public async Task Case5_PrinterSubmitFail_ShouldLogFailedKeepSelectionAndNoRetry()
        {
            // Arrange
            var waybills = new List<string> { "84000123456" };
            _printService.SelectedWaybills = waybills;
            _printService.ValidateResult = true;

            _jmsApiClient.ResponseStatusCode = HttpStatusCode.OK;
            _jmsApiClient.ResponseBody = "{\"code\":\"200\",\"msg\":\"success\",\"data\":\"http://test.com/sample.pdf\"}";
            _coordinator.SetDownloaderFunc((url) => Task.FromResult(_tempPdfPath));

            // Configure spooler failure
            _spoolerSubmitter.SimulateSuccess = false;
            _spoolerSubmitter.FailureReason = "PRINT_SPOOLER_FAILED";

            var request = new PrintJobRequest
            {
                SelectedWaybills = waybills,
                PrintType = 1,
                ApplyTypeCode = 4,
                KeepPdfs = 10,
                Source = "Print"
            };

            // Act
            var result = await _coordinator.PrintAsync(request);

            // Assert
            Assert.False(result.CompletedBySpooler);
            Assert.Equal("PRINT_SPOOLER_FAILED", result.Reason);
            Assert.Equal(1, _spoolerSubmitter.SubmitCount); // Tried exactly once, no auto-retry
            Assert.False(_uiController.ClearPrintGridCalled); 
            Assert.False(_uiController.ClearDashGridCalled);
        }

        [Fact]
        public async Task Case6_SelectionClearing_PrintTab_ShouldClearOnSuccess()
        {
            // Arrange
            var waybills = new List<string> { "84000123456" };
            _printService.SelectedWaybills = waybills;
            _printService.ValidateResult = true;

            _jmsApiClient.ResponseStatusCode = HttpStatusCode.OK;
            _jmsApiClient.ResponseBody = "{\"code\":\"200\",\"msg\":\"success\",\"data\":\"http://test.com/sample.pdf\"}";
            _coordinator.SetDownloaderFunc((url) => Task.FromResult(_tempPdfPath));

            var request = new PrintJobRequest
            {
                SelectedWaybills = waybills,
                PrintType = 1,
                ApplyTypeCode = 4,
                KeepPdfs = 10,
                Source = "Print"
            };

            // Act
            var result = await _coordinator.PrintAsync(request);

            // Assert
            Assert.True(result.CompletedBySpooler);
            Assert.True(_uiController.ClearPrintGridCalled);
        }

        [Fact]
        public async Task Case7_SelectionClearing_DashTab_ShouldClearOnSuccess()
        {
            // Arrange
            var waybills = new List<string> { "84000123456" };
            _printService.SelectedWaybills = waybills;
            _printService.ValidateResult = true;

            _jmsApiClient.ResponseStatusCode = HttpStatusCode.OK;
            _jmsApiClient.ResponseBody = "{\"code\":\"200\",\"msg\":\"success\",\"data\":\"http://test.com/sample.pdf\"}";
            _coordinator.SetDownloaderFunc((url) => Task.FromResult(_tempPdfPath));

            var request = new PrintJobRequest
            {
                SelectedWaybills = waybills,
                PrintType = 1,
                ApplyTypeCode = 4,
                KeepPdfs = 10,
                Source = "Print"
            };

            // Act
            var result = await _coordinator.PrintAsync(request);

            // Assert
            Assert.True(result.CompletedBySpooler);
            Assert.True(_uiController.ClearDashGridCalled);
        }
    }
}
```
