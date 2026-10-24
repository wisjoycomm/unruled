using System;
using System.IO;
using Unruled.Core.Persistence;
using Unruled.Core.Simulation;
using Unruled.View.UI;
using UnityEngine;
using EntityId = Unruled.Core.Ecs.EntityId;

namespace Unruled.View
{
    /// <summary>
    /// Hosts the game through an <see cref="IGameLink"/>. Offline (<see cref="LocalLink"/>): continues the saved game
    /// if there is one, else builds the world from a seed with <see cref="NewGameSetup"/>, advances the clock one tick
    /// per real second (scaled by <see cref="_ticksPerSecond"/>), lets the AI rivals think after each tick and saves
    /// every <see cref="_autosaveTicks"/> and when the app pauses or quits. Online (<see cref="ServerLink"/>, from the
    /// game menu's Join server): the server's world for this player, mirrored; nothing is saved or paused here. Either
    /// way the HUD and the board are bound to the link's world and read it the same way.
    /// </summary>
    public sealed class GameSession : MonoBehaviour
    {
        private const string SaveFileName = "unruled.save";
        private const string AddressKey = "unruled.server.address";

        [SerializeField] private MainScreenView _screen;
        [SerializeField] private MapBoardRenderer _board;
        [SerializeField] private int _worldSeed = 1234;
        [SerializeField] private int _playerId = 1;
        [SerializeField, Range(0f, 60f)] private float _ticksPerSecond = 1f;
        [SerializeField] private bool _continueSavedGame = true;
        [SerializeField, Min(60)] private int _autosaveTicks = 3_600;
        [SerializeField] private string _defaultServer = "localhost:5090";

        private IGameLink _link;
        private ServerLink _joining; // a server link waiting for its first snapshot
        private long _savedTick;

        private static string SavePath => Path.Combine(Application.persistentDataPath, SaveFileName);

        public SimulationWorld Simulation => _link?.World;

        private void Start()
        {
            if (_screen == null)
                throw new MissingReferenceException("GameSession needs its MainScreenView.");
            if (_board == null)
                throw new MissingReferenceException("GameSession needs its MapBoardRenderer.");
            PlayOffline();
            _screen.Menu.BindSession(SetPaused, SaveFromMenu, LoadFromMenu, StartNewGame);
            _screen.Menu.BindOnline(() => _link != null && _link.IsOnline, LinkStatus,
                PlayerPrefs.GetString(AddressKey, _defaultServer), JoinServer, PlayOffline);
        }

        private void PlayOffline()
        {
            CloseLinks();
            var balance = DefaultGameBalance.Create();
            if (_continueSavedGame && File.Exists(SavePath) && TryLoad(balance, out var saved))
                Host(new LocalLink(saved, _playerId, _ticksPerSecond));
            else
                StartNewGame();
        }

        private void StartNewGame()
        {
            CloseLinks();
            var sim = new SimulationWorld(DefaultGameBalance.Create(), unchecked((ulong)_worldSeed));
            NewGameSetup.Create(sim, _playerId);
            Host(new LocalLink(sim, _playerId, _ticksPerSecond));
        }

        // The offline game keeps running until the server's first snapshot arrives; a refusal leaves it in place.
        private string JoinServer(string address, string token)
        {
            if (_link is LocalLink)
                Save();
            _joining?.Close();
            PlayerPrefs.SetString(AddressKey, address);
            _joining = new ServerLink(address, token);
            return "JOINING " + address.ToUpperInvariant();
        }

        private string LinkStatus() => _joining != null ? _joining.Status : _link != null ? _link.Status : string.Empty;

        private void Host(IGameLink link)
        {
            _link = link;
            _savedTick = link.World.Clock.CurrentTick;
            _screen.Bind(link.World, link.PlayerId);
            _screen.SelectFleet(FirstFleet(link.World, link.PlayerId));
            _board.Bind(link.World, link.PlayerId, _screen);
        }

        private void CloseLinks()
        {
            _joining?.Close();
            _joining = null;
            _link?.Close();
            _link = null;
        }

        private void SetPaused(bool paused)
        {
            if (_link is LocalLink local)
                local.Paused = paused;
        }

        private string SaveFromMenu()
        {
            if (_link == null || _link.IsOnline)
                return "THE SERVER KEEPS THIS WORLD";
            Save();
            return "SAVED · " + UiFormat.Clock(_link.World.Clock.CurrentTick);
        }

        private string LoadFromMenu()
        {
            if (!File.Exists(SavePath))
                return "NO SAVED GAME YET";
            if (!TryLoad(DefaultGameBalance.Create(), out var loaded))
                return "THE SAVED GAME COULD NOT BE READ · KEPT ASIDE";
            CloseLinks();
            Host(new LocalLink(loaded, _playerId, _ticksPerSecond) { Paused = true });
            return "LOADED · " + UiFormat.Clock(loaded.Clock.CurrentTick);
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
                Save();
        }

        private void OnApplicationQuit()
        {
            Save();
            CloseLinks();
        }

        private void OnDestroy() => CloseLinks();

        // A save this build cannot read is moved aside (not deleted) and reported, and a new game starts.
        private static bool TryLoad(GameBalanceConfig balance, out SimulationWorld sim)
        {
            try
            {
                sim = SaveGame.Load(balance, File.ReadAllBytes(SavePath));
                return true;
            }
            catch (FormatException e)
            {
                string aside = SavePath + ".unreadable";
                File.Copy(SavePath, aside, overwrite: true);
                Debug.LogError($"Saved game {SavePath} could not be read ({e.Message}); kept as {aside}, starting a new game.");
                sim = null;
                return false;
            }
        }

        // Offline only. Writes beside the save, then swaps it in, so a crash while writing keeps the previous save intact.
        private void Save()
        {
            if (!(_link is LocalLink local))
                return;
            string temp = SavePath + ".tmp";
            File.WriteAllBytes(temp, SaveGame.Write(local.World));
            if (File.Exists(SavePath))
                File.Delete(SavePath);
            File.Move(temp, SavePath);
            _savedTick = local.World.Clock.CurrentTick;
        }

        private static EntityId FirstFleet(SimulationWorld sim, int playerId)
        {
            for (int i = 0; i < sim.Fleets.Count; i++)
                if (sim.Fleets.At(i).OwnerPlayerId == playerId)
                    return sim.Fleets.EntityAt(i);
            return default;
        }

        private void Update()
        {
            if (_joining != null)
            {
                _joining.Update(Time.deltaTime);
                if (_joining.World != null)
                {
                    var online = _joining;
                    _joining = null;
                    _link?.Close();
                    Host(online);
                    _screen.Menu.ShowStatus(online.Status);
                }
                else if (_joining.Failed)
                {
                    _screen.Menu.ShowStatus(_joining.Status);
                    _joining = null;
                }
            }
            if (_link == null)
                return;
            _link.Update(Time.deltaTime);
            if (_link is ServerLink server && server.Failed)
            {
                string status = server.Status;
                PlayOffline();
                _screen.Menu.ShowStatus(status + " · BACK TO THE OFFLINE GAME");
                return;
            }
            if (_link is LocalLink && _link.World.Clock.CurrentTick - _savedTick >= _autosaveTicks)
                Save();
        }
    }
}
