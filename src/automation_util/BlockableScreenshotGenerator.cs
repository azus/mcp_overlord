using System;
using System.Drawing;
using System.Threading;

namespace game_automation_in_csharp.util
{
    public class BlockableScreenshotGenerator : ScreenshotGenerator
    {
        private readonly ManualResetEventSlim _blockEvent;

        public BlockableScreenshotGenerator(ManualResetEventSlim blockEvent)
        {
            _blockEvent = blockEvent;
        }

        public override Bitmap CaptureWindow(IntPtr windowHandle)
        {
            _blockEvent.Wait();
            return base.CaptureWindow(windowHandle);
        }
    }
}
