using Blish_HUD;
using Gw2Sharp.Models;
using Gw2Sharp.WebApi.V2.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using Color = Microsoft.Xna.Framework.Color;
using Point = System.Drawing.Point;

namespace Nekres.Mistwar.Entities {
    public enum WvwObjectiveTier
    {
        Supported,
        Secured,
        Reinforced,
        Fortified
    }

    public class WvwObjectiveEntity
    {
        /// <summary>
        /// Time since the objective has last been modified.
        /// </summary>
        public DateTime LastModified { get; private set; }

        private DateTime _lastFlipped = DateTime.MinValue;
        /// <summary>
        /// The timestamp of when the last time a change of ownership has occurred.
        /// </summary>
        public DateTime LastFlipped {
            get => _lastFlipped;
            set {
                if (!Equals(value,_lastFlipped)) {
                    _lastFlipped = value;
                    this.LastModified = DateTime.UtcNow;
                }
            }
        }

        private WvwOwner _owner = WvwOwner.Neutral;
        /// <summary>
        /// The objective owner.
        /// </summary>
        public WvwOwner Owner {
            get => _owner;
            set {
                if (!Equals(value, _owner)) {
                    _owner = value;
                    this.LastModified = DateTime.UtcNow;
                }
            }
        }

        private Guid _claimedBy = Guid.Empty;
        /// <summary>
        /// Id of the guild that has claimed the objective.
        /// </summary>
        public Guid ClaimedBy { 
            get => _claimedBy;
            set {
                if (!Equals(value,  _claimedBy)) {
                    _claimedBy = value;
                    this.LastModified = DateTime.UtcNow;
                }
            }
        }

        private int _yaksDelivered;
        /// <summary>
        /// Number of Dolyaks delivered to the objective.
        /// </summary>
        public int YaksDelivered { 
            get => _yaksDelivered;
            set {
                if (_yaksDelivered != value) {
                    _yaksDelivered = value;
                    this.LastModified = DateTime.UtcNow;
                }
            }
        }

        private IReadOnlyList<int> _guildUpgrades;
        /// <summary>
        /// List of guild upgrade ids.
        /// </summary>
        public IReadOnlyList<int> GuildUpgrades {
            get => _guildUpgrades;
            set {
                if (value != null && 
                    _guildUpgrades != null && 
                    !value.OrderBy(x => x).SequenceEqual(_guildUpgrades.OrderBy(x => x))) {

                    _guildUpgrades = value;
                    this.LastModified = DateTime.UtcNow;
                    return;
                }

                if (!Equals(value, _guildUpgrades)) {
                    _guildUpgrades    = value;
                }
            }
        }

        /// <summary>
        /// Color of the objective's owning team.
        /// </summary>
        public Color TeamColor => MistwarModule.ModuleInstance.Resources.GetTeamColor(this.Owner);

        /// <summary>
        /// Icon of the objective's type.
        /// </summary>
        public Texture2D Icon { get; }

        /// <summary>
        /// Duration of the protection buff applied when a change of ownership occurs.
        /// </summary>
        public TimeSpan BuffDuration { get; }

        /// <summary>
        /// Texture reflecting the upgrade tier of the objective.
        /// </summary>
        public Texture2D UpgradeTexture => MistwarModule.ModuleInstance.Resources.GetUpgradeTierTexture(GetTier());

        /// <summary>
        /// Texture indicating that a guild has claimed the objective.
        /// </summary>
        public Texture2D ClaimedTexture => MistwarModule.ModuleInstance.Resources.GetClaimedTexture(ClaimedBy);

        /// <summary>
        /// Texture of the protection buff.
        /// </summary>
        public Texture2D BuffTexture => MistwarModule.ModuleInstance.Resources.GetBuffTexture();

        /// <summary>
        /// Sector bounds this objective belongs to.
        /// </summary>
        public IEnumerable<Point> Bounds { get; }

        /// <summary>
        /// Center coordinates of the objective on the world map.
        /// </summary>
        public Point Center { get; }

        /// <summary>
        /// Position of the objective in the game world.
        /// </summary>
        public Vector3 WorldPosition { get; }

        private float _opacity;
        /// <summary>
        /// Opacity of icon and text when drawn.
        /// </summary>
        public float Opacity => GetOpacity();

        public           List<ContinentFloorRegionMapPoi> WayPoints { get; }
        private readonly WvwObjective                     _internalObjective;
        public           string                           Id   => _internalObjective.Id;
        public           string                           Name => _internalObjective.Name;
        public           WvwObjectiveType                 Type => _internalObjective.Type;

