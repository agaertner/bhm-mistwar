using Blish_HUD;
using Blish_HUD.Controls;
using Blish_HUD.Input;
using Blish_HUD.Modules;
using Blish_HUD.Modules.Managers;
using Blish_HUD.Settings;
using Gw2Sharp.WebApi.V2.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Nekres.Mistwar.Core.Services;
using System;
using System.ComponentModel.Composition;
using System.Threading.Tasks;
using Blish_HUD.Extended;

namespace Nekres.Mistwar {
    [Export(typeof(Module))]
    public class MistwarModule : Module
    {

        internal static readonly Logger Logger = Logger.GetLogger<MistwarModule>();

        internal static MistwarModule ModuleInstance;

        #region Service Managers
        internal SettingsManager SettingsManager => this.ModuleParameters.SettingsManager;
        internal ContentsManager ContentsManager => this.ModuleParameters.ContentsManager;
        internal DirectoriesManager DirectoriesManager => this.ModuleParameters.DirectoriesManager;
        internal Gw2ApiManager Gw2ApiManager => this.ModuleParameters.Gw2ApiManager;
        #endregion

        [ImportingConstructor]
        public MistwarModule([Import("ModuleParameters")] ModuleParameters moduleParameters) : base(moduleParameters) { ModuleInstance = this; }

        // General settings
        internal SettingEntry<ColorType> ColorTypeSetting;
        internal SettingEntry<bool> TeamShapesSetting;

        // Hotkeys
        internal SettingEntry<KeyBinding> ToggleMapKeySetting;
        internal SettingEntry<KeyBinding> ToggleMarkersKeySetting;
        internal SettingEntry<KeyBinding> ChatMessageKeySetting;

        // Map settings
        internal SettingEntry<float> ColorIntensitySetting;
        internal SettingEntry<bool>  DrawSectorsSetting;
        internal SettingEntry<bool>  DrawObjectiveNamesSetting;
        internal SettingEntry<bool>  DrawRuinMapSetting;

        // Marker settings
        internal SettingEntry<bool>  EnableMarkersSetting;
        internal SettingEntry<bool>  HideInCombatSetting;
        internal SettingEntry<bool>  HideAlliedMarkersSetting;
        internal SettingEntry<bool>  DrawRuinMarkersSetting;
        internal SettingEntry<bool>  DrawEmergencyWayPointsSetting;
        internal SettingEntry<bool>  DrawDistanceSetting;
        internal SettingEntry<float> MaxViewDistanceSetting;
        internal SettingEntry<float> MarkerScaleSetting;
        internal SettingEntry<bool>  MarkerFixedSizeSetting;
        internal SettingEntry<bool>  MarkerStickySetting;

