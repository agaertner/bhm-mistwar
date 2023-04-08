using Blish_HUD;
using Blish_HUD.Controls;
using Blish_HUD.Extended;
using Gw2Sharp.WebApi.V2.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nekres.Mistwar.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using Rectangle = Microsoft.Xna.Framework.Rectangle;

namespace Nekres.Mistwar.UI.Controls {
    internal class MarkerBillboard : Control
    {
        public IEnumerable<WvwObjectiveEntity> WvwObjectives;

        public MarkerBillboard()
        {
            _spriteBatchParameters = new SpriteBatchParameters();
        }

        public void Toggle(bool forceHide = false, float tDuration = 0.1f)
        {
            if (forceHide || !GameUtil.IsAvailable() || !GameService.Gw2Mumble.CurrentMap.Type.IsWvWMatch() || _visible)
            {
                _visible = false;
                this.Visible = false;
                GameService.Animation.Tweener.Tween(this, new { Opacity = 0.0f }, tDuration).OnComplete(this.Hide);
                return;
            }
            _visible = true;
            this.Visible = true;
            GameService.Animation.Tweener.Tween(this, new { Opacity = 1.0f }, 0.35f);
        }

        protected override void Paint(SpriteBatch spriteBatch, Rectangle bounds)
        {
            if (!GameUtil.IsAvailable() || !GameService.Gw2Mumble.CurrentMap.Type.IsWvWMatch() || !this.Visible || WvwObjectives == null) return;

            this.Size = Parent.AbsoluteBounds.Size; // Always keep at screen size.

            var objectives = WvwObjectives.Where(x => x.Icon != null);
            if (MistwarModule.ModuleInstance.HideAlliedMarkersSetting.Value)
            {
                objectives = objectives.Where(x => x.Owner != MistwarModule.ModuleInstance.WvwService.CurrentTeam);
            }

            // Order by distance.
            var distanceSort = objectives.OrderByDescending(x => x.GetDistance()).ToList(); // Draw further markers first so that closer ones are drawn ontop.
            if (MistwarModule.ModuleInstance.HideInCombatSetting.Value && GameService.Gw2Mumble.PlayerCharacter.IsInCombat)
            {
                distanceSort = distanceSort.IsNullOrEmpty() ? distanceSort : distanceSort.Take(1).ToList(); // Show only the closest objective during combat.
            }

            foreach (var objectiveEntity in distanceSort)
            {
                if (objectiveEntity.Icon == null) continue;

                if (!MistwarModule.ModuleInstance.DrawRuinMarkersSetting.Value && objectiveEntity.Type == WvwObjectiveType.Ruins) continue;

                var dir = GameService.Gw2Mumble.PlayerCamera.Position - objectiveEntity.WorldPosition;
                var angle = MathUtil.RadToDeg * GameService.Gw2Mumble.PlayerCamera.Forward.Angle(dir);

                // Project onto screen space.
                var trs = Matrix.CreateScale(1) * Matrix.CreateTranslation(objectiveEntity.WorldPosition);
                var transformed = Vector3.Transform(trs.Translation, GameService.Gw2Mumble.PlayerCamera.WorldViewProjection).Flatten();

                // Calculate draw bounds.
                var width  = objectiveEntity.Icon.Width;
                var height = objectiveEntity.Icon.Height;
                var dest   = new Rectangle((int)transformed.X, (int)transformed.Y, width, height);

                // Calculate screen bounds.
                var screenBounds = new Rectangle(0, 0, bounds.Width, bounds.Height);

                // Clamp to screen bounds.
                var left  = screenBounds.Left  + dest.Width / 2;
                var right = screenBounds.Right - dest.Width;
                var top      = screenBounds.Top + dest.Height / 2;
                var bottom   = screenBounds.Bottom - dest.Height;

                if (Math.Abs(angle) < 90) // Objective is behind player.
                {
                    if (!MistwarModule.ModuleInstance.MarkerStickySetting.Value)
                    {
                        continue;
                    }

                    // Stick to bottom
                    dest.Y = bottom; //MathUtil.RoundToClosest(dest.Y, top,  bottom);
                }

                // Duplicate check because camera FoV does not correspond to SpriteScreen size.
                // Ie. Out of screen bounds does not equal behind player.
                if (MistwarModule.ModuleInstance.MarkerStickySetting.Value) 
                {
                    dest.X = (int)MathUtil.Clamp(dest.X, left, right);
                    dest.Y = (int)MathUtil.Clamp(dest.Y, top,  bottom);
                }

                // Calculate distance and scale based on distance.
                var minScale = 0.2f;
                var maxScale = MathUtil.Clamp(MistwarModule.ModuleInstance.MarkerScaleSetting.Value / 100, minScale, 1f);
                var scale    = maxScale;
                var distance = objectiveEntity.GetDistance();
                if (!MistwarModule.ModuleInstance.MarkerFixedSizeSetting.Value && distance > 400) 
                {
                    var distScale = (float)Math.Sqrt(Math.Abs(1 - distance / 1500));
                    scale = MathUtil.Clamp(distScale, minScale, maxScale);
                }

                // Draw the objective.
                spriteBatch.DrawWvwObjectiveOnCtrl(this, objectiveEntity, dest, objectiveEntity.Opacity, scale, true, MistwarModule.ModuleInstance.DrawDistanceSetting.Value);
            }
        }

        protected override CaptureType CapturesInput()
        {
            return CaptureType.None;
        }
    }
}
