using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

public class BitmapFinder
{
    private readonly Rectangle rectangle;
    private readonly Image<Bgr, Byte> mainImage;

    public BitmapFinder(Bitmap bitmap, Rectangle? rectangle = null)
    {
        this.rectangle = rectangle ?? new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        this.mainImage = bitmap.ToImage<Bgr, Byte>(); // Convert Bitmap to Image
    }

    public BitmapFinder(Image<Bgr, Byte> image, Rectangle? rectangle = null)
    {
        this.rectangle = rectangle ?? new Rectangle(0, 0, image.Width, image.Height);
        this.mainImage = image;
    }

    public Rectangle FindBitmap(game_automation.Image searchImage, double ratio)
    {
        using (Image<Gray, float> result = mainImage.MatchTemplate(searchImage.getImage(), TemplateMatchingType.CcoeffNormed))
        {
            double[] minValues, maxValues;
            Point[] minLocations, maxLocations;
            result.MinMax(out minValues, out maxValues, out minLocations, out maxLocations);
            Rectangle match = new Rectangle(maxLocations[0], searchImage.getImage().Size);

            if (maxValues[0] >= ratio) // Check if the maximum coefficient is above the threshold
            {
                // Draw a rectangle of the matching area
                Console.WriteLine("Found match at " + searchImage.ImagePath + ":"+ maxValues[0]  + "> "+ match.X + ", " + match.Y + ", " + match.Width + ", " + match.Height);
                return match; // Return the rectangle coordinates of the found image
            }
            else
            {
                Console.WriteLine("Failed to match at " + searchImage.ImagePath + ":" + maxValues[0] + "> " + match.X + ", " + match.Y + ", " + match.Width + ", " + match.Height);
            }

        }

        return Rectangle.Empty; // Return an empty rectangle if no match is found
    }

    public List<Rectangle> FindAllMatches(game_automation.Image searchImage, double ratio)
    {
        List<Rectangle> matches = new List<Rectangle>();

        using (Image<Gray, float> result = mainImage.MatchTemplate(searchImage.getImage(), TemplateMatchingType.CcoeffNormed))
        {
            for (int y = 0; y < result.Height; y++)
            {
                for (int x = 0; x < result.Width; x++)
                {
                    if (result.Data[y, x, 0] >= ratio)
                    {
                        Rectangle match = new Rectangle(x, y, searchImage.getImage().Width, searchImage.getImage().Height);
                        matches.Add(match);
                    }
                }
            }
            // Sort matches by descending coefficient (best match first)
            matches = matches.OrderByDescending(m => result.Data[m.Y, m.X, 0]).ToList();

            // Remove overlapping matches
            List<Rectangle> filteredMatches = new List<Rectangle>();
            if (matches.Count > 0)
            {
                filteredMatches.Add(matches[0]); // Add the best match initially
                for (int i = 1; i < matches.Count; i++)
                {
                    bool overlaps = false;
                    foreach (Rectangle existingMatch in filteredMatches)
                    {
                        if (matches[i].IntersectsWith(existingMatch))
                        {
                            overlaps = true;
                            break;
                        }
                    }

                    if (!overlaps)
                    {
                        filteredMatches.Add(matches[i]);
                    }
                }
            }
            return filteredMatches;
        }
    }

    // extract a sub image from the image
    public BitmapFinder ExtractBitmap(Rectangle rect)
    {
        return new BitmapFinder(mainImage.Copy(rect), rect);
    }

    public Rectangle GetRectangle()
    {
        return rectangle;
    }

    public Bitmap GetBitmap()
    {
        return mainImage.ToBitmap();
    }

    public Image<Bgr, Byte> GetImage()
    {
        return mainImage;
    }

    public static Image<Bgr, Byte> toImage(Bitmap bitmap)
    {
        return bitmap.ToImage<Bgr, Byte>();
    }

    // static method to load a image from a file
    public static Bitmap LoadBitmap(string filePath)
    {
        using (var bmpTemp = new Bitmap(filePath))
        {
            return new Bitmap(bmpTemp);
        }
    }
}
