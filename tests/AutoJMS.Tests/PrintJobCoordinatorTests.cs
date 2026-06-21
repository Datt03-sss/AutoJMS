using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AutoJMS.Tests.Helpers;
using Moq;
using Xunit;

namespace AutoJMS.Tests;

public class PrintJobCoordinatorTests
{
    private readonly Mock<IJmsApiClient> _mockJmsApiClient = new();
    private readonly Mock<IPrinterSpoolerSubmitter> _mockSpoolerSubmitter = new();
    private readonly Mock<IPrintService> _mockPrintService = new();
    private readonly PrintJobCoordinator _coordinator;

    public PrintJobCoordinatorTests()
    {
        _coordinator = new PrintJobCoordinator(
            _mockJmsApiClient.Object,
            _mockSpoolerSubmitter.Object,
            _mockPrintService.Object);
    }

    [Fact]
    public void Case1_FirstPrint_Success()
    {
        StaHelper.Run(async () =>
        {
            // Arrange
            var request = new PrintJobRequest
            {
                Waybills = new List<string> { "123456" },
                CurrentInputText = "123456",
                PrintType = 1,
                ApplyTypeCode = 2,
                ApiUrl = "http://mockapi/print"
            };

            _mockPrintService
                .Setup(x => x.ValidateSelectedBeforePrintAsync(request.Waybills, request.CurrentInputText))
                .ReturnsAsync(true);

            var apiResponse = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"code\":200,\"data\":\"http://mockpdf/123456.pdf\"}")
            };
            _mockJmsApiClient
                .Setup(x => x.PostJsonAsync(request.ApiUrl, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(apiResponse);

            var pdfBytes = new byte[] { 1, 2, 3, 4 };
            _mockJmsApiClient
                .Setup(x => x.GetByteArrayAsync("http://mockpdf/123456.pdf", It.IsAny<CancellationToken>()))
                .ReturnsAsync(pdfBytes);

            _mockSpoolerSubmitter
                .Setup(x => x.SubmitPrintAsync(It.IsAny<PrintJobCacheEntry>(), "123456"))
                .ReturnsAsync(new PrintSubmitResult { CompletedBySpooler = true });

            // Act
            var result = await _coordinator.PrintAsync(request);

            // Assert
            Assert.True(result.CompletedBySpooler);
            _mockPrintService.Verify(x => x.ValidateSelectedBeforePrintAsync(request.Waybills, request.CurrentInputText), Times.Once);
            _mockJmsApiClient.Verify(x => x.PostJsonAsync(request.ApiUrl, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
            _mockJmsApiClient.Verify(x => x.GetByteArrayAsync("http://mockpdf/123456.pdf", It.IsAny<CancellationToken>()), Times.Once);
            _mockSpoolerSubmitter.Verify(x => x.SubmitPrintAsync(It.Is<PrintJobCacheEntry>(e => e.CacheKey == "http://mockpdf/123456.pdf" && e.PdfBytes == pdfBytes), "123456"), Times.Once);
            
            // Grid selection cleared
            _mockPrintService.Verify(x => x.SelectAll(false), Times.Once);
            _mockPrintService.Verify(x => x.ClearSelection(), Times.Once);
        });
    }

    [Fact]
    public void Case2_ReprintWithin60Seconds_ReusesCacheButHitsApi()
    {
        StaHelper.Run(async () =>
        {
            // Arrange
            var request = new PrintJobRequest
            {
                Waybills = new List<string> { "123456" },
                CurrentInputText = "123456",
                PrintType = 1,
                ApplyTypeCode = 2,
                ApiUrl = "http://mockapi/print"
            };

            _mockPrintService
                .Setup(x => x.ValidateSelectedBeforePrintAsync(request.Waybills, request.CurrentInputText))
                .ReturnsAsync(true);

            // Mock API returns same URL both times
            _mockJmsApiClient
                .Setup(x => x.PostJsonAsync(request.ApiUrl, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(() => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"code\":200,\"data\":\"http://mockpdf/123456.pdf\"}")
                }));

            var pdfBytes = new byte[] { 1, 2, 3, 4 };
            _mockJmsApiClient
                .Setup(x => x.GetByteArrayAsync("http://mockpdf/123456.pdf", It.IsAny<CancellationToken>()))
                .ReturnsAsync(pdfBytes);

            _mockSpoolerSubmitter
                .Setup(x => x.SubmitPrintAsync(It.IsAny<PrintJobCacheEntry>(), "123456"))
                .ReturnsAsync(new PrintSubmitResult { CompletedBySpooler = true });

            // Act - Print twice
            var result1 = await _coordinator.PrintAsync(request);
            var result2 = await _coordinator.PrintAsync(request);

            // Assert
            Assert.True(result1.CompletedBySpooler);
            Assert.True(result2.CompletedBySpooler);

            // API is hit twice
            _mockJmsApiClient.Verify(x => x.PostJsonAsync(request.ApiUrl, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
            
            // PDF download is only done ONCE (second time is cached)
            _mockJmsApiClient.Verify(x => x.GetByteArrayAsync("http://mockpdf/123456.pdf", It.IsAny<CancellationToken>()), Times.Once);
            
            // Spooler submission occurs twice
            _mockSpoolerSubmitter.Verify(x => x.SubmitPrintAsync(It.Is<PrintJobCacheEntry>(e => e.CacheKey == "http://mockpdf/123456.pdf"), "123456"), Times.Exactly(2));
        });
    }

    [Fact]
    public void Case3_DoubleSubmit_RapidSpam_IgnoresDuplicate()
    {
        StaHelper.Run(async () =>
        {
            // Arrange
            var request = new PrintJobRequest
            {
                Waybills = new List<string> { "123456" },
                CurrentInputText = "123456",
                PrintType = 1,
                ApplyTypeCode = 2,
                ApiUrl = "http://mockapi/print"
            };

            _mockPrintService
                .Setup(x => x.ValidateSelectedBeforePrintAsync(request.Waybills, request.CurrentInputText))
                .ReturnsAsync(true);

            // Introduce a delay in the API response to simulate active print job in progress
            var apiDelayTcs = new TaskCompletionSource<HttpResponseMessage>();
            _mockJmsApiClient
                .Setup(x => x.PostJsonAsync(request.ApiUrl, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(apiDelayTcs.Task);

            // Start first print job (it will block on PostJsonAsync)
            var printTask1 = _coordinator.PrintAsync(request);

            // Act - Spam with a second print request immediately
            var result2 = await _coordinator.PrintAsync(request);

            // Complete the first print job
            apiDelayTcs.SetResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"code\":200,\"data\":\"http://mockpdf/123456.pdf\"}")
            });
            _mockJmsApiClient
                .Setup(x => x.GetByteArrayAsync("http://mockpdf/123456.pdf", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new byte[] { 1 });
            _mockSpoolerSubmitter
                .Setup(x => x.SubmitPrintAsync(It.IsAny<PrintJobCacheEntry>(), "123456"))
                .ReturnsAsync(new PrintSubmitResult { CompletedBySpooler = true });

            var result1 = await printTask1;

            // Assert
            Assert.True(result1.CompletedBySpooler);
            
            // Second request must be immediately ignored with duplicate reason
            Assert.False(result2.CompletedBySpooler);
            Assert.Equal("DUPLICATE_PRINT_REQUEST_IGNORED", result2.Reason);

            // Spooler submission is called only once
            _mockSpoolerSubmitter.Verify(x => x.SubmitPrintAsync(It.IsAny<PrintJobCacheEntry>(), It.IsAny<string>()), Times.Once);
        });
    }

    [Fact]
    public void Case4_ApiFail_AbortsOperation()
    {
        StaHelper.Run(async () =>
        {
            // Arrange
            var request = new PrintJobRequest
            {
                Waybills = new List<string> { "123456" },
                CurrentInputText = "123456",
                PrintType = 1,
                ApplyTypeCode = 2,
                ApiUrl = "http://mockapi/print"
            };

            _mockPrintService
                .Setup(x => x.ValidateSelectedBeforePrintAsync(request.Waybills, request.CurrentInputText))
                .ReturnsAsync(true);

            // API returns non-success or error code
            var apiResponse = new HttpResponseMessage(HttpStatusCode.InternalServerError);
            _mockJmsApiClient
                .Setup(x => x.PostJsonAsync(request.ApiUrl, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(apiResponse);

            // Act
            var result = await _coordinator.PrintAsync(request);

            // Assert
            Assert.False(result.CompletedBySpooler);
            _mockJmsApiClient.Verify(x => x.GetByteArrayAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            _mockSpoolerSubmitter.Verify(x => x.SubmitPrintAsync(It.IsAny<PrintJobCacheEntry>(), It.IsAny<string>()), Times.Never);
            _mockPrintService.Verify(x => x.SelectAll(It.IsAny<bool>()), Times.Never);
            _mockPrintService.Verify(x => x.ClearSelection(), Times.Never);
        });
    }

    [Fact]
    public void Case5_PrinterSubmitFail_DoesNotClearSelection()
    {
        StaHelper.Run(async () =>
        {
            // Arrange
            var request = new PrintJobRequest
            {
                Waybills = new List<string> { "123456" },
                CurrentInputText = "123456",
                PrintType = 1,
                ApplyTypeCode = 2,
                ApiUrl = "http://mockapi/print"
            };

            _mockPrintService
                .Setup(x => x.ValidateSelectedBeforePrintAsync(request.Waybills, request.CurrentInputText))
                .ReturnsAsync(true);

            var apiResponse = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"code\":200,\"data\":\"http://mockpdf/123456.pdf\"}")
            };
            _mockJmsApiClient
                .Setup(x => x.PostJsonAsync(request.ApiUrl, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(apiResponse);

            _mockJmsApiClient
                .Setup(x => x.GetByteArrayAsync("http://mockpdf/123456.pdf", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new byte[] { 1 });

            // Printer submit fails (returns CompletedBySpooler = false)
            _mockSpoolerSubmitter
                .Setup(x => x.SubmitPrintAsync(It.IsAny<PrintJobCacheEntry>(), "123456"))
                .ReturnsAsync(new PrintSubmitResult { CompletedBySpooler = false, Reason = "Printer offline" });

            // Act
            var result = await _coordinator.PrintAsync(request);

            // Assert
            Assert.False(result.CompletedBySpooler);
            Assert.Equal("Printer offline", result.Reason);
            
            // Grid selection is NOT cleared
            _mockPrintService.Verify(x => x.SelectAll(It.IsAny<bool>()), Times.Never);
            _mockPrintService.Verify(x => x.ClearSelection(), Times.Never);
        });
    }

    [Fact]
    public void Case6_SuccessfulPrint_ClearsGrids()
    {
        StaHelper.Run(async () =>
        {
            // Arrange
            var request = new PrintJobRequest
            {
                Waybills = new List<string> { "123456" },
                CurrentInputText = "123456"
            };

            _mockPrintService
                .Setup(x => x.ValidateSelectedBeforePrintAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<string>()))
                .ReturnsAsync(true);

            _mockJmsApiClient
                .Setup(x => x.PostJsonAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"code\":200,\"data\":\"http://mockpdf/1\"}") });

            _mockJmsApiClient
                .Setup(x => x.GetByteArrayAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new byte[] { 1 });

            _mockSpoolerSubmitter
                .Setup(x => x.SubmitPrintAsync(It.IsAny<PrintJobCacheEntry>(), It.IsAny<string>()))
                .ReturnsAsync(new PrintSubmitResult { CompletedBySpooler = true });

            // Act
            await _coordinator.PrintAsync(request);

            // Assert - Verification that grid clearing occurs
            _mockPrintService.Verify(x => x.SelectAll(false), Times.Once);
            _mockPrintService.Verify(x => x.ClearSelection(), Times.Once);
        });
    }

    [Fact]
    public void Case7_FailedPrint_DoesNotClearGrids()
    {
        StaHelper.Run(async () =>
        {
            // Arrange
            var request = new PrintJobRequest
            {
                Waybills = new List<string> { "123456" },
                CurrentInputText = "123456"
            };

            _mockPrintService
                .Setup(x => x.ValidateSelectedBeforePrintAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<string>()))
                .ReturnsAsync(true);

            _mockJmsApiClient
                .Setup(x => x.PostJsonAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"code\":200,\"data\":\"http://mockpdf/1\"}") });

            _mockJmsApiClient
                .Setup(x => x.GetByteArrayAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new byte[] { 1 });

            _mockSpoolerSubmitter
                .Setup(x => x.SubmitPrintAsync(It.IsAny<PrintJobCacheEntry>(), It.IsAny<string>()))
                .ReturnsAsync(new PrintSubmitResult { CompletedBySpooler = false });

            // Act
            await _coordinator.PrintAsync(request);

            // Assert - Verification that grid clearing does NOT occur
            _mockPrintService.Verify(x => x.SelectAll(It.IsAny<bool>()), Times.Never);
            _mockPrintService.Verify(x => x.ClearSelection(), Times.Never);
        });
    }
}
