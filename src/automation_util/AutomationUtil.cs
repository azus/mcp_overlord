using System;
using System.Collections.Generic;
using System.Drawing;

namespace game_automation
{
    public class AutomationUtil
    {
        private readonly ScreenshotGenerator screenshot_generator;

        public AutomationUtil(ScreenshotGenerator screenshot_generator)
        {
            this.screenshot_generator = screenshot_generator;
        }

        public ScreenshotGenerator GetScreenshotGenerator() 
        { 
            return screenshot_generator; 
        }

        public bool detect_menu(IntPtr hwnd, game_automation.Image menu_button, double ratio = 0.8)
        {
            return detect_menu(game_automation.Window.Capture(hwnd, screenshot_generator), menu_button, ratio);
        }

        public bool detect_menu(Window window, game_automation.Image menu_button, double ratio = 0.8)
        {
            var rectangle = window.FindBitmap(menu_button, ratio);
            return (rectangle != Rectangle.Empty);
        }

        public bool activate_menu(IntPtr hwnd, game_automation.Image menu_button, double ratio = 0.8)
        {
            return activate_menu(game_automation.Window.Capture(hwnd, screenshot_generator), menu_button, ratio);
        }

        public bool activate_menu(Window window, game_automation.Image menu_button, double ratio = 0.8, int click_delay_in_milliseconds = 100)
        {
            var rectangle = window.FindBitmap(menu_button, ratio);
            if (rectangle != Rectangle.Empty)
            {
                click_in_the_middle_of_rectangle(window.Hwnd, rectangle, click_delay_in_milliseconds);
                return true;
            }
            return false;
        }
        public bool activate_menu(BitmapFinder finder, IntPtr hwnd, game_automation.Image menu_button, double ratio = 0.8, int click_delay_in_milliseconds = 100)
        {
            var rectangle = finder.FindBitmap(menu_button, ratio);
            if (rectangle != Rectangle.Empty)
            {
                var roi = finder.GetRectangle();
                if (roi != Rectangle.Empty) 
                {
                    rectangle = new Rectangle(roi.X + rectangle.X, roi.Y + rectangle.Y, rectangle.Width, rectangle.Height);
                }
                click_in_the_middle_of_rectangle(hwnd, rectangle, click_delay_in_milliseconds);
                return true;
            }
            return false;
        }

        public List<Rectangle> find_all_matches(IntPtr hwnd, game_automation.Image menu_button, double ratio = 0.8)
        {
            return find_all_matches(game_automation.Window.Capture(hwnd, screenshot_generator), menu_button, ratio);
        }

        public List<Rectangle> find_all_matches(Window window, game_automation.Image menu_button, double ratio = 0.8)
        {
            return window.FindAllMatches(menu_button, ratio);
        }
        
        public List<Rectangle> find_all_matches(BitmapFinder finder, game_automation.Image menu_button, double ratio = 0.8)
        {
            return finder.FindAllMatches(menu_button, ratio);
        }

        public List<ImageMatch> find_matches(Window window, game_automation.Image[] templates, double ratio = 0.8)
        {
            var matches = new List<ImageMatch>();
            foreach (var template in templates)
            {
                foreach (var rectangle in window.FindAllMatches(template, ratio))
                {
                    matches.Add(new ImageMatch(template, rectangle));
                }
            }
            return matches;
        }

        public void click_in_the_middle_of_rectangle(IntPtr hwnd, Rectangle rectangle, int click_delay_in_milliseconds = 100)
        {
            MouseEventClickHelper.ClickOnPoint(hwnd, rectangle.X + rectangle.Width / 2, rectangle.Y + rectangle.Height / 2, click_delay_in_milliseconds);
        }
    }
}
