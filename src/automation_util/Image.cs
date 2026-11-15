using Emgu.CV;
using Emgu.CV.Structure;
using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace game_automation
{
    public class Image
    {
        private Image<Bgr, Byte> image;
        private string imagePath;

        public Image(string imagePath)
        {
            this.image = LoadSearchBitmap(imagePath);
            this.imagePath = imagePath;
        }
        public Image(string imagePath, Stream stream)
        {
            this.image = LoadSearchBitmap(imagePath, stream);
            this.imagePath = imagePath;
        }

        public static Image<Bgr, Byte> LoadSearchBitmap(string image)
        {
            return BitmapFinder.toImage(LoadBitmap(image));
        }
        public static Image<Bgr, Byte> LoadSearchBitmap(string image, Stream stream)
        {
            return BitmapFinder.toImage(LoadBitmap(image, stream));
        }

        public Image<Bgr, Byte> getImage()
        {
            return image;
        }

        public string ImagePath { get => imagePath; }

        public static Bitmap LoadBitmap(string image)
        {
            var exeAssembly = Assembly.GetEntryAssembly();
            if (exeAssembly == null)
            {
                throw new Exception("Unable to detect calling EXE assembly.");
            }

            // Get the name of the EXE assembly (e.g., "MyApp.exe")
            var exeAssemblyName = exeAssembly.GetName().Name;

            // Construct the URI for the image resource
            string uriString = $"pack://application:,,,/{exeAssemblyName};component/ressources/{image}";

            BitmapImage bitmapImage = new BitmapImage(new Uri(uriString));
            if (bitmapImage == null)
            {
                throw new Exception("Failed to load image " + image);
            }
            return BitmapImage2Bitmap(bitmapImage);
        }

        public static Bitmap LoadBitmap(string imagePath, Stream stream)
        {
            var bitmapImage = new BitmapImage();
            bitmapImage.BeginInit();
            bitmapImage.StreamSource = stream;
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapImage.EndInit();
            bitmapImage.Freeze();
            if (bitmapImage == null)
            {
                throw new Exception("Failed to load image " + imagePath);
            }
            return BitmapImage2Bitmap(bitmapImage);
        }

        public static Bitmap BitmapImage2Bitmap(BitmapImage bitmapImage)
        {
            using (MemoryStream outStream = new MemoryStream())
            {
                BitmapEncoder enc = new BmpBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bitmapImage));
                enc.Save(outStream);
                Bitmap bitmap = new Bitmap(outStream);
                return new Bitmap(bitmap);
            }
        }
    }
}
