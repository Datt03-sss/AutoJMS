# Scope: E2E Testing Track

## Architecture
- **AutoJMS.Tests**: An xUnit test project targeting .NET 8.0-windows to test `PrintJobCoordinator` and `PrintService`.
- **IJmsApiClient / IJmsPrintClient**: Mockable client abstraction to override real HTTP calls to the J&T Express JMS API.
- **IPrinterSpoolerSubmitter**: Mockable printer submission interface to bypass actual physical printing and track queue counts.
- **Grid Selection clearing checks**: Verification that grid selections on both print and dashboard tabs are cleared.

## Milestones
| # | Name | Scope | Dependencies | Status |
|---|------|-------|-------------|--------|
| 1 | Test Infra Setup | Initialize `tests/AutoJMS.Tests` project, write `.csproj`, and update `AutoJMS.slnx` | none | PLANNED |
| 2 | Mocks and Interfaces | Implement mock interfaces for JMS API and Spooler, make them accessible/integratable with `PrintJobCoordinator` | M1 | PLANNED |
| 3 | Test Case Implementation | Implement 7 required test cases across the 4 tiers (Tier 1 to Tier 4) | M2 | PLANNED |
| 4 | Verification and Build | Build solution `AutoJMS.slnx` in Release and execute test suite, verifying 100% pass | M3 | PLANNED |
| 5 | Publish TEST_READY.md | Publish `TEST_READY.md` at project root and hand off report | M4 | PLANNED |

## Interface Contracts
### IJmsApiClient / JmsApiClient Mocking
- `Task<HttpResponseMessage> PostJsonAsync(string url, string jsonBody, string routeName, string routerNameList, string origin, CancellationToken ct)`

### IPrinterSpooler / Submitter Mocking
- `Task<PrintSubmitResult> SubmitPrintImmediatelyAsync(PrintJobCacheEntry job, string firstWaybill)`
