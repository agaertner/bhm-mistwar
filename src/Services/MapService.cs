using Blish_HUD;
using Blish_HUD.Content;
using Blish_HUD.Controls;
using Blish_HUD.Modules.Managers;
using Gw2Sharp.WebApi.V2.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nekres.Mistwar.UI.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Blish_HUD.Extended;
using File = System.IO.File;
using Rectangle = Microsoft.Xna.Framework.Rectangle;
namespace Nekres.Mistwar.Services {
    internal class MapService : IDisposable
    {
        private float _opacity;
        public float Opacity
        {
            get => _opacity;
            set
            {
                _opacity = value;
                _mapControl?.SetOpacity(value);
            }
        }

        private float _colorIntensity;
        public float ColorIntensity
        {
            get => _colorIntensity;
            set
            {
                _colorIntensity = value;
                _mapControl?.SetColorIntensity(value);
            }
        }

        public bool IsLoading { get; private set; }
        public bool IsReady   { get; private set; }

        private readonly IProgress<string>               _loadingIndicator;
        private          Dictionary<int, AsyncTexture2D> _mapCache;
        private          DirectoriesManager              _dir;
        private          WvwService                      _wvw;
        private          MapImage                        _mapControl;
        private          StandardWindow                  _window;

        private const int PADDING_RIGHT = 5;

        public MapService(DirectoriesManager dir, WvwService wvw, IProgress<string> loadingIndicator)
        {
            _dir = dir;
            _wvw = wvw;
            _loadingIndicator = loadingIndicator;
            _mapCache = new Dictionary<int, AsyncTexture2D>();

            _window = new StandardWindow(GameService.Content.DatAssetCache.GetTextureFromAssetId(155985),
                                         new Rectangle(40, 26, 913, 691),
                                         new Rectangle(70, 71, 839, 605)) {
                Parent        = GameService.Graphics.SpriteScreen,
                Title         = string.Empty,
                Emblem        = MistwarModule.ModuleInstance.CornerTex,
                Subtitle      = MistwarModule.ModuleInstance.Name,
                Id            = "Mistwar_Map_86a367fa-61ba-4bab-ae3b-fb08b407214a",
                SavesPosition = true,
                SavesSize     = true,
                CanResize     = true,
                Width         = 800,
                Height        = 800,
                Left = (GameService.Graphics.SpriteScreen.Width - 800) / 2,
                Top = (GameService.Graphics.SpriteScreen.Height - 800) / 2
            };
            
            _mapControl = new MapImage {
                Parent   = _window,
                Width = _window.ContentRegion.Width - PADDING_RIGHT,
                Height = _window.ContentRegion.Height - PADDING_RIGHT,
                Left = 0,
                Top = 0
            };

            _window.ContentResized                                  += OnWindowResized;
            GameService.Gw2Mumble.CurrentMap.MapChanged             += OnMapChanged;
            GameService.Gw2Mumble.UI.IsMapOpenChanged               += OnIsMapOpenChanged;
            GameService.GameIntegration.Gw2Instance.IsInGameChanged += OnIsInGameChanged;
        }

        private void OnWindowResized(object sender, RegionChangedEventArgs e) {
            _mapControl.Size = new Point(e.CurrentRegion.Size.X - PADDING_RIGHT, e.CurrentRegion.Size.Y - PADDING_RIGHT);
        }

        public void DownloadMaps(int[] mapIds)
        {
            if (this.IsReady || this.IsLoading || mapIds.IsNullOrEmpty()) {
                return;
            }

            var thread = new Thread(() => LoadMapsInBackground(mapIds))
            {
                IsBackground = true
            };

            thread.Start();
        }

        private void LoadMapsInBackground(int[] mapIds)
        {
            this.IsLoading = true;

            foreach (var id in mapIds)
            {
                var t = DownloadMapImage(id);
                t.Wait();
                // Progress indicator cannot yet handle total percentage of all tasks combined hence we do not use Task.WaitAll(taskList).
                // Considering tile download on slow connections and tile structuring on low-end hardware in addition to loosing indicator info depth a
                // refactor of the code to support it would not yield much value.
            }
            _loadingIndicator.Report(null);

            this.IsLoading = false;
        }

