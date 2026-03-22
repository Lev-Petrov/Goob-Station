// SPDX-FileCopyrightText: 2026 Corvax Team Contributors
// SPDX-FileCopyrightText: 2026 CyberLanos <cyber.lanos00@gmail.com>
//
// SPDX-License-Identifier: AGPL-3.0-only

namespace Content.Server._Pirate.RoundEnd.PhotoAlbum;

/// <summary>
/// Stores the resolved persistence identifiers for a photo album instance.
/// </summary>
[RegisterComponent]
public sealed partial class PhotoAlbumPersistenceStateComponent : Component
{
    /// <summary>
    /// The owner entity type or category used by persistence.
    /// </summary>
    public string OwnerKind = string.Empty;

    /// <summary>
    /// The unique identifier of the owner instance.
    /// </summary>
    public string OwnerId = string.Empty;

    /// <summary>
    /// The persistence/key namespace for the album.
    /// </summary>
    public string AlbumKey = string.Empty;
}
