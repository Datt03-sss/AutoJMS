# E2E Testing Infrastructure Analysis and Design

This document details the analysis of the AutoJMS printing logic and presents the design for the E2E testing infrastructure.

---

## 1. Summary of Findings

1. **Monolithic Print Action**: Currently, the actual print processing and PDF generation reside directly within event handlers and private methods in `Main.cs` (e.g. `ExecutePrintAsync`, `EnsureReadyToPrintCoreAsync`, `SubmitPrintImmediatelyAsync`, and WMI spooler querying).
2. **Tight Static Couplings**:
   - `JmsApiClient` is a static class whose static methods are directly called.
   - The printer spooler relies on physical PDF printing via `PrintDocument.Print()` and reads queue jobs using WMI (`Win32_PrintJob`).
3. **UI Thread Dependency**: Form grids (`DataGridView` selections) are manipulated directly. In tests, instantiating and modifying WinForms controls requires a Single-Threaded Apartment (STA) thread.
4. **Target Structure**: According to `PROJECT.md`, a `PrintJobCoordinator` will be introduced to orchestrate waybill printing. It must act as the integration point between `Main`, `PrintService` (grid state), `JmsApiClient` (APIs), and the printer spooler.

---

## 2. Mocking Strategy & Interface Design

To make the printing logic testable in complete isolation, we propose introducing mockable interfaces and resolving them through Constructor Injection inside `PrintJobCoordinator` (and optionally `PrintService`).

### A. JmsApiClient Abstraction (`IJmsApiClient`)
Instead of calling `JmsApiClient.PostJsonAsync` directly, we introduce:

```csharp
namespace AutoJMS;

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
```

#### Backward Compatibility Hook
To avoid massive rewrites in non-refactored parts of the codebase, we modify `JmsApiClient` to forward static calls to an instance proxy:

```csharp
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
```

### B. Printer Spooler Abstraction (`IPrinterSpoolerSubmitter`)
We isolate the physical printer interaction by introducing:

```csharp
namespace AutoJMS;

public interface IPrinterSpoolerSubmitter
{
    Task<PrintSubmitResult> SubmitPrintImmediatelyAsync(PrintJobCacheEntry job, string firstWaybill);
}
```

### C. Relocating & Defining DTOs
1. **`PrintSubmitResult`**: Currently a private nested class inside `Main.cs`. It must be moved to a shared file (`src/AutoJMS/Printing/PrintSubmitResult.cs` or `AutoJMS.Abstractions`) and made `public`:
   ```csharp
   namespace AutoJMS;

   public sealed class PrintSubmitResult
   {
       public bool CompletedBySpooler { get; init; }
       public long ElapsedMs { get; init; }
       public string PrinterName { get; init; } = "";
       public string DocumentName { get; init; } = "";
       public int SpoolerJobsBefore { get; init; }
       public int SpoolerJobsAfter { get; init; }
       public string Reason { get; init; } = "";
   }
   ```
2. **`PrintJobRequest`**: Encapsulates the parameters passed to the print coordinator:
   ```csharp
   namespace AutoJMS;

   public sealed class PrintJobRequest
   {
       public List<string> SelectedWaybills { get; init; } = new();
       public int PrintType { get; init; }
       public int ApplyTypeCode { get; init; }
       public int KeepPdfs { get; init; }
       public string Source { get; init; } = "";
   }
   ```

### D. Proposed PrintJobCoordinator Class Structure
```csharp
namespace AutoJMS;

public class PrintJobCoordinator
{
    private readonly IJmsApiClient _jmsApiClient;
    private readonly IPrinterSpoolerSubmitter _spoolerSubmitter;
    private readonly IPrintService _printService;
    private readonly SemaphoreSlim _printLock = new(1, 1);

    public PrintJobCoordinator(
        IJmsApiClient jmsApiClient,
        IPrinterSpoolerSubmitter spoolerSubmitter,
        IPrintService printService)
    {
        _jmsApiClient = jmsApiClient;
        _spoolerSubmitter = spoolerSubmitter;
        _printService = printService;
    }

    public async Task<PrintSubmitResult> PrintAsync(PrintJobRequest request)
    {
        // Enforce single-flight concurrency
        if (!await _printLock.WaitAsync(0))
        {
            AppLogger.Warning("DUPLICATE_PRINT_REQUEST_IGNORED");
            return new PrintSubmitResult { CompletedBySpooler = false, Reason = "duplicate-request" };
        }

        try
        {
            // 1. Validation
            bool safe = await _printService.ValidateSelectedBeforePrintAsync(request.SelectedWaybills, string.Join(Environment.NewLine, request.SelectedWaybills));
            if (!safe)
            {
                return new PrintSubmitResult { CompletedBySpooler = false, Reason = "safety-blocked" };
            }

            // 2. Fetch PDF URL (via _jmsApiClient)
            // ... (construct URL request, fetch body, parse URL)
            
            // 3. Cache Check & Download
            // ... (check local cache, download via HttpClient wrapper or _jmsApiClient)

            // 4. Submit to Spooler
            var cacheEntry = new PrintJobCacheEntry { /* ... */ };
            var result = await _spoolerSubmitter.SubmitPrintImmediatelyAsync(cacheEntry, request.SelectedWaybills[0]);

            // 5. Clear selections on success
            if (result.CompletedBySpooler)
            {
                _printService.SelectAll(false);
                _printService.ClearSelection();
            }

            return result;
        }
        finally
        {
            _printLock.Release();
        }
    }
}
```

