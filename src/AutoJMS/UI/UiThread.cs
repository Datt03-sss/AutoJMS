using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AutoJMS
{
    /// <summary>
    /// Helpers to marshal work onto the WinForms UI thread.
    ///
    /// WebView2 (CoreWebView2.ExecuteScriptAsync / Source / etc.) is UI-thread
    /// affine: touching it from a background thread throws
    /// "CoreWebView2 can only be accessed from the UI thread." Any background
    /// caller that needs the WebView must funnel through here.
    /// </summary>
    [System.Reflection.Obfuscation(Exclude = true, ApplyToMembers = true)]
    public static class UiThread
    {
        /// <summary>
        /// Run <paramref name="action"/> on the UI thread that owns
        /// <paramref name="owner"/> and await its result. If already on the UI
        /// thread (or no marshaling is possible) the action runs inline.
        /// </summary>
        public static async Task<T> InvokeOnUiAsync<T>(Control owner, Func<Task<T>> action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            // No owner / handle not ready / already on UI thread → run inline.
            if (owner == null || !owner.IsHandleCreated || !owner.InvokeRequired)
            {
                return await action().ConfigureAwait(false);
            }

            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                owner.BeginInvoke(new Action(async () =>
                {
                    try { tcs.TrySetResult(await action().ConfigureAwait(true)); }
                    catch (Exception ex) { tcs.TrySetException(ex); }
                }));
            }
            catch (Exception ex)
            {
                // BeginInvoke can throw if the handle is being destroyed.
                tcs.TrySetException(ex);
            }
            return await tcs.Task.ConfigureAwait(false);
        }

        /// <summary>Void-returning overload.</summary>
        public static Task InvokeOnUiAsync(Control owner, Func<Task> action)
        {
            return InvokeOnUiAsync(owner, async () => { await action().ConfigureAwait(true); return true; });
        }
    }
}
