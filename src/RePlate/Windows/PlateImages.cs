using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility.Raii;
using RePlate.Core.Images;

namespace RePlate.Windows;

/// <summary>The selected plate's picture: loading it, attaching a PNG, or capturing and cropping the game view.</summary>
public sealed class PlateImages(ImageFiles files, Func<Task<(Vector2 Position, Vector2 Size)?>> plateWindow) : IDisposable
{
    private sealed record Result(IDalamudTextureWrap? Picture, byte[]? Unsaved, bool Missing, string Message,
        ImageCrop? Suggestion = null, Exception? Error = null);

    private readonly FileDialogManager dialog = new();
    private CancellationTokenSource? cancel;
    private Task<Result>? pending;
    private IDalamudTextureWrap? picture;
    private byte[]? unsaved;
    private ImageCrop? suggestion;
    private Vector2 dragStart;
    private Vector2 dragEnd;
    private bool dragging;
    private Guid selected;
    private string message = "";
    private bool missing;

    public bool Busy => pending != null;

    public void Select(Guid id)
    {
        if (id == selected) return;
        Reset();
        selected = id;
        if (id != Guid.Empty) Run("Loading picture...", token => LoadAsync(id, null, token));
    }

    /// <summary>Removes a deleted plate's picture.</summary>
    public void Delete(Guid id)
    {
        if (id == selected) Reset();
        try { files.Delete(id); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Plugin.Log.Warning(ex, "Couldn't delete a plate picture"); }
    }

    public void Update()
    {
        if (pending is not { IsCompleted: true } done) return;
        var result = done.GetAwaiter().GetResult();
        pending = null;
        cancel?.Dispose();
        cancel = null;
        picture = result.Picture;
        unsaved = result.Unsaved;
        missing = result.Missing;
        message = result.Message;
        ClearSelection();
        suggestion = result.Suggestion;
        if (result.Error != null) Plugin.Log.Warning(result.Error, "Plate picture failed");
    }

    public void Draw(bool canEdit)
    {
        if (selected == Guid.Empty) return;
        using (ImRaii.Disabled(Busy || !canEdit))
        {
            if (ImGui.Button(missing ? "Attach PNG..." : "Replace with PNG..."))
            {
                var target = selected;
                dialog.OpenFileDialog("Choose a picture of this plate", ".png", (ok, path) =>
                {
                    if (ok && target == selected && !Busy) Run("Saving picture...", token => LoadAsync(target, path, token));
                });
            }
            ImGui.SameLine();
            if (ImGui.Button("Capture game view")) StartCapture();
        }
        Ui.TipAlways("Takes a picture of the whole game screen. With your plate open, RePlate suggests its area to crop.");

        if (unsaved != null && picture != null && !Busy)
        {
            var crop = suggestion ?? ImageCrop.FromSelection(dragStart, dragEnd, picture.Width, picture.Height);
            using (ImRaii.Disabled(crop == null || dragging))
            {
                if (ImGui.Button("Crop") && crop is { } area) StartCrop(area);
            }
            ImGui.SameLine();
            if (Theme.PrimaryButton("Use this picture")) StartSave();
            ImGui.SameLine();
            if (ImGui.Button("Discard")) Run("Loading picture...", token => LoadAsync(selected, null, token));
            ImGui.TextDisabled(crop is { } size ? $"Selection {size.Width} x {size.Height}. Drag on the picture to change it." : "Drag on the picture to pick an area.");
        }
        if (message.Length > 0) ImGui.TextWrapped(message);
        DrawPicture();
    }

    public void DrawDialog() => dialog.Draw();

