using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Pirate.Shared._JustDecor.MartialArts.Events;

public sealed partial class LegendaryCQCTakedownPerformedEvent : EntityEventArgs { }

public sealed partial class LegendaryCQCDisarmPerformedEvent : EntityEventArgs { }

public sealed partial class LegendaryCQCThrowPerformedEvent : EntityEventArgs { }

public sealed partial class LegendaryCQCChokePerformedEvent : EntityEventArgs { }

public sealed partial class LegendaryCQCChainPerformedEvent : EntityEventArgs { }

public sealed partial class LegendaryCQCCounterPerformedEvent : EntityEventArgs { }

public sealed partial class LegendaryCQCInterrogationPerformedEvent : EntityEventArgs { }

public sealed partial class LegendaryCQCStealthTakedownPerformedEvent : EntityEventArgs { }

public sealed partial class LegendaryCQCRushPerformedEvent : EntityEventArgs { }

public sealed partial class LegendaryCQCFinisherPerformedEvent : EntityEventArgs { }

[Serializable, NetSerializable]
public sealed partial class LegendaryCQCInterrogationDoAfterEvent : DoAfterEvent
{
    public override DoAfterEvent Clone() => this;
}

[Serializable, NetSerializable]
public sealed partial class LegendaryCQCChokeDoAfterEvent : DoAfterEvent
{
    public override DoAfterEvent Clone() => this;
}
