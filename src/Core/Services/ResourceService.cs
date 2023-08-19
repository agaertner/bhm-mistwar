using Blish_HUD;
using Blish_HUD.Extended;
using Gw2Sharp.WebApi.V2.Models;
using Microsoft.Xna.Framework.Graphics;
using Nekres.Mistwar.Entities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Color = Microsoft.Xna.Framework.Color;

namespace Nekres.Mistwar.Core.Services {
    public class ResourceService : IDisposable {

        private Texture2D _textureFortified         = MistwarModule.ModuleInstance.ContentsManager.GetTexture("1324351.png");
        private Texture2D _textureReinforced        = MistwarModule.ModuleInstance.ContentsManager.GetTexture("1324350.png");
        private Texture2D _textureSecured           = MistwarModule.ModuleInstance.ContentsManager.GetTexture("1324349.png");
        private Texture2D _textureClaimed           = MistwarModule.ModuleInstance.ContentsManager.GetTexture("1304078.png");
        private Texture2D _textureClaimedRepGuild   = MistwarModule.ModuleInstance.ContentsManager.GetTexture("1304077.png");
        private Texture2D _textureBuff              = MistwarModule.ModuleInstance.ContentsManager.GetTexture("righteous_indignation.png");
        private Texture2D _textureRuinEstate        = MistwarModule.ModuleInstance.ContentsManager.GetTexture("ruin_estate.png");
        private Texture2D _textureRuinTemple        = MistwarModule.ModuleInstance.ContentsManager.GetTexture("ruin_temple.png");
        private Texture2D _textureRuinOverlook      = MistwarModule.ModuleInstance.ContentsManager.GetTexture("ruin_overlook.png");
        private Texture2D _textureRuinHollow        = MistwarModule.ModuleInstance.ContentsManager.GetTexture("ruin_hollow.png");
        private Texture2D _textureRuinAscent        = MistwarModule.ModuleInstance.ContentsManager.GetTexture("ruin_ascent.png");
        private Texture2D _textureRuinOther         = MistwarModule.ModuleInstance.ContentsManager.GetTexture("ruin_other.png");
        private Texture2D _textureWayPoint          = MistwarModule.ModuleInstance.ContentsManager.GetTexture("157353.png");
        private Texture2D _textureWayPointHover     = MistwarModule.ModuleInstance.ContentsManager.GetTexture("60970.png");
        private Texture2D _textureWayPointContested = MistwarModule.ModuleInstance.ContentsManager.GetTexture("102349.png");

        private IReadOnlyDictionary<string, Texture2D> _ruinsTexLookUp;

        public readonly Color ColorRed     = new (213, 71,  67);
        public readonly Color ColorGreen   = new (73,  190, 111);
        public readonly Color ColorBlue    = new (100, 164, 228);
        public readonly Color ColorNeutral = Color.DimGray;
        public readonly Color BrightGold   = new (223, 194, 149, 255);

        private AsyncCache<(Map, int), ContinentFloorRegionMap> _mapExpandedCache = new(p => RequestMapExpanded(p.Item1, p.Item2));
        private AsyncCache<int, Map>                            _mapCache         = new(RequestMap);

        public ResourceService() {
            _ruinsTexLookUp = new Dictionary<string, Texture2D>
            {
                {"95-62", _textureRuinTemple},   // Temple of the Fallen
                {"96-62", _textureRuinTemple},   // Temple of Lost Prayers
                {"1099-121", _textureRuinOther}, // Darra's Maze

                {"96-66", _textureRuinAscent},   // Carver's Ascent
                {"95-66", _textureRuinAscent},   // Patrick's Ascent
                {"1099-118", _textureRuinOther}, // Higgins's Ascent

                {"96-63", _textureRuinHollow},   // Battle's Hollow
                {"95-63", _textureRuinHollow},   // Norfolk's Hollow
                {"1099-119", _textureRuinOther}, // Bearce's Dwelling

                {"96-65", _textureRuinOverlook}, // Orchard Overlook
                {"95-65", _textureRuinOverlook}, // Cohen's Overlook
                {"1099-120", _textureRuinOther}, // Zak's Overlook

                {"96-64", _textureRuinEstate},  // Bauer's Estate
                {"95-64", _textureRuinEstate},  // Gertzz's Estate 
                {"1099-122", _textureRuinOther} // Tilly's Encampment
            };
        }

