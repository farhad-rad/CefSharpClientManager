using CefSharp;
using CefSharp.OffScreen;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using static Ako.CefClientManager.Program;

namespace Ako.CefClientManager
{
    public interface ICefClient : IDisposable
    {
        public Guid Id { get; }
        public ChromiumWebBrowser? Browser { get; }
        public Task LoadUrlAsync(string url);
        public Task WaitToLoadAsync();
        public Task WaitForElementRenderAsync(string elementQuery, TimeSpan? maxWaitTime = null);
        public Task ExecuteJsAsync(string jsCode);
        public Task ExecuteJsAsPromiseAsync(string jsCode, TimeSpan? maxWaitTime = null);
        public Task<TResult?> ExecuteJsAsPromiseAsync<TResult>(string jsCode, TimeSpan? maxWaitTime = null);
    }
    public class CefClient : ICefClient
    {
        public Guid Id { get; }
        public ChromiumWebBrowser? Browser { get; set; } = null;
        private bool _init = false;
        private bool _loading = false;
        private string _cachePath = null;
        public CefClient()
        {
            Id = Guid.NewGuid();
            _cachePath = Id.ToString();
        }
        public CefClient(string? cachePath)
        {
            Id = Guid.NewGuid();
            _cachePath = cachePath == null ? Id.ToString() : Path.Combine(cachePath, Id.ToString());
        }
        public static async Task<CefClient> CreateAsync()
            => await CreateAsync(null);
        public static async Task<CefClient> CreateAsync(string? cachePath)
        {
            var result = new CefClient(cachePath);
            await result.InitBrowserAsync();
            return result;
        }
        public async Task InitBrowserAsync()
        {
            if (_init)
            {
                return;
            }
            var tcs = new TaskCompletionSource<bool>();
            EventHandler handler = (s, e) => tcs.SetResult(true);
            Browser = new ChromiumWebBrowser(
                requestContext: new RequestContext(new RequestContextSettings { CachePath = _cachePath, PersistSessionCookies = true })
            );
            Browser.BrowserInitialized += handler;
            Browser.LoadingStateChanged += LoadingHandler;
            Browser.JavascriptMessageReceived += OnJsMessage;
            _init = await tcs.Task;
            Browser.BrowserInitialized -= handler;
        }
        private void LoadingHandler(object? sender, LoadingStateChangedEventArgs e)
        {
            _loading = e.IsLoading;
        }
        public void OnJsMessage(object? sender, JavascriptMessageReceivedEventArgs e)
        {
        }

        private class JsMessageKeyValue<T>
        {
            public string Key { get; set; }
            public bool Value { get; set; }
            public T? Data { get; set; } = default(T);
        }
        private class JsMessageKeyValue : JsMessageKeyValue<object?> { }

        public async Task ExecuteJsAsync(string jsCode)
        {
            await Browser.EvaluateScriptAsync(jsCode);
            return;
        }

