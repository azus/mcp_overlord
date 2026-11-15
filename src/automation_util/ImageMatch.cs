using System.Drawing;

namespace game_automation
{
    public class ImageMatch
    {
        public ImageMatch(game_automation.Image image, Rectangle rectangle)
        {
            Image = image;
            Rectangle = rectangle;
        }

        public game_automation.Image Image { get; }
        public Rectangle Rectangle { get; }
    }
}