        protected override void DefineSettings(SettingCollection settings)
        {
            var generalSettings = settings.AddSubCollection("General");
            generalSettings.RenderInUi = true;
            ColorTypeSetting = generalSettings.DefineSetting("ColorType", ColorType.Normal, 
                () => "Color Type", 
                () => "Select a different color type if you have a color deficiency.");
            TeamShapesSetting = generalSettings.DefineSetting("TeamShapes", true, 
                () => "Team Shapes", 
                () => "Enables uniquely shaped objective markers per team.");

            var hotKeySettings = settings.AddSubCollection("Control Options");
            hotKeySettings.RenderInUi = true;
            ToggleMapKeySetting = hotKeySettings.DefineSetting("ToggleKey", new KeyBinding(Keys.N), 
                () => "Toggle Map", 
                () => "Key used to show and hide the strategic map.");
            ToggleMarkersKeySetting = hotKeySettings.DefineSetting("ToggleMarkersKey", new KeyBinding(Keys.OemOpenBrackets), 
                () => "Toggle Markers", () => "Key used to show and hide the objective markers.");
            ChatMessageKeySetting = hotKeySettings.DefineSetting("ChatMessageKey", new KeyBinding(Keys.Enter), 
                () => "Chat Message", 
                () => "Give focus to the chat edit box.");

            var mapSettings = settings.AddSubCollection("Map");
            mapSettings.RenderInUi = true;
            DrawSectorsSetting = mapSettings.DefineSetting("DrawSectors", true, 
                () => "Show Sector Boundaries", 
                () => "Indicates if the sector boundaries should be drawn.");
            DrawObjectiveNamesSetting = mapSettings.DefineSetting("DrawObjectiveNames", true, 
                () => "Show Objective Names", 
                () => "Indicates if the names of the objectives should be drawn.");
            DrawRuinMapSetting = mapSettings.DefineSetting("ShowRuins", true, 
                () => "Show Ruins", 
                () => "Indicates if the ruins should be shown.");
            DrawEmergencyWayPointsSetting = mapSettings.DefineSetting("ShowEmergencyWayPoints", false, 
                () => "Show Emergency Waypoints", 
                () => "Shows your team's Emergency Waypoints.");
            ColorIntensitySetting = mapSettings.DefineSetting("ColorIntensity", 80f, 
                                                              () => "Color Intensity", 
                                                              () => "Intensity of the background color.");

            var markerSettings = settings.AddSubCollection("Markers", true, false);
            markerSettings.RenderInUi = true;
            EnableMarkersSetting = markerSettings.DefineSetting("EnableMarkers", true, 
                () => "Enable Markers", 
                () => "Enables the markers overlay which shows objectives at their world position.");
            HideAlliedMarkersSetting = markerSettings.DefineSetting("HideAlliedMarkers", false, 
                () => "Hide Allied Objectives", 
                () => "Only hostile objectives will be shown.");
            HideInCombatSetting = markerSettings.DefineSetting("HideInCombat", true, 
                () => "Hide in Combat", 
                () => "Only the closest objective will be shown when in combat.");
            DrawRuinMarkersSetting = markerSettings.DefineSetting("ShowRuinMarkers", true, 
                () => "Show Ruins", 
                () => "Show markers for the ruins.");
            DrawDistanceSetting = markerSettings.DefineSetting("ShowDistance", true, 
                () => "Show Distance", 
                () => "Show flight distance to objectives.");
            MaxViewDistanceSetting = markerSettings.DefineSetting("MaxViewDistance", 50f, 
                () => "Max View Distance", 
                () => "The max view distance at which an objective marker can be seen.");
            MarkerScaleSetting = markerSettings.DefineSetting("ScaleRatio", 70f, 
                () => "Marker Size",
                () => "Changes the maximum size of the markers.");
            MarkerFixedSizeSetting = markerSettings.DefineSetting("FixedSize", false,
                                                                  () => "Fixed Size",
                                                                  () => "Disables the distance-based down-scaling of objective markers.");
            MarkerStickySetting = markerSettings.DefineSetting("Sticky", true,
                                                                  () => "Sticky",
                                                                  () => "Objectives which are out of view will have their marker stick to the edge of your screen if enabled.");
        }

        internal Texture2D CornerTex;
        private CornerIcon _moduleIcon;

        internal WvwService WvW;
        internal MapService Maps;
        internal MarkerService Markers;
        internal ResourceService Resources;

        protected override void Initialize()
        {
            ChatMessageKeySetting.Value.Enabled = false;

            Resources = new ResourceService();
            WvW       = new WvwService();
            Maps      = new MapService(GetModuleProgressHandler());
            if (EnableMarkersSetting.Value) {
                Markers = new MarkerService();
            }
        }

        protected override async Task LoadAsync()
        {
            await WvW.LoadAsync();
        }

        protected override void OnModuleLoaded(EventArgs e)
        {
            CornerTex   = ContentsManager.GetTexture("corner_icon.png");
            _moduleIcon = new CornerIcon(CornerTex, this.Name);

            ColorIntensitySetting.SettingChanged                    += OnColorIntensitySettingChanged;
            ToggleMapKeySetting.Value.Activated                     += OnToggleKeyActivated;
            ToggleMarkersKeySetting.Value.Activated                 += OnToggleMarkersKeyActivated;
            EnableMarkersSetting.SettingChanged                     += OnEnableMarkersSettingChanged;
            GameService.Gw2Mumble.CurrentMap.MapChanged             += OnMapChanged;
            GameService.Gw2Mumble.UI.IsMapOpenChanged               += OnIsMapOpenChanged;
            GameService.GameIntegration.Gw2Instance.IsInGameChanged += OnIsInGameChanged;
            ToggleMapKeySetting.Value.Enabled                       =  true;
            ToggleMarkersKeySetting.Value.Enabled                   =  true;

            OnColorIntensitySettingChanged(null, new ValueChangedEventArgs<float>(0, ColorIntensitySetting.Value));

            _moduleIcon.Click += OnModuleIconClick;
            // Base handler must be called

            Maps.DownloadMaps(WvW.GetWvWMapIds());

            base.OnModuleLoaded(e);
        }

        private void OnModuleIconClick(object o, MouseEventArgs e)
        {
            if (ServicesLoaded()) {
                Maps.Toggle();
            }
        }

        private void OnToggleKeyActivated(object o, EventArgs e) {
            if (ServicesLoaded()) {
                Maps.Toggle();
            }
        }