---

## 3. Test Project Infrastructure Design

### A. Folder Layout
```
tests/
└── AutoJMS.Tests/
    ├── AutoJMS.Tests.csproj
    ├── Helpers/
    │   └── StaTaskScheduler.cs
    ├── Mocks/
    │   ├── MockJmsApiClient.cs
    │   └── MockPrinterSpoolerSubmitter.cs
    └── PrintJobCoordinatorTests.cs
```

### B. Project File (`AutoJMS.Tests.csproj`)
To run Windows Forms code and grid interactions under test, the test project must target `net8.0-windows` and reference Windows Forms:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>disable</Nullable>
    <UseWindowsForms>true</UseWindowsForms>
    <UseWPF>true</UseWPF>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.9.0" />
    <PackageReference Include="xunit" Version="2.6.6" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.7">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="coverlet.collector" Version="6.0.0">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Moq" Version="4.20.70" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\AutoJMS\AutoJMS.csproj" />
  </ItemGroup>

</Project>
```

### C. Solution Wiring (`AutoJMS.slnx`)
Add the test project to the root `.slnx` file:
```xml
<Solution>
  <Project Path="src/AutoJMS/AutoJMS.csproj" DefaultStartup="true" />
  <Project Path="src/AutoJMS.Abstractions/AutoJMS.Abstractions.csproj" />
  <Project Path="tests/AutoJMS.Tests/AutoJMS.Tests.csproj" />
</Solution>
```

### D. Single-Threaded Apartment Thread Helper (`StaTaskScheduler.cs`)
Since WinForms controls must be initialized and manipulated on an STA thread, we design a helper to execute test blocks safely in STA:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS.Tests.Helpers;

public static class StaTaskScheduler
{
    public static Task Run(Action action)
    {
        var tcs = new TaskCompletionSource();
        var thread = new Thread(() =>
        {
            try
            {
                action();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    public static Task<T> Run<T>(Func<T> action)
    {
        var tcs = new TaskCompletionSource<T>();
        var thread = new Thread(() =>
        {
            try
            {
                var result = action();
                tcs.SetResult(result);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }
}
```

---

## 4. Draft Test Case Implementation Designs

We sketch the xUnit test cases covering all 4 tiers required by `SCOPE.md`.

### Case 1 (Tier 1): First Print (Happy Path)
* **Objective**: Verifies that a normal first-time waybill print completes successfully.
* **Pre-conditions**:
  - Cache is empty.
  - Waybill is ticked in the DataGridView.
  - Mock API returns a valid PDF url.
  - Mock Spooler returns success.
* **Draft Code**:
```csharp
[Fact]
public async Task Test_Case1_FirstPrint_Success()
{
    await StaTaskScheduler.Run(async () =>
    {
        // 1. Setup UI elements & PrintService
        var grid = new DataGridView();
        var mockTracking = new Mock<ITrackingService>();
        var mockSafety = new Mock<IPrintSafetyGuard>();
        var mockSiteContext = new Mock<ISiteContextProvider>();

        // Setup mock safety guard to allow printing
        mockSafety.Setup(x => x.ValidateBeforePrint(It.IsAny<string>(), It.IsAny<SiteContext>(), It.IsAny<TrackingRow>()))
                  .Returns(new PrintSafetyResult { CanPrint = true });

        var printService = new PrintService(grid, mockTracking.Object, mockSafety.Object, () => "TEST_TOKEN", mockSiteContext.Object);
        
        // 2. Setup mock JmsApiClient
        var mockJmsClient = new Mock<IJmsApiClient>();
        var apiResponse = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("{\"code\": \"200\", \"data\": \"http://jms-cdn/test.pdf\"}")
        };
        int apiCalls = 0;
        mockJmsClient.Setup(x => x.PostJsonAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                     .Callback(() => apiCalls++)
                     .ReturnsAsync(apiResponse);

        // 3. Setup mock Spooler
        var mockSpooler = new Mock<IPrinterSpoolerSubmitter>();
        mockSpooler.Setup(x => x.SubmitPrintImmediatelyAsync(It.IsAny<PrintJobCacheEntry>(), "JMS123456"))
                   .ReturnsAsync(new PrintSubmitResult { CompletedBySpooler = true, PrinterName = "TestPrinter" });

        var coordinator = new PrintJobCoordinator(mockJmsClient.Object, mockSpooler.Object, printService);

        // Populate and select waybill in grid
        // Mock printService rows and selection
        // ...

        var request = new PrintJobRequest
        {
            SelectedWaybills = new List<string> { "JMS123456" },
            PrintType = 1,
            ApplyTypeCode = 4,
            Source = "Print"
        };

        // Act
        var result = await coordinator.PrintAsync(request);

        // Assert
        Assert.True(result.CompletedBySpooler);
        Assert.Equal(1, apiCalls); // apiRequestCountForJob = 1
        // Verify selection is cleared
        Assert.Empty(printService.GetSelectedWaybills());
    });
}
```

