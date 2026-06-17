#nullable enable
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS.Automation.DevTools
{
    public sealed class DomSnapshotService
    {
        public async Task<DomSnapshot> CaptureAsync(WebView2 webView, string surfaceName, CancellationToken token)
        {
            if (webView == null) throw new ArgumentNullException(nameof(webView));
            token.ThrowIfCancellationRequested();

            return await UiThread.InvokeOnUiAsync(webView, async () =>
            {
                token.ThrowIfCancellationRequested();
                if (webView.CoreWebView2 == null)
                    await webView.EnsureCoreWebView2Async(null);

                string raw = await webView.ExecuteScriptAsync(SnapshotScript);
                string json = UnwrapWebViewJsonString(raw);
                var snapshot = JsonSerializer.Deserialize<DomSnapshot>(json, AppConfig.CreateJsonOptions()) ?? new DomSnapshot();
                snapshot.SurfaceName = surfaceName;
                snapshot.CapturedAtUtc = DateTime.UtcNow;
                return snapshot;
            });
        }

        private static string UnwrapWebViewJsonString(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw) || raw == "null") return "{}";
            try { return JsonSerializer.Deserialize<string>(raw) ?? "{}"; }
            catch { return raw.Trim('"'); }
        }

        private const string SnapshotScript = @"
(() => {
  const MAX_BODY = 4000;
  const MAX_ITEMS = 120;
  const clean = v => String(v || '').replace(/\s+/g, ' ').trim();
  const attr = (el, name) => clean(el && el.getAttribute(name));
  const cssEscape = value => {
    if (window.CSS && CSS.escape) return CSS.escape(String(value));
    return String(value).replace(/[""\\]/g, '\\$&');
  };
  const isVisible = el => {
    if (!el) return false;
    const style = window.getComputedStyle(el);
    const rect = el.getBoundingClientRect();
    return style && style.visibility !== 'hidden' && style.display !== 'none' && rect.width > 0 && rect.height > 0;
  };
  const elementUiClasses = el => Array.from(el.classList || []).filter(c => c.startsWith('el-')).slice(0, 12);
  const xpathLiteral = text => {
    if (!text.includes(""'"")) return ""'"" + text + ""'"";
    if (!text.includes('""')) return '""' + text + '""';
    return ""'"" + text.replace(/'/g, '') + ""'"";
  };
  const xpath = el => {
    if (!el || el.nodeType !== 1) return '';
    if (el.id) return '//*[@id=' + xpathLiteral(el.id) + ']';
    const parts = [];
    let node = el;
    while (node && node.nodeType === 1 && parts.length < 8) {
      let index = 1;
      let prev = node.previousElementSibling;
      while (prev) {
        if (prev.tagName === node.tagName) index++;
        prev = prev.previousElementSibling;
      }
      parts.unshift(node.tagName.toLowerCase() + '[' + index + ']');
      node = node.parentElement;
    }
    return '/' + parts.join('/');
  };
  const cssPath = el => {
    if (!el || el.nodeType !== 1) return '';
    const tag = el.tagName.toLowerCase();
    if (el.id) return '#' + cssEscape(el.id);
    const name = attr(el, 'name');
    if (name) return tag + '[name=""' + cssEscape(name) + '""]';
    const placeholder = attr(el, 'placeholder');
    if (placeholder) return tag + '[placeholder=""' + cssEscape(placeholder) + '""]';
    const aria = attr(el, 'aria-label');
    if (aria) return tag + '[aria-label=""' + cssEscape(aria) + '""]';
    const classes = elementUiClasses(el);
    if (classes.length) return tag + '.' + classes.map(cssEscape).join('.');
    return tag;
  };
  const nearbyLabel = el => {
    const id = attr(el, 'id');
    if (id) {
      const label = document.querySelector('label[for=""' + cssEscape(id) + '""]');
      if (label && clean(label.innerText)) return clean(label.innerText);
    }
    const formItem = el.closest && el.closest('.el-form-item');
    if (formItem) {
      const label = formItem.querySelector('.el-form-item__label,label');
      if (label && clean(label.innerText)) return clean(label.innerText);
    }
    let node = el.parentElement;
    for (let i = 0; node && i < 3; i++, node = node.parentElement) {
      const label = node.querySelector && node.querySelector('label');
      if (label && clean(label.innerText)) return clean(label.innerText);
    }
    return '';
  };
  const read = (el, kind) => ({
    kind,
    tagName: (el.tagName || '').toLowerCase(),
    text: clean(el.innerText || el.value || ''),
    placeholder: attr(el, 'placeholder'),
    name: attr(el, 'name'),
    id: attr(el, 'id'),
    className: attr(el, 'class'),
    type: attr(el, 'type'),
    role: attr(el, 'role'),
    ariaLabel: attr(el, 'aria-label'),
    nearbyLabel: nearbyLabel(el),
    elementUiClasses: elementUiClasses(el),
    cssSelector: cssPath(el),
    xPath: xpath(el),
    isVisible: isVisible(el)
  });
  const uniqueVisible = selector => {
    const seen = new Set();
    return Array.from(document.querySelectorAll(selector))
      .filter(isVisible)
      .filter(el => {
        if (seen.has(el)) return false;
        seen.add(el);
        return true;
      })
      .slice(0, MAX_ITEMS);
  };
  return JSON.stringify({
    capturedAtUtc: new Date().toISOString(),
    url: String(location.href || ''),
    title: document.title || '',
    pathname: location.pathname || '',
    hash: location.hash || '',
    bodyTextSample: clean((document.body && document.body.innerText || '').slice(0, MAX_BODY)),
    inputs: uniqueVisible('input, textarea').map(el => read(el, 'input')),
    buttons: uniqueVisible('button, [role=""button""], .el-button').map(el => read(el, 'button')),
    selects: uniqueVisible('select, .el-select').map(el => read(el, 'select'))
  });
})();";
    }
}
