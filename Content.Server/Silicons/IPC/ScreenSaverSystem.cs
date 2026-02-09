using Content.Shared.Actions;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Silicons.IPC;
using Content.Server.Humanoid;
using Robust.Shared.Prototypes;
using System.Linq;
using System;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;

namespace Content.Server.Silicons.IPC;

public sealed class ScreenSaverSystem : SharedScreenSaverSystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ScreenSaverComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ScreenSaverComponent, ComponentShutdown>(OnShutdown);
        SubscribeNetworkEvent<SelectScreenSaverMessage>(OnSelectScreen);
    }

    private void OnMapInit(EntityUid uid, ScreenSaverComponent component, MapInitEvent args)
    {
        _actions.AddAction(uid, ref component.ActionEntity, component.ActionId);
        UpdateVisuals(uid, component);
        Dirty(uid, component);
    }

    private void OnShutdown(EntityUid uid, ScreenSaverComponent component, ComponentShutdown args)
    {
        _actions.RemoveAction(uid, component.ActionEntity);
    }

    private void OnSelectScreen(SelectScreenSaverMessage msg, EntitySessionEventArgs args)
    {
        var uid = args.SenderSession.AttachedEntity;
        if (uid == null) return;

        if (!TryComp<ScreenSaverComponent>(uid, out var component))
            return;

        if (!TryComp<HumanoidAppearanceComponent>(uid, out var humanoid))
            return;

        if (!MarkingManager.Markings.TryGetValue(msg.MarkingId, out var markingPrototype))
            return;
        
        if (markingPrototype.MarkingCategory != MarkingCategories.Face)
            return;
         
        if (markingPrototype.SpeciesRestrictions != null && !markingPrototype.SpeciesRestrictions.Contains("IPC"))
            return;

        var color = Color.White;
        if (humanoid.MarkingSet.Markings.TryGetValue(MarkingCategories.Face, out var faceMarkings))
        {
            var lastMarking = faceMarkings.LastOrDefault();
            if (lastMarking != null && lastMarking.MarkingColors.Count > 0)
            {
                color = lastMarking.MarkingColors[0];
            }
        }

        if (humanoid.MarkingSet.Markings.ContainsKey(MarkingCategories.Face))
        {
             var toRemove = new List<Marking>(humanoid.MarkingSet.Markings[MarkingCategories.Face]);
             foreach (var m in toRemove)
             {
                 RemoveMarking(uid.Value, m.MarkingId);
             }
        }

        Humanoid.AddMarking(uid.Value, msg.MarkingId, color);
        
        component.CurrentScreen = msg.MarkingId;
        _audio.PlayPvs(new SoundPathSpecifier("/Audio/Machines/terminal_prompt.ogg"), uid.Value);
        UpdateVisuals(uid.Value, component);
        Dirty(uid.Value, component);
    }

    private void RemoveMarking(EntityUid uid, string marking, bool sync = true, HumanoidAppearanceComponent? humanoid = null)
    {
        if (!Resolve(uid, ref humanoid)
            || !MarkingManager.Markings.TryGetValue(marking, out var prototype))
        {
            return;
        }

        humanoid.MarkingSet.Remove(prototype.MarkingCategory, marking);

        if (sync)
            Dirty(uid, humanoid);
    }
}