### Case 2 (Tier 2): Reprint Within 60 Seconds
* **Objective**: Verifies that reprinting the same waybill within 60s hits the cache and avoids a second PDF download, while still calling the API once to fetch/verify print info and incrementing print count by exactly 1.
* **Pre-conditions**:
  - The waybill is printed once successfully.
  - The second print request occurs within 60 seconds.
* **Draft Code**:
```csharp
[Fact]
public async Task Test_Case2_ReprintWithin60s_CacheHit()
{
    await StaTaskScheduler.Run(async () =>
    {
        // 1. Initialize mocks and coordinator
        var grid = new DataGridView();
        var printService = new PrintService(grid, /* mocks */);
        var mockJmsClient = new Mock<IJmsApiClient>();
        var mockSpooler = new Mock<IPrinterSpoolerSubmitter>();
        var coordinator = new PrintJobCoordinator(mockJmsClient.Object, mockSpooler.Object, printService);

        var request = new PrintJobRequest { SelectedWaybills = new List<string> { "JMS123456" } };

        // Mock API success response with PDF url
        mockJmsClient.Setup(x => x.PostJsonAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("...") });

        // Print first time (Cache Miss)
        var result1 = await coordinator.PrintAsync(request);

        // Print second time within 60s (Cache Hit)
        var result2 = await coordinator.PrintAsync(request);

        // Assert
        Assert.True(result2.CompletedBySpooler);
        // Verify no second HTTP client call for downloading PDF bytes
        // Verify print count increments by exactly 1
        // Verify selection is cleared
    });
}
```

### Case 3 (Tier 3): Double-click/Rapid Spam Protection
* **Objective**: Verifies that when a print job is active, rapid duplicate clicks are safely ignored.
* **Pre-conditions**:
  - An ongoing print job is executing and holding the coordinator lock.
* **Draft Code**:
```csharp
[Fact]
public async Task Test_Case3_RapidSpam_Ignored()
{
    await StaTaskScheduler.Run(async () =>
    {
        var grid = new DataGridView();
        var printService = new PrintService(grid, /* mocks */);
        var mockJmsClient = new Mock<IJmsApiClient>();
        var mockSpooler = new Mock<IPrinterSpoolerSubmitter>();
        var coordinator = new PrintJobCoordinator(mockJmsClient.Object, mockSpooler.Object, printService);

        var request = new PrintJobRequest { SelectedWaybills = new List<string> { "JMS123456" } };

        // Introduce a delay in the mock spooler to simulate active printing
        mockSpooler.Setup(x => x.SubmitPrintImmediatelyAsync(It.IsAny<PrintJobCacheEntry>(), It.IsAny<string>()))
                   .Returns(async () => {
                       await Task.Delay(500); // 500ms delay
                       return new PrintSubmitResult { CompletedBySpooler = true };
                   });

        // Trigger first print
        var task1 = coordinator.PrintAsync(request);

        // Trigger second print immediately (spamming)
        var task2 = coordinator.PrintAsync(request);

        var results = await Task.WhenAll(task1, task2);

        // Assert
        // One job should succeed, the other should be rejected/ignored
        int successCount = results.Count(r => r.CompletedBySpooler);
        int rejectedCount = results.Count(r => !r.CompletedBySpooler && r.Reason == "duplicate-request");

        Assert.Equal(1, successCount);
        Assert.Equal(1, rejectedCount);
    });
}
```

### Case 4 (Tier 2): API Fail Handling
* **Objective**: Verifies that if the JMS API fails, the selection is preserved and no printing occurs.
* **Pre-conditions**:
  - JMS API fails (500 Error).
