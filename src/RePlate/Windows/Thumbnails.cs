using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Interface.Textures.TextureWraps;
using RePlate.Core.Images;

namespace RePlate.Windows;

/// <summary>
/// The pictures on the gallery's cards, loaded in the background a few at a time and swapped when a picture changes.
/// </summary>
public sealed class Thumbnails(ImageFiles files) : IDisposable
{
    private const int LoadingAtOnce = 3;
    private static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(2);

    private sealed class Entry
    {
        public DateTime Written;
        public DateTime CheckAt;
        public Task<IDalamudTextureWrap>? Loading;
        public IDalamudTextureWrap? Picture;
    }

    private readonly Dictionary<Guid, Entry> entries = [];

    /// <summary>The picture for a card, or null while it loads or when there isn't one.</summary>
    public IDalamudTextureWrap? Get(Guid id)
    {
        if (!entries.TryGetValue(id, out var entry)) entries[id] = entry = new Entry();

        // Look at the file now and then, not every frame, to notice a new or replaced picture.
        var now = DateTime.UtcNow;
        if (now >= entry.CheckAt)
        {
            entry.CheckAt = now + CheckEvery;
            var path = files.PathFor(id);
            var written = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : default;
            if (written != entry.Written)
            {
                entry.Written = written;
                Drop(entry);
            }
        }

        if (entry.Loading is { IsCompleted: true } done)
        {
            entry.Loading = null;
            if (done.IsCompletedSuccessfully) entry.Picture = done.Result;
            else entry.Written = default;
        }
        if (entry.Picture == null && entry.Loading == null && entry.Written != default &&
            entries.Values.Count(e => e.Loading != null) < LoadingAtOnce)
        {
            var path = files.PathFor(id);
            entry.Loading = Task.Run(async () =>
            {
                var png = await ImageFiles.ReadAsync(path, CancellationToken.None);
                return await Plugin.TextureProvider.CreateFromImageAsync(png, "RePlate card");
            });
        }
        return entry.Picture;
    }

    /// <summary>Lets go of a picture that was deleted.</summary>
    public void Forget(Guid id)
    {
        if (!entries.Remove(id, out var entry)) return;
        Drop(entry);
    }

    public void Dispose()
    {
        foreach (var entry in entries.Values) Drop(entry);
        entries.Clear();
    }

    // A picture still loading is disposed when it arrives, since nothing will show it.
    private static void Drop(Entry entry)
    {
        entry.Picture?.Dispose();
        entry.Picture = null;
        entry.Loading?.ContinueWith(t => { if (t.IsCompletedSuccessfully) t.Result.Dispose(); }, TaskScheduler.Default);
        entry.Loading = null;
    }
}