        public Texture2D GetClaimedTexture(Guid claimedBy) {
            return claimedBy.Equals(MistwarModule.ModuleInstance.WvW.CurrentGuild) ? _textureClaimedRepGuild : _textureClaimed;
        }

        public Texture2D GetBuffTexture() {
            return _textureBuff;
        }

        public Texture2D GetWayPointTexture(bool isHovered, bool isContested) {
            if (isContested) {
                return _textureWayPointContested;
            }

            if (isHovered) {
                return _textureWayPointHover;
            }

            return _textureWayPoint;
        }

        public Texture2D GetObjectiveTexture(WvwObjectiveType type, string id) {
            return type switch {
                WvwObjectiveType.Camp   => MistwarModule.ModuleInstance.ContentsManager.GetTexture($"{type}.png"),
                WvwObjectiveType.Castle => MistwarModule.ModuleInstance.ContentsManager.GetTexture($"{type}.png"),
                WvwObjectiveType.Keep   => MistwarModule.ModuleInstance.ContentsManager.GetTexture($"{type}.png"),
                WvwObjectiveType.Tower  => MistwarModule.ModuleInstance.ContentsManager.GetTexture($"{type}.png"),
                WvwObjectiveType.Ruins  => _ruinsTexLookUp.TryGetValue(id, out var tex) ? tex : ContentService.Textures.TransparentPixel,
                _                       => ContentService.Textures.TransparentPixel
            };
        }

        public Color GetTeamColor(WvwOwner owner) {
            return owner switch {
                WvwOwner.Red   => ColorRed,
                WvwOwner.Blue  => ColorBlue,
                WvwOwner.Green => ColorGreen,
                _              => ColorNeutral
            };
        }

        public Texture2D GetUpgradeTierTexture(WvwObjectiveTier tier) {
            return tier switch {
                WvwObjectiveTier.Fortified  => _textureFortified,
                WvwObjectiveTier.Reinforced => _textureReinforced,
                WvwObjectiveTier.Secured    => _textureSecured,
                _                           => ContentService.Textures.TransparentPixel
            };
        }

        public async Task<Map> GetMap(int mapId) {
            return await _mapCache.GetItem(mapId);
        }

        public async Task<ContinentFloorRegionMap> GetMapExpanded(Map map, int floorId) {
            return await _mapExpandedCache.GetItem((map, floorId));
        }

        private static async Task<Map> RequestMap(int mapId) {
            return await TaskUtil.RetryAsync(() => GameService.Gw2WebApi.AnonymousConnection.Client.V2.Maps.GetAsync(mapId));
        }

        private static async Task<ContinentFloorRegionMap> RequestMapExpanded(Map map, int floorId) {
            return await TaskUtil.RetryAsync(() => GameService.Gw2WebApi.AnonymousConnection.Client.V2.Continents[map.ContinentId].Floors[floorId].Regions[map.RegionId].Maps.GetAsync(map.Id));
        }

        public void Dispose() {
            _textureFortified?.Dispose();
            _textureReinforced?.Dispose();
            _textureSecured?.Dispose();
            _textureClaimed?.Dispose();
            _textureClaimedRepGuild?.Dispose();
            _textureBuff?.Dispose();
            _textureRuinEstate?.Dispose();
            _textureRuinTemple?.Dispose();
            _textureRuinOverlook?.Dispose();
            _textureRuinHollow?.Dispose();
            _textureRuinAscent?.Dispose();
            _textureRuinOther?.Dispose();
            _textureWayPoint?.Dispose();
            _textureWayPointHover?.Dispose();
            _textureWayPointContested?.Dispose();
        }
    }
}
