using Content.Shared.Radio;

namespace Content.Shared._Pirate.Radio;

/// <summary>
/// Raised on the transmitting radio entity once a radio send attempt has passed initial validation.
/// Used for client-targeted transmit feedback sounds without coupling that logic into the core radio systems.
/// </summary>
[ByRefEvent]
public readonly record struct PirateRadioSentEvent(
    EntityUid MessageSource,
    RadioChannelPrototype Channel,
    EntityUid RadioSource,
    int Frequency);

[ByRefEvent]
public readonly record struct PirateRadioReceivedEvent(
    EntityUid MessageSource,
    RadioChannelPrototype Channel,
    EntityUid RadioSource,
    EntityUid RadioReceiver,
    int Frequency,
    bool Important);
