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

namespace Nekres.Mistwar.Entities
{
    public enum WvwObjectiveTier
    {
        Supported,
        Secured,
        Reinforced,
        Fortified
    }

    public class WvwObjectiveEntity : IDisposable
    {
        private static Texture2D _textureFortified         = MistwarModule.ModuleInstance.ContentsManager.GetTexture("1324351.png");
        private static Texture2D _textureReinforced        = MistwarModule.ModuleInstance.ContentsManager.GetTexture("1324350.png");
        private static Texture2D _textureSecured           = MistwarModule.ModuleInstance.ContentsManager.GetTexture("1324349.png");
        private static Texture2D _textureClaimed           = MistwarModule.ModuleInstance.ContentsManager.GetTexture("1304078.png");
        private static Texture2D _textureClaimedRepGuild   = MistwarModule.ModuleInstance.ContentsManager.GetTexture("1304077.png");
        private static Texture2D _textureBuff              = MistwarModule.ModuleInstance.ContentsManager.GetTexture("righteous_indignation.png");
        private static Texture2D _textureRuinEstate        = MistwarModule.ModuleInstance.ContentsManager.GetTexture("ruin_estate.png");
        private static Texture2D _textureRuinTemple        = MistwarModule.ModuleInstance.ContentsManager.GetTexture("ruin_temple.png");
        private static Texture2D _textureRuinOverlook      = MistwarModule.ModuleInstance.ContentsManager.GetTexture("ruin_overlook.png");
        private static Texture2D _textureRuinHollow        = MistwarModule.ModuleInstance.ContentsManager.GetTexture("ruin_hollow.png");
        private static Texture2D _textureRuinAscent        = MistwarModule.ModuleInstance.ContentsManager.GetTexture("ruin_ascent.png");
        private static Texture2D _textureRuinOther         = MistwarModule.ModuleInstance.ContentsManager.GetTexture("ruin_other.png");
        private static Texture2D _textureWayPoint          = MistwarModule.ModuleInstance.ContentsManager.GetTexture("157353.png");
        private static Texture2D _textureWayPointHover     = MistwarModule.ModuleInstance.ContentsManager.GetTexture("60970.png");
        private static Texture2D _textureWayPointContested = MistwarModule.ModuleInstance.ContentsManager.GetTexture("102349.png");

        private static IReadOnlyDictionary<string, Texture2D> _ruinsTexLookUp = new Dictionary<string, Texture2D>
        {
            {"95-62", _textureRuinTemple}, // Temple of the Fallen
            {"96-62", _textureRuinTemple}, // Temple of Lost Prayers
            {"1099-121", _textureRuinOther}, // Darra's Maze

            {"96-66", _textureRuinAscent}, // Carver's Ascent
            {"95-66", _textureRuinAscent}, // Patrick's Ascent
            {"1099-118", _textureRuinOther}, // Higgins's Ascent

            {"96-63", _textureRuinHollow}, // Battle's Hollow
            {"95-63", _textureRuinHollow}, // Norfolk's Hollow
            {"1099-119", _textureRuinOther}, // Bearce's Dwelling

            {"96-65", _textureRuinOverlook}, // Orchard Overlook
            {"95-65", _textureRuinOverlook}, // Cohen's Overlook
            {"1099-120", _textureRuinOther}, // Zak's Overlook

            {"96-64", _textureRuinEstate}, // Bauer's Estate
            {"95-64", _textureRuinEstate}, // Gertzz's Estate 
            {"1099-122", _textureRuinOther} // Tilly's Encampment
        };

