# JMS API Notes

## Overview

AutoJMS integrates with JMS (jtexpress.vn) via:
1. **WebView2**: Browser automation for user actions
2. **HTTP API**: Direct API calls for programmatic access

## URLs

| Service | URL |
|---------|-----|
| JMS Frontend | https://jms.jtexpress.vn |
| JMS API Gateway | https://jmsgw.jtexpress.vn |

## Auth Token

JMS uses a 32-character hex string as auth token.

### Storage Keys

Token stored in browser localStorage under various keys:
- `YL_TOKEN`
- `authToken`
- `token`
- `accessToken`

### Token Format

```
^[a-fA-F0-9]{32}$
```

Example: `a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3`

## API Endpoints Used

### Print Waybill

```
POST /operatingplatform/rebackTransferExpress/printWaybill
```

**Request:**
```json
{
  "waybillIds": ["8XXXXXXXXXXXX"],
  "applyTypeCode": 4,
  "printType": 1,
  "pringType": 1,
  "countryId": "1"
}
```

**Response:**
```json
{
  "code": "200",
  "data": "https://pdf-url...",
  "msg": "Success"
}
```

### Inventory Report

```
POST /businessindicator/bigdataReport/detail/take_ret_mon_detail_doris2
```

**Request:**
```json
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

// startDate = endDate - 30 day
```

**Response:**
```json
{
  "succ": true,
  "data": {
    "records": [...],
    "total": 1234,
    "pages": 13
  }
}
```

## Response Codes

| Code | Meaning |
|------|---------|
| 200 | Success |
| 0 | Success |
| 1 | Success |
| Other | Check msg field |

## Headers Required

```http
Authorization: Bearer <authToken>
Content-Type: application/json
Origin: https://jms.jtexpress.vn
Referer: https://jms.jtexpress.vn
User-Agent: 
```

## WebView2 Automation

### Vue/Element UI Selectors

JMS uses Vue.js with Element UI.

```javascript
// Input field
document.querySelector('.el-input__inner')

// Primary button
document.querySelector('.el-button--primary')

// Form
document.querySelector('.el-form')
```

### Setting Input Values (Vue)

```javascript
const input = document.querySelector('.el-input__inner');
const setter = Object.getOwnPropertyDescriptor(
    HTMLInputElement.prototype, 'value').set;
setter.call(input, 'YOUR_VALUE');
input.dispatchEvent(new Event('input', { bubbles: true }));
```

## Print Types

| Type | applyTypeCode | Description |
|------|---------------|-------------|
| In hoàn | 4 | Return label |
| In chuyển tiếp | 2 | Forward label |
| In lại đơn | - | Reprint original |
| In RV | - | Reverse |

## Action Site Code

Default: `214A02`

Configured via:
- AppConfig.Current.ActionSiteCode
- AutoJMS.json `actionSiteCode`
