using System.Numerics;
using Content.Server._Pirate.Cargo.Systems;
using Content.Server._Pirate.CartridgeLoader.Cartridges;
using Content.Shared._Pirate.CartridgeLoader.Cartridges;
using Robust.Shared.Audio;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._Pirate.Cargo.Components;

[RegisterComponent, AutoGenerateComponentPause]
[Access(typeof(StockMarketSystem), typeof(StockTradingCartridgeSystem))]
public sealed partial class StationStockMarketComponent : Component
{
    [DataField]
    public List<StockCompany> Companies = [];

    [DataField]
    public Dictionary<int, int> StockOwnership = new();

    [DataField]
    public TimeSpan UpdateInterval = TimeSpan.FromSeconds(300);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    [AutoPausedField]
    public TimeSpan NextUpdate = TimeSpan.Zero;

    [DataField]
    public SoundSpecifier ConfirmSound = new SoundPathSpecifier("/Audio/Effects/Cargo/ping.ogg");

    [DataField]
    public SoundSpecifier DenySound = new SoundPathSpecifier("/Audio/Effects/Cargo/buzz_sigh.ogg");

    [DataField]
    public List<MarketChange> MarketChanges =
    [
        new(0.86f, new Vector2(-0.05f, 0.05f)),
        new(0.10f, new Vector2(-0.3f, 0.2f)),
        new(0.03f, new Vector2(-0.5f, 1.5f)),
        new(0.01f, new Vector2(-0.9f, 4.0f)),
    ];
}

[DataRecord]
public record struct MarketChange(float Chance, Vector2 Range);
