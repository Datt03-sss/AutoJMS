# WebView2 Issues

## Common Issues

### Issue: Cross-Thread Access

**Error**: `UnauthorizedAccessException`

**Cause**: WebView2 accessed from background thread

**Fix**: Marshal to UI thread

### Issue: Script Timeout

**Error**: `TimeoutException`

**Cause**: Script execution too slow

**Fix**: Increase timeout

### Issue: Vue Input Not Setting

**Error**: Input value not captured

**Cause**: Wrong Vue setter

**Fix**: Use nativeInputValueSetter pattern

## Debug Tools

### Browser DevTools

1. Open app
2. Right-click in WebView2
3. Select "Inspect"
4. Test selectors in console

### Test Script

```javascript
// Check selector
document.querySelector('.el-input__inner')

// Test Vue setter
const input = document.querySelector('.el-input__inner');
const setter = Object.getOwnPropertyDescriptor(
    HTMLInputElement.prototype, 'value').set;
setter.call(input, 'test');
input.dispatchEvent(new Event('input', { bubbles: true }));
```
