# Logistics Operation Skill

## Overview

AutoJMS automates logistics operations for Vietnam delivery company using JMS (jtexpress.vn).

## Core Operations

### DKCH - Đăng ký Chuyển Hoàn

Register return shipments when delivery fails.

**Flow:**
1. User clicks DKCH1 or DKCH2
2. DkchManager fills JMS web form via WebView2
3. Form submission triggers JMS API call
4. On success: update tracking, add to done list

**DKCH1 vs DKCH2:**
- DKCH1: First attempt
- DKCH2: Second attempt (if DKCH1 failed)

**Exception Handling:**
- `NeedSwitchToDkch1Exception`: Switch to DKCH1
- `NeedSwitchToDkch2Exception`: Switch to DKCH2

### Tracking - Tra cứu vận đơn

Track waybill status.

**Flow:**
1. User enters waybill number(s)
2. Normalize input (handle various formats)
3. Call JMS API with authToken
4. Display results in DataGridView
5. Optional: export to Excel, upload to Google Sheets

### Print - In nhãn

Print shipping labels.

**Modes:**
- In hoàn (return): Print return label
- In chuyển tiếp (forward): Print forward label
- In lại đơn (reprint): Reprint original
- In RV (reverse): Reverse delivery

**Flow:**
1. Search waybill
2. Select mode
3. Generate PDF via JMS API
4. Download PDF
5. Print to system printer

## Waybill Format

### Standard Format

```
8XXXXXXXXXXXX  (12 digits starting with 8)
```

### With Hyphen

```
8XXXXXXXXXXXX-001
```

### Validation Regex

```csharp
var waybillRegex = new Regex(
    @"((8\d{11}|[A-Za-z][A-Za-z0-9]{4,17})(-\d{3})?)",
    RegexOptions.IgnoreCase);
```

## JMS API

### Base URLs

```
Frontend: https://jms.jtexpress.vn
API Gateway: https://jmsgw.jtexpress.vn
```

### Common Endpoints

| Endpoint | Purpose |
|----------|---------|
| /operatingplatform/rebackTransferExpress/printWaybill | Print label |
| /businessindicator/bigdataReport/detail/take_ret_mon_detail_doris2 | Inventory |

### Auth Token

32-character hex string from JMS web session.

```javascript
// Stored in localStorage as:
localStorage.getItem('YL_TOKEN')
localStorage.getItem('authToken')
localStorage.getItem('token')
```

### Response Codes

| Code | Meaning |
|------|---------|
| 200 | Success |
| 0 | Success |
| 1 | Success |
| Other | Check msg field |

## Inventory Sync

Fetch all waybills in detention inventory for tracking.

**JMS API:**
```
POST /businessindicator/bigdataReport/detail/take_ret_mon_detail_doris2

Body:
{
  "current": 1,
  "size": 100,
  "dimension": "3",
  "isFlag": "1",
  "actionSiteCode": "214A02",
  "startDate": "2026-05-01 00:00:00",
  "endDate": "2026-05-26 23:59:59",
  "countryId": "1"
}
```

## FullStackOperation (ULTRA)

Advanced operations form with:

### Dashboard Tab

Realtime waybill list from Supabase with filters.

### Thời Hiệu Tab

SLA monitoring - track time since delivery attempts.

### Chat Tab (Zalo)

Integration with Zalo for customer communication.

## Common Error Handling

| Error | Cause | Recovery |
|-------|-------|----------|
| No data found | Waybill not in system | Manual check |
| 401 Unauthorized | Token expired | Refresh token |
| DKCH fails | JMS business rule | Switch mode |
| Print fails | Printer issue | Check printer |
