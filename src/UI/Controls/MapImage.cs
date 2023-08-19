using Blish_HUD;
using Blish_HUD.Content;
using Blish_HUD.Controls;
using Blish_HUD.Extended;
using Blish_HUD.Input;
using Gw2Sharp.Models;
using Gw2Sharp.WebApi.V2.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended;
using Nekres.Mistwar.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using Color = Microsoft.Xna.Framework.Color;
using Point = Microsoft.Xna.Framework.Point;
using Rectangle = Microsoft.Xna.Framework.Rectangle;
namespace Nekres.Mistwar.UI.Controls {
    internal class MapImage : Container
    {
        private IEnumerable<WvwObjectiveEntity> _wvwObjectives;
        public IEnumerable<WvwObjectiveEntity> WvwObjectives
        {
            get => _wvwObjectives;
            set => SetProperty(ref _wvwObjectives, value);
        }

        private ContinentFloorRegionMap _map;
        public ContinentFloorRegionMap Map
        {
            get => _map;
            set
            {
                if (!SetProperty(ref _map, value)) {
                    return;
                }

                _wayPointBounds?.Clear();
            }
        }

        protected AsyncTexture2D _texture;
        public AsyncTexture2D Texture
        {
            get => _texture;
            private init => SetProperty(ref _texture, value);
        }

        private SpriteEffects _spriteEffects;
        public SpriteEffects SpriteEffects
        {
            get => _spriteEffects;
            set => SetProperty(ref _spriteEffects, value);
        }

        private Rectangle? _sourceRectangle;
        public Rectangle SourceRectangle
        {
            get => _sourceRectangle ?? _texture.Texture.Bounds;
            set => SetProperty(ref _sourceRectangle, value);
        }

        private Color _tint = Color.White;
        public Color Tint
        {
            get => _tint;
            set => SetProperty(ref _tint, value);
        }

        public float TextureOpacity { get; private set; }

        private Effect _grayscaleEffect;

        private SpriteBatchParameters _grayscaleSpriteBatchParams;

        private Texture2D _playerArrow;

        private Dictionary<int, Rectangle> _wayPointBounds;

        private Texture2D _warnTriangle;

        public MapImage()
        {
            _wayPointBounds        = new Dictionary<int, Rectangle>();
            _playerArrow           = MistwarModule.ModuleInstance.ContentsManager.GetTexture("156081.png");
            _warnTriangle          = GameService.Content.GetTexture("common/1444522");
            _spriteBatchParameters = new SpriteBatchParameters();
            _grayscaleEffect       = MistwarModule.ModuleInstance.ContentsManager.GetEffect<Effect>(@"effects\grayscale.mgfx");
            _grayscaleSpriteBatchParams = new SpriteBatchParameters
            {
                Effect = _grayscaleEffect
            };
            this.Texture =  new AsyncTexture2D();
            this.SetOpacity(1);
        }

        internal void SetOpacity(float opacity)
        {
            TextureOpacity = opacity;
            _grayscaleEffect.Parameters["Opacity"].SetValue(opacity);
        }

        public void SetColorIntensity(float colorIntensity)
        {
            _grayscaleEffect.Parameters["Intensity"].SetValue(MathHelper.Clamp(colorIntensity, 0, 1));
        }

        protected override async void OnClick(MouseEventArgs e)
        {
            base.OnClick(e);

            if (Map == null) {
                return;
            }

            foreach (var bound in _wayPointBounds.ToList())
            {
                if (!bound.Value.Contains(this.RelativeMousePosition)) {
                    continue;
                }

                var wp = this.Map.PointsOfInterest.Values.FirstOrDefault(x => x.Id == bound.Key);
                if (wp == null) {
                    break;
                }

                GameService.Content.PlaySoundEffectByName("button-click");
                if (PInvoke.IsLControlPressed())
                {
                    ChatUtil.Send(wp.ChatLink, MistwarModule.ModuleInstance.ChatMessageKeySetting.Value);
                    break;
                }
                if (PInvoke.IsLShiftPressed())
                {
                    ChatUtil.Insert(wp.ChatLink, MistwarModule.ModuleInstance.ChatMessageKeySetting.Value);
                    break;
                }
                if (await ClipboardUtil.WindowsClipboardService.SetTextAsync(wp.ChatLink))
                {
                    ScreenNotification.ShowNotification("Waypoint copied to clipboard!");
                }
                break;
            }
        }

        protected override void OnMouseMoved(MouseEventArgs e)
        {
            base.OnMouseMoved(e);

            if (Map == null) {
                return;
            }

            var wps = _wayPointBounds.ToList();
            foreach (var bound in wps)
            {
                if (!bound.Value.Contains(this.RelativeMousePosition)) {
                    continue;
                }

                var wp = this.Map.PointsOfInterest.Values.FirstOrDefault(x => x.Id == bound.Key);
                if (wp?.Name == null) {
                    break;
                }

                var wpName = wp.Name;
                if (wp.Name.StartsWith(" ")) {
                    var obj = this.WvwObjectives.FirstOrDefault(x => x.WayPoints.Any(y => y.Id == wp.Id));
                    if (obj == null) {
                        break;
                    }

                    wpName = MistwarModule.ModuleInstance.WvwService.GetWorldName(obj.Owner) + wpName;
                }
                this.BasicTooltipText = wpName;
            }
            if (wps.All(x => !x.Value.Contains(this.RelativeMousePosition)))
            {
                this.BasicTooltipText = string.Empty;
            }
        }

