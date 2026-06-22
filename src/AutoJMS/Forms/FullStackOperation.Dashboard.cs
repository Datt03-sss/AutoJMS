using AutoJMS.FullStack.UI.OperationCenter;
using Sunny.UI;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using AutoJMS.Data;

namespace AutoJMS
{
    public partial class FullStackOperation
    {
        private Microsoft.Web.WebView2.WinForms.WebView2 _webView;
        private bool _webViewInitialized = false;
        private static readonly System.Collections.Generic.HashSet<string> StarredWaybills = new(StringComparer.OrdinalIgnoreCase);

        private async System.Threading.Tasks.Task InitializeWebView2Async()
        {
            if (_webViewInitialized) return;
            try
            {
                var userDataFolder = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AppData", "BrowserData");
                var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null, userDataFolder);
                await _webView.EnsureCoreWebView2Async(env);

                _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "autojms.local",
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Web"),
                    Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);

                _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;

                _webView.WebMessageReceived += OnWebViewMessageReceived;
                _webView.CoreWebView2.Navigate("https://autojms.local/index.html");

                _webViewInitialized = true;
            }
            catch (Exception ex)
            {
                AppLogger.Error("InitializeWebView2Async failed", ex);
                MessageBox.Show("Lỗi khởi tạo WebView2: " + ex.Message, "AutoJMS Dashboard", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnWebViewMessageReceived(object sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var rawJson = e.WebMessageAsJson;
                using var doc = System.Text.Json.JsonDocument.Parse(rawJson);
                var root = doc.RootElement;
                if (!root.TryGetProperty("action", out var actionProp)) return;
                var action = actionProp.GetString();

                if (action == "READY")
                {
                    _ = LoadDataAndRefreshViewsAsync();
                }
                else if (action == "SYNC")
                {
                    _ = SyncDataAsync();
                }
                else if (action == "EXPORT")
                {
                    _ = ExportOperationCurrentViewAsync(false);
                }
                else if (action == "CHANGE_SOURCE")
                {
                    if (root.TryGetProperty("source", out var valProp))
                    {
                        var source = valProp.GetString();
                        _selectedSource = source;
                        _ = RefreshDashViewAsync(_cts.Token);
                    }
                }
                else if (action == "CHANGE_SEARCH")
                {
                    if (root.TryGetProperty("text", out var valProp))
                    {
                        var text = valProp.GetString();
                        _searchText = text;
                        RefreshFilteredGrid();
                    }
                }
                else if (action == "CHANGE_TIME_INTERVAL")
                {
                    if (root.TryGetProperty("text", out var valProp))
                    {
                        var text = valProp.GetString();
                        _selectedTimeInterval = text;

                        string intervalText = text?.Replace(" PHÚT", "")?.Replace(" GIỜ", "")?.Trim() ?? "2";
                        int minutes = 2;
                        if (text != null && text.Contains("GIỜ"))
                        {
                            if (int.TryParse(intervalText, out int h)) minutes = h * 60;
                        }
                        else
                        {
                            int.TryParse(intervalText, out minutes);
                        }
                        if (_autoRefreshTimer != null)
                            _autoRefreshTimer.Interval = Math.Max(1, minutes) * 60 * 1000;
                    }
                }
                else if (action == "CHANGE_STATUS_SELECT")
                {
                    if (root.TryGetProperty("text", out var valProp))
                    {
                        var text = valProp.GetString();
                        _selectedStatusSelect = text;
                        RefreshFilteredGrid();
                    }
                }
                else if (action == "FETCH_JOURNEY")
                {
                    if (root.TryGetProperty("waybillNo", out var valProp))
                    {
                        var waybillNo = valProp.GetString();
                        ShowWaybillJourneyWorkspace(waybillNo);
                    }
                }
                else if (action == "SELECT_WAYBILL")
                {
                    if (root.TryGetProperty("waybillNo", out var valProp))
                    {
                        var waybillNo = valProp.GetString();
                    }
                }
                else if (action == "TOGGLE_STAR")
                {
                    if (root.TryGetProperty("waybillNo", out var valProp))
                    {
                        var waybillNo = valProp.GetString();
                        ToggleStarWaybill(waybillNo);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("OnWebViewMessageReceived failed", ex);
            }
        }

        private void LoadStarredWaybills()
        {
            try
            {
                var filePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AppData", "starred_waybills.txt");
                if (System.IO.File.Exists(filePath))
                {
                    var lines = System.IO.File.ReadAllLines(filePath);
                    lock (StarredWaybills)
                    {
                        StarredWaybills.Clear();
                        foreach (var line in lines)
                        {
                            if (!string.IsNullOrWhiteSpace(line))
                            {
                                StarredWaybills.Add(line.Trim());
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("LoadStarredWaybills failed", ex);
            }
        }

        private void SaveStarredWaybills()
        {
            try
            {
                var dirPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AppData");
                if (!System.IO.Directory.Exists(dirPath))
                {
                    System.IO.Directory.CreateDirectory(dirPath);
                }
                var filePath = System.IO.Path.Combine(dirPath, "starred_waybills.txt");
                System.Collections.Generic.List<string> list;
                lock (StarredWaybills)
                {
                    list = new System.Collections.Generic.List<string>(StarredWaybills);
                }
                System.IO.File.WriteAllLines(filePath, list);
            }
            catch (Exception ex)
            {
                AppLogger.Error("SaveStarredWaybills failed", ex);
            }
        }

        private void ToggleStarWaybill(string waybillNo)
        {
            if (string.IsNullOrWhiteSpace(waybillNo)) return;
            lock (StarredWaybills)
            {
                if (StarredWaybills.Contains(waybillNo))
                {
                    StarredWaybills.Remove(waybillNo);
                }
                else
                {
                    StarredWaybills.Add(waybillNo);
                }
            }
            SaveStarredWaybills();
            _ = LoadDataAndRefreshViewsAsync();
        }

        private object MapWaybillToDto(WaybillDbModel row)
        {
            var mains = new System.Collections.Generic.List<string> { "tong-ton" };
            var subs = new System.Collections.Generic.List<string> { "Tất cả" };

            if (row.TrangThaiHienTai == "Hàng đến")
            {
                mains.Add("hang-den");
                subs.Add("Tổng số đơn");
                if (IsNotDispatched(row))
                {
                    subs.Add("Chưa quét đến");
                }
            }

            if (row.TrangThaiHienTai == "Phát hàng")
            {
                mains.Add("phat-hang");
                subs.Add("Cần phát");
                if (IsNotDispatched(row))
                {
                    subs.Add("Chưa phát");
                }
            }

            if (IsNeedsAction(row))
            {
                mains.Add("backlog");
                double days = GetWarehouseAgeDays(row.ThoiGianThaoTac);
                if (days >= 7.0)
                {
                    subs.Add("7D+");
                    subs.Add("7D");
                }
                else if (days >= 6.0) subs.Add("6D");
                else if (days >= 5.0) subs.Add("5D");
                else if (days >= 4.0) subs.Add("4D");
                else if (days >= 3.0) subs.Add("3D");
                else subs.Add("<2D");
            }

            if (row.TrangThaiHienTai == "Chuyển hoàn" || IsPendingReturn(row))
            {
                mains.Add("chuyen-hoan");
                if (row.RebackStatus == "Đợi xét duyệt") subs.Add("Đợi xét duyệt");
                else if (row.RebackStatus == "Đã phê duyệt") subs.Add("Đã phê duyệt");
                else if (row.RebackStatus == "Phát lại") subs.Add("Phát lại");
            }

            if (IsSlaBreached(row.ThoiGianNhanHang))
            {
                mains.Add("kiem-kho");
                subs.Add("Đơn cần kiểm");
                subs.Add("Kiểm thiếu");
            }

            if (HasTaskWaybill(row))
            {
                mains.Add("cskh");
                subs.Add("Ticket CSKH");
                if (IsSlaBreached(row.ThoiGianNhanHang))
                {
                    subs.Add("Khiếu nại SLA");
                }
            }

            double ageDays = GetWarehouseAgeDays(row.ThoiGianThaoTac);
            if (ageDays >= 1.0)
            {
                mains.Add("dung-tram");
                if (ageDays >= 7.0) subs.Add("Dừng >7D");
                else if (ageDays >= 3.0) subs.Add("Dừng >3D");
                else if (ageDays >= 2.0) subs.Add("Dừng >48h");
                else if (ageDays >= 1.0) subs.Add("Dừng >24h");
                else subs.Add("Dừng >12h");
            }

            bool isStarred = false;
            lock (StarredWaybills)
            {
                isStarred = StarredWaybills.Contains(row.WaybillNo);
            }

            if (isStarred)
            {
                mains.Add("star");
                subs.Add("Đơn đã ghim");
                subs.Add("Đang theo dõi");
                if (IsNeedsAction(row)) subs.Add("Chưa xử lý");
                else subs.Add("Đã xử lý");
            }

            if (row.TrangThaiHienTai == "Phát hàng") subs.Add("Quét phát hàng");
            if (row.TrangThaiHienTai == "Chuyển hoàn") subs.Add("Đăng ký chuyển hoàn");
            if (row.ThaoTacCuoi?.ToLower().Contains("phát lại") == true || row.ThaoTacCuoi?.ToLower().Contains("giao lại") == true) subs.Add("Giao lại hàng");
            if (row.TrangThaiHienTai == "Chờ lấy") subs.Add("Chờ lấy hàng");
            if (row.TrangThaiHienTai == "Lưu kho") subs.Add("Lưu kho");

            return new
            {
                code = row.WaybillNo,
                recipient = row.NhanVienNhanHang ?? "Chưa rõ",
                phone = "-",
                addr = row.DiaChiNhanHang ?? "-",
                source = row.BuuCucThaoTac ?? "-",
                cod = row.CODThucTe ?? "0 đ",
                sla = IsSlaBreached(row.ThoiGianNhanHang) ? "Quá hạn" : "Trong hạn",
                slaColor = IsSlaBreached(row.ThoiGianNhanHang) ? "#d23a2e" : "#1c8a3c",
                service = "Chuyển phát",
                weight = row.TrongLuong ?? "-",
                pkgs = "1",
                orderNo = row.WaybillNo,
                sender = row.TenNguoiGui ?? "-",
                staff = row.NguoiThaoTac ?? "-",
                mains = mains,
                subs = subs,
                isStar = isStarred
            };
        }

        private void PostStateToWebView2()
        {
            if (_webView == null || !_webViewInitialized || _webView.CoreWebView2 == null) return;

            try
            {
                string siteId = "214A02";
                string lastUpdateTime = DateTime.Now.ToString("HH:mm:ss");
                if (!string.IsNullOrEmpty(_fullStackStatus))
                {
                    lastUpdateTime = _fullStackStatus.Replace("Local refresh: ", "").Replace("Chưa tải", "Chưa cập nhật");
                }

                string syncStatus = "DB: Synced";

                int tongTonCount = _cloudData.Count;
                int hangDenCount = _cloudData.Count(x => x.TrangThaiHienTai == "Hàng đến");
                int phatHangCount = _cloudData.Count(x => x.TrangThaiHienTai == "Phát hàng");
                int backlogCount = _cloudData.Count(IsNeedsAction);
                int chuyenHoanCount = _cloudData.Count(x => x.TrangThaiHienTai == "Chuyển hoàn" || IsPendingReturn(x));
                int kiemKhoCount = _cloudData.Count(x => IsSlaBreached(x.ThoiGianNhanHang));
                int cskhCount = _cloudData.Count(HasTaskWaybill);
                int dungTramCount = _cloudData.Count(x => GetWarehouseAgeDays(x.ThoiGianThaoTac) >= 1.0);

                int starCount = 0;
                lock (StarredWaybills)
                {
                    starCount = _cloudData.Count(x => StarredWaybills.Contains(x.WaybillNo));
                }

                var mainsObj = new System.Collections.Generic.Dictionary<string, string>
                {
                    { "tong-ton", tongTonCount.ToString("N0") },
                    { "hang-den", hangDenCount.ToString("N0") },
                    { "phat-hang", phatHangCount.ToString("N0") },
                    { "backlog", backlogCount.ToString("N0") },
                    { "chuyen-hoan", chuyenHoanCount.ToString("N0") },
                    { "kiem-kho", kiemKhoCount.ToString("N0") },
                    { "cskh", cskhCount.ToString("N0") },
                    { "dung-tram", dungTramCount.ToString("N0") },
                    { "star", starCount.ToString("N0") }
                };

                var subsObj = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<object>>
                {
                    { "tong-ton", new System.Collections.Generic.List<object> {
                        new { label = "Tất cả", count = tongTonCount.ToString("N0") },
                        new { label = "Quét phát hàng", count = _cloudData.Count(x => x.TrangThaiHienTai == "Phát hàng").ToString("N0") },
                        new { label = "Đăng ký chuyển hoàn", count = _cloudData.Count(x => x.TrangThaiHienTai == "Chuyển hoàn").ToString("N0") },
                        new { label = "Giao lại hàng", count = _cloudData.Count(x => x.ThaoTacCuoi?.ToLower().Contains("phát lại") == true || x.ThaoTacCuoi?.ToLower().Contains("giao lại") == true).ToString("N0") },
                        new { label = "Chờ lấy hàng", count = _cloudData.Count(x => x.TrangThaiHienTai == "Chờ lấy").ToString("N0") },
                        new { label = "Lưu kho", count = _cloudData.Count(x => x.TrangThaiHienTai == "Lưu kho").ToString("N0") }
                    } },
                    { "hang-den", new System.Collections.Generic.List<object> {
                        new { label = "Tổng số đơn", count = hangDenCount.ToString("N0") },
                        new { label = "Chưa quét đến", count = _cloudData.Count(x => x.TrangThaiHienTai == "Hàng đến" && IsNotDispatched(x)).ToString("N0") }
                    } },
                    { "phat-hang", new System.Collections.Generic.List<object> {
                        new { label = "Cần phát", count = phatHangCount.ToString("N0") },
                        new { label = "Đã phát", count = _cloudData.Count(x => x.TrangThaiHienTai == "Phát hàng" && !IsNotDispatched(x)).ToString("N0") },
                        new { label = "Chưa phát", count = _cloudData.Count(x => x.TrangThaiHienTai == "Phát hàng" && IsNotDispatched(x)).ToString("N0") }
                    } },
                    { "backlog", new System.Collections.Generic.List<object> {
                        new { label = "7D+", count = _cloudData.Count(x => IsNeedsAction(x) && GetWarehouseAgeDays(x.ThoiGianThaoTac) >= 7.0).ToString("N0") },
                        new { label = "7D", count = _cloudData.Count(x => IsNeedsAction(x) && GetWarehouseAgeDays(x.ThoiGianThaoTac) >= 7.0).ToString("N0") },
                        new { label = "6D", count = _cloudData.Count(x => IsNeedsAction(x) && GetWarehouseAgeDays(x.ThoiGianThaoTac) >= 6.0 && GetWarehouseAgeDays(x.ThoiGianThaoTac) < 7.0).ToString("N0") },
                        new { label = "5D", count = _cloudData.Count(x => IsNeedsAction(x) && GetWarehouseAgeDays(x.ThoiGianThaoTac) >= 5.0 && GetWarehouseAgeDays(x.ThoiGianThaoTac) < 6.0).ToString("N0") },
                        new { label = "4D", count = _cloudData.Count(x => IsNeedsAction(x) && GetWarehouseAgeDays(x.ThoiGianThaoTac) >= 4.0 && GetWarehouseAgeDays(x.ThoiGianThaoTac) < 5.0).ToString("N0") },
                        new { label = "3D", count = _cloudData.Count(x => IsNeedsAction(x) && GetWarehouseAgeDays(x.ThoiGianThaoTac) >= 3.0 && GetWarehouseAgeDays(x.ThoiGianThaoTac) < 4.0).ToString("N0") },
                        new { label = "<2D", count = _cloudData.Count(x => IsNeedsAction(x) && GetWarehouseAgeDays(x.ThoiGianThaoTac) < 2.0).ToString("N0") }
                    } },
                    { "chuyen-hoan", new System.Collections.Generic.List<object> {
                        new { label = "Đợi xét duyệt", count = _cloudData.Count(x => (x.TrangThaiHienTai == "Chuyển hoàn" || IsPendingReturn(x)) && x.RebackStatus == "Đợi xét duyệt").ToString("N0") },
                        new { label = "Đã phê duyệt", count = _cloudData.Count(x => (x.TrangThaiHienTai == "Chuyển hoàn" || IsPendingReturn(x)) && x.RebackStatus == "Đã phê duyệt").ToString("N0") },
                        new { label = "Phát lại", count = _cloudData.Count(x => (x.TrangThaiHienTai == "Chuyển hoàn" || IsPendingReturn(x)) && x.RebackStatus == "Phát lại").ToString("N0") }
                    } },
                    { "kiem-kho", new System.Collections.Generic.List<object> {
                        new { label = "Đơn cần kiểm", count = kiemKhoCount.ToString("N0") },
                        new { label = "Kiểm thiếu", count = _cloudData.Count(x => IsSlaBreached(x.ThoiGianNhanHang)).ToString("N0") }
                    } },
                    { "cskh", new System.Collections.Generic.List<object> {
                        new { label = "Ticket CSKH", count = cskhCount.ToString("N0") },
                        new { label = "Khiếu nại SLA", count = _cloudData.Count(x => HasTaskWaybill(x) && IsSlaBreached(x.ThoiGianNhanHang)).ToString("N0") },
                        new { label = "Khiếu nại chịu trách nhiệm", count = "0" },
                        new { label = "Kháng cáo CLDV", count = "0" },
                        new { label = "Tiền phạt dự kiến", count = "0" }
                    } },
                    { "dung-tram", new System.Collections.Generic.List<object> {
                        new { label = "Dừng >12h", count = _cloudData.Count(x => GetWarehouseAgeDays(x.ThoiGianThaoTac) >= 0.5).ToString("N0") },
                        new { label = "Dừng >24h", count = _cloudData.Count(x => GetWarehouseAgeDays(x.ThoiGianThaoTac) >= 1.0).ToString("N0") },
                        new { label = "Dừng >48h", count = _cloudData.Count(x => GetWarehouseAgeDays(x.ThoiGianThaoTac) >= 2.0).ToString("N0") },
                        new { label = "Dừng >3D", count = _cloudData.Count(x => GetWarehouseAgeDays(x.ThoiGianThaoTac) >= 3.0).ToString("N0") },
                        new { label = "Dừng >7D", count = _cloudData.Count(x => GetWarehouseAgeDays(x.ThoiGianThaoTac) >= 7.0).ToString("N0") },
                        new { label = "Không có scan mới", count = "0" }
                    } },
                    { "star", new System.Collections.Generic.List<object> {
                        new { label = "Đơn đã ghim", count = starCount.ToString("N0") },
                        new { label = "Đang theo dõi", count = starCount.ToString("N0") },
                        new { label = "Chưa xử lý", count = _cloudData.Count(x => StarredWaybills.Contains(x.WaybillNo) && IsNeedsAction(x)).ToString("N0") },
                        new { label = "Đã xử lý", count = _cloudData.Count(x => StarredWaybills.Contains(x.WaybillNo) && !IsNeedsAction(x)).ToString("N0") }
                    } }
                };

                var waybillsList = _cloudData.Select(MapWaybillToDto).ToList();

                var starredCodesObj = new System.Collections.Generic.Dictionary<string, bool>();
                lock (StarredWaybills)
                {
                    foreach (var code in StarredWaybills)
                    {
                        starredCodesObj[code] = true;
                    }
                }

                var payload = new
                {
                    type = "UPDATE_DATA",
                    siteId = siteId,
                    lastUpdateTime = lastUpdateTime,
                    syncStatus = syncStatus,
                    counts = new
                    {
                        mains = mainsObj,
                        subs = subsObj
                    },
                    waybills = waybillsList,
                    starredCodes = starredCodesObj,
                    selectedSource = _selectedSource,
                    selectedTimeInterval = _selectedTimeInterval,
                    selectedStatusSelect = _selectedStatusSelect,
                    searchText = _searchText
                };

                var options = new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = null
                };
                var json = System.Text.Json.JsonSerializer.Serialize(payload, options);
                _webView.CoreWebView2.PostWebMessageAsJson(json);
            }
            catch (Exception ex)
            {
                AppLogger.Error("PostStateToWebView2 failed", ex);
            }
        }
    }
}
