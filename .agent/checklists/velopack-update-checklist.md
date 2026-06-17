# Velopack Update Checklist

Use this checklist when testing Velopack updates.

## Update Flow

1. User clicks "Kiểm tra cập nhật"
2. App checks version-latest.json
3. If newer: prompt user
4. User confirms
5. Download binary
6. PrepareForUpdate (stop services)
7. ApplyUpdatesAndRestart

## Test Cases

### Check Update (No Update)

1. Launch app
2. Click "Kiểm tra cập nhật"
3. Verify: "Bạn đang dùng phiên bản mới nhất"

### Check Update (Update Available)

1. Install old version
2. Launch app
3. Click "Kiểm tra cập nhật"
4. Verify: version prompt shown
5. Click Yes
6. Verify: download progress
7. Verify: app restarts

### Update During Operation

1. Launch app
2. Start DKCH operation
3. Click "Kiểm tra cập nhật"
4. Confirm update
5. Verify: operations stopped
6. Verify: app restarts
7. Verify: new version runs

## Verify Components

### version-latest.json

- [ ] Correct version
- [ ] Correct provider (github)
- [ ] Correct tag
- [ ] Correct channel

### GitHub Release

- [ ] Tag exists
- [ ] Assets uploaded
- [ ] RELEASES file present

### App Behavior

- [ ] No browser opens
- [ ] Progress shown
- [ ] Services stopped
- [ ] Restart works
- [ ] No data loss

## Rollback

If update fails:

1. [ ] Previous version still works
2. [ ] User data preserved
3. [ ] App starts normally
