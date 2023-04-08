using Blish_HUD;
using Blish_HUD.Controls;
using Blish_HUD.Extended;
using Blish_HUD.Modules.Managers;
using Gw2Sharp.WebApi.V2.Models;
using Nekres.Mistwar.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Nekres.Mistwar.Services {
    internal class WvwService
    {
        public Guid     CurrentGuild   { get; private set; }
        public WvwOwner CurrentTeam    { get; private set; }
        public DateTime LastChange     { get; private set; }
        public string   LoadingMessage { get; private set; }
        public bool     IsLoading      => !string.IsNullOrEmpty(LoadingMessage);

        private Gw2ApiManager _api;

        private AsyncCache<int, List<WvwObjectiveEntity>> _wvwObjectiveCache;

        private IEnumerable<World> _worlds;

        private WvwMatchTeamList _teams;

        private DateTime _prevApiRequestTime;

        private enum WvwMap {
            DesertBorderlands = 1099,
            AlpineBorderlandsBlue = 96,
            AlpineBorderlandsGreen = 95,
            EternalBattlegrounds = 38
        }

        public WvwService(Gw2ApiManager api)
        {
            _prevApiRequestTime = DateTime.MinValue.ToUniversalTime();
            _api = api;
            _wvwObjectiveCache = new AsyncCache<int, List<WvwObjectiveEntity>>(RequestObjectives);

            this.LastChange = DateTime.MinValue.ToUniversalTime();
        }

        public async Task LoadAsync() {
            _worlds = await TaskUtil.RetryAsync(() => _api.Gw2ApiClient.V2.Worlds.AllAsync());
        }

        public async Task Update()
        {
            if (!GameService.Gw2Mumble.CurrentMap.Type.IsWvWMatch() || DateTime.UtcNow.Subtract(_prevApiRequestTime).TotalSeconds < 15) {
                return;
            }
            _prevApiRequestTime = DateTime.UtcNow;

            this.LoadingMessage = "Refreshing";

            this.CurrentGuild   = await GetRepresentedGuild();

            var worldId = await GetWorldId();
            if (worldId >= 0)
            {
                var mapIds = GetWvWMapIds();
                var taskList = new List<Task>();
                foreach (var id in mapIds) {
                    var t = new Task<Task>(() => UpdateObjectives(worldId, id));
                    taskList.Add(t);
                    t.Start();
                }

                if (taskList.Any()) {
                    await Task.WhenAll(taskList.ToArray());
                }
            }

            // Warn if no change for more than a minute, every 2 minutes.
            var mins = Math.Round(this.LastChange.Subtract(DateTime.UtcNow).TotalMinutes);
            if (mins > 0 && mins % 2 == 0) {
                ScreenNotification.ShowNotification($"({MistwarModule.ModuleInstance.Name}) No changes in the last {mins} minutes.", ScreenNotification.NotificationType.Warning);
            }

            LoadingMessage = string.Empty;
        }

        public async Task<Guid> GetRepresentedGuild()
        {
            if (!_api.HasPermissions(new []{TokenPermission.Account, TokenPermission.Characters})) {
                return Guid.Empty;
            }

            var character = await TaskUtil.TryAsync(() => _api.Gw2ApiClient.V2.Characters[GameService.Gw2Mumble.PlayerCharacter.Name].GetAsync());
            return character?.Guild ?? Guid.Empty;
        }

        public string GetWorldName(WvwOwner owner)
        {
            IReadOnlyList<int> team;
            switch (owner)
            {
                case WvwOwner.Red:
                    team = _teams.Red;
                    break;
                case WvwOwner.Blue:
                    team = _teams.Blue;
                    break;
                case WvwOwner.Green:
                    team = _teams.Green;
                    break;
                default: return string.Empty;
            }
            return _worlds.OrderByDescending(x => x.Population.Value).FirstOrDefault(y => team.Contains(y.Id))?.Name ?? string.Empty;
        }

        public async Task<int> GetWorldId()
        {
            if (!_api.HasPermission(TokenPermission.Account)) {
                return -1;
            }

            var account = await TaskUtil.TryAsync(() => _api.Gw2ApiClient.V2.Account.GetAsync());
            return account?.World ?? -1;
        }

        public int[] GetWvWMapIds()
        {
            /*var matches = await TaskUtil.RetryAsync(() => _api.Gw2ApiClient.V2.Wvw.Matches.World(worldId).GetAsync());
            return matches?.Maps.Select(m => m.Id).ToArray() ?? Array.Empty<int>();*/
            return Enum.GetValues(typeof(WvwMap)).Cast<int>().ToArray(); // Saving us a request.
        }

        public async Task<List<WvwObjectiveEntity>> GetObjectives(int mapId)
        {
            return await _wvwObjectiveCache.GetItem(mapId);
        }

        private async Task UpdateObjectives(int worldId, int mapId)
        {
            var objEntities = await GetObjectives(mapId);

            var match = await TaskUtil.TryAsync(() => _api.Gw2ApiClient.V2.Wvw.Matches.World(worldId).GetAsync());
            
            if (match == null) {
                return;
            }

            _teams = match.AllWorlds;
            // Fetch the users team
            this.CurrentTeam =
                _teams.Blue.Contains(worldId) ? WvwOwner.Blue :
                _teams.Red.Contains(worldId) ? WvwOwner.Red :
                _teams.Green.Contains(worldId) ? WvwOwner.Green : WvwOwner.Unknown;

            var objectives = match.Maps.FirstOrDefault(x => x.Id == mapId)?.Objectives;
            if (objectives.IsNullOrEmpty()) {
                return;
            }

            foreach (var objEntity in objEntities)
            {
                var obj = objectives?.FirstOrDefault(v => v.Id.Equals(objEntity.Id, StringComparison.InvariantCultureIgnoreCase));
                if (obj == null) {
                    continue;
                }

                objEntity.LastFlipped = obj.LastFlipped ?? DateTime.MinValue;
                objEntity.Owner = obj.Owner.Value;
                objEntity.ClaimedBy = obj.ClaimedBy ?? Guid.Empty;
                objEntity.GuildUpgrades = obj.GuildUpgrades;
                objEntity.YaksDelivered = obj.YaksDelivered ?? 0;

                if (objEntity.LastModified > this.LastChange) {
                    this.LastChange = objEntity.LastModified;
                }
            }
        }

        private async Task<List<WvwObjectiveEntity>> RequestObjectives(int mapId) {
            var wvwObjectives = await TaskUtil.RetryAsync(() => _api.Gw2ApiClient.V2.Wvw.Objectives.AllAsync());

            if (wvwObjectives.IsNullOrEmpty()) {
                return Enumerable.Empty<WvwObjectiveEntity>().ToList();
            }

            var map           = await MapUtil.GetMap(mapId);
            var mapExpanded   = await MapUtil.GetMapExpanded(map, map.DefaultFloor);

            if (mapExpanded == null) {
                return Enumerable.Empty<WvwObjectiveEntity>().ToList();
            }

            var newObjectives = new List<WvwObjectiveEntity>();
            foreach (var sector in mapExpanded.Sectors.Values) {
                var obj = wvwObjectives.FirstOrDefault(x => x.SectorId == sector.Id);
                if (obj == null) {
                    continue;
                }

                var o = new WvwObjectiveEntity(obj, mapExpanded);
                newObjectives.Add(o);
            }
            return newObjectives;
        }
    }
}
