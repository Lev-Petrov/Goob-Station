// SPDX-FileCopyrightText: 2026 Space Station 14 Contributors
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.Serialization;

namespace Content.Shared._Pirate.Photo;

[RegisterComponent]
public sealed partial class PhotoCardVisualsComponent : Component
{
}

[Serializable, NetSerializable]
public enum PhotoCardVisuals : byte
{
    PreviewImage
}

public enum PhotoCardVisualLayers : byte
{
    Base,
    Preview
}
