using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VM12ImageConverter
{
    public enum ImageType : int
    {
        font    = 0x00,
        tcs     = 0x10,
        tcs1a   = 0x11,
        tcsfa   = 0x12,
        ps      = 0x20,
        ps1a    = 0x21,
        psfa    = 0x22,
    }

    public class ImageConverter
    {
        public static void Main(params string[] args)
        {
            string path = Path.Combine("E:", "Google Drive", "12VM", "Image.png");
            
            Bitmap map = (Bitmap) Image.FromFile(path);

            string result = ConvertImage(map, ImageType.tcs, "test_tcs", null);

            string resultPath = Path.ChangeExtension(path, ".12asm");

            File.WriteAllText(resultPath, result, Encoding.UTF8);

            Process.Start(resultPath);
        }

        public static string ConvertImage(Bitmap map, ImageType type, string name, int? location)
        {
            BitmapData bData = map.LockBits(new Rectangle(Point.Empty, map.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            int size = bData.Stride * bData.Height;

            byte[] data = new byte[size];

            System.Runtime.InteropServices.Marshal.Copy(bData.Scan0, data, 0, size);

            StringBuilder result;
            switch (type)
            {
                case ImageType.font:
                    result = CreateFont(data, bData.Width, bData.Height, type, name, location);
                    break;
                case ImageType.tcs:
                case ImageType.tcs1a:
                case ImageType.tcsfa:
                    result = CreateTcs(data, bData.Width, bData.Height, type, name, location);
                    break;
                case ImageType.ps:
                case ImageType.ps1a:
                case ImageType.psfa:
                    result = CreatePs(data, bData.Width, bData.Height, type, name, location);
                    break;
                default:
                    throw new ArgumentException("Unknown image type: " + type, nameof(type));
            }

            return result.ToString();
        }

        private static StringBuilder CreateFont(byte[] data, int width, int height, ImageType type, string name, int? location)
        {
            return null;
        }

        private static StringBuilder CreateTcs(byte[] data, int width, int height, ImageType type, string name, int? location)
        {
            bool generateAlpha = false;
            int alphaDepth = 0;
            switch (type)
            {
                case ImageType.tcs:
                    alphaDepth = 0;
                    generateAlpha = false;
                    break;
                case ImageType.tcs1a:
                    generateAlpha = true;
                    alphaDepth = 1;
                    break;
                case ImageType.tcsfa:
                    generateAlpha = true;
                    alphaDepth = 3;
                    break;
                default:
                    throw new ArgumentException("Unkown tcs format: " + type, nameof(type));
            }

            StringBuilder sb = new StringBuilder(4 * width * height);

            sb.AppendLine($"; Generated image data \"{name}\"");
            sb.AppendLine();
            sb.AppendLine("!noprintouts");
            sb.AppendLine();
            sb.AppendLine($"<{name}_width = {width}>");
            sb.AppendLine($"<{name}_height = {height}>");
            sb.AppendLine();
            if (location != null)
            {
                sb.AppendLine($"<{name}_sprite = {location}>");
                if (generateAlpha)
                {
                    sb.AppendLine($"<{name}_mask = {location}>");
                }
            }

            sb.Append($":{name}");

            if (location != null)
            {
                sb.Append("\t\t@{name}_sprite");
            }

            byte[] alpha = new byte[data.Length / 4];
            
            for (int i = 0; i < data.Length; i += 4)
            {
                //TODO: Handle integrated alpha
                switch (alphaDepth)
                {
                    case 1:
                        alpha[i / 4] = (byte) (data[i] > 0 ? 1 : 0);
                        break;
                }

                int r = data[i + 2] >> 4;
                int g = data[i + 1] >> 4;
                int b = data[i] >> 4;

                int rgb = r << 8 | g << 4 | b;

                if ((i / 4) % (width / 2) == 0)
                {
                    sb.AppendLine();
                    sb.Append("\t");
                }

                sb.Append($"0x{rgb:X3} ");
            }

            sb.AppendLine();

            return sb;
        }

        private static StringBuilder CreatePs(byte[] data, int width, int height, ImageType type, string name, int? location)
        {
            return null;
        }
    }
}
