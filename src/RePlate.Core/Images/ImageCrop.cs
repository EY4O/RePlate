using System.Numerics;

namespace RePlate.Core.Images;

/// <summary>A rectangle in image pixels. Right and bottom edges are exclusive.</summary>
public readonly record struct ImageCrop(int X, int Y, int Width, int Height)
{
    /// <summary>Turns a drag between two points (0..1 across the image, either direction) into whole pixels.</summary>
    public static ImageCrop? FromSelection(Vector2 start, Vector2 end, int width, int height)
    {
        if (width <= 0 || height <= 0 || !float.IsFinite(start.X) || !float.IsFinite(start.Y) ||
            !float.IsFinite(end.X) || !float.IsFinite(end.Y))
            return null;
        var low = Vector2.Clamp(Vector2.Min(start, end), Vector2.Zero, Vector2.One);
        var high = Vector2.Clamp(Vector2.Max(start, end), Vector2.Zero, Vector2.One);
        if (low.X >= high.X || low.Y >= high.Y) return null;
        var x = (int)MathF.Floor(low.X * width);
        var y = (int)MathF.Floor(low.Y * height);
        return new(x, y, (int)MathF.Ceiling(high.X * width) - x, (int)MathF.Ceiling(high.Y * height) - y);
    }

    /// <summary>
    /// The plate window's rectangle on a full-screen capture, if it stayed put while the capture was taken and sits
    /// fully on screen at one pixel per screen pixel.
    /// </summary>
    public static ImageCrop? FromWindow(Vector2 position, Vector2 size, Vector2? sizeAfter, Vector2? positionAfter,
        Vector2 screen, int width, int height)
    {
        if (sizeAfter != size || positionAfter != position || screen != new Vector2(width, height) ||
            !float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(size.X) || !float.IsFinite(size.Y) ||
            size.X <= 0 || size.Y <= 0 || position.X < 0 || position.Y < 0 ||
            position.X + size.X > width || position.Y + size.Y > height)
            return null;
        var x = (int)MathF.Floor(position.X);
        var y = (int)MathF.Floor(position.Y);
        return new(x, y, (int)MathF.Ceiling(position.X + size.X) - x, (int)MathF.Ceiling(position.Y + size.Y) - y);
    }

    public Vector2 Uv0(int width, int height) => new((float)X / width, (float)Y / height);
    public Vector2 Uv1(int width, int height) => new((float)(X + Width) / width, (float)(Y + Height) / height);
}