        private void UpdateModuleLoading(string loadingMessage)
        {
            if (_moduleIcon == null) {
                return;
            }

            _moduleIcon.LoadingMessage = loadingMessage;
            if (loadingMessage == null && !GameService.Gw2Mumble.CurrentMap.Type.IsWvWMatch()) {
                _moduleIcon?.Dispose(); // Dispose on completion of initialization if we are not in WvW.
            }
        }

        public IProgress<string> GetModuleProgressHandler()
        {
            return new Progress<string>(UpdateModuleLoading);
        }

        private bool ServicesLoaded() {
            if (WvW.IsLoading) {
                ScreenNotification.ShowNotification($"{Name} unavailable. WvW data not loaded.", ScreenNotification.NotificationType.Error);
                GameService.Content.PlaySoundEffectByName("error");
                return false;
            }

            if (Maps.IsLoading) {
                ScreenNotification.ShowNotification($"{Name} unavailable. Map not loaded.", ScreenNotification.NotificationType.Error);
                GameService.Content.PlaySoundEffectByName("error");
                return false;
            }

            return true;
        }

        private void OnToggleMarkersKeyActivated(object o, EventArgs e)
        {
            EnableMarkersSetting.Value = !EnableMarkersSetting.Value;
        }

        protected override async void Update(GameTime gameTime)
        {
            if (!Gw2ApiManager.HasPermission(TokenPermission.Account)) {
                return;
            }
            
            await WvW.Update();
        }

        private void OnColorIntensitySettingChanged(object o, ValueChangedEventArgs<float> e)
        {
            Maps.ColorIntensity = (100 - e.NewValue) / 100f;
        }

        private void OnIsMapOpenChanged(object o, ValueEventArgs<bool> e)
        {
            ToggleMapKeySetting.Value.Enabled = GameService.Gw2Mumble.CurrentMap.Type.IsWvWMatch();
        }

        private void OnMapChanged(object o, ValueEventArgs<int> e)
        {
            if (GameService.Gw2Mumble.CurrentMap.Type.IsWvWMatch())
            {
                _moduleIcon?.Dispose();
                _moduleIcon = new CornerIcon(CornerTex, this.Name);
                _moduleIcon.Click += OnModuleIconClick;
                ToggleMapKeySetting.Value.Enabled = true;
                return;
            }
            _moduleIcon?.Dispose();
            ToggleMapKeySetting.Value.Enabled = false;
        }

        private void OnIsInGameChanged(object o, ValueEventArgs<bool> e)
        {
            if (e.Value && GameService.Gw2Mumble.CurrentMap.Type.IsWvWMatch())
            {
                _moduleIcon?.Dispose();
                _moduleIcon = new CornerIcon(CornerTex, this.Name);
                _moduleIcon.Click += OnModuleIconClick;
                ToggleMapKeySetting.Value.Enabled = true;
                return;
            }
            _moduleIcon?.Dispose();
            ToggleMapKeySetting.Value.Enabled = false;
        }

        private async void OnEnableMarkersSettingChanged(object o, ValueChangedEventArgs<bool> e)
        {
            if (e.NewValue)
            {
                Markers ??= new MarkerService();
                if (WvW.IsLoading || !GameService.Gw2Mumble.CurrentMap.Type.IsWvWMatch()) {
                    return;
                }

                var obj = await WvW.GetObjectives(GameService.Gw2Mumble.CurrentMap.Id);

                if (Markers == null) {
                    return;
                }

                Markers.ReloadMarkers(obj);
                Markers.Toggle();
                return;
            }
            Markers?.Dispose();
            Markers = null;
        }

        /// <inheritdoc />
        protected override void Unload()
        {
            ColorIntensitySetting.SettingChanged                    -= OnColorIntensitySettingChanged;
            ToggleMapKeySetting.Value.Activated                     -= OnToggleKeyActivated;
            ToggleMarkersKeySetting.Value.Activated                 -= OnToggleMarkersKeyActivated;
            EnableMarkersSetting.SettingChanged                     -= OnEnableMarkersSettingChanged;
            ToggleMapKeySetting.Value.Enabled                       =  false;
            ToggleMarkersKeySetting.Value.Enabled                   =  false;
            GameService.GameIntegration.Gw2Instance.IsInGameChanged -= OnIsInGameChanged;
            GameService.Gw2Mumble.CurrentMap.MapChanged             -= OnMapChanged;
            GameService.Gw2Mumble.UI.IsMapOpenChanged               -= OnIsMapOpenChanged;

            Maps?.Dispose();
            _moduleIcon?.Dispose();
            CornerTex?.Dispose();

            Markers?.Dispose();
            WvW?.Dispose();
            Resources?.Dispose();

            // All static members must be manually unset
            ModuleInstance = null;
        }

    }

}