        protected override void DisposeControl()
        {
            _texture?.Dispose();
            _grayscaleEffect?.Dispose();
            _playerArrow?.Dispose();
            base.DisposeControl();
        }

        private static Point ComputeAspectRatioSize(Point parentSize, Point childSize) {
            double parentRatio = parentSize.X / (double)parentSize.Y;
            double childRatio  = childSize.X  / (double)childSize.Y;

            double scaleFactor = Math.Min(parentRatio > childRatio 
                                              ? parentSize.Y / (double)childSize.Y 
                                              : parentSize.X / (double)childSize.X, 1);

            int newWidth  = (int)Math.Round(childSize.X  * scaleFactor);
            int newHeight = (int)Math.Round(childSize.Y * scaleFactor);

            return new Point(newWidth, newHeight);
        }

        private static Point ComputeSize(Point parentSize, Point childSize) {
            double scaleFactorWidth  = parentSize.X / (double)childSize.X;
            double scaleFactorHeight = parentSize.Y / (double)childSize.Y;

            int newWidth  = (int)Math.Round(childSize.X * scaleFactorWidth);
            int newHeight = (int)Math.Round(childSize.Y * scaleFactorHeight);

            return new Point(newWidth, newHeight);
        }

        public override void PaintBeforeChildren(SpriteBatch spriteBatch, Rectangle bounds)
        {
            if (!_texture.HasTexture || WvwObjectives == null || Map == null) {
                return;
            }

            this.SetOpacity(this.Parent.Opacity);

            spriteBatch.End();
            spriteBatch.Begin(_grayscaleSpriteBatchParams);

            var bgSize   = ComputeAspectRatioSize(bounds.Size, _texture.Bounds.Size);
            var bgOffset = new Point((bounds.Width - bgSize.X) / 2, (bounds.Height - bgSize.Y) / 2);
            // Draw the texture
            spriteBatch.DrawOnCtrl(this,
                _texture,
                new Rectangle(bgOffset, bgSize),
                _texture.Bounds,
                _tint,
                0f,
                Vector2.Zero,
                _spriteEffects);

            spriteBatch.End();
            spriteBatch.Begin(_spriteBatchParameters); // Exclude everything below from greyscale effect.

            var widthRatio  = bounds.Width  / (float)_texture.Bounds.Width;
            var heightRatio = bounds.Height / (float)_texture.Bounds.Height;
            var ratio       = widthRatio > heightRatio ? heightRatio : widthRatio;

            if (MistwarModule.ModuleInstance.DrawSectorsSetting.Value)
            {
                // Draw sector boundaries
                // These need to be iterated separately to be drawn before any other content to avoid overlapping.
                foreach (var objectiveEntity in WvwObjectives.OrderBy(x => x.Owner == MistwarModule.ModuleInstance.WvwService.CurrentTeam))
                {
                    var teamColor = objectiveEntity.TeamColor.GetColorBlindType(MistwarModule.ModuleInstance.ColorTypeSetting.Value, (int)(TextureOpacity * 255));

                    var sectorBounds = objectiveEntity.Bounds.Select(p => {
                        var r = new Point((int)(ratio * p.X), (int)(ratio * p.Y)).ToBounds(this.AbsoluteBounds);
                        return new Vector2(r.X, r.Y);
                    }).ToArray();

                    spriteBatch.DrawPolygon(new Vector2(bgOffset.X, bgOffset.Y), sectorBounds, teamColor, 4);
                }
            }

            foreach (var objectiveEntity in WvwObjectives)
            {
                if (objectiveEntity.Icon != null) {

                    if (!MistwarModule.ModuleInstance.DrawRuinMapSetting.Value && objectiveEntity.Type == WvwObjectiveType.Ruins) {
                        continue;
                    }

                    // Calculate draw bounds.
                    var dest = new Rectangle(bgOffset.X + (int)(ratio * objectiveEntity.Center.X), bgOffset.Y + (int)(ratio       * objectiveEntity.Center.Y), 
                                             (int)(ratio              * objectiveEntity.Icon.Bounds.Size.X) * 2, (int)(ratio * objectiveEntity.Icon.Bounds.Size.Y) * 2);

                    // Draw the objective.
                    spriteBatch.DrawWvwObjectiveOnCtrl(this, objectiveEntity, dest, 1f, 0.75f, MistwarModule.ModuleInstance.DrawObjectiveNamesSetting.Value);
                }

                // Draw waypoints belonging to this territory.
                foreach (var wp in objectiveEntity.WayPoints)
                {
                    if (GameUtil.IsEmergencyWayPoint(wp))
                    {
                        if (!MistwarModule.ModuleInstance.DrawEmergencyWayPointsSetting.Value) {
                            continue;
                        }

                        if (objectiveEntity.Owner != MistwarModule.ModuleInstance.WvwService.CurrentTeam) {
                            continue; // Skip opposing team's emergency waypoints.
                        }

                        if (!objectiveEntity.HasEmergencyWaypoint())
                        {
                            _wayPointBounds.Remove(wp.Id);
                            continue;
                        }
                    } 
                    else if (!objectiveEntity.HasRegularWaypoint())
                    {
                        _wayPointBounds.Remove(wp.Id);
                        continue;
                    }

                    var wpDest = new Rectangle(bgOffset.X + (int)(ratio * wp.Coord.X) - (int)(ratio * 64) / 4,
                                               bgOffset.Y + (int)(ratio * wp.Coord.Y) - (int)(ratio * 64) / 4, 
                                               (int)(ratio * 64) * 2, (int)(ratio * 64) * 2);

                    var tex = objectiveEntity.GetWayPointIcon(wpDest.Contains(this.RelativeMousePosition));

                    if (!_wayPointBounds.ContainsKey(wp.Id))
                    {
                        _wayPointBounds.Add(wp.Id, wpDest);
                    }

                    spriteBatch.DrawOnCtrl(this, tex, wpDest);
                }
            }

            if (this.Map != null)
            {
                // Draw player position indicator
                var v = GameService.Gw2Mumble.PlayerCharacter.Position * 39.37008f; // world meters to inches.
                var worldInchesMap = new Vector2(
                    (float)(this.Map.ContinentRect.TopLeft.X + (v.X - this.Map.MapRect.TopLeft.X) / this.Map.MapRect.Width  * this.Map.ContinentRect.Width), 
                    (float)(this.Map.ContinentRect.TopLeft.Y - (v.Y - this.Map.MapRect.TopLeft.Y) / this.Map.MapRect.Height * this.Map.ContinentRect.Height)); // clamp to map bounds
                var mapCenter = GameService.Gw2Mumble.UI.MapCenter.ToXnaVector2();                                                                             // might be (0,0) in competitive..
                var pos = Vector2.Transform(worldInchesMap - mapCenter, Matrix.CreateRotationZ(0f));
                var fit = MapUtil.Refit(new Coordinates2(pos.X, pos.Y), this.Map.ContinentRect.TopLeft); // refit to our 256x256 tiled map

                var size  = ComputeAspectRatioSize(bounds.Size, _playerArrow.Bounds.Size);

                var tDest = new Rectangle(bgOffset.X + (int)(ratio * fit.X), bgOffset.Y + (int)(ratio * fit.Y), size.X, size.Y);
                var rot   = Math.Atan2(GameService.Gw2Mumble.PlayerCharacter.Forward.X, GameService.Gw2Mumble.PlayerCharacter.Forward.Y) * 3.6f / Math.PI; // rotate the arrow in the forward direction

                spriteBatch.DrawOnCtrl(this, _playerArrow, new Rectangle(tDest.X + tDest.Width / 4, tDest.Y + tDest.Height / 4, tDest.Width, tDest.Height), _playerArrow.Bounds, Color.White, (float) rot, new Vector2(_playerArrow.Width / 2f, _playerArrow.Height / 2f));
            }

            if (MistwarModule.ModuleInstance.WvwService.IsLoading)
            {
                var spinnerBnds = new Rectangle(0, 0, 70, 70);
                LoadingSpinnerUtil.DrawLoadingSpinner(this, spriteBatch, spinnerBnds);
                var size = Content.DefaultFont32.MeasureString(MistwarModule.ModuleInstance.WvwService.LoadingMessage);
                var dest = new Rectangle((int)(spinnerBnds.X + spinnerBnds.Width / 2 - size.Width / 2), spinnerBnds.Bottom, (int)size.Width, (int)size.Height);
                spriteBatch.DrawStringOnCtrl(this, MistwarModule.ModuleInstance.WvwService.LoadingMessage, Content.DefaultFont16, dest, Color.White, false, true, 1, HorizontalAlignment.Center);
            }

            var lastChange = DateTime.UtcNow.Subtract(MistwarModule.ModuleInstance.WvwService.LastChange);
            if (lastChange.TotalSeconds > 120) {
                var warnBounds = new Rectangle(bounds.Width - 300, 0, 300, 32);
                spriteBatch.DrawOnCtrl(this, _warnTriangle, new Rectangle(warnBounds.Left - 32, warnBounds.Top, 32, 32));
                spriteBatch.DrawStringOnCtrl(this, $"Last Change: {lastChange.Hours} hours {lastChange.Minutes} minutes ago.", Content.DefaultFont14, warnBounds, Color.White, false, true);
            }
        }
    }
}
