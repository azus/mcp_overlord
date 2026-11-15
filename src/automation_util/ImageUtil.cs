using game_automation;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Text.RegularExpressions;

namespace game_automation_in_csharp.util
{
    public class ImageUtil
    {
        public static Image[] load_images(string regex_pattern)
        {
            Assembly? assembly = Assembly.GetEntryAssembly();
            if (assembly == null)
                return new Image[0];
            string[] resourceNames = assembly.GetManifestResourceNames();
            if (resourceNames == null || resourceNames.Length == 0 || !resourceNames[0].EndsWith(".resources"))
                return new Image[0];
            var stream = assembly.GetManifestResourceStream(resourceNames[0]);
            if (stream == null)
                return new Image[0];
            var set = new ResourceSet(stream);
            return load_images(regex_pattern, set);
        }

        public static Image[] load_images(string regex_pattern, ResourceSet set)
        {
            var result = new List<Image>();
            foreach (DictionaryEntry resource in set)
            {
                if (resource.Key is string key && resource.Value is System.IO.Stream value && Regex.IsMatch(key, regex_pattern))
                {
                    var image = new Image(key, value);
                    result.Add(image);
                }
            }
            return result.OrderBy(image => image.ImagePath).ToArray();
        }
    }
}