        public async Task<TResult?> ExecuteJsAsPromiseAsync<TResult>(string jsCode, TimeSpan? maxWaitTime = null)
            => (TResult?)(await ExecuteJsAsPromiseCoreAsync<TResult>(jsCode, maxWaitTime, true));
        public async Task ExecuteJsAsPromiseAsync(string jsCode, TimeSpan? maxWaitTime = null)
        {
            var result = (bool)(await ExecuteJsAsPromiseCoreAsync<object?>(jsCode, maxWaitTime, false));
            if (!result)
            {
                throw new Exception("Operation did not complete");
            }
        }
        public async Task<object> ExecuteJsAsPromiseCoreAsync<TResult>(string jsCode, TimeSpan? maxWaitTime = null, bool returnData = true)
        {
            if (_loading)
            {
                await WaitToLoadAsync();
            }
            var uniqueName = Guid.NewGuid().ToString().Replace("-", "");
            var js = $@"(function () {{
    return new Promise((resolveCefSharp, rejectCefSharp) => {{
        const cefPromise = {{
            resolve: resolveCefSharp,
            reject: rejectCefSharp,
        }};
        try {{
            {jsCode}
        }} catch (error) {{
            rejectCefSharp();
        }}
    }}).then((Data) => {{
        CefSharp.PostMessage(`AkoCefMessage: ${{JSON.stringify({{ Key: '{uniqueName}', Value: true, Data }})}}`);
    }}).catch((Data) => {{
        CefSharp.PostMessage(`AkoCefMessage: ${{JSON.stringify({{ Key: '{uniqueName}', Value: false, Data }})}}`);
    }});
}})();";
            var doneAlready = false;
            var tcs = new TaskCompletionSource<JsMessageKeyValue<TResult>>();
            EventHandler<JavascriptMessageReceivedEventArgs> handler = (s, e) =>
            {
                if (e.Message is string && (string)e.Message != null)
                {
                    var messageRaw = (string)e.Message;
                    if (!messageRaw.StartsWith("AkoCefMessage"))
                    {
                        return;
                    }
                    JsMessageKeyValue<TResult>? message = null;
                    try
                    {
                        message = JsonSerializer.Deserialize<JsMessageKeyValue<TResult>>(messageRaw.Replace("AkoCefMessage: ", ""), new JsonSerializerOptions
                        {
                            Encoder = JavaScriptEncoder.Create(UnicodeRanges.BasicLatin, UnicodeRanges.Arabic, UnicodeRanges.ArabicExtendedA, UnicodeRanges.ArabicPresentationFormsA, UnicodeRanges.ArabicPresentationFormsB, UnicodeRanges.ArabicSupplement),
                            IgnoreNullValues = true,
                            WriteIndented = false,
                        });
                    }
                    catch { }
                    if (message != null && message.Key == uniqueName)
                    {
                        if (!doneAlready)
                        {
                            doneAlready = true;
                            tcs.SetResult(message);
                        }
                    }
                }
            };
            EventHandler<LoadingStateChangedEventArgs> loadingHandler = (s, e) =>
            {
                if (e.IsLoading)
                {
                    if (!doneAlready)
                    {
                        doneAlready = true;
                        tcs.SetResult(new JsMessageKeyValue<TResult> { Key = uniqueName, Value = true });
                    }
                }
            };
            Browser.LoadingStateChanged += loadingHandler;
            Browser.JavascriptMessageReceived += handler;
            await ExecuteJsAsync(js);
            if (maxWaitTime != null)
            {
                Task.Factory.StartNew(async () =>
                {
                    await Task.Delay(maxWaitTime.Value);
                    if (!doneAlready)
                    {
                        doneAlready = true;
                        tcs.SetResult(new JsMessageKeyValue<TResult> { Key = uniqueName, Value = false });
                    }
                });
            }
            var result = await tcs.Task;
            Browser.JavascriptMessageReceived -= handler;
            Browser.LoadingStateChanged -= loadingHandler;
            return returnData ? result.Data : result.Value;
        }

        public async Task WaitForElementRenderAsync(string elementQuery, TimeSpan? maxWaitTime = null)
        {
            if (_loading)
            {
                await WaitToLoadAsync();
            }

            var js = $@"(function () {{
    const loadingMotor = () => {{
        setTimeout(() => {{
            let el = document.querySelector('{elementQuery.Replace("'", "\\'")}');
            if (!el) {{
                return loadingMotor();
            }}
            cefPromise.resolve();
        }}, 100);
    }};
    loadingMotor();
}})();";
            try
            {
                await ExecuteJsAsPromiseAsync(js, maxWaitTime);
            }
            catch { }
            return;
        }

        public async Task LoadUrlAsync(string url)
        {
            var tcs = new TaskCompletionSource();
            EventHandler<LoadingStateChangedEventArgs> handler = (s, e) =>
            {
                if (!e.IsLoading)
                {
                    tcs.SetResult();
                }
            };
            Browser.Load(url);
            Browser.LoadingStateChanged += handler;
            await tcs.Task;
            Browser.LoadingStateChanged -= handler;
            return;
        }
        public async Task WaitToLoadAsync()
        {
            if (!_loading)
            {
                return;
            }
            var tcs = new TaskCompletionSource();
            EventHandler<LoadingStateChangedEventArgs> handler = (s, e) =>
            {
                if (!e.IsLoading)
                {
                    tcs.SetResult();
                }
            };
            Browser.LoadingStateChanged += handler;
            await tcs.Task;
            Browser.LoadingStateChanged -= handler;
            return;
        }
        public void Dispose()
        {
            Browser.Dispose();
        }
    }
}