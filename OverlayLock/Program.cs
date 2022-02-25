using System;
using System.Text;

namespace OverlayLock
{
    public static class Program
    {
        [STAThread]
        static void Main()
        {
            // for using Encoding.GetEncoding("ISO-8859-8")
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            using var game = new OverlayLockGame();
            game.Run();
        }
    }
}
