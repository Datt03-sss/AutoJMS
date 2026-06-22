import re

filepath = r"d:\v1.2605.2(new-test)\src\AutoJMS\Forms\FullStackOperation.cs"

deleted_fields = [
    "uiTabControl1", "tabDash", "uiTableLayoutPanel4", "uiPanel10", "_filterBarPanel",
    "_queueNavPanel", "_leftContextPanel", "_rightIntelligencePanel", "tabDash_timeUpdateData",
    "tabDash_lblLastUpdate", "tabDash_updateData", "tabDash_dataSource", "tabDash_statusSelect",
    "uiTabControl2", "tabPage3", "tabDash_dataGridView", "tabPage4", "uiDataGridView2",
    "tabChat", "uiTableLayoutPanel3", "tabChat_webViewZalo", "tabChat_leftPanel",
    "tabChat_dataGrid", "uiPanel15", "uiTableLayoutPanel17", "tabChat_statusSelect",
    "uiLabel5", "tabChat_btnReload", "uiPanel4", "tabChat_userName", "tabChat_userAvatar",
    "uiPanel6", "uiTableLayoutPanel19", "tabChat_btnStart", "uiTableLayoutPanel20",
    "tabChat_timeSelect", "uiLabel3", "uiPanel7", "uiTableLayoutPanel16", "tabChat_hasXNCH",
    "tabChat_hasKVD", "tabChat_sumFollow", "uiPanel5", "_dashQuickFilterPanel",
    "_dashQuickFilter", "_dashQueueInsightLabel", "_operationQueueSidebar",
    "_operationDetailPanel", "_operationStatusFooter", "_operationGridFilterToolbar",
    "_operationGridHost", "_operationInventoryWorkspace", "_waybillJourneyWorkspace",
    "_waybillJourneyGrid", "_waybillJourneyTitle", "_waybillJourneyWaybillLabel",
    "_waybillJourneyStatusLabel", "_waybillJourneyCacheButton", "_waybillJourneyRawJsonButton",
    "_waybillJourneyBackButton", "_operationMiniMetricStrip", "_operationFocusStrip",
    "_kpiTotalInventory", "_kpiInbound", "_kpiDelivery", "_kpiBacklog", "_kpiReturn",
    "_kpiInventoryCheck", "_kpiCustomerService", "_kpiStationHalt", "_kpiStarred",
    "_operationHeaderTitle", "_operationHeaderStatus", "_operationRefreshLocalButton"
]

with open(filepath, "r", encoding="utf-8") as f:
    lines = f.readlines()

ref_count = {}
for i, line in enumerate(lines):
    for field in deleted_fields:
        # Check if the field is matched as a whole word in the line
        pattern = r"\b" + re.escape(field) + r"\b"
        if re.search(pattern, line):
            if field not in ref_count:
                ref_count[field] = []
            ref_count[field].append((i + 1, line.strip()))

print("Found references:")
for field, refs in sorted(ref_count.items()):
    print(f"\nField: {field} ({len(refs)} references)")
    for line_num, content in refs[:10]:
        print(f"  Line {line_num}: {content}")
    if len(refs) > 10:
        print(f"  ... and {len(refs) - 10} more")
