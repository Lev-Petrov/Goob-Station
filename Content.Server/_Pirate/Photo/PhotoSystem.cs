// SPDX-FileCopyrightText: 2026 Space Station 14 Contributors
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server.Hands.Systems;
using Content.Server.Materials;
using Content.Server.Popups;
using Content.Server.Administration;
using Content.Shared._Pirate.Photo;
using Content.Shared.Hands.Components;
using Content.Shared.Humanoid;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Materials;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Tag;
using Content.Shared.Timing;
using Content.Shared.UserInterface;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects.Components.Localization;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using System.Numerics;

namespace Content.Server._Pirate.Photo;

public sealed partial class PhotoSystem : SharedPhotoSystem
{
    private static readonly ProtoId<TagPrototype> CameraFilmTag = "CameraFilm";
    private static readonly ProtoId<TagPrototype> WriteTag = "Write";

    [Dependency] private readonly UserInterfaceSystem _userInterface = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly HandsSystem _hands = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly MaterialStorageSystem _material = default!;
    [Dependency] private readonly TransformSystem _transform = default!;
    [Dependency] private readonly UseDelaySystem _delay = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!; // # Pirate: camera
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly QuickDialogSystem _quickDialog = default!;

    //96 KB
    const int MAX_SIZE = 1024 * 96;
    // 16 KB
    const int MAX_PREVIEW_SIZE = 1024 * 16;
    private const int MaxCustomNameLength = 32;
    private const int MaxCustomDescriptionLength = 128;
    private const int MaxCustomCaptionLength = 256;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PhotoCameraComponent, AfterActivatableUIOpenEvent>(OnOpenCameraInterface);
        Subs.BuiEvents<PhotoCameraComponent>(PhotoCameraUiKey.Key, subs =>
        {
            subs.Event<BoundUIClosedEvent>(OnCameraBoundUiClose);
            subs.Event<PhotoCameraTakeImageMessage>(OnTakeImageMessage);
        });
        SubscribeLocalEvent<PhotoCameraComponent, MaterialAmountChangedEvent>(OnPaperInserted);
        SubscribeLocalEvent<PhotoCameraComponent, InteractUsingEvent>(OnCameraInteractUsing);

