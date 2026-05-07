using ObjCRuntime;
using UIKit;

namespace Honua.Mobile.PlatformSmoke;

public static class Program
{
    private static void Main(string[] args)
    {
        UIApplication.Main(args, null, typeof(AppDelegate));
    }
}