* **Draft Code**:
```csharp
[Fact]
public async Task Test_Case4_ApiFail_NoPrint_SelectionRetained()
{
    await StaTaskScheduler.Run(async () =>
    {
        var grid = new DataGridView();
        var printService = new PrintService(grid, /* mocks */);
        var mockJmsClient = new Mock<IJmsApiClient>();
        var mockSpooler = new Mock<IPrinterSpoolerSubmitter>();
        var coordinator = new PrintJobCoordinator(mockJmsClient.Object, mockSpooler.Object, printService);

        // Setup API failure response
        mockJmsClient.Setup(x => x.PostJsonAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError));

        // Mark waybill as checked in grid
        // ...

        var request = new PrintJobRequest { SelectedWaybills = new List<string> { "JMS123456" } };

        // Act
        var result = await coordinator.PrintAsync(request);

        // Assert
        Assert.False(result.CompletedBySpooler);
        mockSpooler.Verify(x => x.SubmitPrintImmediatelyAsync(It.IsAny<PrintJobCacheEntry>(), It.IsAny<string>()), Times.Never);
        // Grid selection must still be active
        Assert.Contains("JMS123456", printService.GetSelectedWaybills());
    });
}
```

### Case 5 (Tier 2): Printer Spooler Failure
* **Objective**: Verifies that if printer spooler submission fails, selection remains checked to prevent silent failure data loss, and warning log `PRINT_SPOOLER_FAILED` is written.
* **Pre-conditions**:
  - JMS API succeeds.
  - Spooler preflight/submission fails.
* **Draft Code**:
```csharp
[Fact]
public async Task Test_Case5_SpoolerFail_SelectionRetained()
{
    await StaTaskScheduler.Run(async () =>
    {
        var grid = new DataGridView();
        var printService = new PrintService(grid, /* mocks */);
        var mockJmsClient = new Mock<IJmsApiClient>();
        var mockSpooler = new Mock<IPrinterSpoolerSubmitter>();
        var coordinator = new PrintJobCoordinator(mockJmsClient.Object, mockSpooler.Object, printService);

        // API success, Spooler fails
        mockSpooler.Setup(x => x.SubmitPrintImmediatelyAsync(It.IsAny<PrintJobCacheEntry>(), It.IsAny<string>()))
                   .ReturnsAsync(new PrintSubmitResult { CompletedBySpooler = false, Reason = "PRINT_SPOOLER_FAILED" });

        var request = new PrintJobRequest { SelectedWaybills = new List<string> { "JMS123456" } };

        // Act
        var result = await coordinator.PrintAsync(request);

        // Assert
        Assert.False(result.CompletedBySpooler);
        // Verify selection is still retained
        Assert.Contains("JMS123456", printService.GetSelectedWaybills());
    });
}
```

### Case 6 & 7 (Tier 4): Grid Selection Clearing (Print Tab and Dashboard Tab)
* **Objective**: Verifies that upon successful print completion, selections in both the Print Grid and Dashboard Grid are fully reset.
* **Pre-conditions**:
  - Waybill cells and rows are highlighted/selected in TabPrint and TabDash grids.
* **Draft Code**:
```csharp
[Fact]
public async Task Test_Case6_7_SelectionClearing_Success()
{
    await StaTaskScheduler.Run(async () =>
    {
        var gridPrint = new DataGridView();
        var gridDash = new DataGridView();

        // 1. Populate grids with test data
        // ...
        
        // 2. Select rows/cells programmatically
        gridPrint.Rows[0].Cells[0].Selected = true;
        gridPrint.CurrentCell = gridPrint.Rows[0].Cells[0];

        gridDash.Rows[0].Cells[0].Selected = true;
        gridDash.CurrentCell = gridDash.Rows[0].Cells[0];

        var printService = new PrintService(gridPrint, /* mocks */);
        var mockJmsClient = new Mock<IJmsApiClient>();
        var mockSpooler = new Mock<IPrinterSpoolerSubmitter>();
        
        // Setup successful submission
        mockSpooler.Setup(x => x.SubmitPrintImmediatelyAsync(It.IsAny<PrintJobCacheEntry>(), It.IsAny<string>()))
                   .ReturnsAsync(new PrintSubmitResult { CompletedBySpooler = true });

        var coordinator = new PrintJobCoordinator(mockJmsClient.Object, mockSpooler.Object, printService);
        var request = new PrintJobRequest { SelectedWaybills = new List<string> { "JMS123456" } };

        // Act
        var result = await coordinator.PrintAsync(request);

        // Mimic selection clearing hook in Main form
        if (result.CompletedBySpooler)
        {
            printService.ClearSelection();
            gridDash.ClearSelection();
            gridDash.CurrentCell = null;
        }

        // Assert
        Assert.True(result.CompletedBySpooler);
        Assert.False(gridPrint.Rows[0].Cells[0].Selected);
        Assert.Null(gridPrint.CurrentCell);
        Assert.False(gridDash.Rows[0].Cells[0].Selected);
        Assert.Null(gridDash.CurrentCell);
    });
}
```