        private async Task DownloadMapImage(int id) {

            AsyncTexture2D tex;

            lock (_mapCache) {
                if (!_mapCache.TryGetValue(id, out tex)) {
                    tex = new AsyncTexture2D();
                    _mapCache.Add(id, tex);
                }
            }

            var filePath = Path.Combine(_dir.GetFullDirectoryPath("mistwar"), $"{id}.png");

            if (LoadFromCache(filePath, tex)) {
                await ReloadMap();
                return;
            }

            var map = await MapUtil.GetMap(id);

            if (map == null) {
                return;
            }

            await MapUtil.BuildMap(map, filePath, true, _loadingIndicator);

            if (LoadFromCache(filePath, tex)) {
                await ReloadMap();
            }
        }

        private bool LoadFromCache(string filePath, AsyncTexture2D cacheTex, int retries = 2, int delayMs = 2000)
        {
            try {
                using var fil = new MemoryStream(File.ReadAllBytes(filePath));
                using var gdc = GameService.Graphics.LendGraphicsDeviceContext();
                var       tex = Texture2D.FromStream(gdc.GraphicsDevice, fil);
                cacheTex.SwapTexture(tex);
                return true;
            } catch (Exception e) {
                if (retries > 0) {
                    MistwarModule.Logger.Warn(e, $"Failed to load map images from disk. Retrying in {delayMs / 1000} second(s) (remaining retries: {retries}).");
                    Task.Delay(delayMs).Wait();
                    LoadFromCache(filePath, cacheTex, retries - 1, delayMs);
                }
                MistwarModule.Logger.Warn(e, $"After multiple attempts '{filePath}' could not be loaded.");
                return false;
            }
        }

        public async Task ReloadMap()
        {
            if (!GameService.Gw2Mumble.CurrentMap.Type.IsWvWMatch()) {
                this.IsReady = false;
                Toggle(true);
                return;
            }

            AsyncTexture2D tex;

            lock(_mapCache)
            {
                if (!_mapCache.TryGetValue(GameService.Gw2Mumble.CurrentMap.Id, out tex) || tex == null) {
                    this.IsReady = false;
                    Toggle(true);
                    return;
                }
            }

            _mapControl.Texture.SwapTexture(tex);

            var map = await GetMap(GameService.Gw2Mumble.CurrentMap.Id);

            if (map == null) {
                this.IsReady = false;
                Toggle(true);
                return;
            }

            _mapControl.Map = map;

            _window.Title = map.Name;

            var wvwObjectives = await _wvw.GetObjectives(GameService.Gw2Mumble.CurrentMap.Id);

            if (wvwObjectives.IsNullOrEmpty()) {
                this.IsReady = false;
                Toggle(true);
                return;
            }
            _mapControl.WvwObjectives = wvwObjectives;
            MistwarModule.ModuleInstance?.MarkerService?.ReloadMarkers(wvwObjectives);

            this.IsReady = true;
        }

        private async Task<ContinentFloorRegionMap> GetMap(int mapId)
        {
            var map = await MapUtil.GetMap(mapId);
            if (map == null) {
                return null;
            }
            return await MapUtil.GetMapExpanded(map, map.DefaultFloor);
        }

        public void Toggle(bool forceHide = false)
        {
            if (forceHide) {
                _window.Hide();
                return;
            }

            if (!this.IsReady) {
                ScreenNotification.ShowNotification($"({MistwarModule.ModuleInstance.Name}) Service unavailable. Current match not loaded.", ScreenNotification.NotificationType.Error);
                return;
            }

            if (!GameUtil.IsAvailable() || !GameService.Gw2Mumble.CurrentMap.Type.IsWvWMatch()) {
                return;
            }

            _window.ToggleWindow();
        }

        private void OnIsMapOpenChanged(object o, ValueEventArgs<bool> e)
        {
            if (!e.Value) {
                return;
            }

            this.Toggle(true);
        }

        private void OnIsInGameChanged(object o, ValueEventArgs<bool> e)
        {
            if (e.Value) {
                return;
            }

            this.Toggle(true);
        }

        private async void OnMapChanged(object o, ValueEventArgs<int> e)
        { 
            await ReloadMap();
        }

        public void Dispose()
        {
            _window.ContentResized                                  -= OnWindowResized;
            GameService.Gw2Mumble.CurrentMap.MapChanged             -= OnMapChanged;
            GameService.Gw2Mumble.UI.IsMapOpenChanged               -= OnIsMapOpenChanged;
            GameService.GameIntegration.Gw2Instance.IsInGameChanged -= OnIsInGameChanged;
            _window.Dispose();
            foreach (var tex in _mapCache.Values)
            {
                tex?.Dispose();
            }
        }
    }
}