    private void DrawPicture()
    {
        if (picture == null) return;
        var room = ImGui.GetContentRegionAvail();
        var scale = Math.Min(1f, Math.Min(Math.Max(1, room.X) / picture.Width, Math.Max(1, room.Y) / picture.Height));
        var size = picture.Size * scale;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, (room.X - size.X) / 2));
        var origin = ImGui.GetCursorScreenPos();
        ImGui.Image(picture.Handle, size);
        if (unsaved == null || Busy) return;

        // An invisible button takes the drag so it doesn't move the window.
        ImGui.SetCursorScreenPos(origin);
        ImGui.InvisibleButton("##crop", size);
        if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            suggestion = null;
            dragStart = dragEnd = Vector2.Clamp((ImGui.GetMousePos() - origin) / size, Vector2.Zero, Vector2.One);
            dragging = true;
        }
        if (dragging)
        {
            dragEnd = Vector2.Clamp((ImGui.GetMousePos() - origin) / size, Vector2.Zero, Vector2.One);
            if (!ImGui.IsMouseDown(ImGuiMouseButton.Left)) dragging = false;
        }
        if ((suggestion ?? ImageCrop.FromSelection(dragStart, dragEnd, picture.Width, picture.Height)) is { } area)
            ImGui.GetWindowDrawList().AddRect(origin + area.Uv0(picture.Width, picture.Height) * size,
                origin + area.Uv1(picture.Width, picture.Height) * size, ImGui.GetColorU32(Theme.Accent), 0f, 2f);
    }

    private void StartCapture()
    {
        var viewport = ImGui.GetMainViewport();
        if (viewport.Size.X > ImageFiles.MaxDimension || viewport.Size.Y > ImageFiles.MaxDimension)
        {
            message = "The game window is larger than 4096 pixels, which is too big to capture.";
            return;
        }
        var id = viewport.ID;
        var screen = viewport.Size;
        Run("Capturing...", token => CaptureAsync(id, screen, token));
    }

    private void StartCrop(ImageCrop area)
    {
        var source = picture!;
        var original = unsaved!;
        picture = null;
        unsaved = null;
        Run("Cropping...", token => CropAsync(source, original, area, token));
    }

    private void StartSave()
    {
        var shown = picture!;
        var bytes = unsaved!;
        var id = selected;
        picture = null;
        unsaved = null;
        Run("Saving picture...", async token =>
        {
            try
            {
                await files.SaveAsync(id, bytes, token).ConfigureAwait(false);
                return new Result(shown, null, false, "Picture saved.");
            }
            catch (Exception ex)
            {
                return new Result(shown, bytes, false, "The picture couldn't be saved. Try again, or see /xllog.", Error: ex);
            }
        });
    }

    private void Run(string status, Func<CancellationToken, Task<Result>> work)
    {
        picture?.Dispose();
        picture = null;
        message = status;
        cancel = new CancellationTokenSource();
        var token = cancel.Token;
        pending = Task.Run(() => work(token));
    }

    private static async Task<Result> LoadAsync(ImageFiles files, Guid id, string? source, CancellationToken token)
    {
        IDalamudTextureWrap? texture = null;
        try
        {
            var bytes = await ImageFiles.ReadAsync(source ?? files.PathFor(id), token).ConfigureAwait(false);
            var (width, height) = ImageFiles.ReadHeader(bytes);
            texture = await Plugin.TextureProvider.CreateFromImageAsync(bytes, "RePlate picture", token).ConfigureAwait(false);
            if (texture.Width != width || texture.Height != height) throw new InvalidDataException("The picture didn't decode properly.");
            if (source != null) await files.SaveAsync(id, bytes, token).ConfigureAwait(false);
            var result = new Result(texture, null, false, source == null ? "" : "Picture saved.");
            texture = null;
            return result;
        }
        catch (Exception ex) when (source == null && ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new(null, null, true, "No picture yet. Attach a PNG or capture the game view.");
        }
        catch (OperationCanceledException)
        {
            return new(null, null, true, "");
        }
        catch (Exception ex)
        {
            return new(null, null, true, "That picture couldn't be loaded. Use a PNG up to 8 MB and 4096 pixels a side.", Error: ex);
        }
        finally
        {
            texture?.Dispose();
        }
    }

    private Task<Result> LoadAsync(Guid id, string? source, CancellationToken token) => LoadAsync(files, id, source, token);

    private async Task<Result> CaptureAsync(uint viewport, Vector2 screen, CancellationToken token)
    {
        IDalamudTextureWrap? texture = null;
        try
        {
            var before = await plateWindow().ConfigureAwait(false);
            // The capture isn't cancelled directly: cancelling it early can leave its task hanging. We stop waiting instead.
            var capture = Plugin.TextureProvider.CreateFromImGuiViewportAsync(new ImGuiViewportTextureArgs
            {
                ViewportId = viewport,
                AutoUpdate = false,
                TakeBeforeImGuiRender = true,
                KeepTransparency = false,
                Uv0 = Vector2.Zero,
                Uv1 = Vector2.One,
            }, "RePlate capture", CancellationToken.None);
            try
            {
                texture = await capture.WaitAsync(TimeSpan.FromSeconds(10), token).ConfigureAwait(false);
            }
            catch
            {
                _ = capture.ContinueWith(t => { if (t.IsCompletedSuccessfully) t.Result.Dispose(); }, TaskScheduler.Default);
                throw;
            }
            if (texture.Width > ImageFiles.MaxDimension || texture.Height > ImageFiles.MaxDimension)
                throw new InvalidDataException("The capture is too large.");

            var after = await plateWindow().ConfigureAwait(false);
            var crop = before is { } b ? ImageCrop.FromWindow(b.Position, b.Size, after?.Size, after?.Position, screen, texture.Width, texture.Height) : null;
            var bytes = await EncodeAsync(texture, token).ConfigureAwait(false);
            var result = new Result(texture, bytes, false, crop == null
                ? "Drag on the picture to pick the plate, then crop."
                : "Your plate's area is marked. Decorations can stick out, so adjust it if needed.", crop);
            texture = null;
            return result;
        }
        catch (OperationCanceledException)
        {
            return new(null, null, true, "");
        }
        catch (Exception ex)
        {
            return new(null, null, true, "The capture didn't work. Try again, or attach a PNG instead.", Error: ex);
        }
        finally
        {
            texture?.Dispose();
        }
    }

    private static async Task<Result> CropAsync(IDalamudTextureWrap source, byte[] original, ImageCrop area, CancellationToken token)
    {
        IDalamudTextureWrap? cropped = null;
        try
        {
            cropped = await Plugin.TextureProvider.CreateFromExistingTextureAsync(source, new TextureModificationArgs
            {
                Uv0 = area.Uv0(source.Width, source.Height),
                Uv1 = area.Uv1(source.Width, source.Height),
                NewWidth = area.Width,
                NewHeight = area.Height,
            }, leaveWrapOpen: true, debugName: "RePlate crop", cancellationToken: token).ConfigureAwait(false);
            var bytes = await EncodeAsync(cropped, token).ConfigureAwait(false);
            var result = new Result(cropped, bytes, false, "Cropped. Use it, crop again, or discard.");
            cropped = null;
            source.Dispose();
            return result;
        }
        catch (Exception ex)
        {
            return new(source, original, false, "The crop didn't work; the full capture is kept.", Error: ex);
        }
        finally
        {
            cropped?.Dispose();
        }
    }

    private static async Task<byte[]> EncodeAsync(IDalamudTextureWrap texture, CancellationToken token)
    {
        var png = Plugin.TextureReadback.GetSupportedImageEncoderInfos().FirstOrDefault(e => e.MimeTypes.Contains("image/png"))
            ?? throw new InvalidOperationException("PNG encoding isn't available.");
        using var stream = ImageFiles.CreateEncodingBuffer();
        await Plugin.TextureReadback.SaveToStreamAsync(texture, png.ContainerGuid, stream,
            leaveWrapOpen: true, leaveStreamOpen: true, cancellationToken: token).ConfigureAwait(false);
        var bytes = stream.ToArray();
        ImageFiles.ReadHeader(bytes);
        return bytes;
    }

    private void ClearSelection()
    {
        dragStart = dragEnd = Vector2.Zero;
        dragging = false;
        suggestion = null;
    }

    private void Reset()
    {
        dialog.Reset();
        cancel?.Cancel();
        if (pending != null)
        {
            var abandoned = pending;
            var source = cancel;
            _ = abandoned.ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully) t.Result.Picture?.Dispose();
                source?.Dispose();
            }, TaskScheduler.Default);
        }
        else cancel?.Dispose();
        pending = null;
        cancel = null;
        picture?.Dispose();
        picture = null;
        unsaved = null;
        ClearSelection();
        missing = false;
        message = "";
        selected = Guid.Empty;
    }

    public void Dispose() => Reset();
}