        private static Color _colorRed = new Color(213, 71, 67);
        private static Color _colorGreen = new Color(73, 190, 111);
        private static Color _colorBlue = new Color(100, 164, 228);
        private static Color _colorNeutral = Color.DimGray;
        public static Color BrightGold = new Color(223, 194, 149, 255);

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
                if (_lastFlipped != value) {
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
                if (_owner != value) {
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
                if (_claimedBy != value) {
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
                // ReSharper disable once PossibleUnintendedReferenceComparison
                if (_guildUpgrades?.SequenceEqual(value) ?? _guildUpgrades != value) {
                    _guildUpgrades = value;
                    this.LastModified = DateTime.UtcNow;
                }
            }
        }

        /// <summary>
        /// Color of the objective's owning team.
        /// </summary>
        public Color TeamColor => GetColor();

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
        public Texture2D UpgradeTexture => GetUpgradeTierTexture();

        /// <summary>
        /// Texture indicating that a guild has claimed the objective.
        /// </summary>
        public Texture2D ClaimedTexture => ClaimedBy.Equals(MistwarModule.ModuleInstance.WvwService.CurrentGuild) ? _textureClaimedRepGuild : _textureClaimed;

        /// <summary>
        /// Texture of the protection buff.
        /// </summary>
        public Texture2D BuffTexture => _textureBuff;

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
            _textureFortified         ??= MistwarModule.ModuleInstance.ContentsManager.GetTexture("1324351.png");
            _textureReinforced        ??= MistwarModule.ModuleInstance.ContentsManager.GetTexture("1324350.png");
            _textureSecured           ??= MistwarModule.ModuleInstance.ContentsManager.GetTexture("1324349.png");
            _textureClaimed           ??= MistwarModule.ModuleInstance.ContentsManager.GetTexture("1304078.png");
            _textureClaimedRepGuild   ??= MistwarModule.ModuleInstance.ContentsManager.GetTexture("1304077.png");
            _textureBuff              ??= MistwarModule.ModuleInstance.ContentsManager.GetTexture("righteous_indignation.png");
            _textureRuinEstate        ??= MistwarModule.ModuleInstance.ContentsManager.GetTexture("ruin_estate.png");
            _textureRuinTemple        ??= MistwarModule.ModuleInstance.ContentsManager.GetTexture("ruin_temple.png");
            _textureRuinOverlook      ??= MistwarModule.ModuleInstance.ContentsManager.GetTexture("ruin_overlook.png");
            _textureRuinHollow        ??= MistwarModule.ModuleInstance.ContentsManager.GetTexture("ruin_hollow.png");
            _textureRuinAscent        ??= MistwarModule.ModuleInstance.ContentsManager.GetTexture("ruin_ascent.png");
            _textureRuinOther         ??= MistwarModule.ModuleInstance.ContentsManager.GetTexture("ruin_other.png");
            _textureWayPoint          ??= MistwarModule.ModuleInstance.ContentsManager.GetTexture("157353.png");
            _textureWayPointHover     ??= MistwarModule.ModuleInstance.ContentsManager.GetTexture("60970.png");
            _textureWayPointContested ??= MistwarModule.ModuleInstance.ContentsManager.GetTexture("102349.png");

            _ruinsTexLookUp ??= new Dictionary<string, Texture2D>
            {
                {"95-62", _textureRuinTemple}, // Temple of the Fallen
                {"96-62", _textureRuinTemple}, // Temple of Lost Prayers
                {"1099-121", _textureRuinOther}, // Darra's Maze

                {"96-66", _textureRuinAscent}, // Carver's Ascent
                {"95-66", _textureRuinAscent}, // Patrick's Ascent
                {"1099-118", _textureRuinOther}, // Higgins's Ascent

                {"96-63", _textureRuinHollow}, // Battle's Hollow
                {"95-63", _textureRuinHollow}, // Norfolk's Hollow
                {"1099-119", _textureRuinOther}, // Bearce's Dwelling

                {"96-65", _textureRuinOverlook}, // Orchard Overlook
                {"95-65", _textureRuinOverlook}, // Cohen's Overlook
                {"1099-120", _textureRuinOther}, // Zak's Overlook

                {"96-64", _textureRuinEstate}, // Bauer's Estate
                {"95-64", _textureRuinEstate}, // Gertzz's Estate 
                {"1099-122", _textureRuinOther} // Tilly's Encampment
            };

            _internalObjective = objective;

            var internalSector = map.Sectors[objective.SectorId];

            _opacity      = 1f;
            Icon          = GetTexture(objective.Type);
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

        public Texture2D GetWayPointIcon(bool hover)
        {
            return this.Owner == MistwarModule.ModuleInstance.WvwService.CurrentTeam ? 
                hover ? _textureWayPointHover : _textureWayPoint : _textureWayPointContested;
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

        private Texture2D GetTexture(WvwObjectiveType type)
        {
            switch (type)
            {
                case WvwObjectiveType.Camp:
                case WvwObjectiveType.Castle:
                case WvwObjectiveType.Keep:
                case WvwObjectiveType.Tower:
                    return MistwarModule.ModuleInstance.ContentsManager.GetTexture($"{type}.png");
                case WvwObjectiveType.Ruins:
                    return _ruinsTexLookUp.TryGetValue(this.Id, out var tex) ? tex : null; 
                default: return null;
            }
        }

        private Color GetColor()
        {
            return Owner switch
            {
                WvwOwner.Red => _colorRed,
                WvwOwner.Blue => _colorBlue,
                WvwOwner.Green => _colorGreen,
                _ => _colorNeutral
            };
        }

        private Texture2D GetUpgradeTierTexture()
        {
            return GetTier() switch
            {
                WvwObjectiveTier.Fortified => _textureFortified,
                WvwObjectiveTier.Reinforced => _textureReinforced,
                WvwObjectiveTier.Secured => _textureSecured,
                _ => ContentService.Textures.TransparentPixel
            };
        }

        private float GetOpacity()
        {
            _opacity = MathUtil.Clamp(MathUtil.Map((GameService.Gw2Mumble.PlayerCamera.Position - this.WorldPosition).Length(), MistwarModule.ModuleInstance.MaxViewDistanceSetting.Value * 50, _opacity, 0f, 1f), 0f, 1f);
            return _opacity;
        }

        public void Dispose() {
            this.Icon?.Dispose();
            _textureFortified?.Dispose();
            _textureFortified = null;
            _textureReinforced?.Dispose();
            _textureReinforced = null;
            _textureSecured?.Dispose();
            _textureSecured = null;
            _textureClaimed?.Dispose();
            _textureClaimed = null;
            _textureClaimedRepGuild?.Dispose();
            _textureClaimedRepGuild = null;
            _textureBuff?.Dispose();
            _textureBuff = null;
            _textureRuinEstate?.Dispose();
            _textureRuinEstate = null;
            _textureRuinTemple?.Dispose();
            _textureRuinTemple = null;
            _textureRuinOverlook?.Dispose();
            _textureRuinOverlook = null;
            _textureRuinHollow?.Dispose();
            _textureRuinHollow = null;
            _textureRuinAscent?.Dispose();
            _textureRuinAscent = null;
            _textureRuinOther?.Dispose();
            _textureRuinOther = null;
            _textureWayPoint?.Dispose();
            _textureWayPoint = null;
            _textureWayPointHover?.Dispose();
            _textureWayPointHover = null;
            _textureWayPointContested?.Dispose();
            _textureWayPointContested = null;
            _ruinsTexLookUp           = null;
        }
    }
}
