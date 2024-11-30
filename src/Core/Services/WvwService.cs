using Blish_HUD;
using Blish_HUD.Controls;
using Blish_HUD.Extended;
using Blish_HUD.Gw2WebApi;
using Flurl;
using Flurl.Http;
using Gw2Sharp.WebApi.V2.Models;
using Nekres.Mistwar.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Nekres.Mistwar.Core.Services {
    internal class WvwService : IDisposable
    {
        public Guid     CurrentGuild   { get; private set; }
        public WvwOwner CurrentTeam    { get; private set; }
        public DateTime LastChange     { get; private set; }
        public string   RefreshMessage { get; private set; }

        public bool IsRefreshing => !string.IsNullOrEmpty(this.RefreshMessage);
        public bool IsLoading    => !_worlds?.Any() ?? true;

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

        public WvwService()
        {
            _prevApiRequestTime = DateTime.MinValue.ToUniversalTime();
            _wvwObjectiveCache  = new AsyncCache<int, List<WvwObjectiveEntity>>(RequestObjectives);

            this.LastChange = DateTime.MinValue.ToUniversalTime();
        }

        public async Task LoadAsync() {
            _worlds = await TaskUtil.RetryAsync(() => MistwarModule.ModuleInstance.Gw2ApiManager.Gw2ApiClient.V2.Worlds.AllAsync());
        }

        public async Task Update()
        {
            // Don't update if we haven't loaded yet.
            if (IsLoading) {
                return;
            }

            // Don't update if we're already updating.
            if (IsRefreshing) {
                return;
            }

            // Only update if we're in WvW.
            if (!GameService.Gw2Mumble.CurrentMap.Type.IsWvWMatch()) {
                return;
            }

            // Only update every 15 seconds.
            if (DateTime.UtcNow.Subtract(_prevApiRequestTime).TotalSeconds < 15) {
                return;
            }

            _prevApiRequestTime = DateTime.UtcNow;

            this.RefreshMessage = "Refreshing";

            this.CurrentGuild   = await GetRepresentedGuild();

            var teamId = await GetTeamId();
            if (teamId >= 0)
            {
                var mapIds = GetWvWMapIds();
                var taskList = new List<Task>();
                foreach (var id in mapIds) {
                    var t = new Task<Task>(() => UpdateObjectives(teamId, id));
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

            this.RefreshMessage = string.Empty;
        }

        public async Task<Guid> GetRepresentedGuild()
        {
            if (!MistwarModule.ModuleInstance.Gw2ApiManager.IsAuthorized(false, TokenPermission.Characters)) {
                return Guid.Empty;
            }

            var character = await TaskUtil.TryAsync(() => MistwarModule.ModuleInstance.Gw2ApiManager.Gw2ApiClient.V2.Characters[GameService.Gw2Mumble.PlayerCharacter.Name].GetAsync());
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
            if (!MistwarModule.ModuleInstance.Gw2ApiManager.IsAuthorized()) {
                return -1;
            }

            var url = MistwarModule.ModuleInstance.Gw2ApiManager.Gw2ApiClient.V2.Account.BaseUrl;
            var account = await TaskUtil.TryAsync(() => MistwarModule.ModuleInstance.Gw2ApiManager.Gw2ApiClient.V2.Account.GetAsync());
            return account?.World ?? -1;
        }

        public async Task<int> GetTeamId() {
            if (!MistwarModule.ModuleInstance.Gw2ApiManager.IsAuthorized()) {
                return -1;
            }
            var requestUrl = "https://api.guildwars2.com/v2/account".SetQueryParams(new {
                v = "2024-07-20T01:00:00.000Z", // Schema to get the wvw object.
                access_token = GetSubToken()
            });
            var json = await TaskUtil.TryAsync(() => requestUrl.GetStringAsync());
            try {
                return JsonDocument.Parse(json).RootElement.GetProperty("wvw").GetProperty("team_id").GetInt32();
            } catch (Exception) {
                return -1; // Account with no wvw data; no assigned team etc. Just ignore.
            }
        }

        public string GetSubToken() {
            var managedConnection = (ManagedConnection)MistwarModule.ModuleInstance.Gw2ApiManager.GetPrivateField("_connection").GetValue(MistwarModule.ModuleInstance.Gw2ApiManager);
            return managedConnection.Connection.AccessToken;
        }

        public int[] GetWvWMapIds()
        {
            return Enum.GetValues(typeof(WvwMap)).Cast<int>().ToArray(); // Saving us a request.
        }

        public async Task<List<WvwObjectiveEntity>> GetObjectives(int mapId)
        {
            return await _wvwObjectiveCache.GetItem(mapId);
        }

        private async Task UpdateObjectives(int teamId, int mapId)
        {
            var objEntities = await GetObjectives(mapId);

            var match = await TaskUtil.TryAsync(() => MistwarModule.ModuleInstance.Gw2ApiManager.Gw2ApiClient.V2.Wvw.Matches.World(teamId).GetAsync());
            
            if (match == null) {
                return;
            }

            _teams = match.AllWorlds;
            // Fetch the users team
            this.CurrentTeam =
                _teams.Blue.Contains(teamId) ? WvwOwner.Blue :
                _teams.Red.Contains(teamId) ? WvwOwner.Red :
                _teams.Green.Contains(teamId) ? WvwOwner.Green : WvwOwner.Unknown;

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
            var wvwObjectives = await TaskUtil.RetryAsync(() => MistwarModule.ModuleInstance.Gw2ApiManager.Gw2ApiClient.V2.Wvw.Objectives.AllAsync());

            if (wvwObjectives.IsNullOrEmpty()) {
                return Enumerable.Empty<WvwObjectiveEntity>().ToList();
            }

            var map         = await MistwarModule.ModuleInstance.Resources.GetMap(mapId);
            var mapExpanded = await MistwarModule.ModuleInstance.Resources.GetMapExpanded(map, map.DefaultFloor);

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

        public void Dispose() {
            _wvwObjectiveCache?.Clear();
        }

    }
}
