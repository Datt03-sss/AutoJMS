## 2026-06-22T02:38:11Z
You are the Print Flow Worker. Your working directory is d:\v1.2605.2(new-test)\.agents\sub_orch_implementation.
Your task is to implement the print refactoring changes on the codebase and verify them using the tests.

Follow these steps exactly:
1. Pull latest code:
   git switch main
   git pull --ff-only origin main
2. Acquire the Git lock in `d:\v1.2605.2(new-test)\.agent-lock.md`. Set it to:
   Current Writer: teamwork_preview_worker
   Mode: WRITE_ACTIVE`
   Scope: Print Refactoring
3. Modify the files:
   a. `src/AutoJMS/Printing/IPrintService.cs`:
      Add:
      ```csharp
      event Action OnPrintSelectionCleared;
      ```
   b. `src/AutoJMS/Printing/PrintService.cs`:
      Add:
      ```csharp
      public event Action OnPrintSelectionCleared;
      ```
      Modify `ClearSelection()`:
      ```csharp
      public void ClearSelection()
      {
          if (_grid.InvokeRequired)
          {
              _grid.Invoke(new Action(() => 
              {
                  _grid.ClearSelection();
                  _grid.CurrentCell = null;
              }));
          }
          else
          {
              _grid.ClearSelection();
              _grid.CurrentCell = null;
          }
          AppLogger.Info("PRINT_SELECTION_CLEAR_DONE");
          try { OnPrintSelectionCleared?.Invoke(); } catch { }
      }
      ```
   c. `src/AutoJMS/Forms/FullStackOperation.cs`:
      Modify `ClearDashGridSelection()` to also set `CurrentCell = null`:
      ```csharp
      public void ClearDashGridSelection()
      {
          if (tabDash_dataGridView == null) return;
          if (tabDash_dataGridView.InvokeRequired)
          {
              tabDash_dataGridView.Invoke(new Action(() => 
              {
                  tabDash_dataGridView.ClearSelection();
                  tabDash_dataGridView.CurrentCell = null;
              }));
          }
          else
          {
              tabDash_dataGridView.ClearSelection();
              tabDash_dataGridView.CurrentCell = null;
          }
      }
      ```
   d. `src/AutoJMS/Printing/PrintJobCoordinator.cs`:
      Rewrite `PrintAsync` completely:
      ```csharp
      public async Task<PrintSubmitResult> PrintAsync(PrintJobRequest request, CancellationToken ct = default)
      {
          if (request == null) throw new ArgumentNullException(nameof(request));

          if (!await _semaphore.WaitAsync(0, ct).ConfigureAwait(false))
          {
              AppLogger.Warning("DUPLICATE_PRINT_REQUEST_IGNORED");
              return new PrintSubmitResult
              {
                  CompletedBySpooler = false,
                  Reason = "DUPLICATE_PRINT_REQUEST_IGNORED"
              };
          }

          try
          {
              AppLogger.Info("PRINT_JOB_START");

              // 1. Coordinate validation
              bool isValid = await _printService.ValidateSelectedBeforePrintAsync(request.Waybills, request.CurrentInputText)
                  .ConfigureAwait(false);
              if (!isValid)
              {
                  return new PrintSubmitResult
                  {
                      CompletedBySpooler = false,
                      Reason = "Validation failed"
                  };
              }

              // 2. API call (Enforce exactly one API request to JMS per print)
              var payload = new Dictionary<string, object>
              {
                  { "waybillIds", request.Waybills },
                  { "applyTypeCode", request.ApplyTypeCode },
                  { "printType", request.PrintType },
                  { "pringType", request.PrintType },
                  { "countryId", "1" }
              };
              string jsonPayload = System.Text.Json.JsonSerializer.Serialize(payload);

              AppLogger.Info("PRINT_API_REQUEST_START");
              string pdfUrl;
              using (var response = await _jmsApiClient.PostJsonAsync(request.ApiUrl, jsonPayload, ct: ct).ConfigureAwait(false))
              {
                  if (response == null || !response.IsSuccessStatusCode)
                  {
                      return new PrintSubmitResult
                      {
                          CompletedBySpooler = false,
                          Reason = "API call failed"
                      };
                  }

                  string rawJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                  using (var doc = System.Text.Json.JsonDocument.Parse(rawJson))
                  {
                      var root = doc.RootElement;
                      if (root.TryGetProperty("code", out var codeElement))
                      {
                          string codeVal = codeElement.ToString();
                          if (codeVal == "200" || codeVal == "0" || codeVal == "1")
                          {
                              pdfUrl = null;
                              if (root.TryGetProperty("data", out var data))
                              {
                                  if (data.ValueKind == System.Text.Json.JsonValueKind.String)
                                  {
                                      pdfUrl = data.GetString();
                                  }
                                  else if (data.ValueKind == System.Text.Json.JsonValueKind.Array && data.GetArrayLength() > 0)
                                  {
                                      pdfUrl = data[0].GetString();
                                  }
                              }
                          }
                          else
                          {
                              return new PrintSubmitResult
                              {
                                  CompletedBySpooler = false,
                                  Reason = $"API returned error code: {codeVal}"
                              };
                          }
                      }
                      else
                      {
                          return new PrintSubmitResult
                          {
                              CompletedBySpooler = false,
                              Reason = "Invalid API response structure"
                          };
                      }
                  }
              }

              if (string.IsNullOrEmpty(pdfUrl))
              {
                  return new PrintSubmitResult
                  {
                      CompletedBySpooler = false,
                      Reason = "No PDF URL found in response"
                  };
              }

              // 3. Cache check (keep/reuse local cache for 60s, but still hit the API once)
              PrintJobCacheEntry entry;
              lock (_cacheLock)
              {
                  if (_cache.TryGetValue(pdfUrl, out var existing) && !existing.IsExpired)
                  {
                      entry = existing;
                  }
                  else
                  {
                      entry = null;
                  }
              }

              if (entry != null)
              {
                  AppLogger.Info("PRINT_CACHE_HIT");
              }
              else
              {
                  // Download PDF bytes
                  byte[] pdfBytes = await _jmsApiClient.GetByteArrayAsync(pdfUrl, ct).ConfigureAwait(false);
                  entry = new PrintJobCacheEntry
                  {
                      CacheKey = pdfUrl,
                      WaybillNo = request.Waybills.Count > 0 ? request.Waybills[0] : "",
                      PdfBytes = pdfBytes,
                      CreatedAt = DateTime.Now,
                      ExpiresAt = DateTime.Now.AddSeconds(60)
                  };

                  lock (_cacheLock)
                  {
                      _cache[pdfUrl] = entry;
                  }
              }

              // 4. Spooler submit
              var firstWaybill = request.Waybills.Count > 0 ? request.Waybills[0] : "";
              var printResult = await _printerSpoolerSubmitter.SubmitPrintAsync(entry, firstWaybill).ConfigureAwait(false);

              if (printResult == null || !printResult.CompletedBySpooler)
              {
                  AppLogger.Warning("PRINT_SPOOLER_FAILED");
                  return new PrintSubmitResult
                  {
                      CompletedBySpooler = false,
                      Reason = printResult?.Reason ?? "PRINT_SPOOLER_FAILED"
                  };
              }

              AppLogger.Info("PRINT_SPOOLER_SUBMIT_DONE");

              // 5. Selection clearing on success spooler submission
              _printService.SelectAll(false);
              _printService.ClearSelection();

              return printResult;
          }
          finally
          {
              _semaphore.Release();
          }
      }
      ```
   e. `src/AutoJMS/Forms/Main.cs`:
      - Add fields:
        ```csharp
        private IPrinterSpoolerSubmitter _printerSpoolerSubmitter;
        private PrintJobCoordinator _printJobCoordinator;
        ```
      - Instantiate them right after `_printService = new PrintService(...)` setup in main constructor (around line 520):
        ```csharp
            _printerSpoolerSubmitter = new MainPrinterSpoolerSubmitter(this);
            _printJobCoordinator = new PrintJobCoordinator(
                JmsApiClient.Instance,
                _printerSpoolerSubmitter,
                _printService);
            _printService.OnPrintSelectionCleared += () =>
            {
                try { _fullStackForm?.ClearDashGridSelection(); } catch { }
            };
        ```
      - At the very end of `Main` class (around line 4556, before the closing brace of the class), add the nested class:
        ```csharp
        private class MainPrinterSpoolerSubmitter : IPrinterSpoolerSubmitter
        {
            private readonly Main _main;
            public MainPrinterSpoolerSubmitter(Main main) => _main = main;
            public Task<PrintSubmitResult> SubmitPrintAsync(PrintJobCacheEntry job, string firstWaybill)
                => _main.SubmitPrintImmediatelyAsync(job, firstWaybill);
        }
        ```
      - Rewrite `ExecutePrintAsync` (around line 2778):
        ```csharp
        private async Task ExecutePrintAsync(bool isAutoMode)
        {
            if (_printService == null) return;
            var selected = _printService.GetSelectedWaybills();
            if (selected == null || selected.Count == 0)
            {
                if (!isAutoMode) ShowPrintMessage("Chưa chọn vận đơn nào!", true);
                return;
            }

            int printType = 1;
            int applyTypeCode = (_printService.CurrentMode == PrintMode.InChuyenTiep) ? 2 : 4;
            string apiUrl = AppConfig.Current.BuildJmsApiUrl("operatingplatform/rebackTransferExpress/printBase64");

            SetPrintButtonState(false);
            try
            {
                var request = new PrintJobRequest
                {
                    Waybills = selected,
                    CurrentInputText = tabPrint_inputWaybill.Text,
                    PrintType = printType,
                    ApplyTypeCode = applyTypeCode,
                    ApiUrl = apiUrl
                };

                var result = await _printJobCoordinator.PrintAsync(request, _appCts.Token);

                if (result.CompletedBySpooler)
                {
                    ShowPrintMessage("Đã in, đang cập nhật trạng thái sau in...", false, 2500);
                    _printService.QueuePostPrintRefresh(selected, printType);
                }
                else
                {
                    if (result.Reason != "DUPLICATE_PRINT_REQUEST_IGNORED")
                    {
                        ShowPrintMessage($"In thất bại: {result.Reason}", true);
                    }
                    else
                    {
                        if (!isAutoMode) ShowPrintMessage("Đang xử lý lệnh in hiện tại...", false, 2000);
                    }
                }
            }
            catch (Exception ex)
            {
                ShowPrintMessage($"Lỗi hệ thống: {ex.Message}", true);
            }
            finally
            {
                SetPrintButtonState(true);
            }
        }
        ```

4. Build and Test:
   Restore the solution: `dotnet restore .\AutoJMS.slnx`
   Build the solution in Release mode: `dotnet build .\AutoJMS.slnx -c Release`
   Run the test suite: `dotnet test .\AutoJMS.slnx -c Release`
   Ensure 100% of the tests pass.

5. Git Commit & Push:
   git status
   git add .
   git commit -m "Refactor print flow: implement concurrency protection, cache logic, spooler submission, selection clearing, and logs"
   git push origin main

6. Release Lock:
   Edit `d:\v1.2605.2(new-test)\.agent-lock.md` to:
   Current Writer: None
   Mode: READ_ONLY
   Scope: None

7. When completed, send a message to the caller conversation ID e7c233c9-1c24-4975-b21f-950abd11aafa.
