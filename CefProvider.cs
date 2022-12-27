using CefSharp;
using CefSharp.OffScreen;
using System.Diagnostics;

namespace Ako.CefClientManager
{
    public interface ICefProvider
    {
        public Task<ICefClient> AddClientAsync();
        public ICefClient? GetClient(Guid id);
        public void RemoveClient(Guid id);
    }
    public class CefProvider : IDisposable, ICefProvider
    {
        public List<ICefClient> Clients = new();
        private CefSettings _settings;
        private string _defaultCachePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AkoCefSharp");
        public CefProvider()
        {
            _settings = new CefSettings()
            {
                //By default CefSharp will use an in-memory cache, you need to specify a Cache Folder to persist data
                CachePath = _defaultCachePath,
            };
            ClearCaches();
            Cef.Initialize(_settings, performDependencyCheck: true, browserProcessHandler: null);
        }
        public CefProvider(CefSettings settings)
        {
            _settings = settings;
            _settings.CachePath ??= _defaultCachePath;
            ClearCaches();
            Cef.Initialize(_settings, performDependencyCheck: true, browserProcessHandler: null);
        }
        private void ClearCaches()
        {
            if (Directory.Exists(_settings.CachePath))
            {
                Directory.Delete(_settings.CachePath, true);
            }
        }
        public async Task<ICefClient> AddClientAsync()
        {
            var result = await CefClient.CreateAsync(_settings.CachePath);
            Clients.Add(result);
            return result;
        }
        public ICefClient? GetClient(Guid id)
        {
            var result = Clients.FirstOrDefault(x => x.Id == id);
            if (result == null)
            {
                return null;
            }
            return result;
        }
        public void RemoveClient(Guid id)
        {
            var result = Clients.FirstOrDefault(x => x.Id == id);
            if (result == null)
            {
                return;
            }
            result.Dispose();
            Clients.Remove(result);
        }
        public void Dispose()
        {
            foreach (var item in Clients)
            {
                item.Dispose();
            }
            Cef.Shutdown();
        }
    }
}