        SubscribeLocalEvent<PhotoCardComponent, AfterActivatableUIOpenEvent>(OnOpenCardInterface);
        SubscribeLocalEvent<PhotoCardComponent, InteractUsingEvent>(OnPhotoCardInteractUsing);
    }

    private void OnOpenCameraInterface(EntityUid uid, PhotoCameraComponent component, AfterActivatableUIOpenEvent args)
    {
        UpdateCameraInterface(uid, component);

        component.User = args.User;
        EnsureComp<PhotoCameraUserComponent>(args.User);
    }

    private void OnCameraBoundUiClose(EntityUid uid, PhotoCameraComponent component, BoundUIClosedEvent args)
    {
        if (component.User == null)
            return;

        RemComp<PhotoCameraUserComponent>(component.User.Value);
        component.User = null;
    }

    private void OnTakeImageMessage(EntityUid uid, PhotoCameraComponent component, PhotoCameraTakeImageMessage message)
    {
        if (message.Data.Length > MAX_SIZE)
            return;

        if (!CheckPngSignature(message.Data))
            return;

        byte[]? previewData = null;
        if (message.PreviewData is { Length: > 0 } preview &&
            preview.Length <= MAX_PREVIEW_SIZE &&
            CheckPngSignature(preview))
        {
            previewData = preview;
        }

        if (TryTakeImage(uid, component, message.Data, previewData, message.CapturedEntities, message.Zoom))
            RaiseLocalEvent(new PhotoCameraTakeImageEvent(uid, message.Actor, message.PhotoPosition, message.Zoom));
    }

    private void UpdateCameraInterface(EntityUid uid, PhotoCameraComponent component, EntityUid? player = null)
    {
        bool hasPaper = _material.CanChangeMaterialAmount(uid, component.CardMaterial, -component.CardCost);

        var state = new PhotoCameraUiState(GetNetEntity(uid), hasPaper);
        _userInterface.SetUiState(uid, PhotoCameraUiKey.Key, state);
    }

    private void OnPaperInserted(EntityUid uid, PhotoCameraComponent component, MaterialAmountChangedEvent args)
    {
        if (TryComp<MaterialStorageComponent>(uid, out var storage))
            Dirty(uid, storage);

        UpdateCameraInterface(uid, component, component.User);
    }

    private void OnCameraInteractUsing(EntityUid uid, PhotoCameraComponent component, InteractUsingEvent args)
    {
        if (args.Handled || !_tag.HasTag(args.Used, CameraFilmTag))
            return;

        args.Handled = true;

        var photosLeft = (int)MathF.Ceiling(_material.GetMaterialAmount(uid, component.CardMaterial) / (float)component.CardCost);
        if (photosLeft > 0)
        {
            _popup.PopupEntity(Loc.GetString("photo-camera-film-not-empty"), uid, args.User);
            return;
        }

        if (!TryComp<MaterialStorageComponent>(uid, out var storage))
            return;

        if (!_material.TryInsertMaterialEntity(args.User, args.Used, uid, storage))
        {
            _popup.PopupEntity(Loc.GetString("photo-camera-film-cannot-insert"), uid, args.User);
            return;
        }

        UpdateCameraInterface(uid, component, component.User);
    }

    private void OnPhotoCardInteractUsing(EntityUid uid, PhotoCardComponent component, InteractUsingEvent args)
    {
        if (args.Handled || !_tag.HasTag(args.Used, WriteTag))
            return;

        args.Handled = true;
        TryPromptPhotoCustomization(args.User, uid);
    }

    private bool TryTakeImage(EntityUid uid, PhotoCameraComponent component, byte[] imageData, byte[]? previewData, List<NetEntity> capturedEntities, float zoom)
    {
        if (_delay.IsDelayed(uid))
            return false;

        var printCard = PrintCard(uid, component, imageData, previewData, capturedEntities, zoom);

        if (printCard)
        {
            _delay.TryResetDelay(uid);
            _audio.PlayPvs(component.PhotoSound, uid);
        }
        else
            _audio.PlayPvs(component.ErrorSound, uid);

        return printCard;
    }

    private bool PrintCard(EntityUid uid, PhotoCameraComponent component, byte[] imageData, byte[]? previewData, List<NetEntity> capturedEntities, float zoom)
    {
        if (!_material.TryChangeMaterialAmount(uid, component.CardMaterial, -component.CardCost))
        {
            if (component.User != null)
                _popup.PopupEntity(Loc.GetString("photo-camera-no-paper"), uid, component.User.Value);

            return false;
        }

        var card = Spawn(component.CardPrototype);
        _transform.SetMapCoordinates(card, _transform.GetMapCoordinates(uid));

        PhotoCaptureMetadata? metadata = null;
        if (component.User is { } user)
            metadata = BuildPhotoMetadata(user, capturedEntities, component.ViewBox, zoom);

        if (TryComp<PhotoCardComponent>(card, out var photo))
        {
            photo.ImageData = imageData;
            photo.PreviewData = previewData;

            if (metadata is { } captureMetadata)
            {
                photo.MobsSeen = new List<EntityUid>(captureMetadata.MobsSeen);
                photo.DeadSeen = new List<EntityUid>(captureMetadata.DeadSeen);
                photo.NamesSeen = new List<string>(captureMetadata.NamesSeen);
                photo.BaseDescription = captureMetadata.Description;
            }

            UpdatePhotoCardAppearance(card, photo);
            UpdatePhotoCardExamineDescription(card, photo);
        }

        if (component.User != null)
        {
            _hands.TryPickupAnyHand(component.User.Value, card);
            TryPromptPhotoCustomization(component.User.Value, card);
        }

        UpdateCameraInterface(uid, component, component.User);

        return true;
    }

    public void UpdatePhotoCardAppearance(EntityUid uid, PhotoCardComponent component)
    {
        if (!TryComp<AppearanceComponent>(uid, out var appearance))
            return;

        _appearance.SetData(uid, PhotoCardVisuals.PreviewImage, component.PreviewData ?? Array.Empty<byte>(), appearance);
    }

    private static bool CheckPngSignature(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8) return false;
        return data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 &&
                data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A;
    }

    private PhotoCaptureMetadata BuildPhotoMetadata(EntityUid photographer, IReadOnlyList<NetEntity> capturedEntities, Vector2 viewBox, float zoom)
    {
        var areaWidth = Math.Max(1, (int) MathF.Ceiling(viewBox.X * Math.Clamp(zoom, 0.01f, 10f)));
        var areaHeight = Math.Max(1, (int) MathF.Ceiling(viewBox.Y * Math.Clamp(zoom, 0.01f, 10f)));

        var descriptionLines = new List<string>
        {
            Loc.GetString("photo-card-area-description", ("width", areaWidth), ("height", areaHeight))
        };

        var mobsSeen = new List<EntityUid>();
        var deadSeen = new List<EntityUid>();
        var namesSeen = new List<string>();
        var seen = new HashSet<EntityUid>();

        foreach (var netEntity in capturedEntities)
        {
            var entity = GetEntity(netEntity);
            if (!Exists(entity) || !seen.Add(entity))
                continue;

            if (!TryComp<MobStateComponent>(entity, out var mobState))
                continue;

            var entityName = Identity.Name(entity, EntityManager, photographer);
            mobsSeen.Add(entity);

            if (mobState.CurrentState == MobState.Dead)
                deadSeen.Add(entity);

            if (TryComp<HumanoidAppearanceComponent>(entity, out _))
                namesSeen.Add(entityName);

            var heldItems = GetHeldItems(entity, photographer);
            var gender = GetPhotoGender(entity);
            descriptionLines.Add(BuildEntityDescriptionLine(entityName, mobState.CurrentState, heldItems, gender));
        }

        return new PhotoCaptureMetadata(
            mobsSeen,
            deadSeen,
            namesSeen,
            string.Join("\n", descriptionLines));
    }

    private List<string> GetHeldItems(EntityUid entity, EntityUid photographer)
    {
        var heldItems = new List<string>();
        if (!TryComp<HandsComponent>(entity, out var hands))
            return heldItems;

        foreach (var held in _hands.EnumerateHeld((entity, hands)))
        {
            if (!Exists(held))
                continue;

            heldItems.Add(Identity.Name(held, EntityManager, photographer));
        }

        return heldItems;
    }

    private string BuildEntityDescriptionLine(string entityName, MobState state, IReadOnlyList<string> heldItems, string gender)
    {
        var hasState = TryGetStateDescription(state, out var stateDescription);
        var hasHeldItems = heldItems.Count > 0;
        var heldItemsText = JoinCommaList(heldItems);

        return (hasState, hasHeldItems) switch
        {
            (true, true) => Loc.GetString("photo-card-mob-description-state-holding",
                ("name", entityName),
                ("state", stateDescription),
                ("gender", gender),
                ("items", heldItemsText)),
            (true, false) => Loc.GetString("photo-card-mob-description-state",
                ("name", entityName),
                ("state", stateDescription)),
            (false, true) => Loc.GetString("photo-card-mob-description-holding",
                ("name", entityName),
                ("gender", gender),
                ("items", heldItemsText)),
            _ => Loc.GetString("photo-card-mob-description",
                ("name", entityName))
        };
    }

    private string GetPhotoGender(EntityUid entity)
    {
        if (TryComp<HumanoidAppearanceComponent>(entity, out var humanoid))
            return GetGenderKey(GetHumanoidPhotoGender(entity, humanoid));

        if (TryComp<GrammarComponent>(entity, out var grammar) && grammar.Gender is { } grammarGender)
            return GetGenderKey(grammarGender);

        return "other";
    }

    private Gender GetHumanoidPhotoGender(EntityUid entity, HumanoidAppearanceComponent humanoid)
    {
        if (TryComp<GrammarComponent>(entity, out var grammar) &&
            grammar.Gender is { } grammarGender &&
            grammarGender != Gender.Neuter)
        {
            return grammarGender;
        }

        if (humanoid.Gender != Gender.Neuter)
            return humanoid.Gender;

        return humanoid.Sex switch
        {
            Sex.Male => Gender.Male,
            Sex.Female => Gender.Female,
            _ => Gender.Epicene
        };
    }

    private static string GetGenderKey(Gender gender)
    {
        return gender switch
        {
            Gender.Male => "male",
            Gender.Female => "female",
            Gender.Epicene => "epicene",
            Gender.Neuter => "neuter",
            _ => "other"
        };
    }

    private static string JoinCommaList(IReadOnlyList<string> values)
    {
        if (values.Count == 0)
            return string.Empty;

        return values.Count == 1 ? values[0] : string.Join(", ", values);
    }

    private bool TryGetStateDescription(MobState state, out string description)
    {
        switch (state)
        {
            case MobState.Critical:
                description = Loc.GetString("photo-card-mob-state-critical");
                return true;
            case MobState.Dead:
                description = Loc.GetString("photo-card-mob-state-dead");
                return true;
        }

        description = string.Empty;
        return false;
    }

    private void TryPromptPhotoCustomization(EntityUid user, EntityUid photo)
    {
        if (!Exists(user) || !Exists(photo))
            return;

        if (!TryComp<ActorComponent>(user, out var actor))
            return;

        if (!TryComp<PhotoCardComponent>(photo, out var photoCard))
            return;

        PromptPhotoName(
            actor.PlayerSession,
            user,
            photo,
            photoCard.CustomName,
            photoCard.CustomDescription);
    }

    private void PromptPhotoName(
        ICommonSession session,
        EntityUid user,
        EntityUid photo,
        string? customName,
        string? customDescription)
    {
        if (!CanContinueCustomization(user, photo))
            return;

        _quickDialog.OpenDialogPrefilled<string>(
            session,
            Loc.GetString("photo-camera-customize-name-title"),
            string.Empty,
            okAction: value =>
            {
                var name = TrimToLength(value, MaxCustomNameLength);
                SetPhotoCustomName(user, photo, name);
                PromptPhotoDescription(session, user, photo, name, customDescription);
            },
            initialValue: customName);
    }

    private void PromptPhotoDescription(
        ICommonSession session,
        EntityUid user,
        EntityUid photo,
        string? customName,
        string? customDescription)
    {
        if (!CanContinueCustomization(user, photo))
            return;

        _quickDialog.OpenDialogPrefilled<LongString>(
            session,
            Loc.GetString("photo-camera-customize-description-title"),
            string.Empty,
            okAction: value =>
            {
                var description = TrimToLength(value, MaxCustomDescriptionLength);
                SetPhotoCustomDescription(user, photo, description);
                PromptPhotoCaption(session, user, photo, customName, description);
            },
            initialValue: customDescription);
    }

    private void PromptPhotoCaption(
        ICommonSession session,
        EntityUid user,
        EntityUid photo,
        string? customName,
        string? customDescription)
    {
        if (!CanContinueCustomization(user, photo))
            return;

        if (!TryComp<PhotoCardComponent>(photo, out var photoCard))
            return;

        _quickDialog.OpenDialogPrefilled<LongString>(
            session,
            Loc.GetString("photo-camera-customize-caption-title"),
            string.Empty,
            okAction: value =>
            {
                var caption = TrimToLength(value, MaxCustomCaptionLength);
                SetPhotoCaption(user, photo, caption);
            },
            initialValue: photoCard.Caption);
    }

    private void SetPhotoCustomName(
        EntityUid user,
        EntityUid photo,
        string? customName)
    {
        if (!CanContinueCustomization(user, photo))
            return;

        if (!TryComp<PhotoCardComponent>(photo, out var photoCard))
            return;

        photoCard.CustomName = customName;
    }

    private void SetPhotoCustomDescription(
        EntityUid user,
        EntityUid photo,
        string? customDescription)
    {
        if (!CanContinueCustomization(user, photo))
            return;

        if (!TryComp<PhotoCardComponent>(photo, out var photoCard))
            return;

        photoCard.CustomDescription = customDescription;
        UpdatePhotoCardExamineDescription(photo, photoCard);
    }

    private void SetPhotoCaption(
        EntityUid user,
        EntityUid photo,
        string? caption)
    {
        if (!CanContinueCustomization(user, photo))
            return;

        if (!TryComp<PhotoCardComponent>(photo, out var photoCard))
            return;

        photoCard.Caption = caption;
    }

    private bool CanContinueCustomization(EntityUid user, EntityUid photo)
    {
        return Exists(user) && Exists(photo);
    }

    private static string? TrimToLength(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
            trimmed = trimmed[..maxLength];

        return trimmed;
    }

    private void UpdatePhotoCardExamineDescription(EntityUid uid, PhotoCardComponent component)
    {
        var baseDescription = component.BaseDescription;
        var customDescription = component.CustomDescription;

        string? composedDescription = null;
        if (!string.IsNullOrWhiteSpace(customDescription))
        {
            composedDescription = string.IsNullOrWhiteSpace(baseDescription)
                ? customDescription
                : $"{customDescription} - {baseDescription}";
        }
        else if (!string.IsNullOrWhiteSpace(baseDescription))
        {
            composedDescription = baseDescription;
        }

        if (!string.IsNullOrWhiteSpace(composedDescription))
            _metaData.SetEntityDescription(uid, composedDescription);
    }

    // Photo Card

    private void OnOpenCardInterface(EntityUid uid, PhotoCardComponent component, AfterActivatableUIOpenEvent args)
    {
        var state = new PhotoCardUiState(component.ImageData, component.CustomName, component.Caption);
        _userInterface.SetUiState(uid, PhotoCardUiKey.Key, state);
    }

    private sealed record PhotoCaptureMetadata(
        List<EntityUid> MobsSeen,
        List<EntityUid> DeadSeen,
        List<string> NamesSeen,
        string Description);
}
