using System;
using Robust.Shared.Serialization;

namespace Content.Shared._Pirate.Ghost;

[Serializable, NetSerializable]
public sealed class GhostRespawnLobbyRequest : EntityEventArgs
{
}

[Serializable, NetSerializable]
public sealed class GhostRespawnStatusEvent : EntityEventArgs
{
    public bool CanRespawn;
    public TimeSpan RemainingTime;

    public GhostRespawnStatusEvent()
    {
    }

    public GhostRespawnStatusEvent(bool canRespawn, TimeSpan remainingTime)
    {
        CanRespawn = canRespawn;
        RemainingTime = remainingTime;
    }
}
