using Blish_HUD;
using Blish_HUD.Content;
using Blish_HUD.Controls;
using Blish_HUD.Extended;
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
using File = System.IO.File;

namespace Nekres.Mistwar.Services {
    internal class MapService : IDisposable
    {
        private DirectoriesManager _dir;
        private WvwService _wvw;

        private MapImage _mapControl;

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

        public bool IsVisible => _mapControl?.Visible ?? false;

        private readonly IProgress<string> _loadingIndicator;

        public bool IsLoading { get; private set; }

        private Dictionary<int, AsyncTexture2D> _mapCache;

        public MapService(DirectoriesManager dir, WvwService wvw, IProgress<string> loadingIndicator)
        {
            _dir = dir;
            _wvw = wvw;
            _loadingIndicator = loadingIndicator;
            _mapCache = new Dictionary<int, AsyncTexture2D>();
            _mapControl = new MapImage
            {
                Parent = GameService.Graphics.SpriteScreen,
                Size = new Point(0, 0),
                Location = new Point(0, 0),
                Visible = false
            };
            GameService.Gw2Mumble.CurrentMap.MapChanged += OnMapChanged;
            GameService.Gw2Mumble.UI.IsMapOpenChanged += OnIsMapOpenChanged;
            GameService.GameIntegration.Gw2Instance.IsInGameChanged += OnIsInGameChanged;
        }

        public void DownloadMaps(int[] mapIds)
        {
            if (mapIds.IsNullOrEmpty()) {
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
            this.IsLoading = false;
            _loadingIndicator.Report(null);
        }

        private async Task DownloadMapImage(int id)
        {
            if (!_mapCache.TryGetValue(id, out var cacheTex))
            {
                cacheTex = new AsyncTexture2D();
                _mapCache.Add(id, cacheTex);
            }

            var filePath = $"{_dir.GetFullDirectoryPath("mistwar")}/{id}.png";

            if (LoadFromCache(filePath, cacheTex))
            {
                await ReloadMap();
                return;
            }

            var map = await MapUtil.GetMap(id);

            if (map == null) {
                return;
            }

            await MapUtil.BuildMap(map, filePath, true, _loadingIndicator);

            if (LoadFromCache(filePath, cacheTex)) {
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
                return;
            }

            if (!_mapCache.TryGetValue(GameService.Gw2Mumble.CurrentMap.Id, out var tex) || tex == null) {
                return;
            }

            _mapControl.Texture.SwapTexture(tex);

            _mapControl.Map = await GetMap(GameService.Gw2Mumble.CurrentMap.Id);

            var wvwObjectives = await _wvw.GetObjectives(GameService.Gw2Mumble.CurrentMap.Id);

            if (wvwObjectives.IsNullOrEmpty()) {
                return;
            }
            _mapControl.WvwObjectives = wvwObjectives;
            MistwarModule.ModuleInstance?.MarkerService?.ReloadMarkers(wvwObjectives);
        }

        private async Task<ContinentFloorRegionMap> GetMap(int mapId)
        {
            var map = await MapUtil.GetMap(mapId);
            return await MapUtil.GetMapExpanded(map, map.DefaultFloor);
        }

        public void Toggle(bool forceHide = false, bool silent = false)
        {
            if (IsLoading)
            {
                ScreenNotification.ShowNotification($"({MistwarModule.ModuleInstance.Name}) Map images are being prepared...", ScreenNotification.NotificationType.Error);
                return;
            }
            _mapControl?.Toggle(forceHide, silent);
        }

        private void OnIsMapOpenChanged(object o, ValueEventArgs<bool> e)
        {
            if (!e.Value) {
                return;
            }

            this.Toggle(true, true);
        }

        private void OnIsInGameChanged(object o, ValueEventArgs<bool> e)
        {
            if (e.Value) {
                return;
            }

            this.Toggle(true, true);
        }

        private async void OnMapChanged(object o, ValueEventArgs<int> e)
        { 
            await ReloadMap();
        }

        public void Dispose()
        {
            GameService.Gw2Mumble.CurrentMap.MapChanged -= OnMapChanged;
            GameService.Gw2Mumble.UI.IsMapOpenChanged -= OnIsMapOpenChanged;
            GameService.GameIntegration.Gw2Instance.IsInGameChanged -= OnIsInGameChanged;
            _mapControl?.Dispose();
            foreach (var tex in _mapCache.Values)
            {
                tex?.Dispose();
            }
        }
    }
}
