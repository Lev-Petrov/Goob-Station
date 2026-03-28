using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Content.Server.GameTicking;
using Content.Server.Ghost.Roles.Components;
using Content.Shared._Pirate.Ghost;
using Content.Shared.Bed.Cryostorage;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Mind.Components;
using Content.Goobstation.Shared.MisandryBox.Thunderdome;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Pirate.Ghost;

public sealed class PirateGhostRespawnSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _player = default!;

    private readonly Dictionary<NetUserId, GhostRespawnState> _states = new();
    private readonly Dictionary<NetUserId, PendingGhostTransition> _pendingTransitions = new();
    private readonly Dictionary<NetUserId, int> _suppressedCrewCycleSeeds = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
        SubscribeLocalEvent<PlayerJoinedLobbyEvent>(OnPlayerJoinedLobby);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
        SubscribeLocalEvent<MindContainerComponent, MindRemovedMessage>(OnMindRemoved);
        SubscribeLocalEvent<MindContainerComponent, MindAddedMessage>(OnMindAdded);
        SubscribeLocalEvent<PlayerAttachedEvent>(OnPlayerAttached);

        SubscribeNetworkEvent<GhostRespawnLobbyRequest>(OnGhostRespawnLobbyRequest);
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent ev)
    {
        EnsureCrewCycle(ev.Player.UserId, ev.Mob);
    }

    private void OnPlayerJoinedLobby(PlayerJoinedLobbyEvent ev)
    {
        ClearState(ev.PlayerSession.UserId);
        _suppressedCrewCycleSeeds.Remove(ev.PlayerSession.UserId);
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        _states.Clear();
        _pendingTransitions.Clear();
        _suppressedCrewCycleSeeds.Clear();
    }

    private void OnMindRemoved(Entity<MindContainerComponent> ent, ref MindRemovedMessage args)
    {
        if (args.Mind.Comp.UserId is not { } userId)
            return;

        var wasInCryo = HasComp<CryostorageContainedComponent>(ent.Owner);

        if (!TryGetState(userId, out var state) || !state.HasCrewCycle || state.TimerArmed)
            return;

        _pendingTransitions[userId] = new PendingGhostTransition
        {
            Immediate = wasInCryo
        };
    }

    private void OnMindAdded(Entity<MindContainerComponent> ent, ref MindAddedMessage args)
    {
        if (args.Mind.Comp.UserId is not { } userId)
            return;

        if (HasComp<GhostComponent>(ent.Owner))
        {
            ArmTimerIfNeeded(userId);
            SendStatusToUser(userId);
            return;
        }

        _pendingTransitions.Remove(userId);
    }

    private void OnPlayerAttached(PlayerAttachedEvent args)
    {
        if (HasComp<GhostComponent>(args.Entity))
        {
            ArmTimerIfNeeded(args.Player.UserId);
            SendStatus(args.Player);
            return;
        }

        EnsureCrewCycle(args.Player.UserId, args.Entity);
    }

    private void OnGhostRespawnLobbyRequest(GhostRespawnLobbyRequest ev, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;

        if (session.AttachedEntity is not { Valid: true } attached || !HasComp<GhostComponent>(attached))
        {
            Log.Warning($"User {session.Name} sent an invalid {nameof(GhostRespawnLobbyRequest)}");
            return;
        }

        ArmTimerIfNeeded(session.UserId);
        var status = GetStatus(session.UserId);
        if (!status.CanRespawn)
        {
            SendStatus(session);
            return;
        }

        if (!_gameTicker.LobbyEnabled)
            _suppressedCrewCycleSeeds[session.UserId] = 2;

        ClearState(session.UserId);
        _gameTicker.Respawn(session);
    }

    public PirateGhostRespawnDebugState GetDebugState(NetUserId userId)
    {
        if (!TryGetState(userId, out var state))
            return default;

        return new PirateGhostRespawnDebugState(state.HasCrewCycle, state.TimerArmed, state.RespawnAvailableAt);
    }

    public PirateGhostRespawnAvailability GetDebugAvailability(NetUserId userId)
    {
        var status = GetStatus(userId);
        return new PirateGhostRespawnAvailability(status.CanRespawn, status.RemainingTime);
    }

    private bool TryGetState(NetUserId userId, [NotNullWhen(true)] out GhostRespawnState state)
    {
        if (_states.TryGetValue(userId, out var found))
        {
            state = found;
            return true;
        }

        state = null!;
        return false;
    }

    private void EnsureCrewCycle(NetUserId userId, EntityUid entity)
    {
        if (!ShouldSeedCrewCycle(entity))
        {
            _pendingTransitions.Remove(userId);
            return;
        }

        if (TryConsumeSuppressedCrewCycleSeed(userId) || _states.ContainsKey(userId))
        {
            _pendingTransitions.Remove(userId);
            return;
        }

        _states[userId] = new GhostRespawnState
        {
            HasCrewCycle = true,
            TimerArmed = false,
            RespawnAvailableAt = TimeSpan.Zero
        };

        _pendingTransitions.Remove(userId);
    }

    private bool TryConsumeSuppressedCrewCycleSeed(NetUserId userId)
    {
        if (!_suppressedCrewCycleSeeds.TryGetValue(userId, out var remaining) || remaining <= 0)
            return false;

        if (remaining == 1)
            _suppressedCrewCycleSeeds.Remove(userId);
        else
            _suppressedCrewCycleSeeds[userId] = remaining - 1;

        return true;
    }

    private bool ShouldSeedCrewCycle(EntityUid entity)
    {
        if (HasComp<GhostComponent>(entity))
            return false;

        if (HasComp<GhostRoleComponent>(entity))
            return false;

        if (HasComp<ThunderdomePlayerComponent>(entity))
            return false;

        return true;
    }

    private void ArmTimerIfNeeded(NetUserId userId)
    {
        if (!TryGetState(userId, out var state) || !state.HasCrewCycle)
        {
            _pendingTransitions.Remove(userId);
            return;
        }

        if (state.TimerArmed)
        {
            _pendingTransitions.Remove(userId);
            return;
        }

        var availableAt = _timing.CurTime + GetRespawnDelay();
        if (_pendingTransitions.Remove(userId, out var pending) && pending is { Immediate: true })
            availableAt = _timing.CurTime;

        state.TimerArmed = true;
        state.RespawnAvailableAt = availableAt;
    }

    private GhostRespawnStatus GetStatus(NetUserId userId)
    {
        if (!TryGetState(userId, out var state) || !state.HasCrewCycle)
            return new GhostRespawnStatus(true, TimeSpan.Zero);

        if (!state.TimerArmed)
            return new GhostRespawnStatus(false, GetRespawnDelay());

        var remainingTime = state.RespawnAvailableAt - _timing.CurTime;
        if (remainingTime <= TimeSpan.Zero)
            return new GhostRespawnStatus(true, TimeSpan.Zero);

        return new GhostRespawnStatus(false, remainingTime);
    }

    private TimeSpan GetRespawnDelay()
    {
        var delay = _cfg.GetCVar(CCVars.GhostRespawnDelay);
        return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
    }

    private void SendStatusToUser(NetUserId userId)
    {
        if (!_player.TryGetSessionById(userId, out var session))
            return;

        SendStatus(session);
    }

    private void SendStatus(ICommonSession session)
    {
        var status = GetStatus(session.UserId);
        RaiseNetworkEvent(new GhostRespawnStatusEvent(status.CanRespawn, status.RemainingTime), session.Channel);
    }

    private void ClearState(NetUserId userId)
    {
        _states.Remove(userId);
        _pendingTransitions.Remove(userId);
    }

    private sealed class GhostRespawnState
    {
        public bool HasCrewCycle;
        public bool TimerArmed;
        public TimeSpan RespawnAvailableAt;
    }

    private sealed class PendingGhostTransition
    {
        public bool Immediate;
    }

    private readonly record struct GhostRespawnStatus(bool CanRespawn, TimeSpan RemainingTime);
}

public readonly record struct PirateGhostRespawnDebugState(bool HasCrewCycle, bool TimerArmed, TimeSpan RespawnAvailableAt);

public readonly record struct PirateGhostRespawnAvailability(bool CanRespawn, TimeSpan RemainingTime);
