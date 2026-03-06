// SPDX-FileCopyrightText: 2026 Space Station 14 Contributors
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Threading.Tasks;
using Content.Shared._Pirate.RoundEnd;

namespace Content.Client._Pirate.RoundEnd.PhotoAlbum;

public sealed class PhotoAlbumSystem : EntitySystem
{
    public List<AlbumData>? Albums { get; private set; }
    public event Action? AlbumsUpdated;
    private readonly Dictionary<Guid, byte[]?> _fullImageData = new();
    private readonly Dictionary<Guid, TaskCompletionSource<byte[]?>> _pendingImageRequests = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<PhotoAlbumEvent>(OnStationImagesReceived);
        SubscribeNetworkEvent<PhotoAlbumImageResponseEvent>(OnPhotoImageReceived);
    }

    private void OnStationImagesReceived(PhotoAlbumEvent ev)
    {
        ClearImageCaches();
        Albums = ev.Albums;
        AlbumsUpdated?.Invoke();
    }

    private void OnPhotoImageReceived(PhotoAlbumImageResponseEvent ev)
    {
        _fullImageData[ev.ImageId] = ev.ImageData;

        if (!_pendingImageRequests.Remove(ev.ImageId, out var pending))
            return;

        pending.TrySetResult(ev.ImageData);
    }

    public Task<byte[]?> GetFullImageDataAsync(Guid imageId)
    {
        if (_fullImageData.TryGetValue(imageId, out var imageData))
            return Task.FromResult(imageData);

        if (_pendingImageRequests.TryGetValue(imageId, out var pending))
            return pending.Task;

        var request = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingImageRequests[imageId] = request;
        RaiseNetworkEvent(new PhotoAlbumImageRequestEvent(imageId));
        return request.Task;
    }

    public void ClearImagesData()
    {
        Albums = null;
        ClearImageCaches();
    }

    private void ClearImageCaches()
    {
        foreach (var request in _pendingImageRequests.Values)
        {
            request.TrySetResult(null);
        }

        _pendingImageRequests.Clear();
        _fullImageData.Clear();
    }
}
