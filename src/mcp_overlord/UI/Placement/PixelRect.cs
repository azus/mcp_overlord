using System.Text.Json.Serialization;

namespace mcp_overlord.UI.Placement
{
    /// <summary>
    /// A rectangle in physical screen pixels. The origin is the top-left of the primary monitor,
    /// so coordinates on a monitor to the left or above it are negative.
    /// </summary>
    public readonly struct PixelRect : IEquatable<PixelRect>
    {
        /// <summary>
        /// Creates a rectangle.
        /// </summary>
        /// <param name="x">Left edge, in physical pixels.</param>
        /// <param name="y">Top edge, in physical pixels.</param>
        /// <param name="width">Width, in physical pixels.</param>
        /// <param name="height">Height, in physical pixels.</param>
        [JsonConstructor]
        public PixelRect(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        /// <summary>Left edge, in physical pixels.</summary>
        public int X { get; }

        /// <summary>Top edge, in physical pixels.</summary>
        public int Y { get; }

        /// <summary>Width, in physical pixels.</summary>
        public int Width { get; }

        /// <summary>Height, in physical pixels.</summary>
        public int Height { get; }

        /// <summary>Right edge, exclusive.</summary>
        [JsonIgnore]
        public int Right
        {
            get { return X + Width; }
        }

        /// <summary>Bottom edge, exclusive.</summary>
        [JsonIgnore]
        public int Bottom
        {
            get { return Y + Height; }
        }

        /// <summary>True when both sides are positive.</summary>
        [JsonIgnore]
        public bool HasArea
        {
            get { return Width > 0 && Height > 0; }
        }

        /// <summary>
        /// Area of the overlap with <paramref name="other"/>, or zero when the rectangles do not overlap.
        /// </summary>
        /// <param name="other">Rectangle to intersect with.</param>
        /// <returns>The overlapping area, in square pixels.</returns>
        public long IntersectionArea(PixelRect other)
        {
            long left = Math.Max(X, other.X);
            long top = Math.Max(Y, other.Y);
            long right = Math.Min((long)X + Width, (long)other.X + other.Width);
            long bottom = Math.Min((long)Y + Height, (long)other.Y + other.Height);
            if (right <= left || bottom <= top)
            {
                return 0;
            }

            return (right - left) * (bottom - top);
        }

        /// <summary>Horizontal center, in physical pixels.</summary>
        [JsonIgnore]
        public long CenterX
        {
            get { return X + (Width / 2L); }
        }

        /// <summary>Vertical center, in physical pixels.</summary>
        [JsonIgnore]
        public long CenterY
        {
            get { return Y + (Height / 2L); }
        }

        /// <inheritdoc />
        public bool Equals(PixelRect other)
        {
            return X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return obj is PixelRect other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCode.Combine(X, Y, Width, Height);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0},{1} {2}x{3}", X, Y, Width, Height);
        }

        /// <summary>Value equality.</summary>
        public static bool operator ==(PixelRect left, PixelRect right)
        {
            return left.Equals(right);
        }

        /// <summary>Value inequality.</summary>
        public static bool operator !=(PixelRect left, PixelRect right)
        {
            return !left.Equals(right);
        }
    }
}
