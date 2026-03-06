// SPDX-FileCopyrightText: 2026 Space Station 14 Contributors
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.Serialization;

namespace Content.Shared._Pirate.Photo;

[Serializable, NetSerializable]
public sealed class PhotoCardUiState(byte[]? imageData, string? customName, string? caption) : BoundUserInterfaceState
{
    /// <summary>
    /// Binary image data for the photo preview/full image.
    /// </summary>
    public byte[]? ImageData = imageData;

    /// <summary>
    /// Custom user-defined name for the image.
    /// </summary>
    public string? CustomName = customName;

    /// <summary>
    /// Caption text shown with the image.
    /// </summary>
    public string? Caption = caption;
}

[Serializable, NetSerializable]
/// <summary>
/// Key representing which <see cref="PlayerBoundUserInterface"/> is open for this entity.
/// A single-member enum is the standard BUI key convention and is future-proof if more UIs are added later.
/// </summary>
public enum PhotoCardUiKey : byte
{
    Key
}