        public int MapId { get; }

        public WvwObjectiveEntity(WvwObjective objective, ContinentFloorRegionMap map)
        {
            _internalObjective = objective;

            var internalSector = map.Sectors[objective.SectorId];

            _opacity      = 1f;
            Icon          = MistwarModule.ModuleInstance.Resources.GetObjectiveTexture(objective.Type, objective.Id);
            MapId         = map.Id;
            Bounds        = internalSector.Bounds.Select(coord => MapUtil.Refit(coord, map.ContinentRect.TopLeft));
            Center        = MapUtil.Refit(internalSector.Coord, map.ContinentRect.TopLeft);
            BuffDuration  = new TimeSpan(0, 5, 0);
            WorldPosition = CalculateWorldPosition(map);

            WayPoints = map.PointsOfInterest.Values.Where(x => x.Type == PoiType.Waypoint).Where(y =>
                PolygonUtil.InBounds(new Vector2((float) y.Coord.X, (float) y.Coord.Y), internalSector.Bounds.Select(z => new Vector2((float)z.X, (float)z.Y)).ToList())).ToList();

            foreach (var wp in WayPoints)
            {
                var fit = MapUtil.Refit(wp.Coord, map.ContinentRect.TopLeft);
                wp.Coord = new Coordinates2(fit.X, fit.Y);
            }
        }

        public bool IsOwned()
        {
            return this.Owner is WvwOwner.Red or WvwOwner.Green or WvwOwner.Red;
        }

        public bool IsClaimed()
        {
            return !ClaimedBy.Equals(Guid.Empty);
        }

        public bool HasGuildUpgrades()
        {
            return !GuildUpgrades.IsNullOrEmpty();
        }

        public bool HasUpgraded()
        {
            return YaksDelivered >= 20;
        }

        public bool HasEmergencyWaypoint()
        {
            return HasGuildUpgrades() && GuildUpgrades.Contains(178);
        }

        public bool HasRegularWaypoint()
        {
            return IsSpawn() || GetTier() == WvwObjectiveTier.Fortified && this.Type is WvwObjectiveType.Keep or WvwObjectiveType.Castle;
        }

        public bool IsSpawn()
        {
            return this.Type == WvwObjectiveType.Spawn;
        }

        public WvwObjectiveTier GetTier()
        {
            return YaksDelivered >= 140 ? WvwObjectiveTier.Fortified : 
                   YaksDelivered >= 60 ? WvwObjectiveTier.Reinforced : 
                   YaksDelivered >= 20 ? WvwObjectiveTier.Secured : WvwObjectiveTier.Supported;
        }

        public bool HasBuff(out TimeSpan remainingTime)
        {
            var buffTime = DateTime.UtcNow.Subtract(LastFlipped);
            remainingTime = BuffDuration.Subtract(buffTime);
            return remainingTime.Ticks > 0;
        }

        public float GetDistance()
        {
            return WorldPosition.Distance(GameService.Gw2Mumble.PlayerCamera.Position);
        }

        private Vector3 CalculateWorldPosition(ContinentFloorRegionMap map)
        {
            var v = _internalObjective.Coord;
            if (_internalObjective.Id.Equals("38-15") && Math.Abs(v.X - 11766.3) < 1 && Math.Abs(v.Y - 14793.5) < 1 && Math.Abs(v.Z - (-2133.39)) < 1) 
            {
                v = new Coordinates3(11462.5f, 15600 - 2650 / 24, _internalObjective.Coord.Z - 500); // Langor fix-hack
            }
            var r = map.ContinentRect;
            var offset = new Vector3(
                (float)((r.TopLeft.X + r.BottomRight.X) / 2.0f),
                0,
                (float)((r.TopLeft.Y + r.BottomRight.Y) / 2.0f));
            return new Vector3(
                WorldUtil.GameToWorldCoord((float)((v.X - offset.X) * 24)),
                WorldUtil.GameToWorldCoord((float)(-(v.Y - offset.Z) * 24)),
                WorldUtil.GameToWorldCoord((float)-v.Z));
        }

        private float GetOpacity()
        {
            _opacity = MathUtil.Clamp(MathUtil.Map((GameService.Gw2Mumble.PlayerCamera.Position - this.WorldPosition).Length(), MistwarModule.ModuleInstance.MaxViewDistanceSetting.Value * 50, _opacity, 0f, 1f), 0f, 1f);
            return _opacity;
        }
    }
}
